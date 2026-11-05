using Microsoft.Extensions.Logging;
using TacitMemory.Core.Configuration;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Logging;
using TacitMemory.Core.Maintenance;

namespace TacitMemory.Core.Backup;

/// <summary>The outcome of reading the restored <c>config.yaml</c>: where its memory repository points, or why it does not load.</summary>
/// <param name="MemoryPath">The <c>memory.path</c> the restored config names; <c>null</c> when it does not load or was not restored.</param>
/// <param name="Problem">The errors of the restored config, in the memory language; <c>null</c> when it loads.</param>
internal sealed record RestoredConfig(string? MemoryPath, string? Problem);

/// <summary>
/// The file work of a restore, in the order the restore runs it: the safety backup, the staging of the backup's memory
/// repository, the swap that puts it in place, the swap of <c>config.yaml</c> and <c>prompts/</c>, then either the clean-up
/// or the rollback. The staging and the previous content live inside <c>memory.path</c>, so every move is a rename on its
/// volume, whatever the volume of the parent folder is; the names start with <c>.tacit-</c> so the index and the backups
/// never take them for content. <c>MemoryService</c> orders this with the index, the watchers and the rebuild.
/// </summary>
internal sealed class RestoreInstall(TacitOptions options, TacitConfig config, ServiceLog log, string id)
{
    private const string StagingPrefix = ".tacit-restore-";
    private const string PreviousPrefix = ".tacit-previous-";

    private readonly string _memory = Path.TrimEndingDirectorySeparator(config.MemoryPath);
    private readonly MoveJournal _journal = new();

    private string Staging => Path.Combine(_memory, StagingPrefix + id);
    private string Previous => Path.Combine(_memory, PreviousPrefix + id);
    private string Work => Path.Combine(options.TacitHome, "restore-" + id);
    private string[] Folders => [Staging, Previous, Work];

