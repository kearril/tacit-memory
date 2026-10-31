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

namespace TacitMemory.Core;

/// <summary>
/// The running memory service: the loaded config, the memory tools (writes run one at a time), the page index, the
/// cached server instructions, a watcher on the memory repository that re-renders the resident blocks when
/// <c>persona.md</c> changes, syncs the index when page files change, and refreshes the instructions when projects
/// appear or disappear under <c>pages/</c>, and a watcher that reloads <c>rules.md</c>. Create it with
/// <see cref="Tacit.OpenService"/>.
/// </summary>
public sealed class MemoryService : IDisposable
{
    /// <summary>How long file events must stay quiet before acting on them; editors save in several steps.</summary>
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);

    private readonly TacitOptions _options;
    private readonly AutomaticBackup _backup;
    private readonly Action<string> _notice;
    private readonly FileSystemWatcher _watcher;
    private readonly FileSystemWatcher _rulesWatcher;
    private readonly Timer _renderTimer;
    private readonly Timer _rulesTimer;
    private readonly Timer _indexTimer;
    private readonly Lock _indexTimerGate = new();
    private readonly Lock _renderGate = new();
    private readonly Lock _instructionsGate = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly PageTools _pages;
    private readonly AuxiliaryModels _models;
    private readonly PageIndex _index;
    private readonly VectorSync _vectors;
    private readonly RecallTool _recall;
    private RecordingCriteria _rules;
    private string? _instructions;
    private string? _instructionsStatus;
    private volatile bool _disposed;

    internal MemoryService(TacitOptions options, TacitConfig config, Action<string> notice)
    {
        _options = options;
        Config = config;
        _notice = notice;
        _rules = config.Rules;
        ServerInstructionText.Check(config.Language, _rules, RulesPath);
        StartupRender = Binder.RenderAll(options, config);
        _models = new AuxiliaryModels(config, options.Time);
        _index = new PageIndex(config.MemoryPath, config.Models.Embedding.Model, config.Language);
        _index.SyncAll();
        _vectors = new VectorSync(_index, _models.Embedding);
        _vectors.FillInBackground();
        _pages = new PageTools(
            options, config, notice, new CredentialCheck(config, _models.Decision), new DuplicatePageCheck(config, _index, _models.Embedding, _vectors, _models.Decision));
        _recall = new RecallTool(config, _index, _pages, _models.Embedding, _models.Reranker, _vectors);
        _backup = new AutomaticBackup(options, config, notice);

        _renderTimer = new Timer(_ => RenderNow());
        _rulesTimer = new Timer(_ => ReloadRules());
        _indexTimer = new Timer(_ => SyncIndexNow());
        _watcher = new FileSystemWatcher(config.MemoryPath) { IncludeSubdirectories = true, InternalBufferSize = 64 * 1024 };
        _watcher.Changed += OnChange;
        _watcher.Created += OnChange;
        _watcher.Deleted += OnChange;
        _watcher.Renamed += OnChange;
        _watcher.Error += OnOverflow;
        _watcher.EnableRaisingEvents = true;

        _rulesWatcher = new FileSystemWatcher(options.TacitHome, "rules.md");
        _rulesWatcher.Changed += OnRulesChange;
        _rulesWatcher.Created += OnRulesChange;
        _rulesWatcher.Deleted += OnRulesChange;
        _rulesWatcher.Renamed += OnRulesChange;
        _rulesWatcher.EnableRaisingEvents = true;
    }

    public TacitConfig Config { get; }

    /// <summary>What happened to each bound context file when the service started.</summary>
    public IReadOnlyList<BlockRender> StartupRender { get; }

    /// <summary>
    /// The server instructions, generated on first use and kept until <c>rules.md</c> or the project list may have
    /// changed, or the status (unavailable models, skipped files) differs from when they were generated.
    /// </summary>
    public string ServerInstructions
    {
        get
        {
            var unavailable = _models.Unavailable;
            var skipped = _index.SkippedFiles;
            var status = string.Join("|", unavailable) + "\n" + string.Join("|", skipped);
            lock (_instructionsGate)
            {
                if (_instructions is null || _instructionsStatus != status)
                {
                    _instructions = ServerInstructionText.Build(
                        Config.Language, _rules, unavailable, skipped, ServerInstructionText.Projects(Config.MemoryPath));
                    _instructionsStatus = status;
                }

                return _instructions;
            }
        }
    }

    private string RulesPath => Path.Combine(_options.TacitHome, "rules.md");

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

    public void Dispose()
    {
        _disposed = true;
        _watcher.Dispose();
        _rulesWatcher.Dispose();
        lock (_renderGate)
        {
            _renderTimer.Dispose();
            _rulesTimer.Dispose();
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
            if (relative == "persona.md")
                ScheduleRender();
            if (relative == "pages" || (relative.StartsWith("pages/", StringComparison.Ordinal) && relative.IndexOf('/', "pages/".Length) < 0))
                InvalidateInstructions();
            if (relative == "pages" || relative.StartsWith("pages/", StringComparison.Ordinal))
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

    private void OnRulesChange(object sender, FileSystemEventArgs e)
    {
        lock (_renderGate)
        {
            if (!_disposed)
                _rulesTimer.Change(Debounce, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Runs on the timer thread. Rules that fail their checks are reported and the previous ones kept.</summary>
    private void ReloadRules()
    {
        lock (_renderGate)
        {
            if (_disposed)
                return;
            try
            {
                var rules = RulesFile.Load(_options.TacitHome, Config.Language);
                ServerInstructionText.Check(Config.Language, rules, RulesPath);
                lock (_instructionsGate)
                {
                    _rules = rules;
                    _instructions = null;
                }
            }
            catch (IOException)
            {
                // The editor still holds the file; try again once it settles.
                _rulesTimer.Change(Debounce, Timeout.InfiniteTimeSpan);
            }
            catch (Exception e) when (e is TacitException or UnauthorizedAccessException)
            {
                _notice(Messages.RulesNotApplied.Format(Config.Language, e.Message));
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
                foreach (var result in Binder.RenderAll(_options, Config))
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
