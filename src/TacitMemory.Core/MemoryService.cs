using TacitMemory.Core.Configuration;
using TacitMemory.Core.Backup;
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

namespace TacitMemory.Core;

/// <summary>
/// The running memory service: the loaded config, the memory tools (writes run one at a time), the page index, the
/// cached server instructions, a watcher on the memory repository that re-renders the resident blocks when
/// <c>persona.md</c> changes, syncs the index when page, activity or persona files change, and refreshes
/// the instructions when projects appear or disappear under <c>pages/</c>, and a watcher that reloads the prompt
/// documents in <c>prompts/</c>, which also regenerates the memory skill. Create it with <see cref="Tacit.OpenService"/>.
/// </summary>
public sealed class MemoryService : IDisposable
{
    /// <summary>How long file events must stay quiet before acting on them; editors save in several steps.</summary>
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);

    private readonly TacitOptions _options;
    private readonly AutomaticBackup _backup;
    private readonly Action<string> _notice;
    private readonly FileSystemWatcher _watcher;
    private FileSystemWatcher? _promptsWatcher;
    private readonly Timer _renderTimer;
    private readonly Timer _promptsTimer;
    private readonly Timer _indexTimer;
    private readonly Lock _indexTimerGate = new();
    private readonly Lock _renderGate = new();
    private readonly Lock _instructionsGate = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly PageTools _pages;
    private readonly RecentTools _recent;
    private readonly PersonaTools _persona;
    private readonly AuxiliaryModels _models;
    private readonly MemoryIndex _index;
    private readonly VectorSync _vectors;
    private readonly RecallTool _recall;
    private PromptDocuments _prompts;
    private string? _instructions;
    private string? _instructionsStatus;
    private volatile bool _disposed;

    internal MemoryService(TacitOptions options, TacitConfig config, Action<string> notice)
    {
        _options = options;
        Config = config;
        _notice = notice;
        _prompts = config.Prompts;
        MemorySkill.Write(options.TacitHome, config.Prompts.Skill);
        StartupRender = Binder.RenderAll(options, config, config.Prompts);
        _models = new AuxiliaryModels(config, options.Time);
        _index = new MemoryIndex(config.MemoryPath, config.Models.Embedding.Model, config.Language);
        _index.SyncAll();
        _vectors = new VectorSync(_index, _models.Embedding);
        _vectors.FillInBackground();
        _pages = new PageTools(
            options, config, notice, new DuplicatePageCheck(config, _index, _models.Embedding, _vectors, _models.Decision));
        _recent = new RecentTools(options, config, notice, _models.Decision);
        _persona = new PersonaTools(
            options, config, notice,
            new SamePreferenceCheck(config, _index, _models.Embedding, _vectors, _models.Decision),
            new RuleConflictCheck(config, _index, _models.Embedding, _vectors, _models.Decision));
        _recall = new RecallTool(config, _index, _pages, _models.Embedding, _models.Reranker, _vectors);
        _backup = new AutomaticBackup(options, config, notice);

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
    /// The server instructions, generated on first use and kept until a prompt document or the project list may have
    /// changed, or the status (unavailable models, skipped files) differs from when they were generated.
    /// </summary>
    public string ServerInstructions
    {
        get
        {
            var unavailable = _models.Unavailable;
            var skipped = _index.SkippedFiles;
            var status = string.Join("|", unavailable) + "\n" + string.Join("|", skipped) + "\n" + _options.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            lock (_instructionsGate)
            {
                if (_instructions is null || _instructionsStatus != status)
                {
                    var recent = RecentSummary.Read(Config.MemoryPath, _options.Today);
                    _instructions = ServerInstructionText.Assemble(
                        Config.Language, _prompts, unavailable, skipped, recent,
                        ServerInstructionText.Projects(Config.MemoryPath, recent.ProjectLabels)).Text;
                    _instructionsStatus = status;
                }

                return _instructions;
            }
        }
    }

    /// <summary>The text of the <c>remember</c> prompt for the given focus, built from the current activities and mainline.</summary>
    public string RememberPrompt(string? focus = null) => RememberText.Render(_options, Config, focus);

    /// <summary>The line to show once the MCP endpoint at <paramref name="endpoint"/> accepts connections.</summary>
    public string ListeningMessage(Uri endpoint) => Messages.ServiceListening.Format(Config.Language, endpoint);

    /// <summary>The line to show when <see cref="TacitConfig.Port"/> is taken by another program.</summary>
    public string PortInUseMessage() =>
        Messages.PortInUse.Format(Config.Language, Config.Port, Path.Combine(_options.TacitHome, "config.yaml"));

    /// <summary>Completes when the backup started by the latest successful write has finished. Tests await it.</summary>
    internal Task BackupCompletion => _backup.Completion;

    /// <summary>Completes when the vectors being filled in the background are stored. Tests await it.</summary>
    internal Task VectorCompletion => _vectors.Background;

    /// <summary>The <c>recall</c> tool: searches pages or reads one. Never changes a file.</summary>
    public Task<ToolResult> RecallAsync(RecallRequest request, CancellationToken cancellationToken = default) =>
        _recall.RunAsync(request, cancellationToken);

    /// <summary>The <c>write_page</c> tool. Writes run one at a time, in the order they arrive.</summary>
    public async Task<ToolResult> WritePageAsync(WritePageRequest request, CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            var result = await _pages.WriteAsync(request, cancellationToken);
            // The index follows the file right away; the watcher's later event finds the hash unchanged.
            if (PageId.TryParse(request.Page ?? "", out var id, out _))
                _index.SyncPage(id);
            if (result.IsError)
                return result;
            _backup.ScheduleIfDue();
            return await _vectors.FillAsync(cancellationToken)
                ? result
                : result with { Text = result.Text + "\n\nNote: the embedding model is unavailable, so until it is back this page is found by full-text search only." };
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>The <c>write_recent</c> tool. Shares the write queue with <c>write_page</c>, so writes land in arrival order.</summary>
    public async Task<ToolResult> WriteRecentAsync(WriteRecentRequest request, CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken);
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
            _writeGate.Release();
        }
    }

    /// <summary>
    /// The <c>write_persona</c> tool. Shares the write queue with the other writes. A written persona re-renders the bound
    /// resident blocks the same way a manual edit of <c>persona.md</c> does: debounced, and a failure only tells the user.
    /// </summary>
    public async Task<ToolResult> WritePersonaAsync(WritePersonaRequest request, CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            // Judgments read the index, so it follows a manual edit first and this write right after; the watcher's later event finds the hash unchanged.
            _index.SyncJudgments();
            var result = await _persona.WriteAsync(request, cancellationToken);
            _index.SyncJudgments();
            if (!result.Written)
                return result;
            // Only a change to persona.md reaches the resident context; a candidate waits for confirmation first.
            if (request.Op != "propose")
                ScheduleRender();
            _backup.ScheduleIfDue();
            return result;
        }
        finally
        {
            _writeGate.Release();
        }
    }

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
        _backup.Completion.Wait(TimeSpan.FromSeconds(5));
        _index.Dispose();
        _models.Dispose();
        _writeGate.Dispose();
    }

    private void OnChange(object sender, FileSystemEventArgs e)
    {
        var paths = e is RenamedEventArgs renamed ? [e.FullPath, renamed.OldFullPath] : new[] { e.FullPath };
        foreach (var relative in paths.Select(path => Path.GetRelativePath(Config.MemoryPath, path).Replace('\\', '/')))
        {
            if (relative == PersonaFiles.Persona)
                ScheduleRender();
            if (relative is PersonaFiles.Persona or PersonaFiles.Candidates)
                ScheduleIndexSync();
            if (relative == "pages" || (relative.StartsWith("pages/", StringComparison.Ordinal) && relative.IndexOf('/', "pages/".Length) < 0))
                InvalidateInstructions();
            if (relative == MainlineSection.RelativePath || relative == RecentFile.Folder || relative.StartsWith($"{RecentFile.Folder}/", StringComparison.Ordinal))
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
            _instructions = null;
    }

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
    private void ReloadPrompts()
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
                    _notice(Messages.PromptsNotApplied.Format(Config.Language, problem));
                MemorySkill.Write(_options.TacitHome, prompts.Skill);
                var residentChanged = prompts.Resident != _prompts.Resident;
                lock (_instructionsGate)
                {
                    _prompts = prompts;
                    _instructions = null;
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
                _notice(Messages.PromptsNotApplied.Format(Config.Language, e.Message));
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

    /// <summary>Runs on the timer thread; renders never overlap, and no failure may escape and end the process.</summary>
    private void RenderNow()
    {
        lock (_renderGate)
        {
            if (_disposed)
                return;
            try
            {
                foreach (var result in Binder.RenderAll(_options, Config, _prompts))
                    _notice(result.Message);
            }
            catch (IOException)
            {
                // The editor still holds a file; try again once it settles.
                _renderTimer.Change(Debounce, Timeout.InfiniteTimeSpan);
            }
            catch (Exception e) when (e is TacitException or UnauthorizedAccessException)
            {
                _notice(Messages.RenderFailed.Format(Config.Language, e.Message));
            }
        }
    }

    private void ScheduleIndexSync()
    {
        lock (_indexTimerGate)
        {
            if (!_disposed)
                _indexTimer.Change(Debounce, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Runs on the timer thread; compares every page file's hash with the index, so missed events do no harm.</summary>
    private void SyncIndexNow()
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
                _notice(Messages.IndexSyncFailed.Format(Config.Language, e.Message));
            }
        }
    }
}
