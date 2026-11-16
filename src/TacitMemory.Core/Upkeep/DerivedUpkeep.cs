using Microsoft.Extensions.Logging;
using TacitMemory.Core.Clients;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Logging;
using TacitMemory.Core.Maintenance;

namespace TacitMemory.Core.Upkeep;

/// <summary>What the derived products are made from in the runtime open now, read afresh each time it is asked for.</summary>
/// <param name="Config">The config the runtime runs.</param>
/// <param name="Prompts">The prompt documents the service runs.</param>
/// <param name="Backup">The automatic backup in progress, or a completed task.</param>
/// <param name="Vectors">The vector fill in progress, or a completed task.</param>
/// <param name="Judgments">The consolidation judgments in progress, or a completed task.</param>
internal sealed record UpkeepRuntime(TacitConfig Config, PromptDocuments Prompts, Task Backup, Task Vectors, Task Judgments);

/// <summary>
/// Keeps the products derived from the memory files in step with them: it hears which source changed, schedules the
/// refreshes that change touches, waits until the sources stay quiet, then runs each refresh under the write gate.
/// It belongs to the service's shell, so a reload does not rebuild it; it reads the runtime open now through an accessor.
/// <para>
/// Every refresh runs the same way: a debounced timer on <see cref="TacitOptions.Time"/>; a transient failure (a file held
/// open, a folder busy) is tried again after the same interval, up to <see cref="RetryLimit"/> times, then reported to the
/// user, and the next change starts again; any other failure is reported at once. Nothing escapes a timer or a callback.
/// </para>
/// </summary>
internal sealed class DerivedUpkeep : IDisposable
{
    /// <summary>How long sources must stay quiet before a refresh runs; editors save in several steps.</summary>
    internal static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);

    /// <summary>How many times a refresh is tried again after a transient failure before the user is told.</summary>
    internal const int RetryLimit = 5;

    private readonly TacitOptions _options;
    private readonly ServiceLog _log;
    private readonly MaintenanceGate _gate;
    private readonly Func<UpkeepRuntime> _runtime;
    private readonly Debounced _resident;
    private readonly HashSet<Task> _running = [];
    private ITimer _dayTimer;
    private bool _disposed;

    public DerivedUpkeep(TacitOptions options, ServiceLog log, MaintenanceGate gate, Func<UpkeepRuntime> runtime)
    {
        _options = options;
        _log = log;
        _gate = gate;
        _runtime = runtime;
        _resident = new Debounced(this, RenderResident, Messages.RenderFailed);
        _dayTimer = ArmDayTimer();
    }

    /// <summary>
    /// The lock held by everything that writes the context files or the skills: the refreshes, a binding and a reload. A
    /// timer or callback checks under it whether the service is disposed.
    /// </summary>
    internal Lock Gate { get; } = new();

    /// <summary>A file or folder in the memory repository changed; <paramref name="relative"/> uses forward slashes.</summary>
    public void MemoryChanged(string relative)
    {
        if (relative == MemoryLayout.Persona || AffectsResident(relative))
            _resident.Schedule();
    }

    /// <summary>A watcher lost events: anything may have changed.</summary>
    public void EventsLost() => _resident.Schedule();

    /// <summary>A write tool has written a source of this kind.</summary>
    public void Written(WrittenSource source)
    {
        // Only a change to the persona reaches the resident block; a candidate waits for confirmation first.
        if (source == WrittenSource.Persona)
            _resident.Schedule();
    }

    /// <summary><c>resident.md</c> now has other text, which the resident blocks show.</summary>
    public void ResidentPromptChanged() => _resident.Schedule();

    /// <summary>
    /// Renders the resident blocks now with <paramref name="config"/> and <paramref name="prompts"/>, under <see cref="Gate"/>,
    /// and drops a render already scheduled, which this one makes needless. Failures are raised to the caller.
    /// </summary>
    public IReadOnlyList<BlockRender> RenderNow(TacitConfig config, PromptDocuments prompts)
    {
        lock (Gate)
        {
            _resident.Cancel();
            return Binder.RenderAll(_options, config, prompts);
        }
    }

    /// <summary>
    /// Completes once the derived products have caught up: the refreshes a change scheduled run now rather than at the end
    /// of their wait (a retry still waits for its interval, as only the file's holder can end it), and then the automatic backup, the vector fill and the consolidation judgments are waited for. Their
    /// failures are reported where they happen, so they end the wait as success does.
    /// </summary>
    public async Task SettledAsync()
    {
        _resident.Flush();
        Task[] running;
        lock (Gate)
            running = [.. _running];
        await Task.WhenAll(running);
        var runtime = _runtime();
        await Task.WhenAll(Settled(runtime.Backup), Settled(runtime.Vectors), Settled(runtime.Judgments));
    }

    /// <summary>
    /// Runs <paramref name="work"/> between the writes: a write replaces the files it changes, and a read still holding one
    /// of them open makes that replacement fail. The wait for the write slot is asynchronous, so a timer thread is never
    /// blocked. Once the gate is disposed the work does not run; a failure is reported, never raised.
    /// </summary>
    internal Task UnderWriteGate(Action work)
    {
        var run = RunUnderWriteGateAsync(work);
        lock (Gate)
            _running.Add(run);
        _ = run.ContinueWith(
            done =>
            {
                lock (Gate)
                    _running.Remove(done);
            },
            TaskScheduler.Default);
        return run;
    }

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
            _log.Notify(LogLevel.Warning, Messages.BackgroundFailed, e.Message);
        }
        finally
        {
            hold.Dispose();
        }
    }

    /// <summary>A render that wrote or left its file alone is information; one that failed is a warning.</summary>
    internal static LogLevel LevelOf(BlockRender render) =>
        render.State is BlockState.Written or BlockState.Unchanged ? LogLevel.Information : LogLevel.Warning;

    private void RenderResident()
    {
        var runtime = _runtime();
        foreach (var result in Binder.RenderAll(_options, runtime.Config, runtime.Prompts))
            _log.Notify(LevelOf(result), result.Message);
    }

    /// <summary>
    /// Whether a change to this path (relative to the repository, with forward slashes) changes the resident block: the
    /// <c>workstreams.md</c>, an activity month file (its counts and its summaries), or a workstream folder directly under <c>pages/</c>.
    /// </summary>
    private static bool AffectsResident(string relative) =>
        relative == MemoryLayout.Workstreams
        || MemoryLayout.IsIn(relative, MemoryLayout.Recent)
        || relative == MemoryLayout.Pages || (relative.StartsWith($"{MemoryLayout.Pages}/", StringComparison.Ordinal) && relative.IndexOf('/', MemoryLayout.Pages.Length + 1) < 0);

    /// <summary>Arms the timer for the next local midnight: the resident block shows dates, so it is rendered again then.</summary>
    private ITimer ArmDayTimer() => _options.Time.CreateTimer(_ => OnDayChanged(), null, UntilNextMidnight(), Timeout.InfiniteTimeSpan);

    /// <summary>The time from now until the next local midnight; always positive.</summary>
    private TimeSpan UntilNextMidnight()
    {
        var now = _options.Time.GetLocalNow().DateTime;
        return now.Date.AddDays(1) - now;
    }

    private void OnDayChanged()
    {
        try
        {
            lock (Gate)
            {
                if (_disposed)
                    return;
                _dayTimer.Dispose();
                _dayTimer = ArmDayTimer();
            }
            _resident.Schedule();
        }
        catch (Exception e)
        {
            _log.Notify(LogLevel.Warning, Messages.BackgroundFailed, e.Message);
        }
    }

    /// <summary>A task that completes once <paramref name="task"/> has, whether it finished or failed.</summary>
    private static Task Settled(Task task) => task.ContinueWith(_ => { }, TaskScheduler.Default);

    public void Dispose()
    {
        lock (Gate)
        {
            _disposed = true;
            _resident.Dispose();
            _dayTimer.Dispose();
        }
    }

    /// <summary>
    /// One refresh: a debounced timer, its retry count, and whether it waits to run. Its state is guarded by the owner's
    /// <see cref="Gate"/>.
    /// </summary>
    private sealed class Debounced : IDisposable
    {
        private readonly DerivedUpkeep _owner;
        private readonly Action _work;
        private readonly Localized _failed;
        private readonly ITimer _timer;
        private int _retries;
        private bool _pending;

        /// <summary>
        /// Whether the scheduled run is a retry: it waits for a file to be let go, which settling cannot hasten, so a flush
        /// leaves it to its timer.
        /// </summary>
        private bool _retrying;

        public Debounced(DerivedUpkeep owner, Action work, Localized failed)
        {
            _owner = owner;
            _work = work;
            _failed = failed;
            _timer = owner._options.Time.CreateTimer(_ => OnTimer(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        /// <summary>Runs the refresh once the sources have been quiet for <see cref="Debounce"/>.</summary>
        public void Schedule()
        {
            lock (_owner.Gate)
            {
                if (_owner._disposed)
                    return;
                _pending = true;
                _retrying = false;
                _timer.Change(Debounce, Timeout.InfiniteTimeSpan);
            }
        }

        /// <summary>Drops a scheduled run; a refresh made now replaces it.</summary>
        public void Cancel()
        {
            lock (_owner.Gate)
            {
                _pending = false;
                _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }
        }

        /// <summary>Starts a run scheduled by a change now instead of at the end of its wait; a retry keeps waiting.</summary>
        public void Flush()
        {
            lock (_owner.Gate)
            {
                if (!_pending || _retrying || _owner._disposed)
                    return;
                _pending = false;
                _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }
            _owner.UnderWriteGate(Run);
        }

        private void OnTimer()
        {
            try
            {
                lock (_owner.Gate)
                {
                    if (!_pending || _owner._disposed)
                        return;
                    _pending = false;
                }
                _owner.UnderWriteGate(Run);
            }
            catch (Exception e)
            {
                _owner._log.Notify(LogLevel.Warning, Messages.BackgroundFailed, e.Message);
            }
        }

        private void Run()
        {
            lock (_owner.Gate)
            {
                if (_owner._disposed)
                    return;
                try
                {
                    _work();
                    _retries = 0;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException && _retries < RetryLimit)
                {
                    // A file is still held open, which Windows refuses to replace; try again once it settles.
                    _retries++;
                    _pending = true;
                    _retrying = true;
                    _timer.Change(Debounce, Timeout.InfiniteTimeSpan);
                }
                catch (Exception e)
                {
                    _retries = 0;
                    _owner._log.Notify(LogLevel.Warning, _failed, e.Message);
                }
            }
        }

        public void Dispose() => _timer.Dispose();
    }
}
