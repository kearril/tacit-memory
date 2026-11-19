using System.Text.Json;
using Microsoft.Extensions.Logging;
using TacitMemory.Core.Configuration;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Logging;
using TacitMemory.Core.Maintenance;

namespace TacitMemory.Core.Backup;

/// <summary>
/// The cloud backup made on request. The memory is packed under the write hold, as a manual backup is, into a folder
/// under the system temporary directory; the upload runs outside the gate, and the folder is deleted afterwards. Every
/// attempt that passes the configuration check is recorded in <c>backups/remote.json</c>, which <c>status</c> reads. That
/// file is not part of any backup, and no copy of the archive is kept under <c>backups/</c>.
/// </summary>
internal sealed class CloudBackup(TacitOptions options, TacitConfig config, ServiceLog log)
{
    private static readonly JsonSerializerOptions RecordJson = new(JsonSerializerDefaults.Web) { WriteIndented = true, TypeInfoResolver = CoreJson.Default };

    private readonly BackupArchive _archive = new(options, config);

    /// <summary>The record of the last attempt, or <c>null</c> when there was none.</summary>
    /// <exception cref="IOException">The record cannot be read.</exception>
    /// <exception cref="JsonException">The record is not valid JSON of the expected shape.</exception>
    public static CloudBackupRecord? ReadLast(TacitOptions options)
    {
        var path = RecordPath(options);
        return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), RecordJson.For<CloudBackupRecord>()) : null;
    }

    /// <summary>Makes a cloud backup and replaces the remote one with it.</summary>
    /// <param name="gate">The service's gate; the write hold is taken only while the memory is packed.</param>
    /// <exception cref="TacitException">The cloud backup is not configured, or the upload failed.</exception>
    public async Task<BackupResult> RunAsync(MaintenanceGate gate, CancellationToken cancellationToken)
    {
        var webDav = config.Backup.WebDav
            ?? throw new TacitException(TacitError.CloudBackupNotConfigured, BackupMessages.CloudBackupNotConfigured.In(config.Language));
        var work = Path.Combine(Path.GetTempPath(), $"tacit-backup-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(work);
            var file = Path.Combine(work, webDav.File);
            var bytes = await PackAsync(file, gate, cancellationToken);
            using (var client = new WebDavClient(webDav, config.Language))
                await client.ReplaceAsync(file, cancellationToken);

            var location = LocationOf(webDav);
            Write(new CloudBackupRecord(options.Time.GetUtcNow(), Succeeded: true, Error: null));
            log.Record(LogLevel.Information, LogMessages.CloudBackupWritten, location, bytes);
            var notice = PlaintextNotice();
            return new BackupResult(location, bytes, BackupMessages.CloudUploaded.Format(config.Language, location, bytes), notice);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Write(new CloudBackupRecord(options.Time.GetUtcNow(), Succeeded: false, Error: e.Message));
            log.Record(LogLevel.Warning, LogMessages.CloudBackupFailed, e.Message);
            throw;
        }
        finally
        {
            DeleteWorkFolder(work);
        }
    }

    /// <summary>Packs the memory into <paramref name="file"/> while holding the gate for a write, so the archive holds the last write's state.</summary>
    private async Task<long> PackAsync(string file, MaintenanceGate gate, CancellationToken cancellationToken)
    {
        using var hold = await gate.WriteAsync(cancellationToken);
        return await _archive.WriteAsync(BackupKind.Cloud, file, cancellationToken);
    }

    /// <summary>The address of the remote backup, with the user information of the URL removed.</summary>
    private static string LocationOf(WebDavConfig webDav)
    {
        var folder = new UriBuilder(webDav.Url) { UserName = "", Password = "" }.Uri;
        return new Uri(folder, webDav.File).AbsoluteUri;
    }

    private string? PlaintextNotice()
    {
        if (!config.HoldsPlaintextSecret)
            return null;
        var notice = BackupMessages.BackupPlaintextKey.Format(config.Language, Path.Combine(options.TacitHome, "config.yaml"));
        log.Record(LogLevel.Information, notice);
        return notice;
    }

    /// <summary>
    /// Writes the record of an attempt. A record that cannot be written is logged, not raised: the attempt's own result
    /// must not turn into an error because of the record.
    /// </summary>
    private void Write(CloudBackupRecord record)
    {
        try
        {
            var path = RecordPath(options);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(record, RecordJson.For<CloudBackupRecord>()));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.Record(LogLevel.Warning, LogMessages.CloudRecordFailed, e.Message);
        }
    }

    private static void DeleteWorkFolder(string work)
    {
        try
        {
            Directory.Delete(work, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Left for the system's temporary-folder cleanup; nothing in it is needed again.
        }
    }

    private static string RecordPath(TacitOptions options) => Path.Combine(options.TacitHome, "backups", "remote.json");
}
