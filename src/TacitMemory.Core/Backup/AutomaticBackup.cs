using Microsoft.Extensions.Logging;
using TacitMemory.Core.Logging;
using System.Globalization;
using System.Text.RegularExpressions;
using TacitMemory.Core.Configuration;
using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Backup;

/// <summary>
/// The daily automatic backup. After a successful write, if <c>TACIT_HOME/backups/</c> holds no automatic backup for
/// today, it packs the memory repository and the config files through <see cref="BackupArchive"/>, then keeps only the
/// newest <c>backup.keep</c> automatic backups. It runs in the background, outside the write queue; a failure only
/// tells the user, and never changes the write that triggered it. Manual and pre-restore backups are never pruned here.
/// </summary>
internal sealed partial class AutomaticBackup(TacitOptions options, TacitConfig config, ServiceLog log)
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
            var target = Path.Combine(_directory, FileNameFor(options.Today));
            if (File.Exists(target))
                return;

            await _archive.WriteAsync(BackupKind.Auto, target, CancellationToken.None);
            Prune();
            log.Record(LogLevel.Information, LogMessages.BackupWritten, target);
            if (config.HoldsPlaintextSecret)
                log.Notify(LogLevel.Information, BackupMessages.BackupPlaintextKey, Path.Combine(options.TacitHome, "config.yaml"));
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
