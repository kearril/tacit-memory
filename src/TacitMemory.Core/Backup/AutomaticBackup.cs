using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TacitMemory.Core.Configuration;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Logging;
using TacitMemory.Core.Maintenance;

namespace TacitMemory.Core.Backup;

/// <summary>
/// The daily automatic backup. After a successful write, if <c>TACIT_HOME/backups/</c> holds no automatic backup for
/// today, it packs the memory repository and the config files through <see cref="BackupArchive"/>, then keeps only the
/// newest <c>backup.keep</c> automatic backups. It runs in the background as a write-slot task of the
/// <see cref="MaintenanceGate"/>: a write that arrives meanwhile waits for it, so the pack never reads a file a write
/// is replacing or a git lock a write holds. A failure only tells the user, and never changes the write that triggered
/// it. Manual and pre-restore backups are never pruned here.
/// </summary>
internal sealed partial class AutomaticBackup(TacitOptions options, TacitConfig config, ServiceLog log, MaintenanceGate gate)
{
    private readonly string _directory = Path.Combine(options.TacitHome, "backups");

    private readonly BackupArchive _archive = new(options, config);

    /// <summary>The last scheduled run. Each run chains onto the one before, so runs never overlap.</summary>
    private Task _completion = Task.CompletedTask;

    /// <summary>Completes when the most recently scheduled run has finished, whether or not it made a backup.</summary>
    internal Task Completion => _completion;

    /// <summary>Starts a backup run in the background and returns at once. Does nothing when backups are off.</summary>
    public void ScheduleIfDue()
    {
        if (config.Backup.Keep == 0)
            return;
        _completion = _completion.ContinueWith(_ => RunIfDueAsync(), TaskScheduler.Default).Unwrap();
    }

    /// <summary>Nothing may escape: nobody awaits this run except tests, and a failure must not disturb anything.</summary>
    private async Task RunIfDueAsync()
    {
        try
        {
            // Holds the write slot from the check to the end of the pack, so no write replaces a file while it is read.
            using var hold = await gate.WriteAsync(CancellationToken.None);
            var target = Path.Combine(_directory, FileNameFor(options.Today));
            if (File.Exists(target))
                return;

            if (options.BeforeBackupPack is { } beforePack)
                await beforePack();
            await _archive.WriteAsync(BackupKind.Auto, target, CancellationToken.None);
            Prune();
            log.Record(LogLevel.Information, LogMessages.BackupWritten, target);
            if (config.HoldsPlaintextSecret)
                log.Notify(LogLevel.Information, BackupMessages.BackupPlaintextKey, Path.Combine(options.TacitHome, "config.yaml"));
        }
        catch (ObjectDisposedException)
        {
            // The service is shutting down; the run is simply not needed.
        }
        catch (Exception e)
        {
            log.Notify(LogLevel.Warning, BackupMessages.BackupFailed, e.Message);
        }
    }

    /// <summary>Deletes the oldest automatic backups beyond <c>backup.keep</c>. File names sort by date.</summary>
    private void Prune()
    {
        var backups = Directory.EnumerateFiles(_directory)
            .Where(path => AutomaticName().IsMatch(Path.GetFileName(path)))
            .Order(StringComparer.Ordinal)
            .ToList();
        foreach (var stale in backups.Take(Math.Max(0, backups.Count - config.Backup.Keep)))
            File.Delete(stale);
    }

    private static string FileNameFor(DateOnly day) => $"auto-{day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.zip";

    [GeneratedRegex(@"^auto-\d{4}-\d{2}-\d{2}\.zip$")]
    private static partial Regex AutomaticName();
}
