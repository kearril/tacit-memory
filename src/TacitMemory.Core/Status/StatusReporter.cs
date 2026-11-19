using System.Globalization;
using System.Text.Json;
using TacitMemory.Core.Backup;
using TacitMemory.Core.ClientRecords;
using TacitMemory.Core.Configuration;
using TacitMemory.Core.Consolidation;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Logging;
using TacitMemory.Core.Sync;

namespace TacitMemory.Core.Status;

/// <summary>
/// Builds the status report. The columns that need no service are built by <see cref="SharedAsync"/> for both the
/// running service and the offline report, so each is written once; the running service adds its own columns on top.
/// Nothing here calls a model or the network, and nothing is written.
/// </summary>
internal static class StatusReporter
{
    /// <summary>A local backup file: the automatic one by day, the manual and pre-restore ones by second.</summary>
    /// <summary>The report of a running service: its endpoint, version and uptime, its models' states, and the shared columns.</summary>
    public static async Task<StatusReport> RunningAsync(
        TacitOptions options, TacitConfig config, Uri endpoint, string version, TimeSpan uptime, IReadOnlyList<ModelStatus> models,
        ExtraListenerStatus? extraListener, IReadOnlyList<PromptProblem> prompts, CancellationToken cancellationToken)
    {
        var language = config.Language;
        var shared = await SharedAsync(options, config, cancellationToken);
        var web = WebAddress(endpoint);
        List<string> lines =
        [
            StatusMessages.ServiceRunning.In(language),
            StatusMessages.ServiceAddress.Format(language, endpoint),
            StatusMessages.ServiceVersion.Format(language, version),
            StatusMessages.ServiceUptime.Format(language, Uptime(language, uptime)),
            StatusMessages.WebAddress.Format(language, web),
        ];
        if (extraListener is not null)
            lines.Add(extraListener.Line(language));
        return shared with
        {
            Service = new ServiceStatus(
                StatusMessages.ServiceTitle.In(language),
                lines,
                Running: true,
                endpoint,
                version,
                (long)uptime.TotalSeconds,
                OtherProgramAnswered: false,
                web,
                extraListener),
            Models = Models(language, models),
            Prompts = prompts.Count == 0 ? null : new PromptsStatus(
                StatusMessages.PromptsTitle.In(language),
                [.. prompts.Select(item => StatusMessages.PromptKept.Format(language, $"prompts/{FileName(item.Name)}", item.Problem))],
                prompts),
        };
    }

    private static string FileName(PromptName name) => Path.GetFileName(PromptFiles.PathOf("", name));

    /// <summary>The Web interface's address: the loopback address of the service's MCP endpoint, at <c>/ui/</c>.</summary>
    private static Uri WebAddress(Uri endpoint) => new(endpoint, "/ui/");

    /// <summary>The extra listener's line: its address when it answers, and its address with the reason when it did not.</summary>

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
            Service = OfflineService(config.Language, config.Port, otherProgramAnswered, config.ExtraAddress),
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

