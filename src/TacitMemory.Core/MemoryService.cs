using System.Net;
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
using TacitMemory.Core.Upkeep;
using TacitMemory.Core.Workstreams;

namespace TacitMemory.Core;

/// <summary>
/// The running memory service. Create it with <see cref="Tacit.OpenService"/>. Its work is split by caller into facets, each
/// a property: <see cref="Tools"/> for MCP, and <see cref="Browse"/>, <see cref="Reports"/>, <see cref="Settings"/>,
/// <see cref="Maintenance"/> and <see cref="Access"/> for the management API. The service itself keeps the lifecycle and
/// what the host records.
/// <para>
/// It has two layers. The shell lives as long as the process: the lock that orders the work, the start time, the extra
/// listener's outcome, the access control, the client records, the derived upkeep (which re-renders the resident blocks,
/// syncs the index and reloads the prompt documents as their sources change) and the timers. The runtime is everything the
/// config decides (the index, the auxiliary models, the tools, the backups and the file watchers); <see cref="OpenRuntime"/>
/// is the one path that builds it, at the start and again on each <see cref="ReloadAsync"/>. The facets read the runtime
/// the service runs now, so one taken before a reload reaches the reopened memory.
/// </para>
/// </summary>
public sealed class MemoryService : IDisposable
{
    /// <summary>The category the service's events are logged under.</summary>
    internal const string LogCategory = "TacitMemory";

    private readonly TacitOptions _options;
    private AutomaticBackup _backup = null!;
    private ManualBackup _manual = null!;
    private CloudBackup _remote = null!;
    private readonly ServiceLog _log;
    private readonly ClientRecordStore _clients;
    private ISourceWatch _sources = null!;

    /// <summary>Keeps the derived products in step with the memory files; it outlives every runtime.</summary>
    private readonly DerivedUpkeep _upkeep;

    private readonly MaintenanceGate _gate = new();
    private readonly MaintenanceRunner _runner;
    private PageTools _pages = null!;
    private RecentTools _recent = null!;
    private PersonaTools _persona = null!;
    private AuxiliaryModels _models = null!;
    private MemoryIndex _index = null!;
    private VectorSync _vectors = null!;
    private JudgmentPool _judgments = null!;
    private ConsolidationServices _consolidation = null!;
    private RecallPipeline _pipeline = null!;
    private MemoryReader _reader = null!;

    private RecallTool _recall = null!;
    private volatile ExtraListenerStatus? _extraListener;

    /// <summary>Whether a runtime is open; false only after a reload could open none, which ends the service.</summary>
    private bool _runtimeOpen;

    /// <summary>Completes when a reload has failed with both the new config and the old one: the service can no longer run.</summary>
    private readonly TaskCompletionSource<Exception> _failed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal MemoryService(TacitOptions options, TacitConfig config, Action<string> notice, ILogger logger)
    {
        _options = options;
        var startedAt = options.Time.GetUtcNow();
        Config = config;
        _log = new ServiceLog(logger, config.Language, notice);
        Access = new AccessControl(options, config, logger);
        SkillSet.Write(options.TacitHome, config.Prompts.Skill);
        StartupRender = Binder.RenderAll(options, config, config.Prompts);
        _log.Record(LogLevel.Information, LogMessages.Started, ProductVersion.Current, config.Port, config.MemoryPath);
        foreach (var render in StartupRender)
            _log.Record(DerivedUpkeep.LevelOf(render), render.Message);
        _clients = new ClientRecordStore(ClientRecordStore.PathFor(options), options.Time, _log);

        _upkeep = new DerivedUpkeep(
            options, _log, _gate, () => new UpkeepRuntime(Config, _sources, _index, _vectors, _models, _backup.Completion, _judgments.Completion));
        _runner = new MaintenanceRunner(_gate, _upkeep, _log, () => [_vectors.Background, _judgments.Completion]);

        Tools = new ServiceTools(
            options, _gate, _upkeep, _log, () => new ToolRuntime(Config, _recall, _pages, _recent, _persona, _consolidation));
        Browse = new MemoryBrowser(_gate, () => new BrowseRuntime(Config, _index, _reader), () => _options.Today);
        Reports = new ServiceReports(options, startedAt, _upkeep, () => new ReportRuntime(Config, _models), () => _extraListener);
        Settings = new ServiceSettings(options, _gate, _upkeep, _runner, Access, () => Config, ReloadCore);
        Maintenance = new ServiceMaintenance(
            options, _gate, _upkeep, _runner, _log,
            () => new MaintenanceRuntime(Config, _index, _vectors, _models, _sources, _manual, _remote), ReloadCore);
        OpenRuntime(config);
    }

