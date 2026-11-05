using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using TacitMemory.Core.Backup;
using TacitMemory.Core.Checkup;
using TacitMemory.Core.Configuration;
using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Status;

/// <summary>
/// Builds the status report. The columns that need no service are built by <see cref="SharedAsync"/> for both the
/// running service and the offline report, so each is written once; the running service adds its own columns on top.
/// Nothing here calls a model or the network, and nothing is written.
/// </summary>
internal static class StatusReporter
{
    /// <summary>A local backup file: the automatic one by day, the manual and pre-restore ones by second.</summary>
    private static readonly Regex LocalBackupName = new(
        @"^(?:auto-\d{4}-\d{2}-\d{2}|(?:manual|pre-restore)-\d{4}-\d{2}-\d{2}-\d{6})\.zip$", RegexOptions.CultureInvariant);

    /// <summary>The report of a running service: its endpoint, version and uptime, its models' states, and the shared columns.</summary>
    public static async Task<StatusReport> RunningAsync(
        TacitOptions options, TacitConfig config, Uri endpoint, string version, TimeSpan uptime, IReadOnlyList<ModelStatus> models,
        CancellationToken cancellationToken)
    {
        var language = config.Language;
        var shared = await SharedAsync(options, config, cancellationToken);
        return shared with
        {
            Service = new ServiceStatus(
                ManagementApiMessages.ServiceTitle.In(language),
                [
                    ManagementApiMessages.ServiceRunning.In(language),
                    ManagementApiMessages.ServiceAddress.Format(language, endpoint),
                    ManagementApiMessages.ServiceVersion.Format(language, version),
                    ManagementApiMessages.ServiceUptime.Format(language, Uptime(language, uptime)),
                ],
                Running: true,
                endpoint,
                version,
                (long)uptime.TotalSeconds,
                OtherProgramAnswered: false),
            Models = Models(language, models),
        };
    }

    /// <summary>
    /// The report when no service answers. It reads no model; the service column says so, and says whether another
    /// program answers on the port.
    /// </summary>
    public static async Task<StatusReport> OfflineAsync(TacitOptions options, TacitConfig config, bool otherProgramAnswered, CancellationToken cancellationToken)
    {
        var language = config.Language;
        var line = otherProgramAnswered
            ? ManagementApiMessages.ServiceNotRunningOtherProgram.Format(language, config.Port)
            : ManagementApiMessages.ServiceNotRunning.In(language);
        var shared = await SharedAsync(options, config, cancellationToken);
        return shared with
        {
            Service = new ServiceStatus(
                ManagementApiMessages.ServiceTitle.In(language),
                [line],
                Running: false,
                Endpoint: null,
                Version: null,
                UptimeSeconds: null,
                OtherProgramAnswered: otherProgramAnswered),
        };
    }

    /// <summary>The report when <c>config.yaml</c> does not load: every error line from the load, and the logs location.</summary>
    public static StatusReport InvalidConfig(TacitOptions options, TacitException error)
    {
        var language = ConfigLoader.LanguageOf(options);
        var lines = error.Message.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return new StatusReport
        {
            Config = new ConfigStatus(ManagementApiMessages.ConfigTitle.In(language), lines),
            Logs = Logs(options, language),
        };
    }

    /// <summary>The line that tells the user a command needs the service to run.</summary>
    public static string NotRunningHint(TacitConfig config, bool otherProgramAnswered) => otherProgramAnswered
        ? ManagementApiMessages.NotRunningHintOtherProgram.Format(config.Language, config.Port)
        : ManagementApiMessages.NotRunningHint.Format(config.Language);

    /// <summary>
    /// The columns that need no service: bindings, the checkup, the pending counts, the local backups, the secrets and
    /// the logs. The checkup and pending columns share one read; when the history cannot be read, they say why and the
    /// rest of the report still comes out.
    /// </summary>
    private static async Task<StatusReport> SharedAsync(TacitOptions options, TacitConfig config, CancellationToken cancellationToken)
    {
        var language = config.Language;
        var (summary, unreadable) = await ReadCheckupAsync(options, config, cancellationToken);
        return new StatusReport
        {
            Bindings = BindingChecks.Produce(options, language),
            Checkup = Checkup(options, language, summary, unreadable),
            Pending = Pending(language, summary, unreadable),
            Backups = Backups(options, config),
            Logs = Logs(options, language),
            Secrets = Secrets(config),
        };
    }

