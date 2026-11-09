using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using TacitMemory.Core.Backup;
using TacitMemory.Core.Consolidation;
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
    /// The report when no service answers. It reads no model; the service column says so, says whether another program
    /// answers on the port, and gives the endpoint the service would listen on. The models column lists only the models
    /// the config leaves out, since the states of the others need the service.
    /// </summary>
    public static async Task<StatusReport> OfflineAsync(TacitOptions options, TacitConfig config, bool otherProgramAnswered, CancellationToken cancellationToken)
    {
        var shared = await SharedAsync(options, config, cancellationToken);
        return shared with
        {
            Service = OfflineService(config.Language, config.Port, otherProgramAnswered),
            Models = OfflineModels(config.Language, config.Models),
        };
    }

    /// <summary>
    /// The models column when no service answers: one line per model the config leaves out, or <c>null</c> when every
    /// model is configured. A configured model's state needs the service, so it is not listed.
    /// </summary>
    private static ModelsStatus? OfflineModels(MemoryLanguage language, ModelsConfig models)
    {
        var unconfigured = ModelRoles.Unconfigured(models)
            .Select(role => new ModelStatus(ModelRoles.Name(role), ModelState.Unconfigured, null))
            .ToList();
        return unconfigured.Count == 0 ? null : Models(language, unconfigured);
    }

    /// <summary>The service column when nothing answers: not running, the endpoint it would listen on, and whether another program holds the port.</summary>
    private static ServiceStatus OfflineService(MemoryLanguage language, int port, bool otherProgramAnswered)
    {
        var endpoint = new Uri($"http://127.0.0.1:{port}/mcp");
        var line = otherProgramAnswered
            ? ManagementApiMessages.ServiceNotRunningOtherProgram.Format(language, port)
            : ManagementApiMessages.ServiceNotRunning.In(language);
        return new ServiceStatus(
            ManagementApiMessages.ServiceTitle.In(language),
            [line, ManagementApiMessages.ServiceAddress.Format(language, endpoint)],
            Running: false,
            Endpoint: endpoint,
            Version: null,
            UptimeSeconds: null,
            OtherProgramAnswered: otherProgramAnswered);
    }

    /// <summary>
    /// The report when <c>config.yaml</c> does not load: every error line from the load, the logs location, and the service
    /// column with the endpoint of the port the file gives, or the default. The caller adds the service's own report when a
    /// service answers.
    /// </summary>
    public static StatusReport InvalidConfig(TacitOptions options, TacitException error, LooseSettings settings, bool otherProgramAnswered)
    {
        var language = settings.Language;
        var lines = error.Message.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return new StatusReport
        {
            Config = new ConfigStatus(ManagementApiMessages.ConfigTitle.In(language), lines),
            Service = OfflineService(language, settings.Port, otherProgramAnswered),
            Logs = Logs(options, language),
        };
    }

    /// <summary>The line that tells the user a command needs the service to run.</summary>
    public static string NotRunningHint(MemoryLanguage language, int port, bool otherProgramAnswered) => otherProgramAnswered
        ? ManagementApiMessages.NotRunningHintOtherProgram.Format(language, port)
        : ManagementApiMessages.NotRunningHint.Format(language);

    /// <summary>
    /// The columns that need no service: bindings, the consolidation, the pending counts, the local backups, the secrets and
    /// the logs. The consolidation and pending columns share one read; when the history cannot be read, they say why and the
    /// rest of the report still comes out.
    /// </summary>
    private static async Task<StatusReport> SharedAsync(TacitOptions options, TacitConfig config, CancellationToken cancellationToken)
    {
        var language = config.Language;
        var (summary, unreadable) = await ReadConsolidationAsync(options, config, cancellationToken);
        return new StatusReport
        {
            Bindings = BindingChecks.Produce(options, language),
            Consolidation = Consolidation(options, language, summary, unreadable),
            Pending = Pending(language, summary, unreadable),
            Backups = Backups(options, config),
            Logs = Logs(options, language),
            Secrets = Secrets(config),
        };
    }

    /// <summary>The consolidation summary, or the reason it could not be read: the repository's history, or a file held open.</summary>
    private static async Task<(ConsolidationSummary? Summary, string? Unreadable)> ReadConsolidationAsync(
        TacitOptions options, TacitConfig config, CancellationToken cancellationToken)
    {
        try
        {
            return (await ConsolidationSummary.ReadAsync(config, options.Today, cancellationToken), null);
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

    private static ConsolidationStatus Consolidation(TacitOptions options, MemoryLanguage language, ConsolidationSummary? summary, string? unreadable)
    {
        var title = ManagementApiMessages.ConsolidationTitle.In(language);
        if (summary is null)
            return new ConsolidationStatus(title, [ManagementApiMessages.ConsolidationUnreadable.Format(language, unreadable)], null, null);

        var baseline = summary.Baseline;
        if (baseline.Commit is not { } commit)
            return new ConsolidationStatus(title, [ManagementApiMessages.ConsolidationNever.In(language)], null, null);

        var day = commit.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var ago = DaysAgo(language, summary.DaysSince!.Value);
        var line = baseline.Kind == BaselineKind.Consolidation
            ? ManagementApiMessages.ConsolidationOn.Format(language, day, ago)
            : ManagementApiMessages.ConsolidationFromInit.Format(language, day, ago);
        return new ConsolidationStatus(title, [line], commit.Day, summary.DaysSince);
    }

    private static PendingStatus Pending(MemoryLanguage language, ConsolidationSummary? summary, string? unreadable)
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

    /// <summary>
    /// Where each secret came from. The values are never read into the report. A model or the WebDAV backup that is not
    /// configured needs no secret: it gets a line saying so and no item.
    /// </summary>
    private static SecretsStatus Secrets(TacitConfig config)
    {
        var language = config.Language;
        var items = new List<SecretStatus>();
        var lines = new List<string>();

        void Add(string owner, string key, string variable, bool configured, SecretValue? secret)
        {
            if (!configured)
            {
                lines.Add(ManagementApiMessages.SecretNotConfigured.Format(language, owner));
                return;
            }
            var item = SecretItem($"{owner}.{key}", variable, secret);
            items.Add(item);
            lines.Add(SecretLine(language, item));
        }

        Add("models.embedding", "apiKey", SecretVariables.EmbeddingApiKey, config.Models.Embedding is not null, config.Models.Embedding?.ApiKey);
        Add("models.reranker", "apiKey", SecretVariables.RerankerApiKey, config.Models.Reranker is not null, config.Models.Reranker?.ApiKey);
        Add("models.decision", "apiKey", SecretVariables.DecisionApiKey, config.Models.Decision is not null, config.Models.Decision?.ApiKey);
        Add("backup.webdav", "password", SecretVariables.WebDavPassword, config.Backup.WebDav is not null, config.Backup.WebDav?.Password);
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
        ModelState.Unconfigured => ManagementApiMessages.ModelUnconfigured.Format(language, model.Role),
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