    /// <summary>
    /// Builds the runtime from <paramref name="config"/>: the auxiliary models, the index (synced with the files), the tools,
    /// the backups and the watch of the sources. The parts that can fail are built before any is put in place, so a failure
    /// leaves nothing half open: what was built is let go, and the exception is raised.
    /// </summary>
    private void OpenRuntime(TacitConfig config)
    {
        AuxiliaryModels? models = null;
        MemoryIndex? index = null;
        ISourceWatch? sources = null;
        try
        {
            OnOpening?.Invoke(config);
            models = new AuxiliaryModels(config, _options.ModelClock, _log);
            index = new MemoryIndex(
                config.MemoryPath, config.Models.Embedding?.Model,
                () => _options.Today, () => new GitCli(config.MemoryPath, config.Language).LastCommitDays(MemoryLayout.Pages));
            index.SyncAll();
            sources = _options.Sources?.Attach(_upkeep) ?? new FileSourceWatch(config.MemoryPath, _options.TacitHome, _upkeep, _log);
        }
        catch
        {
            sources?.Dispose();
            index?.Dispose();
            models?.Dispose();
            throw;
        }

        Config = config;
        _upkeep.UsePrompts(config.Prompts);
        _models = models;
        if (_models.Unconfigured is { Count: > 0 } unconfigured)
            _log.Record(LogLevel.Information, LogMessages.ModelsNotConfigured, string.Join(", ", unconfigured));
        _index = index;
        _vectors = new VectorSync(_index, _models.Embedding);
        var judge = new ChoiceJudge(_index, _models.Decision);
        _judgments = new JudgmentPool(_index, judge, _options.Time, config.Consolidation.MaxConcurrency);
        _consolidation = new ConsolidationServices(_index, _models.Embedding, _vectors, _judgments);
        _vectors.FillInBackground();
        var writer = new MemoryWriter(config, _log, _index, _vectors, OnWritten);
        _pages = new PageTools(
            _options, config, new DuplicatePageCheck(config, _index, _models.Embedding, _vectors, judge), writer);
        _recent = new RecentTools(_options, config, judge, writer);
        _persona = new PersonaTools(
            _options, config,
            new SamePreferenceCheck(config, _index, _models.Embedding, _vectors, judge),
            new RuleConflictCheck(config, _index, _models.Embedding, _vectors, judge),
            writer);
        _pipeline = new RecallPipeline(config, _index, _models.Embedding, _models.Reranker, _vectors);
        _reader = new MemoryReader(_index, _pages, _pipeline, new WorkstreamBrief(config, _index));
        _recall = new RecallTool(config, _index, _reader);
        _backup = new AutomaticBackup(_options, config, _log, _gate);
        _manual = new ManualBackup(_options, config, _log);
        _remote = new CloudBackup(_options, config, _log);

        _sources = sources;
        _runtimeOpen = true;
        _upkeep.InvalidateInstructions();
    }

    /// <summary>Lets go of the runtime's files and models: the watchers, the index and the model clients. Its background work has already settled.</summary>
    private void CloseRuntime()
    {
        if (!_runtimeOpen)
            return;
        _runtimeOpen = false;
        _sources.Dispose();
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
        _runner.RunAsync("reload", _ => Task.FromResult(ReloadCore()), cancellationToken);

    /// <summary>The reload itself, run inside a maintenance operation: by <see cref="ReloadAsync"/>, a saved config and a restore.</summary>
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

        _upkeep.RefreshNow(Refresh.Skills | Refresh.Resident);
        return RestartChange.Between(running, loaded);
    }

    /// <summary>
    /// Completes, with the reason, when a reload could open the runtime neither with the new config nor with the old one. The
    /// service cannot go on; whoever runs it stops it, and the next start reports the cause.
    /// </summary>
    public Task<Exception> Failed => _failed.Task;

    /// <summary>The config the service runs with: the one it started with, or the one the last reload put in place.</summary>
    public TacitConfig Config { get; private set; }

