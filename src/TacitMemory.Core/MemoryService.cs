using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TacitMemory.Core.Logging;
using TacitMemory.Core.Configuration;
using TacitMemory.Core.Backup;
using TacitMemory.Core.Consolidation;
using TacitMemory.Core.Clients;
using TacitMemory.Core.Index;
using TacitMemory.Core.Models;
using TacitMemory.Core.Pages;
using TacitMemory.Core.Decisions;
using TacitMemory.Core.Retrieval;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Persona;
using TacitMemory.Core.Recent;
using TacitMemory.Core.Skills;
using TacitMemory.Core.Status;
using TacitMemory.Core.Maintenance;
using TacitMemory.Core.Git;

namespace TacitMemory.Core;

/// <summary>
/// The running memory service: the loaded config, the memory tools (writes run one at a time), the page index, the
/// cached server instructions, a watcher on the memory repository that re-renders the resident blocks when
/// <c>persona.md</c> changes, syncs the index when page, activity or persona files change, and refreshes
/// the instructions when projects appear or disappear under <c>pages/</c>, and a watcher that reloads the prompt
/// documents in <c>prompts/</c>, which also regenerates the skills. Create it with <see cref="Tacit.OpenService"/>.
/// </summary>
public sealed class MemoryService : IDisposable
{
    /// <summary>How long file events must stay quiet before acting on them; editors save in several steps.</summary>
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);

    /// <summary>The category the service's events are logged under.</summary>
    internal const string LogCategory = "TacitMemory";

    private readonly TacitOptions _options;
    private readonly DateTimeOffset _startedAt;
    private readonly AutomaticBackup _backup;
    private readonly ManualBackup _manual;
    private readonly CloudBackup _remote;
    private readonly ServiceLog _log;
    private readonly FileSystemWatcher _watcher;
    private FileSystemWatcher? _promptsWatcher;
    private readonly Timer _renderTimer;
    private readonly Timer _promptsTimer;
    private readonly Timer _indexTimer;
    private readonly Lock _indexTimerGate = new();
    private readonly Lock _renderGate = new();
    private readonly Lock _instructionsGate = new();
    private readonly SemaphoreSlim _instructionsBuild = new(1, 1);
    private readonly MaintenanceGate _gate = new();
    private readonly PageTools _pages;
    private readonly RecentTools _recent;
    private readonly PersonaTools _persona;
    private readonly AuxiliaryModels _models;
    private readonly MemoryIndex _index;
    private readonly VectorSync _vectors;
    private readonly JudgmentPool _judgments;
    private readonly ConsolidationServices _consolidation;
    private readonly RecallTool _recall;
    private PromptDocuments _prompts;
    private string? _instructions;
    private string? _instructionsStatus;
    private int _instructionsVersion;
    private volatile bool _disposed;

    internal MemoryService(TacitOptions options, TacitConfig config, Action<string> notice, ILogger logger)
    {
        _options = options;
        _startedAt = options.Time.GetUtcNow();
        Config = config;
        _log = new ServiceLog(logger, config.Language, notice);
        _prompts = config.Prompts;
        SkillSet.Write(options.TacitHome, config.Language, config.Prompts.Skill);
        StartupRender = Binder.RenderAll(options, config, config.Prompts);
        _log.Record(LogLevel.Information, LogMessages.Started, ProductVersion.Current, config.Port, config.MemoryPath);
        foreach (var render in StartupRender)
            _log.Record(LevelOf(render), render.Message);
        _models = new AuxiliaryModels(config, options.Time, _log);
        if (_models.Unconfigured is { Count: > 0 } unconfigured)
            _log.Record(LogLevel.Information, LogMessages.ModelsNotConfigured, string.Join(", ", unconfigured));
        _index = new MemoryIndex(
            config.MemoryPath, config.Models.Embedding?.Model, config.Language,
            () => options.Today, () => new GitCli(config.MemoryPath, config.Language).LastCommitDays("pages"));
        _index.SyncAll();
        _vectors = new VectorSync(_index, _models.Embedding);
        _judgments = new JudgmentPool(_index, _models.Decision, options.Time, config.Consolidation.MaxConcurrency);
        _consolidation = new ConsolidationServices(_index, _models.Embedding, _vectors, _judgments);
        _vectors.FillInBackground();
        _pages = new PageTools(
            options, config, _log, new DuplicatePageCheck(config, _index, _models.Embedding, _vectors, _models.Decision));
        _recent = new RecentTools(options, config, _log, _models.Decision);
        _persona = new PersonaTools(
            options, config, _log,
            new SamePreferenceCheck(config, _index, _models.Embedding, _vectors, _models.Decision),
            new RuleConflictCheck(config, _index, _models.Embedding, _vectors, _models.Decision));
        _recall = new RecallTool(config, _index, _pages, _models.Embedding, _models.Reranker, _vectors);
        _backup = new AutomaticBackup(options, config, _log);
        _manual = new ManualBackup(options, config, _log);
        _remote = new CloudBackup(options, config, _log);

        _renderTimer = new Timer(_ => RenderNow());
        _promptsTimer = new Timer(_ => ReloadPrompts());
        _indexTimer = new Timer(_ => SyncIndexNow());
        _watcher = new FileSystemWatcher(config.MemoryPath) { IncludeSubdirectories = true, InternalBufferSize = 64 * 1024 };
        _watcher.Changed += OnChange;
        _watcher.Created += OnChange;
        _watcher.Deleted += OnChange;
        _watcher.Renamed += OnChange;
        _watcher.Error += OnOverflow;
        _watcher.EnableRaisingEvents = true;

        WatchPrompts();
    }

    public TacitConfig Config { get; }

    /// <summary>What happened to each bound context file when the service started.</summary>
    public IReadOnlyList<BlockRender> StartupRender { get; }

    /// <summary>
    /// The server instructions, generated on first use and kept until something they are built from may have changed: a
    /// prompt document, the persona or its candidates, a page or activity file, the mainline, the memory repository's
    /// commits, the project list, or the status (unavailable models, skipped files, the date). Builds run one at a time.
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
            var projects = ServerInstructionText.Projects(Config.MemoryPath, recent.ProjectLabels);
            var consolidation = await ConsolidationReminder.RenderAsync(Config, _options.Today, cancellationToken);
            var text = ServerInstructionText.Assemble(Config.Language, prompts, unavailable, skipped, recent, projects, consolidation).Text;

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
            _options, Config, endpoint, ProductVersion.Current, uptime < TimeSpan.Zero ? TimeSpan.Zero : uptime, _models.Statuses, cancellationToken);
    }

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

    /// <summary>The message for a request the management API refuses, in the memory language; <paramref name="port"/> is the port it arrived on.</summary>
    public string ApiRefusalMessage(TacitError error, int port) => error switch
    {
        TacitError.ApiOriginRefused => ManagementApiMessages.ApiOriginRefused.Format(Config.Language),
        TacitError.ApiHostRefused => ManagementApiMessages.ApiHostRefused.Format(Config.Language, port),
        TacitError.ApiContentTypeRefused => ManagementApiMessages.ApiContentTypeRefused.Format(Config.Language),
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, "Not a management API refusal."),
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

    /// <summary>Writes the management API's event for one call: its method, route, status code and time taken.</summary>
    public void RecordApiCall(string method, string route, int statusCode, TimeSpan elapsed) =>
        _log.Record(LogLevel.Information, LogMessages.ApiCall, method, route, statusCode, (int)elapsed.TotalMilliseconds);

    /// <summary>The <c>recall</c> tool: searches pages or reads one. Never changes a file.</summary>
    public Task<ToolResult> RecallAsync(RecallRequest request, CancellationToken cancellationToken = default) =>
        RecordToolCall("recall", null, Targets(request.Page), () => RecallCoreAsync(request, cancellationToken));

    private async Task<ToolResult> RecallCoreAsync(RecallRequest request, CancellationToken cancellationToken)
    {
        using var hold = await _gate.ReadAsync(cancellationToken);
        return await _recall.RunAsync(request, cancellationToken);
    }

    /// <summary>
    /// How often <c>recall</c> has returned each page's units, for every page in the index. Derived state: <c>reindex</c>
    /// resets it, and no search reads it.
    /// </summary>
    public IReadOnlyList<PageUsage> PageUsages() =>
        _index.PageUsages()
            .Select(row => new PageUsage(row.Page, (int)row.Count, row.Last is null ? null : DateOnly.ParseExact(row.Last, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)))
            .ToList();

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
        RecordToolCall("write_recent", request.Op, Targets(request.Replaces, request.Hash), () => WriteRecentCoreAsync(request, cancellationToken));

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
        using var hold = await _gate.MaintenanceAsync(cancellationToken);
        await Task.WhenAll(Settled(_vectors.Background), Settled(_judgments.Completion), Settled(_backup.Completion));
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
        if (config)
            ApplyPrompts();
        InvalidateInstructions();

        if (config)
        {
            notices.Add(RestoreMessages.ConfigNotice.In(language));
            notices.Add(RestoreMessages.PromptsApplied.In(language));
            if (restored.Problem is not null)
                notices.Add(RestoreMessages.ConfigProblem.Format(language, restored.Problem));
            else if (!string.Equals(Path.GetFullPath(restored.MemoryPath!), Path.GetFullPath(memory), StringComparison.OrdinalIgnoreCase))
                notices.Add(RestoreMessages.MemoryPathNotice.Format(language, restored.MemoryPath, memory));
        }

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

        _log.Record(LogLevel.Information, LogMessages.RestoreApplied, head, safety, reindex.Files);
        var report = string.Join('\n', new[]
        {
            RestoreMessages.Restored.Format(language, head),
            RestoreMessages.SafetyBackup.Format(language, safety),
            reindex.Report,
        }.Concat(notices));
        return new RestoreResult(safety, head, reindex, notices, report);
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
        _watcher.Dispose();
        lock (_renderGate)
        {
            _promptsWatcher?.Dispose();
            _renderTimer.Dispose();
            _promptsTimer.Dispose();
        }
        lock (_indexTimerGate)
            _indexTimer.Dispose();
        _vectors.Background.Wait(TimeSpan.FromSeconds(5));
        _judgments.Completion.Wait(TimeSpan.FromSeconds(5));
        _backup.Completion.Wait(TimeSpan.FromSeconds(5));
        _index.Dispose();
        _models.Dispose();
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
        var paths = e is RenamedEventArgs renamed ? [e.FullPath, renamed.OldFullPath] : new[] { e.FullPath };
        foreach (var relative in paths.Select(path => Path.GetRelativePath(Config.MemoryPath, path).Replace('\\', '/')))
        {
            if (relative == PersonaFiles.Persona)
                ScheduleRender();
            if (relative is PersonaFiles.Persona or PersonaFiles.Candidates)
                ScheduleIndexSync();
            if (AffectsInstructions(relative))
                InvalidateInstructions();
            if (relative == "pages" || relative.StartsWith("pages/", StringComparison.Ordinal) || relative.StartsWith($"{RecentFile.Folder}/", StringComparison.Ordinal))
                ScheduleIndexSync();
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

    /// <summary>Whether a change to this path (relative to the repository, with forward slashes) can change the server instructions.</summary>
    private static bool AffectsInstructions(string relative) =>
        relative is PersonaFiles.Persona or PersonaFiles.Candidates or MainlineSection.RelativePath
        || relative == RecentFile.Folder || relative.StartsWith($"{RecentFile.Folder}/", StringComparison.Ordinal)
        || relative == "pages" || relative.StartsWith("pages/", StringComparison.Ordinal)
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
                var problems = new List<string>();
                var prompts = PromptFiles.Reload(_options.TacitHome, Config.Language, _prompts, problems);
                foreach (var problem in problems)
                    _log.Notify(LogLevel.Warning, Messages.PromptsNotApplied, problem);
                SkillSet.Write(_options.TacitHome, Config.Language, prompts.Skill);
                _log.Record(LogLevel.Information, LogMessages.PromptsReloaded);
                var residentChanged = prompts.Resident != _prompts.Resident;
                lock (_instructionsGate)
                {
                    _prompts = prompts;
                    ClearInstructionsLocked();
                }
                // The resident blocks show resident.md: render them with the new text, as a persona change does.
                if (residentChanged)
                    _renderTimer.Change(Debounce, Timeout.InfiniteTimeSpan);
            }
            catch (Exception e) when (e is IOException or ArgumentException)
            {
                // The editor still holds a file, or the folder is busy; try again once it settles.
                _promptsTimer.Change(Debounce, Timeout.InfiniteTimeSpan);
            }
            catch (Exception e) when (e is TacitException or UnauthorizedAccessException)
            {
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
            }
            catch (IOException)
            {
                // The editor still holds a file; try again once it settles.
                _renderTimer.Change(Debounce, Timeout.InfiniteTimeSpan);
            }
            catch (Exception e) when (e is TacitException or UnauthorizedAccessException)
            {
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
    /// replaces the files it changes, and a read still holding one of them open makes that replacement fail. Once the
    /// service is disposed, the read does not run.
    /// </summary>
    private void UnderWriteGate(Action work)
    {
        Hold hold;
        try
        {
            hold = _gate.WriteAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            work();
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
            catch (Exception e) when (e is UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
            {
                _log.Notify(LogLevel.Warning, Messages.IndexSyncFailed, e.Message);
            }
        }
    });
}
