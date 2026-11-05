namespace TacitMemory.Core.Tests;

/// <summary>
/// Retries of the auxiliary models through the facade: which failures are retried, the pauses and the 20-second budget,
/// and the cooldown after a request finally fails. Pauses and attempt timeouts are timers on <see cref="TestHome.Clock"/>;
/// each test moves the clock past them, so nothing waits in real time.
/// </summary>
public sealed class ModelRetryTests : IDisposable
{
    /// <summary>The pauses between attempts, in order: 0.5, 1, 2 and 4 seconds.</summary>
    private static readonly TimeSpan[] Schedule = [TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)];

    /// <summary>A bound on each await: a retry that waits on a timer the test never moves fails here instead of hanging.</summary>
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);

    private readonly TestHome _home = new();
    private readonly ModelDouble _models = new();
    private MemoryService? _service;

    public ModelRetryTests() => _home.InitReady(models: _models);

    public void Dispose()
    {
        _service?.Dispose();
        _models.Dispose();
        _home.Dispose();
    }

    private MemoryService Service => _service ??= _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

    private FixedTime Clock => _home.Clock;

    private Task<ToolResult> Recall() => Service.RecallAsync(new RecallRequest { Query = "部署" }).WaitAsync(Guard);

    private Task<ToolResult> Write(string text) => Service.WriteRecentAsync(new WriteRecentRequest { Op = "add_activity", Text = text }).WaitAsync(Guard);

    /// <summary>Makes the decision model pick the option whose text contains <paramref name="text"/> with <paramref name="probability"/>.</summary>
    private void Match(string text, double probability) => _models.Choose = (_, options) => options.ToDictionary(
        option => option.Key,
        option => option.Key != options.Keys.Last() && option.Value.Contains(text, StringComparison.Ordinal) ? probability : 0.0);

    /// <summary>
    /// Passes the pauses of a request in order: waits for the client to create each pause, then moves the clock past it.
    /// Pauses are matched by length, since attempt timeouts are timers of their own.
    /// </summary>
    private async Task PassPauses(params TimeSpan[] pauses)
    {
        foreach (var pause in pauses)
        {
            await Clock.NextTimerAsync(pause);
            Clock.Elapse(pause);
        }
    }

    private static ModelReply Failure(string kind) => kind switch
    {
        "429" => ModelReply.Status(429),
        "500" => ModelReply.Status(500),
        "503" => ModelReply.Status(503),
        _ => ModelReply.Drop(),
    };

    public static IEnumerable<object[]> RetryableFailures() =>
        from kind in new[] { "429", "500", "503", "dropped" }
        from failures in Enumerable.Range(1, 4)
        select new object[] { kind, failures };

    [Theory]
    [MemberData(nameof(RetryableFailures))]
    public async Task A_retryable_failure_is_retried_until_a_later_attempt_succeeds(string kind, int failures)
    {
        _models.ScriptEmbedding(Enumerable.Range(0, failures).Select(_ => Failure(kind)).ToArray());

        var recall = Recall();
        await PassPauses(Schedule.Take(failures).ToArray());
        var result = await recall;

        Assert.Equal(failures + 1, _models.EmbeddingCalls);
        Assert.DoesNotContain("embedding model is unavailable", result.Text);
        Assert.DoesNotContain("Unavailable auxiliary models", await Service.ServerInstructionsAsync());
    }

    [Fact]
    public async Task A_429_waits_the_length_of_its_retry_after()
    {
        _models.ScriptEmbedding(ModelReply.Status(429, retryAfterSeconds: 15));

        var recall = Recall();
        await PassPauses(TimeSpan.FromSeconds(15));
        var result = await recall;

        Assert.Equal(2, _models.EmbeddingCalls);
        Assert.DoesNotContain("embedding model is unavailable", result.Text);
    }

    [Fact]
    public async Task A_retry_after_that_outlasts_the_budget_fails_at_once()
    {
        _models.ScriptEmbedding(ModelReply.Status(429, retryAfterSeconds: 30));

        var result = await Recall();

        Assert.Equal(1, _models.EmbeddingCalls);
        Assert.Contains("embedding model is unavailable", result.Text);
        Assert.Contains("- Unavailable auxiliary models: embedding.", await Service.ServerInstructionsAsync());
    }

    [Fact]
    public async Task The_twenty_second_budget_cuts_the_attempts_short()
    {
        _models.ScriptEmbedding(ModelReply.Status(429, 8), ModelReply.Status(429, 8), ModelReply.Hang());

        var recall = Recall();
        await PassPauses(TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(8));
        // The third attempt has 20 - 16 = 4 seconds left, not its own 5; it hangs until that runs out.
        await Clock.NextTimerAsync(TimeSpan.FromSeconds(4));
        await _models.EmbeddingRequestsReached(3);
        Clock.Elapse(TimeSpan.FromSeconds(4));
        var result = await recall;

        Assert.Equal(3, _models.EmbeddingCalls);
        Assert.Contains("embedding model is unavailable", result.Text);
    }

    /// <summary>
    /// Lets attempt <paramref name="attempt"/> hang until its timeout: waits for the client to start the timer of
    /// <paramref name="limit"/>, for the server to receive the request, then moves the clock past the timeout.
    /// </summary>
    private async Task TimeOut(int attempt, TimeSpan limit)
    {
        await Clock.NextTimerAsync(limit);
        await _models.EmbeddingRequestsReached(attempt);
        Clock.Elapse(limit);
    }

    public static IEnumerable<object[]> LaterAttempts() => Enumerable.Range(2, 4).Select(attempt => new object[] { attempt });

    [Theory]
    [MemberData(nameof(LaterAttempts))]
    public async Task A_single_attempt_timeout_is_retried_until_a_later_attempt_succeeds(int attempt)
    {
        // The first attempt hangs; the attempts in between fail quickly with 503, then the model answers as set.
        _models.ScriptEmbedding([ModelReply.Hang(), .. Enumerable.Repeat(ModelReply.Status(503), attempt - 2)]);

        var recall = Recall();
        await TimeOut(1, TimeSpan.FromSeconds(5));
        await PassPauses(Schedule.Take(attempt - 1).ToArray());
        var result = await recall;

        Assert.Equal(attempt, _models.EmbeddingCalls);
        Assert.DoesNotContain("embedding model is unavailable", result.Text);
        Assert.DoesNotContain("Unavailable auxiliary models", await Service.ServerInstructionsAsync());
    }

    [Fact]
    public async Task Hung_attempts_run_into_the_budget_and_the_last_one_is_cut_to_what_remains()
    {
        _models.ScriptEmbedding(Enumerable.Repeat(ModelReply.Hang(), 4).ToArray());

        var recall = Recall();
        await TimeOut(1, TimeSpan.FromSeconds(5));
        await PassPauses(TimeSpan.FromSeconds(0.5));
        await TimeOut(2, TimeSpan.FromSeconds(5));
        await PassPauses(TimeSpan.FromSeconds(1));
        await TimeOut(3, TimeSpan.FromSeconds(5));
        await PassPauses(TimeSpan.FromSeconds(2));
        // Three hung attempts and three pauses leave 20 - 18.5 = 1.5 seconds: the fourth attempt's timeout is cut to that.
        await TimeOut(4, TimeSpan.FromSeconds(1.5));
        var result = await recall;

        Assert.Equal(4, _models.EmbeddingCalls);
        Assert.Contains("embedding model is unavailable", result.Text);
        Assert.Contains("- Unavailable auxiliary models: embedding.", await Service.ServerInstructionsAsync());
    }

    [Fact]
    public async Task Five_failed_attempts_cool_the_model_down_and_it_is_tried_again_after_thirty_seconds()
    {
        _models.ScriptEmbedding(Enumerable.Repeat(ModelReply.Status(503), 5).ToArray());

        var recall = Recall();
        await PassPauses(Schedule);
        var failed = await recall;

        Assert.Equal(5, _models.EmbeddingCalls);
        Assert.Contains("embedding model is unavailable", failed.Text);
        Assert.Contains("- Unavailable auxiliary models: embedding.", await Service.ServerInstructionsAsync());

        var during = await Recall();
        Assert.Equal(5, _models.EmbeddingCalls);
        Assert.Contains("embedding model is unavailable", during.Text);

        Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.DoesNotContain("Unavailable auxiliary models", await Service.ServerInstructionsAsync());
        var after = await Recall();
        Assert.Equal(6, _models.EmbeddingCalls);
        Assert.DoesNotContain("embedding model is unavailable", after.Text);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(422)]
    public async Task A_client_error_is_not_retried(int status)
    {
        _models.ScriptEmbedding(ModelReply.Status(status));

        var result = await Recall();

        Assert.Equal(1, _models.EmbeddingCalls);
        Assert.Contains("embedding model is unavailable", result.Text);
        Assert.Contains("- Unavailable auxiliary models: embedding.", await Service.ServerInstructionsAsync());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"data\":[{\"index\":0,\"embedding\":[]}]}")]
    [InlineData("not json")]
    public async Task An_answer_that_is_not_what_was_asked_is_not_retried(string body)
    {
        _models.ScriptEmbedding(ModelReply.Body(body));

        var result = await Recall();

        Assert.Equal(1, _models.EmbeddingCalls);
        Assert.Contains("embedding model is unavailable", result.Text);
        Assert.Contains("- Unavailable auxiliary models: embedding.", await Service.ServerInstructionsAsync());
    }

    [Fact]
    public async Task A_write_judgment_is_retried_before_it_decides()
    {
        // The embedding model is down, so the background vector fill after each write fails at once and sets no timer.
        _models.EmbeddingDown = true;
        await Write("先查了索引");
        Match("先查了索引", 0.95);
        _models.ScriptDecision(ModelReply.Status(503));

        var write = Write("索引查完了，问题在分词器");
        await PassPauses(TimeSpan.FromSeconds(0.5));
        var result = await write;

        Assert.Equal(2, _models.DecisionCalls);
        Assert.Contains("one merged sentence", result.Text);
    }
}
