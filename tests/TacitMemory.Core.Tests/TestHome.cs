using System.Diagnostics;
using System.Globalization;

namespace TacitMemory.Core.Tests;

/// <summary>A throwaway user home and <c>TACIT_HOME</c> under %TEMP%.</summary>
internal sealed class TestHome : IDisposable
{
    public TestHome()
    {
        Root = Path.Combine(Path.GetTempPath(), "tacit-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(UserHome);
    }

    public string Root { get; }

    public string UserHome => Path.Combine(Root, "home");

    public string TacitHome => Path.Combine(UserHome, ".tacit");

    /// <summary>Where <c>init</c> puts the memory repository unless told otherwise.</summary>
    public string DefaultMemory => Path.Combine(TacitHome, "memory");

    public string PathFor(string name) => Path.Combine(Root, name);

    /// <summary>The date the test clock shows.</summary>
    public static readonly DateOnly Today = new(2026, 11, 2);

    /// <summary>The clock of every <see cref="Tacit"/> made here; tests may move it forward.</summary>
    public FixedTime Clock { get; } = new(new DateTimeOffset(Today.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero));

    /// <summary>
    /// The secret environment variables every <see cref="Tacit"/> made here sees. It starts empty, so the machine's own
    /// variables are never read; a test sets the ones it needs, e.g. <c>TACIT_DECISION_API_KEY</c>.
    /// </summary>
    public Dictionary<string, string> EnvironmentVariables { get; } = [];

    public Tacit Tacit(string uiCulture = "zh-CN") =>
        new(new TacitOptions
        {
            TacitHome = TacitHome,
            UserHome = UserHome,
            UiCulture = CultureInfo.GetCultureInfo(uiCulture),
            Time = Clock,
            Secrets = EnvironmentVariables,
        });

    /// <summary>A <c>models</c> section that passes validation; nothing is listening at these addresses in tests.</summary>
    public const string Models = """
        models:
          embedding: { endpoint: 'http://127.0.0.1:38417/v1', model: qwen3-embedding-0.6b }
          reranker: { endpoint: 'http://127.0.0.1:38417/v1', model: qwen3-reranker-0.6b }
          decision: { endpoint: 'https://api.typesafe.ai/v1', model: jev-latest, apiKey: test-key }

        """;

    /// <summary>
    /// Runs <c>init</c> in Chinese and replaces the config with one that loads, with every model at
    /// <paramref name="models"/>, or at <see cref="ModelDouble.Quiet"/> when none is given; the decision model has a key.
    /// </summary>
    public void InitReady(string extraConfig = "", ModelDouble? models = null, MemoryLanguage language = MemoryLanguage.Zh)
    {
        Tacit().InitAsync(new InitRequest { Language = language }).GetAwaiter().GetResult();
        File.WriteAllText(Path.Combine(TacitHome, "config.yaml"), ((models ?? ModelDouble.Quiet).ModelsYaml(decisionKey: "test-key") + extraConfig).ReplaceLineEndings("\n"));
    }

    /// <summary>Runs <c>git</c> in <paramref name="directory"/> and returns its standard output.</summary>
    public static string Git(string directory, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8 };
        foreach (var arg in (string[])["-c", "core.quotepath=off", .. args])
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', args)} failed: {process.StandardError.ReadToEnd()}");
        return output;
    }

    public void Dispose()
    {
        try
        {
            // Git marks its object files read-only, which Directory.Delete refuses.
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Left for the system to clean up.
        }
    }
}

/// <summary>
/// A clock that stands still unless moved, in UTC. The code under test creates a timer for each attempt's timeout and
/// for each pause between attempts. <see cref="Advance"/> moves the clock without firing timers; <see cref="Elapse"/>
/// moves it and fires the timers now due, so a test decides when an attempt times out or a pause ends.
/// </summary>
internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    private readonly Lock _gate = new();
    private readonly List<PendingTimer> _pending = [];
    private readonly List<TimeSpan> _created = [];
    private readonly List<(TimeSpan Duration, TaskCompletionSource<TimeSpan> Created)> _waiters = [];
    private int _claimed;
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
            return _now;
    }

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    /// <summary>Moves the clock forward; no timer fires.</summary>
    public void Advance(TimeSpan by)
    {
        lock (_gate)
            _now += by;
    }

    /// <summary>Moves the clock forward and fires every timer now due, earliest first.</summary>
    public void Elapse(TimeSpan by)
    {
        lock (_gate)
            _now += by;
        while (true)
        {
            PendingTimer? due;
            lock (_gate)
            {
                due = _pending.Where(timer => timer.Due <= _now).OrderBy(timer => timer.Due).FirstOrDefault();
                if (due is not null)
                    _pending.Remove(due);
            }
            if (due is null)
                return;
            due.Fire();
        }
    }

    /// <summary>
    /// Waits for the next timer of <paramref name="duration"/> the code under test creates, and claims it: the next call
    /// looks only at timers created after it. Attempt timeouts and pauses are told apart by their lengths, which is
    /// why a background request that creates timers of its own does not shift what a test waits for.
    /// </summary>
    public Task<TimeSpan> NextTimerAsync(TimeSpan duration)
    {
        lock (_gate)
        {
            for (var i = _claimed; i < _created.Count; i++)
            {
                if (_created[i] == duration)
                {
                    _claimed = i + 1;
                    return Task.FromResult(duration);
                }
            }
            var created = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((duration, created));
            return created.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        if (period != Timeout.InfiniteTimeSpan)
            throw new NotSupportedException("Test timers fire once.");
        lock (_gate)
        {
            var timer = new PendingTimer(this, callback, state, _now + dueTime);
            _pending.Add(timer);
            _created.Add(dueTime);
            var waiter = _waiters.FirstOrDefault(candidate => candidate.Duration == dueTime);
            if (waiter.Created is not null)
            {
                _waiters.Remove(waiter);
                _claimed = _created.Count;
                waiter.Created.SetResult(dueTime);
            }
            return timer;
        }
    }

    private void Forget(PendingTimer timer)
    {
        lock (_gate)
            _pending.Remove(timer);
    }

    private sealed class PendingTimer(FixedTime clock, TimerCallback callback, object? state, DateTimeOffset due) : ITimer
    {
        public DateTimeOffset Due => due;

        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException("Test timers are not rescheduled.");

        public void Dispose() => clock.Forget(this);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