    /// <summary>The checkup summary, or the reason it could not be read: the repository's history, or a file held open.</summary>
    private static async Task<(CheckupSummary? Summary, string? Unreadable)> ReadCheckupAsync(
        TacitOptions options, TacitConfig config, CancellationToken cancellationToken)
    {
        try
        {
            return (await CheckupSummary.ReadAsync(config, options.Today, cancellationToken), null);
        }
        catch (TacitException e) when (e.Error == TacitError.GitFailed)
        {
            return (null, e.Message);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (null, e.Message);
        }
    }

    private static CheckupStatus Checkup(TacitOptions options, MemoryLanguage language, CheckupSummary? summary, string? unreadable)
    {
        var title = ManagementApiMessages.CheckupTitle.In(language);
        if (summary is null)
            return new CheckupStatus(title, [ManagementApiMessages.CheckupUnreadable.Format(language, unreadable)], null, null);

        var baseline = summary.Baseline;
        if (baseline.Commit is not { } commit)
            return new CheckupStatus(title, [ManagementApiMessages.CheckupNever.In(language)], null, null);

        var day = commit.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var ago = DaysAgo(language, summary.DaysSince!.Value);
        var line = baseline.Kind == BaselineKind.Checkup
            ? ManagementApiMessages.CheckupOn.Format(language, day, ago)
            : ManagementApiMessages.CheckupFromInit.Format(language, day, ago);
        return new CheckupStatus(title, [line], commit.Day, summary.DaysSince);
    }

    private static PendingStatus Pending(MemoryLanguage language, CheckupSummary? summary, string? unreadable)
    {
        var title = ManagementApiMessages.PendingTitle.In(language);
        if (summary is null)
            return new PendingStatus(title, [ManagementApiMessages.PendingUnreadable.Format(language, unreadable)], null, null, null);

        var confirmable = summary.CandidatesReadable ? summary.Confirmable : (int?)null;
        return new PendingStatus(
            title,
            [
                confirmable is { } count
                    ? ManagementApiMessages.PendingConfirmable.Format(language, count)
                    : ManagementApiMessages.PendingConfirmableUnknown.In(language),
                ManagementApiMessages.PendingFormatProblems.Format(language, summary.Problems),
                summary.MainlineStale ? ManagementApiMessages.PendingMainlineStale.In(language) : ManagementApiMessages.PendingMainlineFresh.In(language),
            ],
            confirmable,
            summary.Problems,
            summary.MainlineStale);
    }

