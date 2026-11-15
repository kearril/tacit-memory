using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TacitMemory.Core.Backup;
using TacitMemory.Core.ClientRecords;
using TacitMemory.Core.Clients;
using TacitMemory.Core.Configuration;
using TacitMemory.Core.Consolidation;
using TacitMemory.Core.Decisions;
using TacitMemory.Core.Git;
using TacitMemory.Core.Index;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Logging;
using TacitMemory.Core.Maintenance;
using TacitMemory.Core.Models;
using TacitMemory.Core.Pages;
using TacitMemory.Core.Persona;
using TacitMemory.Core.Recent;
using TacitMemory.Core.Retrieval;
using TacitMemory.Core.Skills;
using TacitMemory.Core.Status;
using TacitMemory.Core.Workstreams;

namespace TacitMemory.Core;

/// <summary>
/// The running memory service: the loaded config, the memory tools (writes run one at a time), the page index, the
/// cached server instructions, a watcher on the memory repository that re-renders the resident blocks when
/// <c>persona.md</c>, <c>workstreams.md</c>, the activity log or a workstream folder changes and again at each local midnight,
/// syncs the index when page, activity or persona files change, and a watcher that reloads the prompt
/// documents in <c>prompts/</c>, which also regenerates the skills. Create it with <see cref="Tacit.OpenService"/>.
/// <para>
/// It has two layers. The shell lives as long as the process: the lock that orders the work, the start time, the extra
/// listener's outcome, the access control, the client records and the timers. The runtime is everything the config decides
/// (the index, the auxiliary models, the tools, the backups and the file watchers); <see cref="OpenRuntime"/> is the one path
/// that builds it, at the start and again on each <see cref="ReloadAsync"/>.
/// </para>
/// </summary>
public sealed class MemoryService : IDisposable
{
    /// <summary>How long file events must stay quiet before acting on them; editors save in several steps.</summary>
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);

    /// <summary>The category the service's events are logged under.</summary>
    internal const string LogCategory = "TacitMemory";

    private readonly TacitOptions _options;
    private readonly DateTimeOffset _startedAt;
    private AutomaticBackup _backup = null!;
    private ManualBackup _manual = null!;
    private CloudBackup _remote = null!;
    private readonly ServiceLog _log;
    private readonly ClientRecordStore _clients;
    private FileSystemWatcher _watcher = null!;
    private FileSystemWatcher? _promptsWatcher;
    private readonly Timer _renderTimer;
    private readonly Timer _promptsTimer;
    private readonly Timer _indexTimer;

    /// <summary>Fires at each local midnight: the resident block shows dates, so it is rendered again then. Replaced each time it fires.</summary>
    private ITimer? _dayTimer;
    private readonly Lock _indexTimerGate = new();
    private readonly Lock _renderGate = new();

    /// <summary>How many debounced retries a render gets when a file it replaces is held open; guarded by <c>_renderGate</c>.</summary>
    private const int RenderRetryLimit = 5;

    private int _renderRetries;

    /// <summary>How many debounced retries a prompt reload gets when a file it replaces is held open; guarded by <c>_renderGate</c>.</summary>
    private int _promptsRetries;

    private readonly Lock _instructionsGate = new();
    private readonly SemaphoreSlim _instructionsBuild = new(1, 1);
    private readonly MaintenanceGate _gate = new();
    private PageTools _pages = null!;
    private RecentTools _recent = null!;
    private PersonaTools _persona = null!;
    private AuxiliaryModels _models = null!;
    private MemoryIndex _index = null!;
    private VectorSync _vectors = null!;
    private JudgmentPool _judgments = null!;
    private ConsolidationServices _consolidation = null!;
    private RecallPipeline _pipeline = null!;
    private WorkstreamBrief _brief = null!;
    private RecallTool _recall = null!;
    private volatile ExtraListenerStatus? _extraListener;
    private PromptDocuments _prompts = null!;

    /// <summary>The refusal of each prompt document whose file the service does not run, keeping its previous text instead.</summary>
    private IReadOnlyDictionary<PromptName, string> _promptProblems = new Dictionary<PromptName, string>();
    private string? _instructions;
    private string? _instructionsStatus;
    private int _instructionsVersion;
    private volatile bool _disposed;

    /// <summary>Whether a runtime is open; false only after a reload could open none, which ends the service.</summary>
    private bool _runtimeOpen;

    /// <summary>Completes when a reload has failed with both the new config and the old one: the service can no longer run.</summary>
    private readonly TaskCompletionSource<Exception> _failed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal MemoryService(TacitOptions options, TacitConfig config, Action<string> notice, ILogger logger)
    {
        _options = options;
        _startedAt = options.Time.GetUtcNow();
        Config = config;
        _log = new ServiceLog(logger, config.Language, notice);
        Access = new AccessControl(options, config, logger);
        SkillSet.Write(options.TacitHome, config.Language, config.Prompts.Skill);
        StartupRender = Binder.RenderAll(options, config, config.Prompts);
        _log.Record(LogLevel.Information, LogMessages.Started, ProductVersion.Current, config.Port, config.MemoryPath);
        foreach (var render in StartupRender)
            _log.Record(LevelOf(render), render.Message);
        _clients = new ClientRecordStore(ClientRecordStore.PathFor(options), options.Time, _log);

        _renderTimer = new Timer(_ => RenderNow());
        _promptsTimer = new Timer(_ => ReloadPrompts());
        _indexTimer = new Timer(_ => SyncIndexNow());
        _dayTimer = ArmDayTimer();
        OpenRuntime(config);
    }

    /// <summary>
    /// Builds the runtime from <paramref name="config"/>: the auxiliary models, the index (synced with the files), the tools,
    /// the backups and the two file watchers. The parts that can fail are built before any is put in place, so a failure
    /// leaves nothing half open: what was built is let go, and the exception is raised.
    /// </summary>
    private void OpenRuntime(TacitConfig config)
    {
        AuxiliaryModels? models = null;
        MemoryIndex? index = null;
        FileSystemWatcher? watcher = null;
        try
        {
            OnOpening?.Invoke(config);
            models = new AuxiliaryModels(config, _options.Time, _log);
            index = new MemoryIndex(
                config.MemoryPath, config.Models.Embedding?.Model, config.Language,
                () => _options.Today, () => new GitCli(config.MemoryPath, config.Language).LastCommitDays(PageId.Folder));
            index.SyncAll();
            watcher = new FileSystemWatcher(config.MemoryPath) { IncludeSubdirectories = true, InternalBufferSize = 64 * 1024 };
        }
        catch
        {
            watcher?.Dispose();
            index?.Dispose();
            models?.Dispose();
            throw;
        }

        Config = config;
        lock (_instructionsGate)
        {
            // A config that loads has every prompt document as its file holds it.
            _prompts = config.Prompts;
            _promptProblems = new Dictionary<PromptName, string>();
        }
        _models = models;
        if (_models.Unconfigured is { Count: > 0 } unconfigured)
            _log.Record(LogLevel.Information, LogMessages.ModelsNotConfigured, string.Join(", ", unconfigured));
        _index = index;
        _vectors = new VectorSync(_index, _models.Embedding);
        _judgments = new JudgmentPool(_index, _models.Decision, _options.Time, config.Consolidation.MaxConcurrency);
        _consolidation = new ConsolidationServices(_index, _models.Embedding, _vectors, _judgments);
        _vectors.FillInBackground();
        _pages = new PageTools(
            _options, config, _log, new DuplicatePageCheck(config, _index, _models.Embedding, _vectors, _models.Decision));
        _recent = new RecentTools(_options, config, _log, _models.Decision);
        _persona = new PersonaTools(
            _options, config, _log,
            new SamePreferenceCheck(config, _index, _models.Embedding, _vectors, _models.Decision),
            new RuleConflictCheck(config, _index, _models.Embedding, _vectors, _models.Decision));
        _pipeline = new RecallPipeline(config, _index, _models.Embedding, _models.Reranker, _vectors);
        _brief = new WorkstreamBrief(config, _index);
        _recall = new RecallTool(config, _index, _pages, _pipeline, _brief);
        _backup = new AutomaticBackup(_options, config, _log, _gate);
        _manual = new ManualBackup(_options, config, _log);
        _remote = new CloudBackup(_options, config, _log);

        _watcher = watcher;
        _watcher.Changed += OnChange;
        _watcher.Created += OnChange;
        _watcher.Deleted += OnChange;
        _watcher.Renamed += OnChange;
        _watcher.Error += OnOverflow;
        _watcher.EnableRaisingEvents = true;
        WatchPrompts();
        _runtimeOpen = true;
        InvalidateInstructions();
    }

    /// <summary>Lets go of the runtime's files and models: the watchers, the index and the model clients. Its background work has already settled.</summary>
    private void CloseRuntime()
    {
        if (!_runtimeOpen)
            return;
        _runtimeOpen = false;
        _watcher.Dispose();
        lock (_renderGate)
            _promptsWatcher?.Dispose();
        _index.Dispose();
        _models.Dispose();
    }

    /// <summary>Called with the config at the start of each runtime open; a test throws from it to make an open fail.</summary>
    internal Action<TacitConfig>? OnOpening { get; set; }

    /// <summary>
    /// Opens the runtime again with <c>config.yaml</c> as it is now, as a maintenance operation: the reads and writes under
    /// way finish first, and those that arrive meanwhile wait and then run with the new config. Five settings do not change
    /// with a reload and keep their running values until the service restarts: <c>server.port</c>, <c>server.host</c>,
    /// <c>memory.path</c>, <c>language</c> and <c>limits.page.minAliases</c>.
    /// </summary>
    /// <exception cref="TacitException">
    /// The config does not load, and nothing changed; or the runtime could not be opened with it, and the service runs on
    /// with the config it had (<see cref="TacitError.ReloadFailed"/>). When even that fails, <see cref="Failed"/> completes.
    /// </exception>
    /// <returns>The settings that wait for a restart and differ in the file from what the service runs.</returns>
    public Task<IReadOnlyList<RestartChange>> ReloadAsync(CancellationToken cancellationToken = default) =>
        RunMaintenanceAsync("reload", _ => Task.FromResult(ReloadCore()), cancellationToken);

    private IReadOnlyList<RestartChange> ReloadCore()
    {
        var running = Config;
        var loaded = ConfigLoader.Read(_options);
        var next = loaded with
        {
            Port = running.Port,
            ExtraAddress = running.ExtraAddress,
            MemoryPath = running.MemoryPath,
            Language = running.Language,
            Limits = loaded.Limits with { Page = running.Limits.Page },
            Prompts = loaded.Language == running.Language ? loaded.Prompts : PromptFiles.Load(_options.TacitHome, running.Language),
        };

        CloseRuntime();
        try
        {
            OpenRuntime(next);
        }
        catch (Exception failure)
        {
            try
            {
                OpenRuntime(running);
            }
            catch (Exception again)
            {
                _log.Record(LogLevel.Error, LogMessages.ReloadFatal, failure.Message, again.Message);
                _failed.TrySetResult(again);
                throw new TacitException(TacitError.ReloadFailed, MaintenanceMessages.ReloadFatal.Format(running.Language, again.Message), again);
            }

            _log.Record(LogLevel.Warning, LogMessages.ReloadFailed, failure.Message);
            throw new TacitException(TacitError.ReloadFailed, MaintenanceMessages.ReloadFailed.Format(running.Language, failure.Message), failure);
        }

        // Under the render lock, as a binding and the timed render are: they write the same context files.
        lock (_renderGate)
        {
            SkillSet.Write(_options.TacitHome, next.Language, next.Prompts.Skill);
            foreach (var render in Binder.RenderAll(_options, next, next.Prompts))
                _log.Record(LevelOf(render), render.Message);
        }
        return RestartChange.Between(running, loaded);
    }

    /// <summary>
    /// Completes, with the reason, when a reload could open the runtime neither with the new config nor with the old one. The
    /// service cannot go on; whoever runs it stops it, and the next start reports the cause.
    /// </summary>
    public Task<Exception> Failed => _failed.Task;

    /// <summary>The config the service runs with: the one it started with, or the one the last reload put in place.</summary>
    public TacitConfig Config { get; private set; }

    /// <summary>The access password and its sessions, as the service reads them.</summary>
    public AccessControl Access { get; }

    /// <summary>The version of TacitMemory the service runs.</summary>
    public string Version => ProductVersion.Current;

    /// <summary>
    /// Refuses to start a service that listens beside loopback without an access password: <c>server.host</c> names an
    /// address that is not loopback, and no password is set. Called by the host before it binds anything.
    /// </summary>
    /// <exception cref="TacitException">A <c>server.host</c> that names an extra listener, with no access password set.</exception>
    public void CheckExtraListenerAllowed()
    {
        if (Config.ExtraAddress is { } extra && !Access.PasswordSet)
            throw new TacitException(TacitError.ConfigInvalid, AccessMessages.PasswordRequiredForExtraAddress.Format(Config.Language, extra));
    }

    /// <summary>What happened to each bound context file when the service started.</summary>
    public IReadOnlyList<BlockRender> StartupRender { get; }

    /// <summary>
    /// The server instructions, generated on first use and kept until something they are built from may have changed: a
    /// prompt document, the persona or its candidates, a page or activity file, <c>workstreams.md</c> (which the consolidation
    /// reminder reads), the memory repository's commits, or the status (unavailable models, skipped files, the date).
    /// Builds run one at a time.
    /// </summary>
    public async Task<string> ServerInstructionsAsync(CancellationToken cancellationToken = default)
    {
        using var hold = await _gate.ReadAsync(cancellationToken);
        await _instructionsBuild.WaitAsync(cancellationToken);
        try
        {
            var unavailable = _models.Unavailable;
            var skipped = _index.SkippedFiles;
            var status = string.Join("|", unavailable) + "\n" + string.Join("|", skipped) + "\n" + _options.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            int version;
            PromptDocuments prompts;
            lock (_instructionsGate)
            {
                if (_instructions is not null && _instructionsStatus == status)
                    return _instructions;
                version = _instructionsVersion;
                prompts = _prompts;
            }

            var recent = RecentSummary.Read(Config.MemoryPath, _options.Today);
            var consolidation = await ConsolidationReminder.RenderAsync(Config, _options.Today, cancellationToken);
            var text = ServerInstructionText.Assemble(Config.Language, prompts, unavailable, skipped, recent, consolidation).Text;

            lock (_instructionsGate)
            {
                // A change that arrived while the text was being built makes it stale, so it is not kept.
                if (version == _instructionsVersion)
                {
                    _instructions = text;
                    _instructionsStatus = status;
                }
            }
            return text;
        }
        finally
        {
            _instructionsBuild.Release();
        }
    }

    /// <summary>
    /// The text of the consolidation report resource, built from the repository as it is now: the repository's path, today's
    /// date and the consolidation report. Reads only. Cancelling it abandons the report; nothing is written either way.
    /// </summary>
    public async Task<string> ConsolidationReportAsync(CancellationToken cancellationToken = default)
    {
        using var hold = await _gate.ReadAsync(cancellationToken);
        return await ConsolidationResource.RenderAsync(_options, Config, _consolidation, cancellationToken);
    }

    /// <summary>The line to show once the MCP endpoint at <paramref name="endpoint"/> accepts connections.</summary>
    public string ListeningMessage(Uri endpoint) => Messages.ServiceListening.Format(Config.Language, endpoint);

    /// <summary>The extra listener <c>server.host</c> names, as the host bound it when the service started; <c>null</c> when none is named.</summary>
    public ExtraListenerStatus? ExtraListener => _extraListener;

    /// <summary>The text the web interface answers with when its files were not built, in the memory language.</summary>
    public string WebNotBuiltMessage() => ManagementApiMessages.WebNotBuilt.In(Config.Language);

    /// <summary>The line to show for a bound extra listener at <paramref name="address"/>, in the memory language.</summary>
    public string ExtraListeningMessage(Uri address) =>
        ManagementApiMessages.ExtraListenerAddress.Format(Config.Language, ManagementApiMessages.ExtraListenerName(address, Config.Language));

    /// <summary>
    /// Records the outcome of the host's attempt to bind the address <c>server.host</c> names, at <paramref name="port"/>. A
    /// failure is logged as a warning, shown to the user, and kept for the status report; the reason is the system's error
    /// put in words where it can be, such as a port that is taken.
    /// </summary>
    /// <param name="address">The address that was to be listened on besides loopback.</param>
    /// <param name="port">The port of the service.</param>
    /// <param name="failure">The error the bind failed with; <c>null</c> when the address answers.</param>
    public void RecordExtraListener(IPAddress address, int port, Exception? failure)
    {
        var listener = new ExtraListenerStatus(ExtraListenerStatus.AddressOf(address, port), failure is null ? null : FailureReason(address, port, failure));
        _extraListener = listener;
        if (listener.FailureReason is null)
            _log.Record(LogLevel.Information, LogMessages.ExtraListenerBound, ManagementApiMessages.ExtraListenerName(listener.Address, Config.Language));
        else
            _log.Notify(LogLevel.Warning, LogMessages.ExtraListenerFailed, ManagementApiMessages.ExtraListenerName(listener.Address, Config.Language), listener.FailureReason);
    }

    private string FailureReason(IPAddress address, int port, Exception failure) => failure is SocketException socket
        ? socket.SocketErrorCode switch
        {
            SocketError.AddressAlreadyInUse => ManagementApiMessages.ExtraPortInUse.Format(Config.Language, port),
            SocketError.AddressNotAvailable => ManagementApiMessages.ExtraAddressUnavailable.Format(Config.Language, address),
            _ => socket.Message,
        }
        : failure.Message;

    /// <summary>The line to show when <see cref="TacitConfig.Port"/> is taken by another program.</summary>
    public string PortInUseMessage() =>
        Messages.PortInUse.Format(Config.Language, Config.Port, Path.Combine(_options.TacitHome, "config.yaml"));

    /// <summary>
    /// The status report of this running service, reachable at <paramref name="endpoint"/>: its version, uptime, the
    /// auxiliary models' states and the shared columns. Calls no model and reads no network.
    /// </summary>
    public Task<StatusReport> StatusAsync(Uri endpoint, CancellationToken cancellationToken = default)
    {
        var uptime = _options.Time.GetUtcNow() - _startedAt;
        return StatusReporter.RunningAsync(
            _options, Config, endpoint, ProductVersion.Current, uptime < TimeSpan.Zero ? TimeSpan.Zero : uptime, _models.Statuses,
            _extraListener, PromptProblems(), cancellationToken);
    }

    /// <summary>
    /// The activity log's counts per day over the last 53 weeks, for the dashboard's heatmap, with the consolidation days in
    /// that window. Reads files and the repository's history only, so it does not wait for a maintenance operation.
    /// </summary>
    public Task<ActivityReport> ActivityAsync(CancellationToken cancellationToken = default) =>
        ActivityCounts.ReadAsync(Config, _options.Today, cancellationToken);

    /// <summary>The days the service log has a file for, newest first, and today. Reads the directory only.</summary>
    public ServiceLogDays LogDays() => new(_options.Today, ServiceLogReader.Days(_options.TacitHome));

    /// <summary>
    /// The events of one day's service log, oldest first, read from the file the service may still be writing to. Today
    /// without a file has no events yet. With <paramref name="after"/>, only the events past that many are returned, so a
    /// reader that follows the log asks for what is new; the events of a file never move, as it is only appended to.
    /// </summary>
    /// <param name="date">The day as <c>yyyy-MM-dd</c>.</param>
    /// <param name="after">How many events the caller already has, as written in the request; none means 0.</param>
    /// <exception cref="TacitException">
    /// <see cref="TacitError.RequestInvalid"/> when <paramref name="date"/> is not a date or <paramref name="after"/> not a
    /// count; <see cref="TacitError.LogDateNotFound"/> for another day without a file.
    /// </exception>
    public ServiceLogDay ServiceLog(string date, string? after = null)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            throw new TacitException(TacitError.RequestInvalid, ManagementApiMessages.LogDateInvalid.Format(Config.Language, date));
        var skip = 0;
        if (after is not null && !int.TryParse(after, NumberStyles.None, CultureInfo.InvariantCulture, out skip))
            throw new TacitException(TacitError.RequestInvalid, ManagementApiMessages.LogAfterInvalid.Format(Config.Language, after));
        var today = _options.Today;
        var entries = ServiceLogReader.Read(_options.TacitHome, day)
            ?? (day == today ? [] : throw new TacitException(TacitError.LogDateNotFound, ManagementApiMessages.LogDateNotFound.Format(Config.Language, date)));
        return new ServiceLogDay(day, today, [.. entries.Skip(skip)], entries.Count);
    }

    /// <summary>
    /// The local backups, newest first, whether the cloud backup is configured, and its last attempt. Reads the backup
    /// folder and <c>remote.json</c> only: no network is touched, and it does not wait for a maintenance operation.
    /// </summary>
    public BackupList Backups() => LocalBackups.List(_options, Config);

    /// <summary>
    /// The bindings and what is wrong with each, as the status report's bindings column has them, with the MCP server a bound
    /// client registers.
    /// </summary>
    /// <exception cref="TacitException"><c>bindings.json</c> cannot be read.</exception>
    public BindingList Bindings() => new(BindingChecks.Produce(_options, Config.Language).Items, McpServer.On(Config.Port));

    /// <summary>
    /// Binds a client by the rules of <c>tacit connect</c>; a refusal changes no file. Holds the render lock, so the service
    /// does not render the resident blocks while the binding is written.
    /// </summary>
    /// <exception cref="TacitException">A refusal, with the reason <c>tacit connect</c> gives.</exception>
    public BindingDone Connect(ConnectRequest request)
    {
        lock (_renderGate)
            return BindingDone.From(ClientName.Normalize(request.Client), Binder.Connect(_options, request));
    }

    /// <summary>Unbinds a client by the rules of <c>tacit disconnect</c>, under the render lock as <see cref="Connect"/>.</summary>
    /// <exception cref="TacitException">No binding has that name.</exception>
    public BindingDone Disconnect(string client)
    {
        lock (_renderGate)
            return BindingDone.From(ClientName.Normalize(client), Binder.Disconnect(_options, client));
    }

    /// <summary>
    /// Probes the three auxiliary models as the saved <c>config.yaml</c> configures them, which is what <c>tacit probe</c>
    /// reads, which is also what the service runs once a save has reloaded it. An unconfigured model is reported and skipped.
    /// No model's health changes, and it does not wait for a maintenance operation.
    /// </summary>
    /// <exception cref="TacitException">The saved configuration cannot be loaded.</exception>
    public async Task<ProbeReport> ProbeAsync(CancellationToken cancellationToken = default)
    {
        var config = ConfigLoader.Read(_options);
        var results = await ModelProbe.RunAsync(config, [.. ModelRoles.All], _options.Time, cancellationToken);
        return new ProbeReport(results, ProbeLines.Build(config.Language, results, explicitTargets: false));
    }

    /// <summary>
    /// The four prompt documents for the prompts page: each file as it is now, or the built-in default where there is no
    /// file. Reads the files only, so it does not wait for a maintenance operation.
    /// </summary>
    public PromptList Prompts() => new([.. Enum.GetValues<PromptName>().Select(PromptView)]);

    /// <summary>
    /// Checks <paramref name="text"/> as the prompt document <paramref name="name"/> without saving it: its measure beside
    /// the other documents as the service runs them, and every refusal a save of it would give.
    /// </summary>
    public PromptCheck CheckPrompt(PromptName name, string text)
    {
        var running = RunningPrompts();
        return new PromptCheck(
            PromptFiles.Measure(Config.Language, running, name, text),
            PromptFiles.Check(_options.TacitHome, Config.Language, running, name, text));
    }

    /// <summary>The prompt documents the service runs with now.</summary>
    private PromptDocuments RunningPrompts()
    {
        lock (_instructionsGate)
            return _prompts;
    }

    /// <summary>
    /// The prompt document <paramref name="name"/> as it is now, measured beside the documents the service runs with, and
    /// why the service keeps its previous text, if it does.
    /// </summary>
    private PromptDocumentView PromptView(PromptName name)
    {
        PromptDocuments running;
        string? problem;
        lock (_instructionsGate)
        {
            running = _prompts;
            problem = _promptProblems.GetValueOrDefault(name);
        }
        return PromptFiles.View(_options.TacitHome, Config.Language, running, name, problem);
    }

    /// <summary>Each prompt document whose file the service does not run, in the order of <see cref="PromptName"/>, with the refusal.</summary>
    private IReadOnlyList<PromptProblem> PromptProblems()
    {
        lock (_instructionsGate)
            return [.. Enum.GetValues<PromptName>().Where(_promptProblems.ContainsKey).Select(name => new PromptProblem(name, _promptProblems[name]))];
    }

    /// <summary>The prompt document named as the management API writes it: <c>instructions</c>, <c>rules</c>, <c>resident</c> or <c>skill</c>.</summary>
    /// <exception cref="TacitException"><see cref="TacitError.PromptUnknown"/> for any other name.</exception>
    public PromptName PromptNamed(string name)
    {
        foreach (var candidate in Enum.GetValues<PromptName>())
        {
            if (string.Equals(candidate.ToString(), name, StringComparison.OrdinalIgnoreCase) && name == name.ToLowerInvariant())
                return candidate;
        }
        throw new TacitException(TacitError.PromptUnknown, ManagementApiMessages.PromptUnknown.Format(Config.Language, name));
    }

    /// <summary>
    /// Saves <paramref name="text"/> as the prompt document <paramref name="name"/>, with LF line breaks, and applies it at
    /// once: the skills are written again, the resident blocks rendered again and the next server instructions built from it.
    /// <paramref name="hash"/> names the file the caller read the text from, <c>null</c> when there was none; a file changed
    /// since is not overwritten. The text is checked first by the rules a load applies, so a refused text changes no file.
    /// </summary>
    /// <exception cref="TacitException">
    /// <see cref="TacitError.PromptChanged"/>, or <see cref="TacitError.PromptRejected"/> with every refusal; in each case
    /// the file is as it was.
    /// </exception>
    public async Task<PromptSaved> SavePromptAsync(PromptName name, string? hash, string text, CancellationToken cancellationToken = default)
    {
        var hold = await _gate.WriteAsync(cancellationToken);
        try
        {
            var path = PromptFiles.PathOf(_options.TacitHome, name);
            var current = PromptView(name);
            if (current.Hash != hash)
                throw new TacitException(TacitError.PromptChanged, ManagementApiMessages.PromptChanged.Format(Config.Language, Path.GetFileName(path)));

            var content = text.ReplaceLineEndings("\n");
            RefuseUnless(name, content);
            Directory.CreateDirectory(PromptFiles.Folder(_options.TacitHome));
            ConfigWriter.Replace(path, content);
            return Applied(name);
        }
        finally
        {
            hold.Dispose();
        }
    }

    /// <summary>
    /// Returns the prompt document <paramref name="name"/> to its built-in default by deleting its file, and applies that at
    /// once as a save does. Without a file it changes nothing. It is refused when the built-in text would not fit beside
    /// the other documents, as when a long <c>instructions.md</c> leaves no room for the built-in recording criteria.
    /// </summary>
    /// <exception cref="TacitException"><see cref="TacitError.PromptRejected"/>; the file is kept.</exception>
    public async Task<PromptSaved> RestorePromptDefaultAsync(PromptName name, CancellationToken cancellationToken = default)
    {
        var hold = await _gate.WriteAsync(cancellationToken);
        try
        {
            var path = PromptFiles.PathOf(_options.TacitHome, name);
            if (!File.Exists(path))
                return new PromptSaved(PromptView(name), []);

            RefuseUnless(name, PromptFiles.DefaultText(name, Config.Language));
            File.Delete(path);
            return Applied(name);
        }
        finally
        {
            hold.Dispose();
        }
    }

    /// <summary>
    /// Applies the prompt documents now, then gives the document <paramref name="name"/> as the service runs it: the view is
    /// read after the apply, as its measure and its problem depend on what the service runs.
    /// </summary>
    private PromptSaved Applied(PromptName name)
    {
        var notices = ApplyPromptsNow(name);
        return new PromptSaved(PromptView(name), notices);
    }

    /// <summary>Refuses <paramref name="text"/> as the document <paramref name="name"/> when it breaks a rule, with every refusal.</summary>
    private void RefuseUnless(PromptName name, string text)
    {
        var refusals = PromptFiles.Check(_options.TacitHome, Config.Language, RunningPrompts(), name, text);
        if (refusals.Count == 0)
            return;
        var file = Path.GetFileName(PromptFiles.PathOf(_options.TacitHome, name));
        throw new TacitException(TacitError.PromptRejected, string.Join(" ", refusals))
        {
            Problems = [.. refusals.Select(refusal => new ConfigProblem(file, refusal))],
        };
    }

    /// <summary>
    /// Applies the prompt documents as a change to <c>prompts/</c> does, without waiting for the watcher; after a change to
    /// <c>resident.md</c> it renders the resident blocks now as well. Returns what went wrong in the render.
    /// </summary>
    private IReadOnlyList<string> ApplyPromptsNow(PromptName name)
    {
        ApplyPrompts();
        if (name != PromptName.Resident)
            return [];

        var notices = new List<string>();
        lock (_renderGate)
        {
            // Applying a changed resident.md schedules a render, which this one makes needless.
            _renderTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            try
            {
                foreach (var render in Binder.RenderAll(_options, Config, _prompts))
                {
                    _log.Record(LevelOf(render), render.Message);
                    if (LevelOf(render) != LogLevel.Information)
                        notices.Add(render.Message);
                }
            }
            catch (Exception e) when (e is TacitException or IOException or UnauthorizedAccessException)
            {
                _log.Record(LogLevel.Warning, Messages.RenderFailed, e.Message);
                notices.Add(Messages.RenderFailed.Format(Config.Language, e.Message));
            }
        }
        return notices;
    }

    /// <summary>
    /// <c>config.yaml</c> for the settings page: as the file holds it, as the service runs it and at its defaults, with what
    /// each key accepts and the hash a save must name. Secrets carry no value. Reads the file only, so it does not wait for a
    /// maintenance operation.
    /// </summary>
    /// <exception cref="TacitException">The file does not load, as when it was broken by hand after the service started.</exception>
    public ConfigView ConfigView()
    {
        var path = ConfigFilePath;
        var text = File.ReadAllText(path);
        var loaded = ConfigLoader.Read(_options, path, text);
        return new ConfigView(
            ContentHash.Of(text),
            WithoutSecrets(ConfigFile.From(loaded)),
            WithoutSecrets(ConfigFile.From(Config)),
            ConfigSchema.Defaults(_options),
            ConfigSchema.Keys,
            ConfigSchema.SecretStates(loaded, _options));
    }

    /// <summary>
    /// The value of the secret at <paramref name="path"/> as the service would use it with <c>config.yaml</c> as it is now:
    /// the environment variable's, else the file's.
    /// </summary>
    /// <exception cref="TacitException"><paramref name="path"/> is not a secret (<see cref="TacitError.SecretUnknown"/>).</exception>
    public SecretShown Secret(string path) =>
        new(ConfigSchema.SecretValue(ConfigLoader.Read(_options, ConfigFilePath, File.ReadAllText(ConfigFilePath)), _options, path));

    /// <summary>
    /// Saves <paramref name="file"/> as <c>config.yaml</c> and reloads, as one maintenance operation. <paramref name="hash"/>
    /// names the content the caller read the settings from; a file changed since is not overwritten. The secrets stay as the
    /// file holds them. When the reload cannot open the new config, the file goes back to what it was.
    /// </summary>
    /// <exception cref="TacitException">
    /// <see cref="TacitError.ConfigChanged"/>, <see cref="TacitError.ConfigRejected"/> with every problem, or
    /// <see cref="TacitError.ReloadFailed"/>; in each case <c>config.yaml</c> is as it was.
    /// </exception>
    public Task<ConfigSaved> SaveConfigAsync(
        ConfigFile file, string hash, IReadOnlyDictionary<string, string?>? secrets = null, CancellationToken cancellationToken = default) =>
        RunMaintenanceAsync("config", _ => Task.FromResult(SaveConfigCore(file, hash, secrets ?? new Dictionary<string, string?>())), cancellationToken);

    private ConfigSaved SaveConfigCore(ConfigFile file, string hash, IReadOnlyDictionary<string, string?> secrets)
    {
        var path = ConfigFilePath;
        var current = File.ReadAllText(path);
        if (ContentHash.Of(current) != hash)
            throw new TacitException(TacitError.ConfigChanged, ManagementApiMessages.ConfigChanged.In(Config.Language));

        var held = ConfigLoader.Read(_options, path, current);
        var unknown = secrets.Keys.Where(key => ConfigSchema.Secrets.All(secret => secret.Path != key)).ToList();
        if (unknown.Count > 0)
        {
            var refused = unknown.Select(key => new ConfigProblem(key, ManagementApiMessages.SecretNotSecret.In(Config.Language))).ToList();
            throw new TacitException(TacitError.ConfigRejected, string.Join(" ", refused.Select(problem => problem.Message))) { Problems = refused };
        }

        var content = ConfigWriter.Render(WithSecrets(WithSecretsOf(file, ConfigFile.From(held)), secrets), _options);
        TacitConfig saved;
        try
        {
            saved = ConfigLoader.Read(_options, path, content);
        }
        catch (TacitException e) when (e.Error == TacitError.ConfigInvalid)
        {
            throw new TacitException(TacitError.ConfigRejected, e.Message, e) { Problems = e.Problems };
        }

        var problems = new List<ConfigProblem>();
        if (saved.MemoryPath != held.MemoryPath)
            problems.Add(new ConfigProblem("memory.path", ManagementApiMessages.ConfigReadOnly.In(Config.Language)));
        if (saved.Language != held.Language)
            problems.Add(new ConfigProblem("language", ManagementApiMessages.ConfigReadOnly.In(Config.Language)));
        if (saved.ExtraAddress is not null && !Access.PasswordSet)
            problems.Add(new ConfigProblem("server.host", ManagementApiMessages.ConfigExtraNeedsPassword.In(Config.Language)));
        if (problems.Count > 0)
            throw new TacitException(TacitError.ConfigRejected, string.Join(" ", problems.Select(problem => problem.Message))) { Problems = problems };

        ConfigWriter.Replace(path, content);
        IReadOnlyList<RestartChange> restart;
        try
        {
            restart = ReloadCore();
        }
        catch
        {
            ConfigWriter.Replace(path, current);
            throw;
        }

        return new ConfigSaved(ContentHash.Of(content), restart);
    }

    private string ConfigFilePath => Path.Combine(_options.TacitHome, "config.yaml");

    /// <summary>The hash that names one content of <c>config.yaml</c>.</summary>
    /// <summary>The settings with every secret left out, as they leave the service.</summary>
    private static ConfigFile WithoutSecrets(ConfigFile file) => file with
    {
        Models = new ModelsFile(
            file.Models.Embedding is { } embedding ? embedding with { ApiKey = null } : null,
            file.Models.Reranker is { } reranker ? reranker with { ApiKey = null } : null,
            file.Models.Decision is { } decision ? decision with { ApiKey = null } : null),
        Backup = file.Backup with { WebDav = file.Backup.WebDav is { } webdav ? webdav with { Password = null } : null },
    };

    /// <summary>
    /// <paramref name="file"/> with each secret <paramref name="secrets"/> names set to its value, or removed where the value
    /// is <c>null</c>. A secret whose model or cloud backup <paramref name="file"/> does not configure is left out with it.
    /// </summary>
    private static ConfigFile WithSecrets(ConfigFile file, IReadOnlyDictionary<string, string?> secrets)
    {
        ModelFile? Model(ModelFile? model, string path) =>
            model is not null && secrets.TryGetValue(path, out var key) ? model with { ApiKey = key } : model;

        var webdav = file.Backup.WebDav;
        return file with
        {
            Models = new ModelsFile(
                Model(file.Models.Embedding, "models.embedding.apiKey"),
                Model(file.Models.Reranker, "models.reranker.apiKey"),
                Model(file.Models.Decision, "models.decision.apiKey")),
            Backup = file.Backup with
            {
                WebDav = webdav is not null && secrets.TryGetValue("backup.webdav.password", out var password)
                    ? webdav with { Password = password }
                    : webdav,
            },
        };
    }

    /// <summary><paramref name="file"/> with each secret as <paramref name="held"/> holds it, for each model and cloud backup both have.</summary>
    private static ConfigFile WithSecretsOf(ConfigFile file, ConfigFile held) => file with
    {
        Models = new ModelsFile(
            file.Models.Embedding is { } embedding ? embedding with { ApiKey = held.Models.Embedding?.ApiKey } : null,
            file.Models.Reranker is { } reranker ? reranker with { ApiKey = held.Models.Reranker?.ApiKey } : null,
            file.Models.Decision is { } decision ? decision with { ApiKey = held.Models.Decision?.ApiKey } : null),
        Backup = file.Backup with
        {
            WebDav = file.Backup.WebDav is { } webdav ? webdav with { Password = held.Backup.WebDav?.Password } : null,
        },
    };

    /// <summary>The refusal for a request body the management API cannot read, in the memory language; <paramref name="jsonPath"/> is the member at fault, when the parser names one.</summary>
    public TacitException InvalidRequestBody(string? jsonPath)
    {
        var field = jsonPath?.TrimStart('$').TrimStart('.');
        return new TacitException(
            TacitError.RequestInvalid,
            string.IsNullOrEmpty(field)
                ? ManagementApiMessages.ApiBodyInvalid.In(Config.Language)
                : ManagementApiMessages.ApiBodyInvalidField.Format(Config.Language, field));
    }

    /// <summary>The refusal for a file or folder the operation could not use; the message names the reason the system gave.</summary>
    public TacitException FileFailure(Exception cause) =>
        new(TacitError.FileSystemFailed, ManagementApiMessages.ApiFileFailed.Format(Config.Language, cause.Message), cause);

    /// <summary>The refusal for a failure no operation expected: logged with its kind and message, answered without them.</summary>
    public TacitException UnexpectedFailure(Exception cause)
    {
        _log.Record(LogLevel.Error, LogMessages.UnexpectedFailure, cause.GetType().Name, cause.Message);
        return new TacitException(TacitError.InternalFailure, ManagementApiMessages.ApiInternalFailure.In(Config.Language), cause);
    }

    /// <summary>The message for a request the host refuses (the management API's guards, its connection rule, or the MCP endpoint's), in the memory language.</summary>
    public string RefusalMessage(TacitError error) => error switch
    {
        TacitError.ApiOriginRefused => ManagementApiMessages.ApiOriginRefused.Format(Config.Language),
        TacitError.ApiHostRefused => ManagementApiMessages.ApiHostRefused.Format(Config.Language),
        TacitError.ApiContentTypeRefused => ManagementApiMessages.ApiContentTypeRefused.Format(Config.Language),
        TacitError.ApiUnauthorized => ManagementApiMessages.ApiUnauthorized.Format(Config.Language),
        TacitError.McpRemoteRefused => ManagementApiMessages.McpRemoteRefused.Format(Config.Language),
        TacitError.RemotePathRefused => ManagementApiMessages.RemotePathRefused.Format(Config.Language),
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, "Not a host refusal."),
    };

    /// <summary>
    /// Makes a backup now. A local one waits in the write queue like a write does, so the archive holds the repository as
    /// the last write left it, and it is never pruned. A cloud one packs the same way under the write gate, then uploads
    /// outside it and replaces the remote backup.
    /// </summary>
    /// <param name="target">Where the backup goes.</param>
    /// <param name="directory">The absolute directory to write a local backup into, created when missing; <c>null</c> means <c>TACIT_HOME/backups/</c>. Not allowed with the cloud target.</param>
    public async Task<BackupResult> BackupAsync(BackupTarget target, string? directory = null, CancellationToken cancellationToken = default)
    {
        if (directory is not null && !Path.IsPathFullyQualified(directory))
            throw new TacitException(TacitError.RequestInvalid, BackupMessages.BackupDirectoryNotAbsolute.Format(Config.Language, directory));
        if (target == BackupTarget.Cloud)
        {
            if (directory is not null)
                throw new TacitException(TacitError.RequestInvalid, BackupMessages.BackupCloudWithDirectory.In(Config.Language));
            return await _remote.RunAsync(_gate, cancellationToken);
        }

        var hold = await _gate.WriteAsync(cancellationToken);
        try
        {
            return await _manual.CreateAsync(directory, cancellationToken);
        }
        finally
        {
            hold.Dispose();
        }
    }

    /// <summary>Completes when the backup started by the latest successful write has finished. Tests await it.</summary>
    internal Task BackupCompletion => _backup.Completion;

    /// <summary>Completes when the vectors being filled in the background are stored. Tests await it.</summary>
    internal Task VectorCompletion => _vectors.Background;

    /// <summary>Completes when the consolidation judgments started so far have finished and their answers are cached. Tests await it.</summary>
    internal Task JudgmentCompletion => _judgments.Completion;

    /// <summary>
    /// Records what a client declared on a request: its name, version, protocol revision, capabilities and extensions. It only
    /// observes; no behaviour depends on the record. It takes no part in the memory's write lock, and a file that cannot be
    /// written is logged without failing the call.
    /// </summary>
    public void RecordClient(ClientObservation observation) => _clients.Observe(observation);

    /// <summary>The route of the recall trace, which reads like a GET though it is a POST.</summary>
    public const string RecallTracePath = "/api/recall/trace";

    /// <summary>
    /// Writes the management API's event for one call: its method, route, status code and time taken. A successful read is not
    /// written: a GET, or a recall trace, changed nothing, and the Web UI's polling or a user trying query after query would
    /// otherwise fill the log. The event never holds a request body, so a trace's query is never written.
    /// </summary>
    public void RecordApiCall(string method, string route, int statusCode, TimeSpan elapsed)
    {
        var read = string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
            || (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase) && string.Equals(route, RecallTracePath, StringComparison.OrdinalIgnoreCase));
        if (read && statusCode is >= 200 and < 300)
            return;
        _log.Record(LogLevel.Information, LogMessages.ApiCall, method, route, statusCode, (int)elapsed.TotalMilliseconds);
    }

    /// <summary>The <c>recall</c> tool: searches pages or reads one. Never changes a file.</summary>
    public Task<ToolResult> RecallAsync(RecallRequest request, CancellationToken cancellationToken = default) =>
        RecordToolCall("recall", null, Targets(request.Page), () => RecallCoreAsync(request, cancellationToken));

    private async Task<ToolResult> RecallCoreAsync(RecallRequest request, CancellationToken cancellationToken)
    {
        using var hold = await _gate.ReadAsync(cancellationToken);
        return await _recall.RunAsync(request, cancellationToken);
    }

    /// <summary>
    /// A recall trace for the management API: the search <c>recall</c> runs for the same parameters, stage by stage, with
    /// up to three retrieval values replaced. Counts no return and changes no file.
    /// </summary>
    /// <exception cref="TacitException"><see cref="TacitError.RequestInvalid"/> with every problem of the request.</exception>
    public async Task<RecallTraceReport> TraceRecallAsync(RecallTraceRequest request, CancellationToken cancellationToken = default)
    {
        var search = RecallTraceRun.Check(request, Config.Language);
        var trace = await TraceRecallAsync(search.Query, search.Filter, search.Overrides, cancellationToken);
        return RecallTraceRun.Report(trace, Config);
    }

    /// <summary>The search <c>recall</c> runs, stage by stage; counts no return.</summary>
    internal async Task<RecallTrace> TraceRecallAsync(
        string query, IndexFilter filter, RetrievalOverrides overrides, CancellationToken cancellationToken = default)
    {
        using var hold = await _gate.ReadAsync(cancellationToken);
        return await _pipeline.RunAsync(query, filter, overrides, cancellationToken);
    }

    /// <summary>Everything a workstream's brief lists, whole; counts no return. <c>null</c> when the workstream has nothing.</summary>
    internal async Task<WorkstreamBrief.Parts?> GatherBriefAsync(string workstream, CancellationToken cancellationToken = default)
    {
        using var hold = await _gate.ReadAsync(cancellationToken);
        return _brief.Gather(workstream, null, null);
    }

    /// <summary>
    /// The persona and its candidates for the memory page, with the two files' paths and problems. Reads the files only:
    /// counts nothing and changes nothing.
    /// </summary>
    public async Task<PersonaReport> PersonaAsync(CancellationToken cancellationToken = default)
    {
        using var hold = await _gate.ReadAsync(cancellationToken);
        return PersonaBrowse.Read(Config);
    }

    /// <summary>Every detail page, for the memory page's list. Counts nothing.</summary>
    public async Task<PageList> PagesAsync(CancellationToken cancellationToken = default)
    {
        using var hold = await _gate.ReadAsync(cancellationToken);
        return PageBrowse.List(Config, _index);
    }

    /// <summary>One detail page for the memory page, with its sections' dates and counts. Counts nothing.</summary>
    /// <exception cref="TacitException">
    /// <see cref="TacitError.RequestInvalid"/> for a page ID that is not valid; <see cref="TacitError.MemoryNotFound"/> when there is no such page.
    /// </exception>
    public async Task<PageView> PageAsync(string workstream, string slug, CancellationToken cancellationToken = default)
    {
        using var hold = await _gate.ReadAsync(cancellationToken);
        return PageBrowse.Read(Config, _index, workstream, slug);
    }

    /// <summary>A whole page; counts no return. <c>null</c> when it does not exist or cannot be read.</summary>
    internal async Task<PageDocument?> FetchPageAsync(PageId id, CancellationToken cancellationToken = default)
    {
        using var hold = await _gate.ReadAsync(cancellationToken);
        return _pages.Fetch(id, _index).Document;
    }

    /// <summary>
    /// How often <c>recall</c> has returned each page's units, for every page in the index. Derived state: <c>reindex</c>
    /// resets it, and no search reads it.
    /// </summary>
    internal IReadOnlyList<PageUsage> PageUsages() =>
        _index.PageUsages()
            .Select(row => new PageUsage(row.Page, (int)row.Count, row.Last is null ? null : DateOnly.ParseExact(row.Last, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)))
            .ToList();

    /// <summary>How often <c>recall</c> has returned each activity, by its short code. Derived state, as <see cref="PageUsages"/> is.</summary>
    internal IReadOnlyDictionary<string, long> ActivityUsages() => _index.ActivityUsages();

    /// <summary>The <c>write_page</c> tool. Writes run one at a time, in the order they arrive.</summary>
    public Task<ToolResult> WritePageAsync(WritePageRequest request, CancellationToken cancellationToken = default) =>
        RecordToolCall("write_page", request.Op, Targets(request.Page, request.Hash), () => WritePageCoreAsync(request, cancellationToken));

    private async Task<ToolResult> WritePageCoreAsync(WritePageRequest request, CancellationToken cancellationToken)
    {
        var hold = await _gate.WriteAsync(cancellationToken);
        try
        {
            var result = await _pages.WriteAsync(request, cancellationToken);
            // The index follows the file right away; the watcher's later event finds the hash unchanged.
            if (PageId.TryParse(request.Page ?? "", out var id, out _))
                _index.SyncPage(id);
            // Page counts and format problems come from the pages, so the instructions are rebuilt on their next use.
            InvalidateInstructions();
            if (result.IsError)
                return result;
            _backup.ScheduleIfDue();
            return await _vectors.FillAsync(cancellationToken)
                ? result
                : result with { Text = result.Text + "\n\nNote: the embedding model is unavailable, so until it is back this page is found by full-text search only." };
        }
        finally
        {
            hold.Dispose();
        }
    }

    /// <summary>The <c>write_recent</c> tool. Shares the write queue with <c>write_page</c>, so writes land in arrival order.</summary>
    public Task<ToolResult> WriteRecentAsync(WriteRecentRequest request, CancellationToken cancellationToken = default) =>
        RecordToolCall("write_recent", null, Targets(request.Replaces), () => WriteRecentCoreAsync(request, cancellationToken));

    private async Task<ToolResult> WriteRecentCoreAsync(WriteRecentRequest request, CancellationToken cancellationToken)
    {
        var hold = await _gate.WriteAsync(cancellationToken);
        try
        {
            var result = await _recent.WriteAsync(request, cancellationToken);
            // Like write_page, the index follows the file right away, so the new activity is searchable at once.
            _index.SyncActivities();
            InvalidateInstructions();
            if (!result.Written)
                return result;
            _backup.ScheduleIfDue();
            return await _vectors.FillAsync(cancellationToken)
                ? result
                : result with { Text = result.Text + "\n\nNote: the embedding model is unavailable, so until it is back this activity is found by full-text search only." };
        }
        finally
        {
            hold.Dispose();
        }
    }

    /// <summary>
    /// The <c>write_persona</c> tool. Shares the write queue with the other writes. A written persona re-renders the bound
    /// resident blocks the same way a manual edit of <c>persona.md</c> does: debounced, and a failure only tells the user.
    /// </summary>
    public Task<ToolResult> WritePersonaAsync(WritePersonaRequest request, CancellationToken cancellationToken = default) =>
        RecordToolCall("write_persona", request.Op, Targets(request.Hash), () => WritePersonaCoreAsync(request, cancellationToken));

    private async Task<ToolResult> WritePersonaCoreAsync(WritePersonaRequest request, CancellationToken cancellationToken)
    {
        var hold = await _gate.WriteAsync(cancellationToken);
        try
        {
            // Judgments read the index, so it follows a manual edit first and this write right after; the watcher's later event finds the hash unchanged.
            _index.SyncJudgments();
            var result = await _persona.WriteAsync(request, cancellationToken);
            // The candidates and the persona feed the consolidation reminder, so the instructions are rebuilt on their next use.
            InvalidateInstructions();
            _index.SyncJudgments();
            if (!result.Written)
                return result;
            // Only a change to persona.md reaches the resident context; a candidate waits for confirmation first.
            if (request.Op != ToolValues.WritePersonaOps.Propose)
                ScheduleRender();
            _backup.ScheduleIfDue();
            return result;
        }
        finally
        {
            hold.Dispose();
        }
    }

    /// <summary>
    /// Rebuilds the derived index from the memory files: the full-text table, the units, the vectors and the judgment
    /// cache. It waits until the vectors are filled in; when the embedding model cannot be reached, the vectors wait for
    /// it and the background fill takes them over. Runs as a maintenance operation, so the memory tools wait for it.
    /// </summary>
    public Task<ReindexResult> ReindexAsync(CancellationToken cancellationToken = default) =>
        RunMaintenanceAsync("reindex", ReindexCoreAsync, cancellationToken);

    private async Task<ReindexResult> ReindexCoreAsync(CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        _index.Rebuild();
        _index.SyncAll();
        var filled = await _vectors.FillAsync(cancellationToken);
        if (!filled)
            _vectors.FillInBackground();
        var counts = _index.Counts();
        var skipped = _index.SkippedFiles;
        var elapsed = Stopwatch.GetElapsedTime(started);
        var milliseconds = (long)elapsed.TotalMilliseconds;
        _log.Record(LogLevel.Information, LogMessages.IndexRebuilt, counts.Files, counts.Units, counts.Vectors, skipped.Count, milliseconds);
        if (!filled)
            _log.Record(LogLevel.Warning, LogMessages.VectorsWaiting);
        return new ReindexResult(counts.Files, counts.Units, counts.Vectors, skipped, !filled, milliseconds, ReindexReport(counts, skipped, !filled, elapsed));
    }

    /// <summary>The summary of a rebuild for the user: the counts and the time, the skipped files, and the vectors still to come.</summary>
    private string ReindexReport(IndexCounts counts, IReadOnlyList<string> skipped, bool vectorsPending, TimeSpan elapsed)
    {
        var lines = new List<string>
        {
            MaintenanceMessages.Reindexed.Format(Config.Language, counts.Files, counts.Units, counts.Vectors, elapsed.TotalSeconds),
        };
        if (skipped.Count > 0)
        {
            lines.Add(MaintenanceMessages.SkippedFiles.In(Config.Language));
            lines.AddRange(skipped.Select(path => $"  {path}"));
        }

        if (!_models.Embedding.Configured)
            lines.Add(MaintenanceMessages.EmbeddingNotConfigured.In(Config.Language));
        if (vectorsPending)
            lines.Add(MaintenanceMessages.VectorsPending.In(Config.Language));
        return string.Join('\n', lines);
    }

    /// <summary>
    /// Runs a maintenance operation with the memory to itself. First it waits for the background work already started (vector
    /// fills, consolidation judgments and automatic backups) to finish, so the operation never races them. While it runs, memory
    /// tool calls, server instructions, the consolidation report and background syncs wait, then run. The operation must not call the read
    /// or write paths, which would wait for it. Its start, its end and its failure are logged.
    /// </summary>
    private async Task<T> RunMaintenanceAsync<T>(string name, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        // An automatic backup takes the write slot, which this operation holds alone, so it is waited for before the gate.
        await Settled(_backup.Completion);
        using var hold = await _gate.MaintenanceAsync(cancellationToken);
        await Task.WhenAll(Settled(_vectors.Background), Settled(_judgments.Completion));
        _log.Record(LogLevel.Information, LogMessages.MaintenanceStarted, name);
        var started = Stopwatch.GetTimestamp();
        try
        {
            var result = await operation(cancellationToken);
            _log.Record(LogLevel.Information, LogMessages.MaintenanceFinished, name, (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return result;
        }
        catch (Exception e)
        {
            _log.Record(LogLevel.Warning, LogMessages.MaintenanceFailed, name, (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds, e.Message);
            throw;
        }
    }

    /// <summary>
    /// What <c>tacit restore</c> would do: the backup at <paramref name="path"/> (or, with <paramref name="remote"/>, the
    /// cloud backup, which is downloaded first), its manifest, and what it would replace in the memory repository as it is
    /// now (and in <c>config.yaml</c> and <c>prompts/</c> when <paramref name="config"/> is set). Exactly one source is
    /// needed. Changes nothing; a cloud preview only fills <c>backups/.download/</c>.
    /// </summary>
    /// <param name="path">The backup zip, as an absolute path; <c>null</c> with <paramref name="remote"/>.</param>
    /// <param name="remote">Preview the cloud backup instead of a zip.</param>
    /// <param name="config">Whether the restore would also take <c>config.yaml</c> and <c>prompts/</c> from the backup.</param>
    public async Task<RestorePreview> PreviewRestoreAsync(string? path, bool remote = false, bool config = false, CancellationToken cancellationToken = default)
    {
        if ((path is not null) == remote)
            throw new TacitException(TacitError.RequestInvalid, RestoreMessages.SourceCount.In(Config.Language));
        if (remote)
            return await PreviewRemoteRestoreAsync(config, cancellationToken);
        RequireAbsolutePath(path!);
        return await PreviewLocalRestoreAsync(path!, config, cancellationToken);
    }

    /// <summary>
    /// What <c>tacit restore --remote</c> would do: downloads the cloud backup into <c>backups/.download/</c>, then previews
    /// it. The preview's path is the downloaded file, which is the file a restore takes, so what is confirmed is what is
    /// restored. The download touches no memory file and runs outside the queue.
    /// </summary>
    /// <param name="config">Whether the restore would also take <c>config.yaml</c> and <c>prompts/</c> from the backup.</param>
    public async Task<RestorePreview> PreviewRemoteRestoreAsync(bool config = false, CancellationToken cancellationToken = default)
    {
        var file = await new CloudDownload(_options, Config, _log).FetchAsync(cancellationToken);
        return await PreviewLocalRestoreAsync(file, config, cancellationToken);
    }

    private async Task<RestorePreview> PreviewLocalRestoreAsync(string path, bool config, CancellationToken cancellationToken)
    {
        using var hold = await _gate.ReadAsync(cancellationToken);
        using var archive = RestoreArchive.Open(path, Config.Language, config);
        return await RestorePreviewBuilder.BuildAsync(_options, Config, path, archive, config, cancellationToken);
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
    public async Task<RestoreResult> RestoreAsync(string? path, bool remote = false, bool config = false, CancellationToken cancellationToken = default)
    {
        if (remote)
            throw new TacitException(TacitError.RequestInvalid, RestoreMessages.RemotePreviewFirst.In(Config.Language));
        if (path is null)
            throw new TacitException(TacitError.RequestInvalid, RestoreMessages.SourceCount.In(Config.Language));
        RequireAbsolutePath(path);
        var result = await RunMaintenanceAsync("restore", _ => RestoreCoreAsync(path, config), cancellationToken);
        // A restore from the cloud download is done with the download once it has succeeded; a failed one keeps it.
        if (CloudDownload.IsDownload(_options, path))
            CloudDownload.Discard(_options);
        return result;
    }

    private async Task<RestoreResult> RestoreCoreAsync(string path, bool config)
    {
        var language = Config.Language;
        // A bad backup is refused here, before the index, the watchers or any file is touched.
        using var archive = RestoreArchive.Open(path, language, config);
        var memory = Path.TrimEndingDirectorySeparator(Config.MemoryPath);
        var install = new RestoreInstall(_options, Config, _log, Guid.NewGuid().ToString("N")[..8]);
        _log.Record(LogLevel.Information, LogMessages.RestoreStarted, path, (config ? RestoreMessages.ScopeWithConfig : RestoreMessages.ScopeMemory).In(language));

        // The watchers and the index let go of the files the restore replaces; the index reopens once they are final.
        StopWatching();
        _index.Dispose();

        var notices = new List<string>();
        string safety;
        try
        {
            safety = await install.WriteSafetyBackupAsync(notices, CancellationToken.None);
        }
        catch (Exception e)
        {
            ReopenIndexAndWatchers();
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
            ReopenIndexAndWatchers();
            throw failure;
        }

        // Nothing is left to undo: the old content and the staged leftovers go, then the service picks the restored state up.
        install.Finish();
        StartWatching();
        // A restored config that loads is reloaded as a save on the settings page is, which also applies the prompts and
        // renders the resident blocks; one that does not load leaves the service on the config it had.
        IReadOnlyList<RestartChange> restart = [];
        var reloaded = false;
        if (config && restored.Problem is null)
        {
            try
            {
                restart = ReloadCore();
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
        if (config && !reloaded)
            ApplyPrompts();
        InvalidateInstructions();

        if (config)
        {
            notices.Add(RestoreMessages.PromptsApplied.In(language));
            if (restart.Count > 0)
                notices.Add(string.Join('\n', restart
                    .Select(change => RestoreMessages.RestartPendingItem.Format(language, change.Path, change.Running, change.Saved))
                    .Prepend(RestoreMessages.RestartPending.In(language))));
            if (restored.Problem is null
                && !string.Equals(Path.GetFullPath(restored.MemoryPath!), Path.GetFullPath(memory), StringComparison.OrdinalIgnoreCase))
                notices.Add(RestoreMessages.MemoryPathNotice.Format(language, restored.MemoryPath, memory));
        }

        if (!reloaded)
        {
            try
            {
                foreach (var render in Binder.RenderAll(_options, Config, _prompts))
                    _log.Record(LevelOf(render), render.Message);
            }
            catch (Exception e) when (e is TacitException or IOException or UnauthorizedAccessException)
            {
                _log.Record(LogLevel.Warning, Messages.RenderFailed, e.Message);
                notices.Add(Messages.RenderFailed.Format(language, e.Message));
            }
        }

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
    private void ReopenIndexAndWatchers()
    {
        try
        {
            _index.Rebuild();
            _index.SyncAll();
            _vectors.FillInBackground();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            _log.Notify(LogLevel.Warning, Messages.IndexSyncFailed, e.Message);
        }
        finally
        {
            StartWatching();
        }
    }

    /// <summary>Stops both file watchers, so the restore's own moves are not taken for edits by the user.</summary>
    private void StopWatching()
    {
        _watcher.EnableRaisingEvents = false;
        lock (_renderGate)
            _promptsWatcher?.Dispose();
    }

    /// <summary>Arms both file watchers again: the memory repository and the prompts folder.</summary>
    private void StartWatching()
    {
        _watcher.EnableRaisingEvents = true;
        WatchPrompts();
    }

    private void RequireAbsolutePath(string path)
    {
        if (!Path.IsPathFullyQualified(path))
            throw new TacitException(TacitError.RequestInvalid, RestoreMessages.PathNotAbsolute.Format(Config.Language, path));
    }

    /// <summary>A task that completes once <paramref name="task"/> has, whether it finished or failed: its failure is reported where it happened.</summary>
    private static Task Settled(Task task) => task.ContinueWith(_ => { }, TaskScheduler.Default);

    public void Dispose()
    {
        _disposed = true;
        lock (_renderGate)
        {
            _renderTimer.Dispose();
            _promptsTimer.Dispose();
            _dayTimer?.Dispose();
        }
        lock (_indexTimerGate)
            _indexTimer.Dispose();
        _vectors.Background.Wait(TimeSpan.FromSeconds(5));
        _judgments.Completion.Wait(TimeSpan.FromSeconds(5));
        _backup.Completion.Wait(TimeSpan.FromSeconds(5));
        CloseRuntime();
        _gate.Dispose();
        _instructionsBuild.Dispose();
    }

    /// <summary>
    /// Runs one memory tool and writes its event: the tool, its op, the page IDs and short codes the call names, the outcome
    /// and the time taken. It is the only place tool calls are logged, and no event carries the text a call sends.
    /// </summary>
    private async Task<ToolResult> RecordToolCall(string tool, string? op, string targets, Func<Task<ToolResult>> call)
    {
        var started = Stopwatch.GetTimestamp();
        ToolResult? result = null;
        try
        {
            result = await call();
            return result;
        }
        finally
        {
            var outcome = result is null ? LogMessages.ToolAborted
                : result.IsError ? LogMessages.ToolRefused
                : result.Written ? LogMessages.ToolWritten
                : LogMessages.ToolDone;
            _log.Record(
                LogLevel.Information, LogMessages.ToolCall, tool, OpName(op), targets, outcome.In(Config.Language),
                (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    /// <summary>The page IDs and short codes a call names, as far as they have their shape; <c>-</c> when none does. Other text is never logged.</summary>
    private static string Targets(params string?[] names)
    {
        var shown = new List<string>();
        foreach (var name in names)
        {
            if (name is null)
                continue;
            if (PageId.TryParse(name, out var id, out _))
                shown.Add(id.ToString());
            else if (ShortCode.LooksLike(name))
                shown.Add(name);
        }

        return shown.Count == 0 ? "-" : string.Join(' ', shown);
    }

    /// <summary>The op of a call when it is a plain word, such as <c>upsert_section</c>; otherwise <c>-</c>.</summary>
    private static readonly Regex OpShape = new("^[a-z_]{1,32}$", RegexOptions.CultureInvariant);

    private static string OpName(string? op) => op is not null && OpShape.IsMatch(op) ? op : "-";

    private void OnChange(object sender, FileSystemEventArgs e)
    {
        try
        {
            var paths = e is RenamedEventArgs renamed ? [e.FullPath, renamed.OldFullPath] : new[] { e.FullPath };
            foreach (var relative in paths.Select(path => Path.GetRelativePath(Config.MemoryPath, path).Replace('\\', '/')))
            {
                if (relative == PersonaFiles.Persona || AffectsResident(relative))
                    ScheduleRender();
                if (relative is PersonaFiles.Persona or PersonaFiles.Candidates)
                    ScheduleIndexSync();
                if (AffectsInstructions(relative))
                    InvalidateInstructions();
                if (relative == PageId.Folder || relative.StartsWith($"{PageId.Folder}/", StringComparison.Ordinal) || relative.StartsWith($"{RecentFile.Folder}/", StringComparison.Ordinal))
                    ScheduleIndexSync();
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // An event handler must not throw into the watcher's thread; the overflow path or the next event catches up.
            _log.Notify(LogLevel.Warning, Messages.BackgroundFailed, ex.Message);
        }
    }

    /// <summary>Events were lost: assume anything may have changed.</summary>
    private void OnOverflow(object sender, ErrorEventArgs e)
    {
        InvalidateInstructions();
        ScheduleRender();
        ScheduleIndexSync();
    }

    private void InvalidateInstructions()
    {
        lock (_instructionsGate)
            ClearInstructionsLocked();
    }

    /// <summary>Drops the cached text. A build that started before this is not kept either.</summary>
    private void ClearInstructionsLocked()
    {
        _instructions = null;
        _instructionsVersion++;
    }

    /// <summary>
    /// Whether a change to this path (relative to the repository, with forward slashes) changes the resident block: the
    /// <c>workstreams.md</c>, an activity month file (its counts and its summaries), or a workstream folder directly under <c>pages/</c>.
    /// </summary>
    private static bool AffectsResident(string relative) =>
        relative == WorkstreamFile.RelativePath
        || relative == RecentFile.Folder || relative.StartsWith($"{RecentFile.Folder}/", StringComparison.Ordinal)
        || relative == PageId.Folder || (relative.StartsWith($"{PageId.Folder}/", StringComparison.Ordinal) && relative.IndexOf('/', PageId.Folder.Length + 1) < 0);

    /// <summary>Arms the timer for the next local midnight.</summary>
    private ITimer ArmDayTimer() => _options.Time.CreateTimer(_ => OnDayChanged(), null, UntilNextMidnight(), Timeout.InfiniteTimeSpan);

    /// <summary>The time from now until the next local midnight; always positive.</summary>
    private TimeSpan UntilNextMidnight()
    {
        var now = _options.Time.GetLocalNow().DateTime;
        return now.Date.AddDays(1) - now;
    }

    /// <summary>Runs on the timer thread at midnight: the date has changed, so the resident blocks are rendered again.</summary>
    private void OnDayChanged()
    {
        lock (_renderGate)
        {
            if (_disposed)
                return;
            _dayTimer?.Dispose();
            _dayTimer = ArmDayTimer();
        }
        ScheduleRender();
    }

    /// <summary>Whether a change to this path (relative to the repository, with forward slashes) can change the server instructions.</summary>
    private static bool AffectsInstructions(string relative) =>
        relative is PersonaFiles.Persona or PersonaFiles.Candidates or WorkstreamFile.RelativePath
        || relative == RecentFile.Folder || relative.StartsWith($"{RecentFile.Folder}/", StringComparison.Ordinal)
        || relative == PageId.Folder || relative.StartsWith($"{PageId.Folder}/", StringComparison.Ordinal)
        || IsGitHistory(relative);

    /// <summary>The files a commit moves: HEAD, the refs and their logs, and the packed refs.</summary>
    private static bool IsGitHistory(string relative) =>
        relative is ".git/HEAD" or ".git/packed-refs"
        || relative.StartsWith(".git/refs/", StringComparison.Ordinal)
        || relative.StartsWith(".git/logs/", StringComparison.Ordinal);

    private void OnPromptsChange(object sender, FileSystemEventArgs e) => SchedulePromptsReload();

    /// <summary>Events were lost, or the watcher failed: reload, which also arms the watcher again.</summary>
    private void OnPromptsError(object sender, ErrorEventArgs e) => SchedulePromptsReload();

    private void SchedulePromptsReload()
    {
        lock (_renderGate)
        {
            if (!_disposed)
                _promptsTimer.Change(Debounce, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// Watches <c>prompts/</c>, creating the folder when it is gone. Each reload arms a new watcher before it reads the
    /// files, so a change made while reading is seen by the next reload.
    /// </summary>
    private void WatchPrompts()
    {
        var folder = PromptFiles.Folder(_options.TacitHome);
        Directory.CreateDirectory(folder);
        _promptsWatcher?.Dispose();
        _promptsWatcher = new FileSystemWatcher(folder);
        _promptsWatcher.Changed += OnPromptsChange;
        _promptsWatcher.Created += OnPromptsChange;
        _promptsWatcher.Deleted += OnPromptsChange;
        _promptsWatcher.Renamed += OnPromptsChange;
        _promptsWatcher.Error += OnPromptsError;
        _promptsWatcher.EnableRaisingEvents = true;
    }

    /// <summary>
    /// Runs on the timer thread. Each prompt document that fails its checks is reported and keeps its previous text; the
    /// others take their new text.
    /// </summary>
    private void ReloadPrompts() => UnderWriteGate(ApplyPrompts);

    /// <summary>
    /// Reloads the prompt documents, as a change to <c>prompts/</c> does. It runs under the write gate, or while a
    /// maintenance operation holds the gate: a restore applies the documents it has just put back through this as well.
    /// </summary>
    private void ApplyPrompts()
    {
        lock (_renderGate)
        {
            if (_disposed)
                return;
            try
            {
                WatchPrompts();
                var problems = new Dictionary<PromptName, string>();
                var prompts = PromptFiles.Reload(_options.TacitHome, Config.Language, _prompts, problems);
                foreach (var problem in problems.Values.Distinct())
                    _log.Notify(LogLevel.Warning, Messages.PromptsNotApplied, problem);
                SkillSet.Write(_options.TacitHome, Config.Language, prompts.Skill);
                _log.Record(LogLevel.Information, LogMessages.PromptsReloaded);
                var residentChanged = prompts.Resident != _prompts.Resident;
                lock (_instructionsGate)
                {
                    _prompts = prompts;
                    _promptProblems = problems;
                    ClearInstructionsLocked();
                }
                _promptsRetries = 0;
                // The resident blocks show resident.md: render them with the new text, as a persona change does.
                if (residentChanged)
                    _renderTimer.Change(Debounce, Timeout.InfiniteTimeSpan);
            }
            catch (Exception e) when ((e is IOException or ArgumentException) || (e is UnauthorizedAccessException && _promptsRetries < RenderRetryLimit))
            {
                // The editor still holds a file, or the folder is busy; try again once it settles. Windows refuses a
                // replacement it cannot make with UnauthorizedAccessException, which the render path retries the same way.
                if (e is UnauthorizedAccessException)
                    _promptsRetries++;
                _promptsTimer.Change(Debounce, Timeout.InfiniteTimeSpan);
            }
            catch (Exception e) when (e is TacitException or UnauthorizedAccessException)
            {
                _promptsRetries = 0;
                _log.Notify(LogLevel.Warning, Messages.PromptsNotApplied, e.Message);
            }
        }
    }

    private void ScheduleRender()
    {
        lock (_renderGate)
        {
            if (!_disposed)
                _renderTimer.Change(Debounce, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// Runs on the timer thread, between writes: renders never overlap, and no failure may escape and end the process.
    /// It reads <c>persona.md</c>, which a write replaces, so it reads only under the write gate.
    /// </summary>
    private void RenderNow() => UnderWriteGate(() =>
    {
        lock (_renderGate)
        {
            if (_disposed)
                return;
            try
            {
                foreach (var result in Binder.RenderAll(_options, Config, _prompts))
                    _log.Notify(LevelOf(result), result.Message);
                _renderRetries = 0;
            }
            catch (Exception e) when ((e is IOException or UnauthorizedAccessException) && _renderRetries < RenderRetryLimit)
            {
                // The editor or another client still holds a file, which Windows refuses to replace; try again once it settles.
                _renderRetries++;
                _renderTimer.Change(Debounce, Timeout.InfiniteTimeSpan);
            }
            catch (Exception e) when (e is TacitException or IOException or UnauthorizedAccessException)
            {
                _renderRetries = 0;
                _log.Notify(LogLevel.Warning, Messages.RenderFailed, e.Message);
            }
        }
    });

    /// <summary>A render that wrote or left its file alone is information; one that failed is a warning.</summary>
    private static LogLevel LevelOf(BlockRender render) =>
        render.State is BlockState.Written or BlockState.Unchanged ? LogLevel.Information : LogLevel.Warning;

    private void ScheduleIndexSync()
    {
        lock (_indexTimerGate)
        {
            if (!_disposed)
                _indexTimer.Change(Debounce, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// Runs a background read of the memory repository, such as an index sync or a render, between the writes. A write
    /// replaces the files it changes, and a read still holding one of them open makes that replacement fail. The wait for
    /// the write slot is asynchronous, so a timer thread is never blocked by a write in progress. Once the service is
    /// disposed, the read does not run.
    /// </summary>
    private void UnderWriteGate(Action work) => _ = RunUnderWriteGateAsync(work);

    private async Task RunUnderWriteGateAsync(Action work)
    {
        Hold hold;
        try
        {
            hold = await _gate.WriteAsync(CancellationToken.None);
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            work();
        }
        catch (Exception e)
        {
            // Nothing may escape a timer or watcher callback: the failure is reported, and the next event tries again.
            _log.Notify(LogLevel.Warning, Messages.BackgroundFailed, e.Message);
        }
        finally
        {
            hold.Dispose();
        }
    }

    /// <summary>
    /// Runs on the timer thread, between writes: compares every page file's hash with the index, so missed events do no
    /// harm. It reads the files only under the write gate, as a write replaces them.
    /// </summary>
    private void SyncIndexNow() => UnderWriteGate(() =>
    {
        lock (_indexTimerGate)
        {
            if (_disposed)
                return;
            try
            {
                _index.SyncAll();
                _vectors.FillInBackground();
            }
            catch (IOException)
            {
                // The editor still holds a file; try again once it settles.
                _indexTimer.Change(Debounce, Timeout.InfiniteTimeSpan);
            }
            catch (Exception e) when (e is UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException or TacitException)
            {
                // A git process that cannot start surfaces as a TacitException from the full build's commit dates.
                _log.Notify(LogLevel.Warning, Messages.IndexSyncFailed, e.Message);
            }
        }
    });
}
