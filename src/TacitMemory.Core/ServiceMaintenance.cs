using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TacitMemory.Core.Backup;
using TacitMemory.Core.Configuration;
using TacitMemory.Core.Git;
using TacitMemory.Core.Index;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Logging;
using TacitMemory.Core.Maintenance;
using TacitMemory.Core.Models;
using TacitMemory.Core.Upkeep;

namespace TacitMemory.Core;

/// <summary>
/// The backups and the maintenance operations: the backup list, a backup made now, the preview of a restore, the restore
/// itself and the rebuild of the derived index. The restore and the rebuild have the memory to themselves while they run.
/// </summary>
public sealed class ServiceMaintenance
{
    private readonly TacitOptions _options;
    private readonly MaintenanceGate _gate;
    private readonly DerivedUpkeep _upkeep;
    private readonly MaintenanceRunner _runner;
    private readonly ServiceLog _log;
    private readonly Func<MaintenanceRuntime> _runtime;
    private readonly Func<IReadOnlyList<RestartChange>> _reload;

    /// <param name="reload">Opens the runtime again with <c>config.yaml</c> as it is now; called inside a maintenance operation.</param>
    internal ServiceMaintenance(
        TacitOptions options, MaintenanceGate gate, DerivedUpkeep upkeep, MaintenanceRunner runner, ServiceLog log,
        Func<MaintenanceRuntime> runtime, Func<IReadOnlyList<RestartChange>> reload)
    {
        _options = options;
        _gate = gate;
        _upkeep = upkeep;
        _runner = runner;
        _log = log;
        _runtime = runtime;
        _reload = reload;
    }

    private MemoryLanguage Language => _runtime().Config.Language;

    /// <summary>
    /// The local backups, newest first, whether the cloud backup is configured, and its last attempt. Reads the backup
    /// folder and <c>remote.json</c> only: no network is touched, and it does not wait for a maintenance operation.
    /// </summary>
    public BackupList Backups() => LocalBackups.List(_options, _runtime().Config);

    /// <summary>
    /// Makes a backup now. A local one waits in the write queue like a write does, so the archive holds the repository as
    /// the last write left it, and it is never pruned. A cloud one packs the same way under the write gate, then uploads
    /// outside it and replaces the remote backup.
    /// </summary>
    /// <param name="target">Where the backup goes.</param>
    /// <param name="directory">The absolute directory to write a local backup into, created when missing; <c>null</c> means <c>TACIT_HOME/backups/</c>. Not allowed with the cloud target.</param>
    /// <param name="cancellationToken">Cancels the wait and the backup.</param>
    public async Task<BackupResult> BackupAsync(BackupTarget target, string? directory = null, CancellationToken cancellationToken = default)
    {
        if (directory is not null && !Path.IsPathFullyQualified(directory))
            throw new TacitException(TacitError.RequestInvalid, BackupMessages.BackupDirectoryNotAbsolute.Format(Language, directory));
        if (target == BackupTarget.Cloud)
        {
            if (directory is not null)
                throw new TacitException(TacitError.RequestInvalid, BackupMessages.BackupCloudWithDirectory.In(Language));
            return await _runtime().Remote.RunAsync(_gate, cancellationToken);
        }

        using var hold = await _gate.WriteAsync(cancellationToken);
        return await _runtime().Manual.CreateAsync(directory, cancellationToken);
    }

    /// <summary>
    /// Rebuilds the derived index from the memory files: the full-text table, the units, the vectors and the judgment
    /// cache. It waits until the vectors are filled in; when the embedding model cannot be reached, the vectors wait for
    /// it and the background fill takes them over. Runs as a maintenance operation, so the memory tools wait for it.
    /// </summary>
    public Task<ReindexResult> ReindexAsync(CancellationToken cancellationToken = default) =>
        _runner.RunAsync("reindex", ReindexCoreAsync, cancellationToken);

    private async Task<ReindexResult> ReindexCoreAsync(CancellationToken cancellationToken)
    {
        var runtime = _runtime();
        var started = Stopwatch.GetTimestamp();
        runtime.Index.Rebuild();
        runtime.Index.SyncAll();
        var filled = await runtime.Vectors.FillAsync(cancellationToken);
        if (!filled)
            runtime.Vectors.FillInBackground();
        var counts = runtime.Index.Counts();
        var skipped = runtime.Index.SkippedFiles;
        var elapsed = Stopwatch.GetElapsedTime(started);
        var milliseconds = (long)elapsed.TotalMilliseconds;
        _log.Record(LogLevel.Information, LogMessages.IndexRebuilt, counts.Files, counts.Units, counts.Vectors, skipped.Count, milliseconds);
        if (!filled)
            _log.Record(LogLevel.Warning, LogMessages.VectorsWaiting);
        return new ReindexResult(
            counts.Files, counts.Units, counts.Vectors, skipped, !filled, milliseconds,
            ReindexReport(runtime, counts, skipped, !filled, elapsed));
    }

