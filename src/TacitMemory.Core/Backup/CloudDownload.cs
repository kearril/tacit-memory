using Microsoft.Extensions.Logging;
using TacitMemory.Core.Configuration;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Logging;

namespace TacitMemory.Core.Backup;

/// <summary>
/// The download of the cloud backup for <c>restore --remote</c>. The file is written to <c>backups/.download/</c>, a folder
/// that is not a backup: the status, the automatic pruning and the backup listing read only the files directly in
/// <c>backups/</c>, so the download never counts as one. The restore that takes the file deletes it once it has succeeded;
/// a restore that fails keeps it for inspection.
/// </summary>
internal sealed class CloudDownload(TacitOptions options, TacitConfig config, ServiceLog log)
{
    private const string FolderName = ".download";

    /// <summary>The file a download of <paramref name="webDav"/> writes: <c>backups/.download/&lt;file&gt;</c>.</summary>
    public static string FileOf(TacitOptions options, WebDavConfig webDav) => Path.Combine(FolderOf(options), webDav.File);

    /// <summary>Whether <paramref name="path"/> is a download: a file directly in <c>backups/.download/</c>.</summary>
    public static bool IsDownload(TacitOptions options, string path) =>
        string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Path.GetFullPath(FolderOf(options)), StringComparison.OrdinalIgnoreCase);

    private static string FolderOf(TacitOptions options) => Path.Combine(options.TacitHome, "backups", FolderName);

    /// <summary>Downloads the remote backup, replacing the previous download, and returns the file's full path.</summary>
    /// <exception cref="TacitException">The cloud backup is not configured, or the download failed.</exception>
    public async Task<string> FetchAsync(CancellationToken cancellationToken)
    {
        var webDav = config.Backup.WebDav
            ?? throw new TacitException(TacitError.CloudBackupNotConfigured, BackupMessages.CloudBackupNotConfigured.In(config.Language));
        var file = FileOf(options, webDav);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        using (var client = new WebDavClient(webDav, config.Language))
            await client.DownloadAsync(file, cancellationToken);

        log.Record(LogLevel.Information, LogMessages.CloudBackupDownloaded, file, new FileInfo(file).Length);
        return file;
    }

    /// <summary>Deletes the download at <paramref name="path"/> after a restore from it has succeeded. Best effort: a file that stays is only disk space.</summary>
    public static void Discard(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Left in place; the next download replaces it.
        }
    }
}
