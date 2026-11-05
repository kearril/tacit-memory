using Microsoft.Extensions.Logging;
using TacitMemory.Core.Configuration;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Logging;

namespace TacitMemory.Core.Backup;

/// <summary>
/// A manual backup made on request: <c>manual-YYYY-MM-DD-HHmmss.zip</c> in <c>TACIT_HOME/backups/</c>, or in the
/// directory the caller names. It is never pruned and never counts against <c>backup.keep</c>. The caller packs it
/// under the write gate, so it holds the repository as the last write left it.
/// </summary>
internal sealed class ManualBackup(TacitOptions options, TacitConfig config, ServiceLog log)
{
    private readonly BackupArchive _archive = new(options, config);

    /// <summary>Writes the archive into <paramref name="directory"/>, or <c>backups/</c> when none is given, and describes it.</summary>
    public async Task<BackupResult> CreateAsync(string? directory, CancellationToken cancellationToken)
    {
        var folder = directory ?? Path.Combine(options.TacitHome, "backups");
        var target = Path.Combine(folder, $"manual-{BackupNames.Stamp(options)}.zip");
        try
        {
            var bytes = await _archive.WriteAsync(BackupKind.Manual, target, cancellationToken);
            var notice = config.HoldsPlaintextSecret
                ? BackupMessages.BackupPlaintextKey.Format(config.Language, Path.Combine(options.TacitHome, "config.yaml"))
                : null;
            log.Record(LogLevel.Information, LogMessages.ManualBackupWritten, target, bytes);
            if (notice is not null)
                log.Record(LogLevel.Information, notice);
            return new BackupResult(target, bytes, BackupMessages.ManualBackupWritten.Format(config.Language, target, bytes), notice);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.Record(LogLevel.Warning, LogMessages.ManualBackupFailed, e.Message);
            throw;
        }
    }
}