    /// <summary>The summary of a rebuild for the user: the counts and the time, the skipped files, and the vectors still to come.</summary>
    private static string ReindexReport(
        MaintenanceRuntime runtime, IndexCounts counts, IReadOnlyList<string> skipped, bool vectorsPending, TimeSpan elapsed)
    {
        var language = runtime.Config.Language;
        var lines = new List<string>
        {
            MaintenanceMessages.Reindexed.Format(language, counts.Files, counts.Units, counts.Vectors, elapsed.TotalSeconds),
        };
        if (skipped.Count > 0)
        {
            lines.Add(MaintenanceMessages.SkippedFiles.In(language));
            lines.AddRange(skipped.Select(path => $"  {path}"));
        }

        if (!runtime.Models.Embedding.Configured)
            lines.Add(MaintenanceMessages.EmbeddingNotConfigured.In(language));
        if (vectorsPending)
            lines.Add(MaintenanceMessages.VectorsPending.In(language));
        return string.Join('\n', lines);
    }

    /// <summary>
    /// What <c>tacit restore</c> would do: the backup at <paramref name="path"/> (or, with <paramref name="remote"/>, the
    /// cloud backup, which is downloaded first), its manifest, and what it would replace in the memory repository as it is
    /// now (and in <c>config.yaml</c> and <c>prompts/</c> when <paramref name="config"/> is set). Exactly one source is
    /// needed. Changes nothing; a cloud preview only fills <c>backups/.download/</c>. The preview of a cloud backup names
    /// the downloaded file, which is the file a restore takes, so what is confirmed is what is restored.
    /// </summary>
    /// <param name="path">The backup zip, as an absolute path; <c>null</c> with <paramref name="remote"/>.</param>
    /// <param name="remote">Preview the cloud backup instead of a zip.</param>
    /// <param name="config">Whether the restore would also take <c>config.yaml</c> and <c>prompts/</c> from the backup.</param>
    /// <param name="cancellationToken">Cancels the download and the preview.</param>
    public async Task<RestorePreview> PreviewRestoreAsync(string? path, bool remote = false, bool config = false, CancellationToken cancellationToken = default)
    {
        if ((path is not null) == remote)
            throw new TacitException(TacitError.RequestInvalid, RestoreMessages.SourceCount.In(Language));
        if (remote)
        {
            // The download touches no memory file and runs outside the queue.
            var file = await new CloudDownload(_options, _runtime().Config, _log).FetchAsync(cancellationToken);
            return await PreviewLocalRestoreAsync(file, config, cancellationToken);
        }
        RequireAbsolutePath(path!);
        return await PreviewLocalRestoreAsync(path!, config, cancellationToken);
    }

    private async Task<RestorePreview> PreviewLocalRestoreAsync(string path, bool config, CancellationToken cancellationToken)
    {
        using var hold = await _gate.ReadAsync(cancellationToken);
        var running = _runtime().Config;
        using var archive = RestoreArchive.Open(path, running.Language, config);
        return await RestorePreviewBuilder.BuildAsync(_options, running, path, archive, config, cancellationToken);
    }

    /// <summary>
    /// Restores the memory repository from the backup at <paramref name="path"/>, as a maintenance operation. Before anything
    /// is replaced, the state as it is now is kept as a safety backup, <c>pre-restore-*.zip</c>. With <paramref name="config"/>,
    /// <c>config.yaml</c> and <c>prompts/</c> are replaced as well; <c>bindings.json</c> never is. The memory repository goes
    /// back to the configured <c>memory.path</c>. Then the index is rebuilt in full, the watchers are armed again and the
    /// resident blocks are rendered. Once the swap starts, the restore runs to its end or rolls back: a dropped request
    /// does not stop it. A cloud backup is never restored directly: it is previewed, and the restore takes the downloaded file.
    /// </summary>
    /// <param name="path">The backup zip, as an absolute path; <c>null</c> is refused.</param>
    /// <param name="remote">Must be <c>false</c>: a cloud backup is previewed first, then restored from the path the preview names.</param>
    /// <param name="config">Whether to replace <c>config.yaml</c> and <c>prompts/</c> as well.</param>
    /// <param name="cancellationToken">Cancels the wait for the memory; once the swap starts, it is not stopped.</param>
    public async Task<RestoreResult> RestoreAsync(string? path, bool remote = false, bool config = false, CancellationToken cancellationToken = default)
    {
        if (remote)
            throw new TacitException(TacitError.RequestInvalid, RestoreMessages.RemotePreviewFirst.In(Language));
        if (path is null)
            throw new TacitException(TacitError.RequestInvalid, RestoreMessages.SourceCount.In(Language));
        RequireAbsolutePath(path);
        var result = await _runner.RunAsync("restore", _ => RestoreCoreAsync(path, config), cancellationToken);
        // A restore from the cloud download is done with the download once it has succeeded; a failed one keeps it.
        if (CloudDownload.IsDownload(_options, path))
            CloudDownload.Discard(path);
        return result;
    }

