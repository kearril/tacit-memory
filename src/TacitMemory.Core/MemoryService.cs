using TacitMemory.Core.Clients;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Localization;

namespace TacitMemory.Core;

/// <summary>
/// The running memory service's state: the loaded config, the cached server instructions, and a watcher on the
/// memory repository that re-renders the resident blocks when <c>persona.md</c> changes and refreshes the
/// instructions when projects appear or disappear under <c>pages/</c>. Create it with <see cref="Tacit.OpenService"/>.
/// </summary>
public sealed class MemoryService : IDisposable
{
    /// <summary>How long file events must stay quiet before acting on them; editors save in several steps.</summary>
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);

    private readonly TacitOptions _options;
    private readonly Action<BlockRender> _rendered;
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _personaTimer;
    private readonly Lock _gate = new();
    private string? _instructions;

    internal MemoryService(TacitOptions options, TacitConfig config, Action<BlockRender> rendered)
    {
        _options = options;
        Config = config;
        _rendered = rendered;
        StartupRender = Binder.RenderAll(options, config);

        _personaTimer = new Timer(_ => RenderNow());
        _watcher = new FileSystemWatcher(config.MemoryPath) { IncludeSubdirectories = true, InternalBufferSize = 64 * 1024 };
        _watcher.Changed += OnChange;
        _watcher.Created += OnChange;
        _watcher.Deleted += OnChange;
        _watcher.Renamed += OnChange;
        _watcher.EnableRaisingEvents = true;
    }

    public TacitConfig Config { get; }

    /// <summary>What happened to each bound context file when the service started.</summary>
    public IReadOnlyList<BlockRender> StartupRender { get; }

    /// <summary>The server instructions, generated on first use and kept until the memory repository changes.</summary>
    public string ServerInstructions
    {
        get
        {
            lock (_gate)
                return _instructions ??= Injection.ServerInstructions.Build(Config.Language, Injection.ServerInstructions.Projects(Config.MemoryPath));
        }
    }

    /// <summary>The line to show once the MCP endpoint at <paramref name="endpoint"/> accepts connections.</summary>
    public string ListeningMessage(Uri endpoint) => Messages.ServiceListening.Format(Config.Language, endpoint);

    /// <summary>The line to show when <see cref="TacitConfig.Port"/> is taken by another program.</summary>
    public string PortInUseMessage() =>
        Messages.PortInUse.Format(Config.Language, Config.Port, Path.Combine(_options.TacitHome, "config.yaml"));

    public void Dispose()
    {
        _watcher.Dispose();
        _personaTimer.Dispose();
    }

    private void OnChange(object sender, FileSystemEventArgs e)
    {
        var relative = Path.GetRelativePath(Config.MemoryPath, e.FullPath).Replace('\\', '/');
        var oldRelative = e is RenamedEventArgs renamed ? Path.GetRelativePath(Config.MemoryPath, renamed.OldFullPath).Replace('\\', '/') : null;
        if (relative == "persona.md" || oldRelative == "persona.md")
            _personaTimer.Change(Debounce, Timeout.InfiniteTimeSpan);
        if (IsProjectDirectory(relative) || (oldRelative is not null && IsProjectDirectory(oldRelative)))
        {
            lock (_gate)
                _instructions = null;
        }
    }

    /// <summary>A direct child of <c>pages/</c>: creating, deleting or renaming one changes the project list.</summary>
    private static bool IsProjectDirectory(string relative) =>
        relative.StartsWith("pages/", StringComparison.Ordinal) && relative.IndexOf('/', "pages/".Length) < 0;

    private void RenderNow()
    {
        try
        {
            foreach (var result in Binder.RenderAll(_options, Config))
                _rendered(result);
        }
        catch (IOException)
        {
            // The editor still holds a file; try again once it settles.
            _personaTimer.Change(Debounce, Timeout.InfiniteTimeSpan);
        }
    }
}