    /// <summary>
    /// The newest local backup of any kind, by when its file was last written, and the last attempt at the cloud backup.
    /// Reads the backup folder and <c>remote.json</c> only: no network is touched.
    /// </summary>
    private static BackupsStatus Backups(TacitOptions options, TacitConfig config)
    {
        var language = config.Language;
        var title = BackupMessages.BackupsTitle.In(language);
        var directory = Path.Combine(options.TacitHome, "backups");
        var latest = Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory)
                .Where(path => LocalBackupName.IsMatch(Path.GetFileName(path)))
                .Select(path => new FileInfo(path))
                .MaxBy(file => file.LastWriteTimeUtc)
            : null;
        var local = latest is null
            ? BackupMessages.BackupNone.In(language)
            : BackupMessages.BackupLatest.Format(language, latest.Name, new DateTimeOffset(latest.LastWriteTime).LocalDateTime);
        var (remote, lastRemote) = RemoteLine(options, config);
        return new BackupsStatus(
            title,
            [local, remote],
            latest?.Name,
            latest is null ? null : new DateTimeOffset(latest.LastWriteTime),
            lastRemote);
    }

    /// <summary>The cloud backup's line, and the attempt it shows; the attempt is <c>null</c> when none can be shown.</summary>
    private static (string Line, CloudBackupRecord? Record) RemoteLine(TacitOptions options, TacitConfig config)
    {
        var language = config.Language;
        if (config.Backup.WebDav is null)
            return (BackupMessages.BackupCloudNotConfigured.In(language), null);

        try
        {
            if (CloudBackup.ReadLast(options) is not { } record)
                return (BackupMessages.BackupCloudNever.In(language), null);
            var when = record.At.LocalDateTime;
            return record.Succeeded
                ? (BackupMessages.BackupCloudSucceeded.Format(language, when), record)
                : (BackupMessages.BackupCloudFailed.Format(language, when, record.Error), record);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return (BackupMessages.BackupCloudUnreadable.Format(language, e.Message), null);
        }
    }

    /// <summary>Where each secret came from. The values are never read into the report.</summary>
    private static SecretsStatus Secrets(TacitConfig config)
    {
        var language = config.Language;
        var decision = config.Models.Decision.ApiKey;
        var items = new List<SecretStatus>
        {
            SecretItem("models.decision.apiKey", SecretVariables.DecisionApiKey, decision),
        };
        var lines = new List<string>();
        if (config.Backup.WebDav is { } webDav)
            items.Add(SecretItem("backup.webdav.password", SecretVariables.WebDavPassword, webDav.Password));
        else
            lines.Add(ManagementApiMessages.SecretWebDavNotConfigured.In(language));

        lines.InsertRange(0, items.Select(item => SecretLine(language, item)));
        return new SecretsStatus(ManagementApiMessages.SecretsTitle.In(language), lines, items);
    }

    private static SecretStatus SecretItem(string key, string variable, SecretValue? secret) => new(
        key,
        variable,
        secret?.Source,
        Overridden: secret is { Source: SecretSource.Environment, WrittenInConfig: true });

    private static string SecretLine(MemoryLanguage language, SecretStatus item) => item.Source switch
    {
        SecretSource.Environment when item.Overridden => ManagementApiMessages.SecretFromEnvironmentOverridden.Format(language, item.Key, item.Variable),
        SecretSource.Environment => ManagementApiMessages.SecretFromEnvironment.Format(language, item.Key, item.Variable),
        SecretSource.ConfigFile => ManagementApiMessages.SecretFromConfig.Format(language, item.Key),
        _ => ManagementApiMessages.SecretMissing.Format(language, item.Key, item.Variable),
    };

    private static ModelsStatus Models(MemoryLanguage language, IReadOnlyList<ModelStatus> models) => new(
        ManagementApiMessages.ModelsTitle.In(language),
        models.Select(model => ModelLine(language, model)).ToList(),
        models);

    private static string ModelLine(MemoryLanguage language, ModelStatus model) => model.State switch
    {
        ModelState.CoolingDown => ManagementApiMessages.ModelCoolingDown.Format(language, model.Role, model.CoolingSeconds),
        _ => ManagementApiMessages.ModelAvailable.Format(language, model.Role),
    };

    /// <summary>"距今 1 天" or "1 day ago", and so on for the other counts.</summary>
    private static string DaysAgo(MemoryLanguage language, int days) =>
        days == 1 ? ManagementApiMessages.DayAgo.In(language) : ManagementApiMessages.DaysAgo.Format(language, days);

    /// <summary>The logs column. It needs only the home directory, so it is shown even when the config does not load.</summary>
    private static LogsStatus Logs(TacitOptions options, MemoryLanguage language)
    {
        var directory = Path.Combine(options.TacitHome, "logs");
        return new LogsStatus(ManagementApiMessages.LogsTitle.In(language), [ManagementApiMessages.LogsDirectory.Format(language, directory)], directory);
    }

    /// <summary>The uptime in the memory language, with the units that are not zero: "2 小时 3 分钟 4 秒" or "2 h 3 min 4 s".</summary>
    private static string Uptime(MemoryLanguage language, TimeSpan uptime)
    {
        var parts = new List<string>();
        if (uptime.Days > 0)
            parts.Add(ManagementApiMessages.UptimeDays.Format(language, uptime.Days));
        if (uptime.Hours > 0)
            parts.Add(ManagementApiMessages.UptimeHours.Format(language, uptime.Hours));
        if (uptime.Minutes > 0)
            parts.Add(ManagementApiMessages.UptimeMinutes.Format(language, uptime.Minutes));
        parts.Add(ManagementApiMessages.UptimeSeconds.Format(language, uptime.Seconds));
        return string.Join(" ", parts);
    }
}
