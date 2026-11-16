using Microsoft.Extensions.Logging;
using TacitMemory.Core.Configuration;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Logging;

namespace TacitMemory.Core.Upkeep;

/// <summary>
/// Where the upkeep hears that a source of the derived products changed: a file in the memory repository, a file in
/// <c>prompts/</c>, or events lost. The runtime holds one and lets go of it when it closes.
/// </summary>
internal interface ISourceWatch : IDisposable
{
    /// <summary>Stops telling the upkeep of changes, so a restore's own moves are not taken for edits by the user.</summary>
    void Pause();

    /// <summary>Tells the upkeep of changes again, watching <c>prompts/</c> afresh.</summary>
    void Resume();

    /// <summary>
    /// Watches <c>prompts/</c> afresh, creating the folder when it is gone. Each prompt reload calls it before it reads the
    /// files, so a change made while reading is seen by the next reload.
    /// </summary>
    void WatchPromptsAgain();
}

/// <summary>
/// Watches the memory repository, with its subfolders, and <c>prompts/</c>, and tells the upkeep of each change. A
/// callback never throws into the watcher's thread: anything unexpected is reported to the user.
/// </summary>
internal sealed class FileSourceWatch : ISourceWatch
{
    private readonly string _memoryPath;
    private readonly string _prompts;
    private readonly DerivedUpkeep _upkeep;
    private readonly ServiceLog _log;
    private readonly FileSystemWatcher _memory;
    private readonly Lock _gate = new();
    private FileSystemWatcher? _promptsWatcher;
    private bool _paused;
    private bool _disposed;

    /// <summary>Starts watching both; the memory repository must exist.</summary>
    public FileSourceWatch(string memoryPath, string tacitHome, DerivedUpkeep upkeep, ServiceLog log)
    {
        _memoryPath = memoryPath;
        _prompts = PromptFiles.Folder(tacitHome);
        _upkeep = upkeep;
        _log = log;
        _memory = new FileSystemWatcher(memoryPath) { IncludeSubdirectories = true, InternalBufferSize = 64 * 1024 };
        try
        {
            _memory.Changed += OnMemoryChange;
            _memory.Created += OnMemoryChange;
            _memory.Deleted += OnMemoryChange;
            _memory.Renamed += OnMemoryChange;
            _memory.Error += OnMemoryError;
            _memory.EnableRaisingEvents = true;
            WatchPromptsAgain();
        }
        catch
        {
            _memory.Dispose();
            throw;
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            _paused = true;
            _memory.EnableRaisingEvents = false;
            _promptsWatcher?.Dispose();
            _promptsWatcher = null;
        }
    }

    public void Resume()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _paused = false;
            _memory.EnableRaisingEvents = true;
        }
        WatchPromptsAgain();
    }

    public void WatchPromptsAgain()
    {
        lock (_gate)
        {
            Directory.CreateDirectory(_prompts);
            if (_disposed || _paused)
                return;
            _promptsWatcher?.Dispose();
            _promptsWatcher = new FileSystemWatcher(_prompts);
            _promptsWatcher.Changed += OnPromptsChange;
            _promptsWatcher.Created += OnPromptsChange;
            _promptsWatcher.Deleted += OnPromptsChange;
            _promptsWatcher.Renamed += OnPromptsChange;
            _promptsWatcher.Error += OnPromptsError;
            _promptsWatcher.EnableRaisingEvents = true;
        }
    }

    private void OnMemoryChange(object sender, FileSystemEventArgs e) => Tell(() =>
    {
        var paths = e is RenamedEventArgs renamed ? [e.FullPath, renamed.OldFullPath] : new[] { e.FullPath };
        foreach (var path in paths)
            _upkeep.MemoryChanged(Path.GetRelativePath(_memoryPath, path).Replace('\\', '/'));
    });

    /// <summary>Events were lost: anything may have changed.</summary>
    private void OnMemoryError(object sender, ErrorEventArgs e) => Tell(_upkeep.EventsLost);

    private void OnPromptsChange(object sender, FileSystemEventArgs e) => Tell(_upkeep.PromptsChanged);

    /// <summary>Events were lost, or the watcher failed: reload, which also watches the folder afresh.</summary>
    private void OnPromptsError(object sender, ErrorEventArgs e) => Tell(_upkeep.PromptsChanged);

    /// <summary>Runs a callback's work; a callback must not throw into the watcher's thread, and the next event catches up.</summary>
    private void Tell(Action work)
    {
        try
        {
            work();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _log.Notify(LogLevel.Warning, Messages.BackgroundFailed, e.Message);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _memory.Dispose();
            _promptsWatcher?.Dispose();
        }
    }
}

/// <summary>
/// Tells the upkeep of the changes a test names, in place of the file watchers: a test changes a file, says so here, and
/// settles the service, without waiting for a watcher to deliver the event. It serves each runtime the service opens.
/// </summary>
internal sealed class ManualSources
{
    private DerivedUpkeep? _upkeep;

    /// <summary>The watch a runtime opens: it tells <paramref name="upkeep"/> of the changes named here until it is let go.</summary>
    internal ISourceWatch Attach(DerivedUpkeep upkeep) => new Attached(this, upkeep);

    /// <summary>The file at <paramref name="relative"/> in the memory repository changed.</summary>
    public void MemoryChanged(string relative) => _upkeep?.MemoryChanged(relative);

    /// <summary>A file in <c>prompts/</c> changed.</summary>
    public void PromptsChanged() => _upkeep?.PromptsChanged();

    /// <summary>The watcher of the memory repository lost events.</summary>
    public void EventsLost() => _upkeep?.EventsLost();

    private sealed class Attached : ISourceWatch
    {
        private readonly ManualSources _owner;
        private readonly DerivedUpkeep _upkeep;

        public Attached(ManualSources owner, DerivedUpkeep upkeep)
        {
            _owner = owner;
            _upkeep = upkeep;
            owner._upkeep = upkeep;
        }

        public void Pause() => _owner._upkeep = null;

        public void Resume() => _owner._upkeep = _upkeep;

        public void WatchPromptsAgain()
        {
        }

        public void Dispose()
        {
            if (_owner._upkeep == _upkeep)
                _owner._upkeep = null;
        }
    }
}
