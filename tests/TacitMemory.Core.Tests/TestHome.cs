using System.Diagnostics;

namespace TacitMemory.Core.Tests;

/// <summary>A throwaway user home and <c>TACIT_HOME</c> under %TEMP%.</summary>
internal sealed partial class TestHome : IDisposable
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
    /// The clock of the auxiliary model requests; null gives them <see cref="Clock"/>. A test that moves the consolidation
    /// budget sets its own, so the move does not end a model request's timeout or retry pause as well.
    /// </summary>
    public FixedTime? ModelClock { get; set; }

    /// <summary>
    /// The secret environment variables every <see cref="Tacit"/> made here sees. It starts empty, so the machine's own
    /// variables are never read; a test sets the ones it needs, e.g. <c>TACIT_DECISION_API_KEY</c>.
    /// </summary>
    public Dictionary<string, string> EnvironmentVariables { get; } = [];

    /// <summary>The options of every <see cref="Tacit"/> made here: this home, this clock and the secrets above.</summary>
    public TacitOptions Options()
    {
        var options = new TacitOptions
        {
            TacitHome = TacitHome,
            UserHome = UserHome,
            Time = Clock,
            ModelTime = ModelClock,
            Secrets = EnvironmentVariables,
        };
        WithSources(ref options);
        return options;
    }

    /// <summary>Gives the options the changes a Core test tells the service of; the hosts' tests watch the files.</summary>
    partial void WithSources(ref TacitOptions options);

    public Tacit Tacit() => new(Options());

    /// <summary>
    /// Binds the client <paramref name="client"/> to the omp paths under the user home, creating its folders first as a user
    /// would have. The paths are test data; the product takes any paths the user gives.
    /// </summary>
    public BindingResult Bind(string client = "omp")
    {
        Directory.CreateDirectory(Path.Combine(UserHome, ".omp", "agent", "skills"));
        return Tacit().Connect(new ConnectRequest(client, Path.Combine(UserHome, ".omp", "agent", "AGENTS.md"), Path.Combine(UserHome, ".omp", "agent", "skills")));
    }

    /// <summary>Unbinds <paramref name="client"/>, as <c>tacit disconnect --client</c> does.</summary>
    public BindingResult Unbind(string client = "omp") => Tacit().Disconnect(client);

    /// <summary>A <c>models</c> section that passes validation; nothing is listening at these addresses in tests.</summary>
    public const string Models = """
        models:
          embedding: { url: 'http://127.0.0.1:38417/v1/embeddings', model: qwen3-embedding-0.6b }
          reranker: { url: 'http://127.0.0.1:38417/v1/rerank', model: qwen3-reranker-0.6b }
          decision: { url: 'https://api.typesafe.ai/v1/systemone', model: jev-latest, apiKey: test-key }

        """;

    /// <summary>
    /// Initializes the home in <paramref name="language"/>, Chinese by default, and replaces the config with one that loads,
    /// with every model at <paramref name="models"/>, or at <see cref="ModelDouble.Quiet"/> when none is given; the
    /// decision model has a key. A fresh home gets a copy of what <c>init</c> makes; a home that already has a
    /// <c>TACIT_HOME</c> runs <c>init</c> itself, which then reads what is there.
    /// </summary>
    public void InitReady(string extraConfig = "", ModelDouble? models = null, MemoryLanguage language = MemoryLanguage.Zh)
    {
        if (Directory.Exists(TacitHome))
            Tacit().InitAsync(new InitRequest { Language = language }).GetAwaiter().GetResult();
        else
            CopyDirectory(InitializedTemplate(language), TacitHome);
        var config = $"language: {language.ToString().ToLowerInvariant()}\n" + (models ?? ModelDouble.Quiet).ModelsYaml(decisionKey: "test-key") + extraConfig;
        File.WriteAllText(Path.Combine(TacitHome, "config.yaml"), config.ReplaceLineEndings("\n"));
    }

    private static readonly Dictionary<MemoryLanguage, TestHome> InitTemplates = [];

    /// <summary>
    /// A <c>TACIT_HOME</c> that <c>init</c> has made in <paramref name="language"/>, made once per test process and copied
    /// by <see cref="InitReady"/>. Most of a test's time otherwise goes on the three git processes <c>init</c> starts.
    /// Nothing <c>init</c> writes names the home it was made in, except the config, which <see cref="InitReady"/> replaces.
    /// </summary>
    private static string InitializedTemplate(MemoryLanguage language)
    {
        lock (InitTemplates)
        {
            if (!InitTemplates.TryGetValue(language, out var template))
            {
                template = new TestHome();
                template.Tacit().InitAsync(new InitRequest { Language = language }).GetAwaiter().GetResult();
                InitTemplates.Add(language, template);
                if (InitTemplates.Count == 1)
                {
                    AppDomain.CurrentDomain.ProcessExit += (_, _) =>
                    {
                        foreach (var made in InitTemplates.Values)
                            made.Dispose();
                    };
                }
            }
            return template.TacitHome;
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));
    }

    /// <summary>
    /// Runs <c>git</c> in <paramref name="directory"/> and returns its standard output. A commit gets the same author the
    /// service uses, so a test never depends on the identity configured on the machine it runs on.
    /// </summary>
    public static string Git(string directory, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8 };
        foreach (var arg in (string[])["-c", "core.quotepath=off", "-c", "user.name=tacit", "-c", "user.email=tacit@localhost", .. args])
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
/// for each pause between attempts, and the service a debounced timer for each refresh, which it schedules again with
/// <see cref="ITimer.Change"/>. <see cref="Advance"/> moves the clock without firing timers; <see cref="Elapse"/> moves it
/// and fires the timers now due, so a test decides when an attempt times out, a pause ends or a refresh runs.
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
            var timer = new PendingTimer(this, callback, state);
            if (dueTime == Timeout.InfiniteTimeSpan)
                return timer;
            timer.Due = _now + dueTime;
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

    /// <summary>Schedules <paramref name="timer"/> again; an infinite due time stops it.</summary>
    private void Reschedule(PendingTimer timer, TimeSpan dueTime)
    {
        lock (_gate)
        {
            _pending.Remove(timer);
            if (dueTime == Timeout.InfiniteTimeSpan)
                return;
            timer.Due = _now + dueTime;
            _pending.Add(timer);
        }
    }

    private sealed class PendingTimer(FixedTime clock, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset Due { get; set; }

        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (period != Timeout.InfiniteTimeSpan)
                throw new NotSupportedException("Test timers fire once.");
            clock.Reschedule(this, dueTime);
            return true;
        }

        public void Dispose() => clock.Forget(this);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
