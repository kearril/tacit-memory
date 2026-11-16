using System.Globalization;
using Microsoft.Extensions.Logging;
using TacitMemory.Core.Clients;
using TacitMemory.Core.Consolidation;
using TacitMemory.Core.Index;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Logging;
using TacitMemory.Core.Maintenance;
using TacitMemory.Core.Models;
using TacitMemory.Core.Recent;

namespace TacitMemory.Core.Upkeep;

/// <summary>What the derived products are made from in the runtime open now, read afresh each time it is asked for.</summary>
/// <param name="Config">The config the runtime runs.</param>
/// <param name="Prompts">The prompt documents the service runs.</param>
/// <param name="Index">The derived index.</param>
/// <param name="Vectors">The vector fill, which an index sync starts in the background.</param>
/// <param name="Models">The auxiliary models, whose availability the server instructions report.</param>
/// <param name="Backup">The automatic backup in progress, or a completed task.</param>
/// <param name="Judgments">The consolidation judgments in progress, or a completed task.</param>
internal sealed record UpkeepRuntime(
    TacitConfig Config, PromptDocuments Prompts, MemoryIndex Index, VectorSync Vectors, AuxiliaryModels Models, Task Backup, Task Judgments);

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
    private readonly Debounced _index;
    private readonly Lock _instructionsGate = new();
    private readonly SemaphoreSlim _instructionsBuild = new(1, 1);
    private string? _instructions;
    private string? _instructionsStatus;
    private int _instructionsVersion;
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
        _index = new Debounced(this, SyncIndex, Messages.IndexSyncFailed);
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
        if (relative is MemoryLayout.Persona or MemoryLayout.Candidates
            || MemoryLayout.IsIn(relative, MemoryLayout.Pages) || MemoryLayout.IsIn(relative, MemoryLayout.Recent))
            _index.Schedule();
        if (AffectsInstructions(relative))
            InvalidateInstructions();
    }

    /// <summary>A watcher lost events: anything may have changed.</summary>
    public void EventsLost()
    {
        InvalidateInstructions();
        _resident.Schedule();
        _index.Schedule();
    }

    /// <summary>
    /// A write tool has written a source of this kind, and has already synced the index for it. Page counts, format
    /// problems, recent activities and the persona all feed the server instructions, so they are built again on their next
    /// use; only a change to the persona reaches the resident block, while a candidate waits for confirmation first.
    /// </summary>
    public void Written(WrittenSource source)
    {
        InvalidateInstructions();
        if (source == WrittenSource.Persona)
            _resident.Schedule();
    }

    /// <summary>Drops the cached server instructions. A build that started before this is not kept either.</summary>
    public void InvalidateInstructions()
    {
        lock (_instructionsGate)
        {
            _instructions = null;
            _instructionsVersion++;
        }
    }

    /// <summary>
    /// The server instructions, built on first use and kept until something they are built from may have changed: a
    /// prompt document, the persona or its candidates, a page or activity file, <c>workstreams.md</c> (which the consolidation
    /// reminder reads), the memory repository's commits, or the status (unavailable models, skipped files, the date). Builds
    /// run one at a time. The caller holds the read gate.
    /// </summary>
    public async Task<string> InstructionsAsync(CancellationToken cancellationToken)
    {
        await _instructionsBuild.WaitAsync(cancellationToken);
        try
        {
            var runtime = _runtime();
            var today = _options.Today;
            var unavailable = runtime.Models.Unavailable;
            var skipped = runtime.Index.SkippedFiles;
            var status = string.Join("|", unavailable) + "\n" + string.Join("|", skipped) + "\n" + today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            int version;
            lock (_instructionsGate)
            {
                if (_instructions is not null && _instructionsStatus == status)
                    return _instructions;
                version = _instructionsVersion;
            }

            var recent = RecentSummary.Read(runtime.Config.MemoryPath, today);
            var consolidation = await ConsolidationReminder.RenderAsync(runtime.Config, today, cancellationToken);
            var text = ServerInstructionText.Assemble(runtime.Config.Language, runtime.Prompts, unavailable, skipped, recent, consolidation).Text;

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
        _index.Flush();
        Task[] running;
        lock (Gate)
            running = [.. _running];
        await Task.WhenAll(running);
        var runtime = _runtime();
        await Task.WhenAll(Settled(runtime.Backup), Settled(runtime.Vectors.Background), Settled(runtime.Judgments));
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
    /// Compares every source file's hash with the index, so missed events do no harm, and fills the vectors of what changed
    /// in the background. It reads the files only under the write gate, as a write replaces them.
    /// </summary>
    private void SyncIndex()
    {
        var runtime = _runtime();
        runtime.Index.SyncAll();
        runtime.Vectors.FillInBackground();
    }

    /// <summary>Whether a change to this path (relative to the repository, with forward slashes) can change the server instructions.</summary>
    private static bool AffectsInstructions(string relative) =>
        relative is MemoryLayout.Persona or MemoryLayout.Candidates or MemoryLayout.Workstreams
        || MemoryLayout.IsIn(relative, MemoryLayout.Recent)
        || MemoryLayout.IsIn(relative, MemoryLayout.Pages)
        || IsGitHistory(relative);

    /// <summary>The files a commit moves: HEAD, the refs and their logs, and the packed refs.</summary>
    private static bool IsGitHistory(string relative) =>
        relative is ".git/HEAD" or ".git/packed-refs"
        || relative.StartsWith(".git/refs/", StringComparison.Ordinal)
        || relative.StartsWith(".git/logs/", StringComparison.Ordinal);

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
            _index.Dispose();
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