    /// <summary>
    /// The service column when nothing answers: not running, the endpoint it would listen on, whether another program holds
    /// the port, and the extra listener <c>server.host</c> names, said not to listen yet.
    /// </summary>
    private static ServiceStatus OfflineService(MemoryLanguage language, int port, bool otherProgramAnswered, System.Net.IPAddress? extra = null)
    {
        var endpoint = new Uri($"http://127.0.0.1:{port}/mcp");
        var line = otherProgramAnswered
            ? StatusMessages.ServiceNotRunningOtherProgram.Format(language, port)
            : StatusMessages.ServiceNotRunning.In(language);
        var web = WebAddress(endpoint);
        var extraListener = extra is null ? null : new ExtraListenerStatus(ExtraListenerStatus.AddressOf(extra, port), null);
        List<string> lines = [line, StatusMessages.ServiceAddress.Format(language, endpoint), StatusMessages.WebAddress.Format(language, web)];
        if (extraListener is not null)
            lines.Add(StatusMessages.ExtraListenerNotRunning.Format(language, StatusMessages.ExtraListenerName(extraListener.Address, language)));
        return new ServiceStatus(
            StatusMessages.ServiceTitle.In(language),
            lines,
            Running: false,
            Endpoint: endpoint,
            Version: null,
            UptimeSeconds: null,
            OtherProgramAnswered: otherProgramAnswered,
            WebAddress: web,
            ExtraListener: extraListener);
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
            Config = new ConfigStatus(StatusMessages.ConfigTitle.In(language), lines),
            Service = OfflineService(language, settings.Port, otherProgramAnswered),
            Clients = Clients(options, language),
            Logs = Logs(options, language),
        };
    }

    /// <summary>The line that tells the user a command needs the service to run.</summary>
    public static string NotRunningHint(MemoryLanguage language, int port, bool otherProgramAnswered) => otherProgramAnswered
        ? StatusMessages.NotRunningHintOtherProgram.Format(language, port)
        : StatusMessages.NotRunningHint.Format(language);

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
            Clients = Clients(options, language),
            Bindings = BindingChecks.Produce(options, language),
            Consolidation = Consolidation(language, summary, unreadable),
            Pending = Pending(language, summary, unreadable),
            Backups = Backups(options, config),
            Sync = await SyncAsync(options, config, cancellationToken),
            Logs = Logs(options, language),
            Secrets = Secrets(config),
        };
    }

    /// <summary>
    /// The clients column, newest first, one line each. Read from the state file the service keeps, so a stopped service
    /// shows the same clients as a running one.
    /// </summary>
    private static ClientsStatus Clients(TacitOptions options, MemoryLanguage language)
    {
        var title = StatusMessages.ClientsTitle.In(language);
        var (clients, unreadable) = ClientRecordStore.ReadFor(ClientRecordStore.PathFor(options));
        if (unreadable is not null)
            return new ClientsStatus(title, [StatusMessages.ClientsUnreadable.Format(language, unreadable)], []);
        if (clients.Count == 0)
            return new ClientsStatus(title, [StatusMessages.ClientsNone.In(language)], []);

        var items = clients.OrderByDescending(client => client.LastSeen).ThenBy(client => client.Name, StringComparer.Ordinal).ToList();
        var lines = items.Select(client => StatusMessages.ClientLine.Format(
            language,
            client.Name,
            client.LastSeen.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            client.ProtocolVersion,
            Declared(client.Capabilities, language),
            Declared(client.Extensions, language)))
            .ToList();
        return new ClientsStatus(title, lines, items);
    }

    /// <summary>What a client declared, joined; the word for none when it declared nothing.</summary>
    private static string Declared(IReadOnlyList<string> values, MemoryLanguage language) =>
        values.Count == 0 ? StatusMessages.ClientNothingDeclared.In(language) : string.Join(", ", values);

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

    private static ConsolidationStatus Consolidation(MemoryLanguage language, ConsolidationSummary? summary, string? unreadable)
    {
        var title = StatusMessages.ConsolidationTitle.In(language);
        if (summary is null)
            return new ConsolidationStatus(title, [StatusMessages.ConsolidationUnreadable.Format(language, unreadable)], null, null);

        var baseline = summary.Baseline;
        if (baseline.Commit is not { } commit)
            return new ConsolidationStatus(title, [StatusMessages.ConsolidationNever.In(language)], null, null);

        var day = commit.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var ago = DaysAgo(language, summary.DaysSince!.Value);
        var line = baseline.Kind == BaselineKind.Consolidation
            ? StatusMessages.ConsolidationOn.Format(language, day, ago)
            : StatusMessages.ConsolidationFromInit.Format(language, day, ago);
        return new ConsolidationStatus(title, [line], commit.Day, summary.DaysSince);
    }

    private static PendingStatus Pending(MemoryLanguage language, ConsolidationSummary? summary, string? unreadable)
    {
        var title = StatusMessages.PendingTitle.In(language);
        if (summary is null)
            return new PendingStatus(title, [StatusMessages.PendingUnreadable.Format(language, unreadable)], null, null, null);

        var candidates = summary.CandidatesReadable ? summary.Candidates : (int?)null;
        return new PendingStatus(
            title,
            [
                candidates is { } count
                    ? StatusMessages.PendingCandidates.Format(language, count)
                    : StatusMessages.PendingCandidatesUnknown.In(language),
                StatusMessages.PendingFormatProblems.Format(language, summary.Problems),
                summary.WorkstreamEmpty ? StatusMessages.PendingWorkstreamEmpty.In(language) : StatusMessages.PendingWorkstreamSet.In(language),
                StatusMessages.PendingWeeks.Format(language, summary.UnsummarizedWeeks),
            ],
            candidates,
            summary.Problems,
            summary.WorkstreamEmpty,
            summary.UnsummarizedWeeks);
    }

    /// <summary>
    /// The newest local backup of any kind, by when its file was last written, and the last attempt at the cloud backup.
    /// Reads the backup folder and <c>remote.json</c> only: no network is touched.
    /// </summary>
    private static BackupsStatus Backups(TacitOptions options, TacitConfig config)
    {
        var language = config.Language;
        var title = BackupMessages.BackupsTitle.In(language);
        var latest = LocalBackups.Read(options).FirstOrDefault();
        var local = latest is null
            ? BackupMessages.BackupNone.In(language)
            : BackupMessages.BackupLatest.Format(language, latest.Name, latest.At.LocalDateTime);
        var (remote, lastRemote) = RemoteLine(options, config);
        return new BackupsStatus(
            title,
            [local, remote],
            latest?.Name,
            latest?.At,
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
    /// The sync's lines: the remote, the last sync, the commits not pushed and, while the sync is paused, what to do. Reads
    /// <c>sync.json</c> and the repository only: no network is touched.
    /// </summary>
    private static async Task<SyncStatus> SyncAsync(TacitOptions options, TacitConfig config, CancellationToken cancellationToken)
    {
        var language = config.Language;
        var title = SyncMessages.Title.In(language);
        if (config.Sync.Remote is not { } remote)
            return new SyncStatus(title, [SyncMessages.StatusOff.In(language)], null, null, null);

        SyncRecord? last = null;
        string lastLine;
        try
        {
            last = MemorySync.ReadLast(options);
            lastLine = last is null
                ? SyncMessages.StatusNever.In(language)
                : SyncMessages.StatusLast.Format(language, last.At.LocalDateTime, OutcomeText(language, last));
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            lastLine = SyncMessages.StatusUnreadable.Format(language, e.Message);
        }

        int? unpushed = null;
        string unpushedLine;
        try
        {
            unpushed = await MemorySync.UnpushedAsync(config, cancellationToken);
            unpushedLine = SyncMessages.StatusUnpushed.Format(language, unpushed);
        }
        catch (TacitException e) when (e.Error == TacitError.GitFailed)
        {
            unpushedLine = SyncMessages.StatusUnpushedUnknown.Format(language, e.Message);
        }

        List<string> lines = [SyncMessages.StatusRemote.Format(language, remote), lastLine, unpushedLine];
        if (last is { Paused: true })
            lines.Add((last.Outcome == SyncOutcome.Conflict ? SyncMessages.StatusResolve : SyncMessages.StatusChooseSide).In(language));
        return new SyncStatus(title, lines, remote, last, unpushed);
    }

    private static string OutcomeText(MemoryLanguage language, SyncRecord record) => record.Outcome switch
    {
        SyncOutcome.Synced => SyncMessages.OutcomeSynced.In(language),
        SyncOutcome.Failed => SyncMessages.OutcomeFailed.Format(language, record.Detail),
        SyncOutcome.HandEdits => SyncMessages.OutcomeHandEdits.Format(language, record.Detail),
        SyncOutcome.Conflict => SyncMessages.OutcomeConflict.Format(language, record.Detail),
        SyncOutcome.Unrelated => SyncMessages.OutcomeUnrelated.In(language),
        _ => SyncMessages.OutcomeRestorePaused.In(language),
    };

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
                lines.Add(StatusMessages.SecretNotConfigured.Format(language, owner));
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
        return new SecretsStatus(StatusMessages.SecretsTitle.In(language), lines, items);
    }

    private static SecretStatus SecretItem(string key, string variable, SecretValue? secret) => new(
        key,
        variable,
        secret?.Source,
        Overridden: secret is { Source: SecretSource.Environment, WrittenInConfig: true });

    private static string SecretLine(MemoryLanguage language, SecretStatus item) => item.Source switch
    {
        SecretSource.Environment when item.Overridden => StatusMessages.SecretFromEnvironmentOverridden.Format(language, item.Key, item.Variable),
        SecretSource.Environment => StatusMessages.SecretFromEnvironment.Format(language, item.Key, item.Variable),
        SecretSource.ConfigFile => StatusMessages.SecretFromConfig.Format(language, item.Key),
        _ => StatusMessages.SecretMissing.Format(language, item.Key, item.Variable),
    };

    private static ModelsStatus Models(MemoryLanguage language, IReadOnlyList<ModelStatus> models) => new(
        StatusMessages.ModelsTitle.In(language),
        models.Select(model => ModelLine(language, model)).ToList(),
        models);

    private static string ModelLine(MemoryLanguage language, ModelStatus model) => model.State switch
    {
        ModelState.CoolingDown => StatusMessages.ModelCoolingDown.Format(language, model.Role, model.CoolingSeconds),
        ModelState.Unconfigured => StatusMessages.ModelUnconfigured.Format(language, model.Role),
        _ => StatusMessages.ModelAvailable.Format(language, model.Role),
    };

    /// <summary>"距今 1 天" or "1 day ago", and so on for the other counts.</summary>
    private static string DaysAgo(MemoryLanguage language, int days) =>
        days == 1 ? StatusMessages.DayAgo.In(language) : StatusMessages.DaysAgo.Format(language, days);

    /// <summary>
    /// The logs column: where the files are, and how many warnings and errors today's file holds so far, or why it cannot be
    /// read. It needs only the home directory, so it is shown even when the service does not run or the config does not load.
    /// </summary>
    private static LogsStatus Logs(TacitOptions options, MemoryLanguage language)
    {
        var directory = ServiceLogFiles.DirectoryOf(options.TacitHome);
        var title = StatusMessages.LogsTitle.In(language);
        var where = StatusMessages.LogsDirectory.Format(language, directory);
        IReadOnlyList<ServiceLogEntry> today;
        try
        {
            today = ServiceLogReader.Read(options.TacitHome, options.Today) ?? [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new LogsStatus(title, [where, StatusMessages.LogsTodayUnreadable.Format(language, e.Message)], directory, null, null);
        }

        var warnings = today.Count(entry => entry.Level == ServiceLogLevel.Warning);
        var errors = today.Count(entry => entry.Level == ServiceLogLevel.Error);
        var counts = warnings + errors == 0
            ? StatusMessages.LogsTodayQuiet.In(language)
            : StatusMessages.LogsToday.Format(language, warnings, errors);
        return new LogsStatus(title, [where, counts], directory, warnings, errors);
    }

    /// <summary>The uptime in the memory language, with the units that are not zero: "2 小时 3 分钟 4 秒" or "2 h 3 min 4 s".</summary>
    private static string Uptime(MemoryLanguage language, TimeSpan uptime)
    {
        var parts = new List<string>();
        if (uptime.Days > 0)
            parts.Add(StatusMessages.UptimeDays.Format(language, uptime.Days));
        if (uptime.Hours > 0)
            parts.Add(StatusMessages.UptimeHours.Format(language, uptime.Hours));
        if (uptime.Minutes > 0)
            parts.Add(StatusMessages.UptimeMinutes.Format(language, uptime.Minutes));
        parts.Add(StatusMessages.UptimeSeconds.Format(language, uptime.Seconds));
        return string.Join(" ", parts);
    }
}