    private async Task<RestoreResult> RestoreCoreAsync(string path, bool config)
    {
        // The runtime the restore takes apart and puts back; a reload of the restored config replaces it at the end.
        var runtime = _runtime();
        var language = runtime.Config.Language;
        // A bad backup is refused here, before the index, the watchers or any file is touched.
        using var archive = RestoreArchive.Open(path, language, config);
        var memory = Path.TrimEndingDirectorySeparator(runtime.Config.MemoryPath);
        var install = new RestoreInstall(_options, runtime.Config, _log, Guid.NewGuid().ToString("N")[..8]);
        _log.Record(LogLevel.Information, LogMessages.RestoreStarted, path, (config ? RestoreMessages.ScopeWithConfig : RestoreMessages.ScopeMemory).In(language));

        // The watchers and the index let go of the files the restore replaces; the index reopens once they are final.
        runtime.Sources.Pause();
        runtime.Index.Dispose();

        var notices = new List<string>();
        string safety;
        try
        {
            safety = await install.WriteSafetyBackupAsync(notices, CancellationToken.None);
        }
        catch (Exception e)
        {
            ReopenIndexAndWatchers(runtime);
            throw new TacitException(TacitError.RestoreFailed, RestoreMessages.SafetyFailed.Format(language, e.Message), e);
        }

        RestoredConfig restored;
        string head;
        ReindexResult reindex;
        try
        {
            restored = install.Apply(archive, config);
            reindex = await ReindexCoreAsync(CancellationToken.None);
            head = (await new GitCli(memory, language).RunAsync(CancellationToken.None, "rev-parse", "HEAD")).Trim();
        }
        catch (Exception e)
        {
            var failure = install.RollBack(e, safety);
            ReopenIndexAndWatchers(runtime);
            throw failure;
        }

        // Nothing is left to undo: the old content and the staged leftovers go, then the service picks the restored state up.
        install.Finish();
        runtime.Sources.Resume();
        // A restored config that loads is reloaded as a save on the settings page is, which also applies the prompts and
        // renders the resident blocks; one that does not load leaves the service on the config it had.
        IReadOnlyList<RestartChange> restart = [];
        var reloaded = false;
        if (config && restored.Problem is null)
        {
            try
            {
                restart = _reload();
                reloaded = true;
                notices.Add(RestoreMessages.ConfigApplied.In(language));
            }
            catch (TacitException e) when (e.Error is TacitError.ReloadFailed or TacitError.ConfigInvalid)
            {
                notices.Add(RestoreMessages.ConfigNotApplied.Format(language, e.Message));
            }
        }
        else if (config)
            notices.Add(RestoreMessages.ConfigProblem.Format(language, restored.Problem));
        _upkeep.RefreshNow((config && !reloaded ? Refresh.Prompts : Refresh.None) | Refresh.Instructions);

        if (config)
        {
            notices.Add(RestoreMessages.PromptsApplied.In(language));
            if (restart.Count > 0)
                notices.Add(string.Join('\n', restart
                    .Select(change => RestoreMessages.RestartPendingItem.Format(language, ConfigKeys.Of(change.Path).Path, change.Running, change.Saved))
                    .Prepend(RestoreMessages.RestartPending.In(language))));
            if (restored.Problem is null
                && !string.Equals(Path.GetFullPath(restored.MemoryPath!), Path.GetFullPath(memory), StringComparison.OrdinalIgnoreCase))
                notices.Add(RestoreMessages.MemoryPathNotice.Format(language, restored.MemoryPath, memory));
        }

        if (!reloaded)
            notices.AddRange(_upkeep.RefreshNow(Refresh.Resident));

        _log.Record(LogLevel.Information, LogMessages.RestoreApplied, head, safety, reindex.Files);
        var report = string.Join('\n', new[]
        {
            RestoreMessages.Restored.Format(language, head),
            RestoreMessages.SafetyBackup.Format(language, safety),
            reindex.Report,
        }.Concat(notices));
        return new RestoreResult(safety, head, reindex, notices, restart, report);
    }

    /// <summary>
    /// Rebuilds the index from the files as they are now and arms the watchers again. Used after a rollback and after a
    /// failed safety backup, when the index was closed and nothing was replaced. A failed rebuild is noted, not raised.
    /// </summary>
    private void ReopenIndexAndWatchers(MaintenanceRuntime runtime)
    {
        try
        {
            runtime.Index.Rebuild();
            runtime.Index.SyncAll();
            runtime.Vectors.FillInBackground();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            _log.Notify(LogLevel.Warning, Messages.IndexSyncFailed, e.Message);
        }
        finally
        {
            runtime.Sources.Resume();
        }
    }

    private void RequireAbsolutePath(string path)
    {
        if (!Path.IsPathFullyQualified(path))
            throw new TacitException(TacitError.RequestInvalid, RestoreMessages.PathNotAbsolute.Format(Language, path));
    }
}

/// <summary>The parts of the running memory the maintenance operations take apart and rebuild; a reload replaces them.</summary>
internal sealed record MaintenanceRuntime(
    TacitConfig Config, MemoryIndex Index, VectorSync Vectors, AuxiliaryModels Models, ISourceWatch Sources, ManualBackup Manual, CloudBackup Remote);