    /// <summary>Whether a name in the repository is one of the folders a restore makes, which are never part of the content.</summary>
    public static bool IsRestoreFolder(string name) =>
        name.StartsWith(StagingPrefix, StringComparison.Ordinal) || name.StartsWith(PreviousPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Writes the state before the restore as <c>pre-restore-*.zip</c>, and the plaintext secrets notice it may owe.
    /// A failure here propagates: nothing has been changed yet.
    /// </summary>
    public async Task<string> WriteSafetyBackupAsync(List<string> notices, CancellationToken cancellationToken)
    {
        var target = Path.Combine(options.TacitHome, "backups", $"pre-restore-{BackupNames.Stamp(options)}.zip");
        var bytes = await new BackupArchive(options, config).WriteAsync(BackupKind.PreRestore, target, cancellationToken);
        log.Record(LogLevel.Information, LogMessages.PreRestoreWritten, target, bytes);
        if (config.HoldsPlaintextSecret)
        {
            var notice = BackupMessages.BackupPlaintextKey.Format(config.Language, Path.Combine(options.TacitHome, "config.yaml"));
            log.Record(LogLevel.Information, notice);
            notices.Add(notice);
        }

        return target;
    }

    /// <summary>
    /// Puts the backup's memory repository in place of the current one, and with <paramref name="withConfig"/> the backup's
    /// <c>config.yaml</c> and <c>prompts/</c>. Every move is recorded, so a failure can be rolled back with
    /// <see cref="RollBack"/>. A restored <c>config.yaml</c> that does not load is not a failure: it is reported in the
    /// result, and the service is restarted with it later.
    /// </summary>
    public RestoredConfig Apply(RestoreArchive archive, bool withConfig)
    {
        archive.ExtractMemory(Staging);
        Directory.CreateDirectory(Previous);
        SwapMemory();
        if (!withConfig)
            return new RestoredConfig(null, null);

        Directory.CreateDirectory(Work);
        SwapConfigAndPrompts(archive);
        return ReadRestoredConfig();
    }

    /// <summary>
    /// Reads the restored config as it is now, to report where it points and whether it loads. It is read from
    /// <c>TACIT_HOME</c>, where the swap has just put it.
    /// </summary>
    private RestoredConfig ReadRestoredConfig()
    {
        try
        {
            return new RestoredConfig(ConfigLoader.Read(options).MemoryPath, null);
        }
        catch (TacitException e)
        {
            return new RestoredConfig(null, e.Message);
        }
    }

    /// <summary>Makes the restore final: the moved-away content is removed and the rollback is no longer possible.</summary>
    public void Finish()
    {
        _journal.Forget();
        foreach (var folder in Folders)
            CleanUp(folder);
    }

    /// <summary>
    /// Puts everything the restore moved back as it was and returns the refusal for the caller to throw. The folders the
    /// restore made are removed, unless the rollback itself failed: then they stay, since they may hold the only copy of
    /// the content.
    /// </summary>
    public TacitException RollBack(Exception cause, string safety)
    {
        var language = config.Language;
        string? undoFailure = null;
        try
        {
            _journal.Undo();
        }
        catch (Exception undo)
        {
            undoFailure = undo.Message;
        }

        if (undoFailure is null)
        {
            foreach (var folder in Folders)
                CleanUp(folder);
            log.Record(LogLevel.Warning, LogMessages.RestoreRolledBack, safety);
            return new TacitException(TacitError.RestoreFailed, RestoreMessages.Failed.Format(language, cause.Message, safety), cause);
        }

        var kept = string.Join(", ", Folders.Where(Directory.Exists));
        log.Record(LogLevel.Warning, LogMessages.RestoreRollbackFailed, undoFailure, kept);
        return new TacitException(
            TacitError.RestoreFailed, RestoreMessages.FailedRollbackFailed.Format(language, cause.Message, undoFailure, kept, safety), cause);
    }

    /// <summary>Moves the repository's content, except its index cache and the restore's own folders, into the previous folder, then the staged content in.</summary>
    private void SwapMemory()
    {
        foreach (var child in Directory.EnumerateFileSystemEntries(_memory).ToList())
        {
            var name = Path.GetFileName(child);
            if (name != ".cache" && !IsRestoreFolder(name))
                _journal.Move(child, Path.Combine(Previous, name));
        }

        foreach (var child in Directory.EnumerateFileSystemEntries(Staging).ToList())
            _journal.Move(child, Path.Combine(_memory, Path.GetFileName(child)));
    }

    /// <summary>
    /// Stages <c>config.yaml</c> and <c>prompts/</c> from the backup beside the home, then moves the current ones aside and the
    /// staged ones in. Both stay on the home's volume, so each move is a rename.
    /// </summary>
    private void SwapConfigAndPrompts(RestoreArchive archive)
    {
        var previous = Path.Combine(Work, "previous");
        Directory.CreateDirectory(previous);
        var stagedConfig = Path.Combine(Work, "config.yaml");
        var stagedPrompts = Path.Combine(Work, "prompts");
        archive.ExtractConfig(stagedConfig);
        archive.ExtractPrompts(stagedPrompts);

        var configFile = Path.Combine(options.TacitHome, "config.yaml");
        if (File.Exists(configFile))
            _journal.Move(configFile, Path.Combine(previous, "config.yaml"));
        _journal.Move(stagedConfig, configFile);

        var prompts = PromptFiles.Folder(options.TacitHome);
        if (Directory.Exists(prompts))
            _journal.Move(prompts, Path.Combine(previous, "prompts"));
        _journal.Move(stagedPrompts, prompts);
    }

    /// <summary>Deletes a folder the restore made. A folder that cannot be deleted is named in the log, not failed over.</summary>
    private void CleanUp(string folder)
    {
        try
        {
            MoveJournal.DeleteTree(folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.Record(LogLevel.Warning, LogMessages.TemporaryFolderKept, folder);
        }
    }
}
