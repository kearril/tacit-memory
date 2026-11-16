using System.Globalization;
using Microsoft.Extensions.Logging;
using TacitMemory.Core.Clients;
using TacitMemory.Core.Configuration;
using TacitMemory.Core.Consolidation;
using TacitMemory.Core.Index;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Logging;
using TacitMemory.Core.Maintenance;
using TacitMemory.Core.Models;
using TacitMemory.Core.Recent;
using TacitMemory.Core.Skills;

namespace TacitMemory.Core.Upkeep;

/// <summary>What the derived products are made from in the runtime open now, read afresh each time it is asked for.</summary>
/// <param name="Config">The config the runtime runs.</param>
/// <param name="Sources">Where the runtime hears of changes to the sources.</param>
/// <param name="Index">The derived index.</param>
/// <param name="Vectors">The vector fill, which an index sync starts in the background.</param>
/// <param name="Models">The auxiliary models, whose availability the server instructions report.</param>
/// <param name="Backup">The automatic backup in progress, or a completed task.</param>
/// <param name="Judgments">The consolidation judgments in progress, or a completed task.</param>
internal sealed record UpkeepRuntime(
    TacitConfig Config, ISourceWatch Sources, MemoryIndex Index, VectorSync Vectors, AuxiliaryModels Models, Task Backup, Task Judgments);

/// <summary>The derived products a refresh made now brings up to date; they combine.</summary>
[Flags]
internal enum Refresh
{
    None = 0,

    /// <summary>Read the prompt documents again, as a change to <c>prompts/</c> does: the skills, the instructions and, when <c>resident.md</c> changed, a scheduled render follow.</summary>
    Prompts = 1,

    /// <summary>Write the skills from the prompt documents the service runs.</summary>
    Skills = 2,

    /// <summary>Render the resident blocks; what went wrong is returned.</summary>
    Resident = 4,