    /// <summary>The memory language the service words everything in: the language of the config it runs.</summary>
    public MemoryLanguage Language => Config.Language;

    /// <summary>The memory tools, the server instructions and the consolidation report: everything MCP reaches.</summary>
    public ServiceTools Tools { get; }

    /// <summary>The memory as the memory page browses it, and the recall trace, read as the service runs it now.</summary>
    public MemoryBrowser Browse { get; }

    /// <summary>The status report, the activity heatmap, the consolidation signal, the service log and the model probe.</summary>
    public ServiceReports Reports { get; }

    /// <summary><c>config.yaml</c>, the prompt documents and the bindings, as the settings and prompts pages change them.</summary>
    public ServiceSettings Settings { get; }

    /// <summary>The backups, the restore and the rebuild of the derived index.</summary>
    public ServiceMaintenance Maintenance { get; }

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
            throw new TacitException(TacitError.ConfigInvalid, AccessMessages.PasswordRequiredForExtraAddress.Format(Language, extra));
    }

    /// <summary>What happened to each bound context file when the service started.</summary>
    public IReadOnlyList<BlockRender> StartupRender { get; }

    /// <summary>The extra listener <c>server.host</c> names, as the host bound it when the service started; <c>null</c> when none is named.</summary>
    public ExtraListenerStatus? ExtraListener => _extraListener;

    /// <summary>
    /// Records the outcome of the host's attempt to bind the address <c>server.host</c> names, at <paramref name="port"/>. A
    /// failure is logged as a warning, shown to the user, and kept for the status report.
    /// </summary>
    /// <param name="address">The address that was to be listened on besides loopback.</param>
    /// <param name="port">The port of the service.</param>
    /// <param name="failureReason">Why the bind failed, in the memory language; <c>null</c> when the address answers.</param>
    public void RecordExtraListener(IPAddress address, int port, string? failureReason)
    {
        var listener = new ExtraListenerStatus(ExtraListenerStatus.AddressOf(address, port), failureReason);
        _extraListener = listener;
        if (listener.FailureReason is null)
            _log.Record(LogLevel.Information, LogMessages.ExtraListenerBound, StatusMessages.ExtraListenerName(listener.Address, Language));
        else
            _log.Notify(LogLevel.Warning, LogMessages.ExtraListenerFailed, StatusMessages.ExtraListenerName(listener.Address, Language), listener.FailureReason);
    }

    /// <summary>The failure no operation expected, logged with its kind and message; the host answers without them.</summary>
    public void RecordUnexpectedFailure(Exception cause) =>
        _log.Record(LogLevel.Error, LogMessages.UnexpectedFailure, cause.GetType().Name, cause.Message);

    /// <summary>
    /// Completes once the refreshes scheduled by a change have run and the background work has finished: the automatic
    /// backup, the vector fill and the consolidation judgments. Tests settle the service with it before they look.
    /// </summary>
    internal Task SettledAsync() => _upkeep.SettledAsync();

    /// <summary>
    /// Records what a client declared on a request: its name, version, protocol revision, capabilities and extensions. It only
    /// observes; no behaviour depends on the record. It takes no part in the memory's write lock, and a file that cannot be
    /// written is logged without failing the call.
    /// </summary>
    public void RecordClient(ClientObservation observation) => _clients.Observe(observation);

    /// <summary>
    /// Writes the management API's event for one call: its method, route, status code and time taken. The host decides which
    /// calls are written; the event never holds a request body.
    /// </summary>
    public void RecordApiCall(string method, string route, int statusCode, TimeSpan elapsed) =>
        _log.Record(LogLevel.Information, LogMessages.ApiCall, method, route, statusCode, (int)elapsed.TotalMilliseconds);

    /// <summary>
    /// What follows a write once its file is written and the index has it: the day's first write leaves a backup, and the
    /// upkeep refreshes what the written source feeds.
    /// </summary>
    private void OnWritten(WrittenSource source)
    {
        _backup.ScheduleIfDue();
        _upkeep.Written(source);
    }

    public void Dispose()
    {
        _upkeep.Dispose();
        _vectors.Background.Wait(TimeSpan.FromSeconds(5));
        _judgments.Completion.Wait(TimeSpan.FromSeconds(5));
        _backup.Completion.Wait(TimeSpan.FromSeconds(5));
        CloseRuntime();
        _gate.Dispose();
    }
}
