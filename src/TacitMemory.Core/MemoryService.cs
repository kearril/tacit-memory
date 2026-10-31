using TacitMemory.Core.Clients;
using TacitMemory.Core.Pages;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Localization;

namespace TacitMemory.Core;

/// <summary>
/// The running memory service: the loaded config, the memory tools (writes run one at a time), the cached server
/// instructions, and a watcher on the memory repository that re-renders the resident blocks when <c>persona.md</c>
/// changes and refreshes the instructions when projects appear or disappear under <c>pages/</c>. Create it with
/// <see cref="Tacit.OpenService"/>.
/// </summary>
public sealed class MemoryService : IDisposable
{
    /// <summary>How long file events must stay quiet before acting on them; editors save in several steps.</summary>
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);

    private readonly TacitOptions _options;
    private readonly Action<string> _notice;
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _renderTimer;
    private readonly Lock _renderGate = new();
    private readonly Lock _instructionsGate = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly PageTools _pages;
    private string? _instructions;
    private volatile bool _disposed;

    internal MemoryService(TacitOptions options, TacitConfig config, Action<string> notice)
    {
        _options = options;
        Config = config;
        _notice = notice;
        StartupRender = Binder.RenderAll(options, config);
        _pages = new PageTools(options, config, notice);

        _renderTimer = new Timer(_ => RenderNow());
        _watcher = new FileSystemWatcher(config.MemoryPath) { IncludeSubdirectories = true, InternalBufferSize = 64 * 1024 };
        _watcher.Changed += OnChange;
        _watcher.Created += OnChange;
        _watcher.Deleted += OnChange;
        _watcher.Renamed += OnChange;
        _watcher.Error += OnOverflow;
        _watcher.EnableRaisingEvents = true;
    }

    public TacitConfig Config { get; }

    /// <summary>What happened to each bound context file when the service started.</summary>
    public IReadOnlyList<BlockRender> StartupRender { get; }

    /// <summary>The server instructions, generated on first use and kept until the project list may have changed.</summary>
    public string ServerInstructions
    {
        get
        {
            lock (_instructionsGate)
                return _instructions ??= ServerInstructionText.Build(Config.Language, ServerInstructionText.Projects(Config.MemoryPath));
        }
    }

    /// <summary>The line to show once the MCP endpoint at <paramref name="endpoint"/> accepts connections.</summary>
    public string ListeningMessage(Uri endpoint) => Messages.ServiceListening.Format(Config.Language, endpoint);

    /// <summary>The line to show when <see cref="TacitConfig.Port"/> is taken by another program.</summary>
    public string PortInUseMessage() =>
        Messages.PortInUse.Format(Config.Language, Config.Port, Path.Combine(_options.TacitHome, "config.yaml"));

    /// <summary>The <c>recall</c> tool: reads a page. Never changes a file.</summary>
    public Task<ToolResult> RecallAsync(RecallRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(_pages.Read(request));

    /// <summary>The <c>write_page</c> tool. Writes run one at a time, in the order they arrive.</summary>
    public async Task<ToolResult> WritePageAsync(WritePageRequest request, CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            return await _pages.WriteAsync(request, cancellationToken);
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
            _renderTimer.Dispose();
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
        }
    }

    /// <summary>Events were lost: assume anything may have changed.</summary>
    private void OnOverflow(object sender, ErrorEventArgs e)
    {
        InvalidateInstructions();
        ScheduleRender();
    }

    private void InvalidateInstructions()
    {
        lock (_instructionsGate)
            _instructions = null;
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
}