    /// <summary>Drop the cached server instructions.</summary>
    Instructions = 8,
}

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
    private readonly Debounced _prompts;
    private readonly Lock _promptsGate = new();
    private PromptDocuments _promptDocuments = null!;
    private IReadOnlyDictionary<PromptName, string> _promptProblems = new Dictionary<PromptName, string>();
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
        _resident = new Debounced(this, RenderResident, Messages.RenderFailed, RenderLock);
        _index = new Debounced(this, SyncIndex, Messages.IndexSyncFailed, exclusive: null);
        // Watching prompts/ afresh throws ArgumentException while the folder is being replaced.
        _prompts = new Debounced(this, ReloadPrompts, Messages.PromptsNotApplied, RenderLock, e => e is ArgumentException);
        _dayTimer = ArmDayTimer();
    }

    /// <summary>
    /// The lock held by everything that writes the context files or the skills: a render, a prompt reload, a refresh made
    /// now and a binding. The index sync does not take it.
    /// </summary>
    internal Lock RenderLock { get; } = new();

    /// <summary>
    /// Guards the state of the refreshes: what waits to run, the runs started, the retry counts and whether the upkeep is
    /// disposed. It is held only briefly, never while a refresh runs, so a watcher's thread is not kept waiting.
    /// </summary>
    private readonly Lock _state = new();

    /// <summary>A file or folder in the memory repository changed; <paramref name="relative"/> uses forward slashes.</summary>
    public void MemoryChanged(string relative)
    {
        if (AffectsResident(relative))
            _resident.Schedule();
        if (AffectsIndex(relative))
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
            var text = ServerInstructionText.Assemble(runtime.Config.Language, PromptState.Running, unavailable, skipped, recent, consolidation).Text;

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

    /// <summary>A file in <c>prompts/</c> changed, or its watcher lost events or failed.</summary>
    public void PromptsChanged() => _prompts.Schedule();

    /// <summary>The prompt documents the service runs, and the refusal of each one whose file it does not run.</summary>
    public (PromptDocuments Running, IReadOnlyDictionary<PromptName, string> Problems) PromptState
    {
        get
        {
            lock (_promptsGate)
                return (_promptDocuments, _promptProblems);
        }
    }

    /// <summary>Runs <paramref name="prompts"/>, as a runtime opened with a config that loads has every document as its file holds it.</summary>
    public void UsePrompts(PromptDocuments prompts)
    {
        lock (_promptsGate)
        {
            _promptDocuments = prompts;
            _promptProblems = new Dictionary<PromptName, string>();
        }
    }

    /// <summary>
    /// Brings the derived products in <paramref name="what"/> up to date now, in the caller's hold on the gate, and drops
    /// the refreshes of them already scheduled, which this makes needless. Returns what went wrong for the user to see: a
    /// resident block not written, or a render that failed. A prompt reload reports its own problems as the timed one does.
    /// </summary>
    public IReadOnlyList<string> RefreshNow(Refresh what)
    {
        var notices = new List<string>();
        lock (RenderLock)
        {
            if (what.HasFlag(Refresh.Prompts))
                _prompts.RunNow();
            if (what.HasFlag(Refresh.Skills))
                SkillSet.Write(_options.TacitHome, _runtime().Config.Language, PromptState.Running.Skill);
            if (what.HasFlag(Refresh.Instructions))
                InvalidateInstructions();
            if (what.HasFlag(Refresh.Resident))
                RenderResidentNow(notices);
        }
        return notices;
    }

    /// <summary>
    /// Completes once the derived products have caught up: the refreshes a change scheduled run now rather than at the end
    /// of their wait, and the runs under way finish; a retry still waits for its interval, as only the file's holder can
    /// end it. Then the automatic backup, the vector fill and the consolidation judgments are waited for. Their failures
    /// are reported where they happen, so they end the wait as success does.
    /// </summary>
    public async Task SettledAsync()
    {
        // A refresh can schedule another, as a prompt reload that changed resident.md schedules a render: go on until no
        // refresh starts and none runs.
        while (true)
        {
            var started = _prompts.Flush() | _resident.Flush() | _index.Flush();
            Task[] running;
            lock (_state)
                running = [.. _running];
            if (!started && running.Length == 0)
                break;
            await Task.WhenAll(running);
        }
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
        lock (_state)
            return Start(work);
    }

    /// <summary>
    /// Starts <paramref name="work"/> under the write gate and counts it among the runs under way, in the caller's hold on
    /// <see cref="_state"/>: a refresh that leaves the waiting state is a run under way in the same step, so settling
    /// cannot see it as neither.
    /// </summary>
    private Task Start(Action work)
    {
        var run = Task.Run(() => RunUnderWriteGateAsync(work));
        _running.Add(run);
        _ = run.ContinueWith(
            done =>
            {
                lock (_state)
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

    /// <summary>Renders the resident blocks of every binding with the config and the prompt documents the service runs.</summary>
    private IReadOnlyList<BlockRender> RenderAll() => Binder.RenderAll(_options, _runtime().Config, PromptState.Running);

    private void RenderResident()
    {
        foreach (var result in RenderAll())
            _log.Notify(LevelOf(result), result.Message);
    }

    /// <summary>Renders the resident blocks now, adding what went wrong to <paramref name="notices"/>; nothing is raised.</summary>
    private void RenderResidentNow(List<string> notices)
    {
        _resident.Cancel();
        try
        {
            foreach (var render in RenderAll())
            {
                _log.Record(LevelOf(render), render.Message);
                if (LevelOf(render) != LogLevel.Information)
                    notices.Add(render.Message);
            }
        }
        catch (Exception e) when (e is TacitException or IOException or UnauthorizedAccessException)
        {
            _log.Record(LogLevel.Warning, Messages.RenderFailed, e.Message);
            notices.Add(Messages.RenderFailed.Format(_runtime().Config.Language, e.Message));
        }
    }

    /// <summary>
    /// Reads the prompt documents again. Each one that fails its checks is reported and keeps its previous text; the others
    /// take their new text. The folder is watched afresh first, so a change made while reading is seen by the next reload.
    /// </summary>
    private void ReloadPrompts()
    {
        var runtime = _runtime();
        runtime.Sources.WatchPromptsAgain();
        var language = runtime.Config.Language;
        var current = PromptState.Running;
        var problems = new Dictionary<PromptName, string>();
        var prompts = PromptFiles.Reload(_options.TacitHome, language, current, problems);
        foreach (var problem in problems.Values.Distinct())
            _log.Notify(LogLevel.Warning, Messages.PromptsNotApplied, problem);
        SkillSet.Write(_options.TacitHome, language, prompts.Skill);
        _log.Record(LogLevel.Information, LogMessages.PromptsReloaded);
        lock (_promptsGate)
        {
            _promptDocuments = prompts;
            _promptProblems = problems;
        }
        InvalidateInstructions();
        // The resident blocks show resident.md: render them with the new text, as a persona change does.
        if (prompts.Resident != current.Resident)
            _resident.Schedule();
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

    /// <summary>Whether a change to this path (relative to the repository, with forward slashes) has entries in the derived index.</summary>
    private static bool AffectsIndex(string relative) =>
        relative is MemoryLayout.Persona or MemoryLayout.Candidates
        || MemoryLayout.IsIn(relative, MemoryLayout.Pages)
        || MemoryLayout.IsIn(relative, MemoryLayout.Recent);

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
    /// persona, <c>workstreams.md</c>, an activity month file (its counts and its summaries), or a workstream folder directly under <c>pages/</c>.
    /// </summary>
    private static bool AffectsResident(string relative) =>
        relative is MemoryLayout.Persona or MemoryLayout.Workstreams
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
            lock (_state)
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
        lock (_state)
        {
            _disposed = true;
            _resident.Dispose();
            _index.Dispose();
            _prompts.Dispose();
            _dayTimer.Dispose();
        }
    }

    /// <summary>
    /// One refresh: a debounced timer, its retry count, and whether it waits to run. Its state is guarded by the owner's
    /// <see cref="_state"/>; its work runs outside that lock, under <see cref="RenderLock"/> when it writes context files.
    /// </summary>
    private sealed class Debounced : IDisposable
    {
        private readonly DerivedUpkeep _owner;
        private readonly Action _work;
        private readonly Localized _failed;
        private readonly Func<Exception, bool> _alsoTransient;
        private readonly Lock? _exclusive;
        private readonly ITimer _timer;
        private int _retries;
        private bool _pending;

        /// <summary>
        /// Whether the scheduled run is a retry: it waits for a file to be let go, which settling cannot hasten, so a flush
        /// leaves it to its timer.
        /// </summary>
        private bool _retrying;

        /// <param name="exclusive">A lock the work runs under, when it writes files that others write too.</param>
        /// <param name="alsoTransient">Failures of this refresh tried again as a held file is, beside <see cref="IOException"/> and <see cref="UnauthorizedAccessException"/>.</param>
        public Debounced(DerivedUpkeep owner, Action work, Localized failed, Lock? exclusive, Func<Exception, bool>? alsoTransient = null)
        {
            _owner = owner;
            _work = work;
            _failed = failed;
            _exclusive = exclusive;
            _alsoTransient = alsoTransient ?? (_ => false);
            _timer = owner._options.Time.CreateTimer(_ => OnTimer(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        /// <summary>Runs the refresh once the sources have been quiet for <see cref="Debounce"/>.</summary>
        public void Schedule()
        {
            lock (_owner._state)
            {
                if (_owner._disposed)
                    return;
                // A new change starts the count again: the file that was held may not be the one that matters now.
                _retries = 0;
                _pending = true;
                _retrying = false;
                _timer.Change(Debounce, Timeout.InfiniteTimeSpan);
            }
        }

        /// <summary>Drops a scheduled run; a refresh made now replaces it.</summary>
        public void Cancel()
        {
            lock (_owner._state)
            {
                if (_owner._disposed)
                    return;
                _pending = false;
                _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }
        }

        /// <summary>Runs the refresh now, in the caller's hold on the write gate, in place of a scheduled run; failures are handled as a timed run's are.</summary>
        public void RunNow()
        {
            Cancel();
            Run();
        }

        /// <summary>Starts a run scheduled by a change now instead of at the end of its wait, and says whether it did; a retry keeps waiting.</summary>
        public bool Flush()
        {
            lock (_owner._state)
            {
                if (!_pending || _retrying || _owner._disposed)
                    return false;
                _pending = false;
                _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                _owner.Start(Run);
                return true;
            }
        }

        private void OnTimer()
        {
            try
            {
                lock (_owner._state)
                {
                    if (!_pending || _owner._disposed)
                        return;
                    _pending = false;
                    _owner.Start(Run);
                }
            }
            catch (Exception e)
            {
                _owner._log.Notify(LogLevel.Warning, Messages.BackgroundFailed, e.Message);
            }
        }

        private void Run()
        {
            lock (_owner._state)
            {
                if (_owner._disposed)
                    return;
            }
            try
            {
                if (_exclusive is null)
                    _work();
                else
                    lock (_exclusive)
                        _work();
                lock (_owner._state)
                    _retries = 0;
            }
            catch (Exception e)
            {
                lock (_owner._state)
                {
                    if (_owner._disposed)
                        return;
                    if ((e is IOException or UnauthorizedAccessException || _alsoTransient(e)) && _retries < RetryLimit)
                    {
                        // A file is still held open, which Windows refuses to replace; try again once it settles.
                        _retries++;
                        _pending = true;
                        _retrying = true;
                        _timer.Change(Debounce, Timeout.InfiniteTimeSpan);
                        return;
                    }
                    _retries = 0;
                }
                _owner._log.Notify(LogLevel.Warning, _failed, e.Message);
            }
        }

        public void Dispose() => _timer.Dispose();
    }
}
