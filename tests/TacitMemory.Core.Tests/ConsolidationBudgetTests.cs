namespace TacitMemory.Core.Tests;

/// <summary>
/// The consolidation's time budget, worker pool and model cooldown through the facade. The decision model holds its answers
/// until the test releases them, and the budget is a timer on the test clock, so each test decides when the budget runs out.
/// </summary>
public sealed class ConsolidationBudgetTests : IDisposable
{
    /// <summary>A bound on each await: a wait for a timer the test never moves fails here instead of hanging.</summary>
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Not a retry pause (the client waits 0.5, 1, 2 and 4 seconds): timers are matched by duration, so a budget equal to a
    /// pause would let a retry be taken for the budget's timer.
    /// </summary>
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(3);

    private readonly TestHome _home = new();
    private readonly ModelDouble _models = new();
    private MemoryService? _service;

    public void Dispose()
    {
        _models.ReleaseDecisions();
        _service?.Dispose();
        _models.Dispose();
        _home.Dispose();
    }

    private MemoryService Service => _service ??= _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

    private const string Removal = "Persona candidates to remove";

    private const string PersonaWithPreference =
        "## 红线\n\n## 协作规则\n\n## 偏好\n- 沟通：先要结论\n  <!-- 2026-10-20 原话「先要结论」 -->\n\n## 档案\n";

    private void Configure(string extra = "") => _home.InitReady(extra, _models);

    private void Write(params string[] candidateTitles)
    {
        File.WriteAllText(Path.Combine(_home.DefaultMemory, "persona.md"), PersonaWithPreference.ReplaceLineEndings("\n"));
        var blocks = string.Concat(candidateTitles.Select(title => $"### {title}\n归属：偏好\n- 2026-10-30 原话「{title}」\n- 2026-10-31 原话「{title}」\n\n"));
        File.WriteAllText(Path.Combine(_home.DefaultMemory, "candidates.md"), ("## 候选\n\n" + blocks + "## 拒绝列表\n").ReplaceLineEndings("\n"));
    }

    private async Task CreatePage(string name, string title, string body)
    {
        var result = await Service.WritePageAsync(new WritePageRequest
        {
            Op = "create",
            Type = ToolValues.PageTypes.Work,
            Page = "tacit-memory/" + name,
            Title = title,
            Aliases = [title + "的坑", title + "问题"],
            Sections = [new("要点", body)],
        });
        Assert.False(result.IsError, result.Text);
    }

    /// <summary>Changes a page's text by hand, without a commit.</summary>
    private void EditPage(string name, string from, string to)
    {
        var path = Path.Combine(_home.DefaultMemory, "pages", "tacit-memory", name + ".md");
        File.WriteAllText(path, File.ReadAllText(path).Replace(from, to, StringComparison.Ordinal));
    }

    private void PickPersonaEntry() => _models.Choose = (_, options) => options.ToDictionary(
        option => option.Key,
        option => option.Value.Contains("先要结论", StringComparison.Ordinal) ? 0.95
            : option.Key == options.Keys.Last() ? 0.05
            : 0.0);

    /// <summary>The text of one report category: from its heading to the next heading, or to the end.</summary>
    private static string Section(string text, string title)
    {
        var start = text.IndexOf($"### {title}\n", StringComparison.Ordinal);
        Assert.True(start >= 0, $"No category \"{title}\" in the report.");
        var body = text[(start + title.Length + 5)..];
        var next = body.IndexOf("\n### ", StringComparison.Ordinal);
        return next < 0 ? body : body[..next];
    }

    /// <summary>Starts a consolidation and waits until its budget timer exists, so the test can run the budget out.</summary>
    private async Task<Task<string>> StartConsolidationAsync()
    {
        var report = Service.ConsolidationReportAsync().WaitAsync(Guard);
        await _home.Clock.NextTimerAsync(Budget).WaitAsync(Guard);
        return report;
    }

    [Fact]
    public async Task The_report_returns_when_the_budget_runs_out_and_says_what_is_left_unchecked()
    {
        Configure($"consolidation:\n  budgetSeconds: {Budget.TotalSeconds}\n");
        Write("回答尽量简短", "回复别太长");
        PickPersonaEntry();
        _models.HoldDecisions();

        var report = await StartConsolidationAsync();
        await _models.DecisionRequestsReached(2).WaitAsync(Guard);
        _home.Clock.Elapse(Budget);
        var text = await report;

        Assert.Contains("not checked yet", Section(text, Removal));
        Assert.Contains("consolidation", Section(text, Removal));
    }

    [Fact]
    public async Task A_judgment_still_running_when_the_budget_runs_out_is_not_cancelled_and_is_cached_for_the_next_consolidation()
    {
        Configure($"consolidation:\n  budgetSeconds: {Budget.TotalSeconds}\n");
        Write("回答尽量简短", "回复别太长");
        PickPersonaEntry();
        _models.HoldDecisions();
        var report = await StartConsolidationAsync();
        await _models.DecisionRequestsReached(2).WaitAsync(Guard);
        _home.Clock.Elapse(Budget);
        await report;

        _models.ReleaseDecisions();
        await Service.JudgmentCompletion.WaitAsync(Guard);
        var complete = await Service.ConsolidationReportAsync().WaitAsync(Guard);

        Assert.Equal(2, _models.DecisionCalls);
        Assert.DoesNotContain("not checked yet", Section(complete, Removal));
        Assert.Contains("回答尽量简短", Section(complete, Removal));
        Assert.Contains("回复别太长", Section(complete, Removal));
    }

    [Fact]
    public async Task Judgments_not_yet_issued_when_the_budget_runs_out_are_never_issued()
    {
        Configure($"consolidation:\n  budgetSeconds: {Budget.TotalSeconds}\n  maxConcurrency: 1\n");
        Write("回答尽量简短", "回复别太长", "别绕弯子");
        PickPersonaEntry();
        _models.HoldDecisions();
        var report = await StartConsolidationAsync();
        await _models.DecisionRequestsReached(1).WaitAsync(Guard);

        _home.Clock.Elapse(Budget);
        var text = await report;
        _models.ReleaseDecisions();
        await Service.JudgmentCompletion.WaitAsync(Guard);

        Assert.Equal(1, _models.DecisionCalls);
        Assert.Contains("not checked yet", Section(text, Removal));
    }

    [Fact]
    public async Task No_more_judgments_run_at_once_than_the_configured_concurrency()
    {
        Configure("consolidation:\n  maxConcurrency: 2\n");
        Write("回答尽量简短", "回复别太长", "别绕弯子", "少用表情", "别客套");
        PickPersonaEntry();
        _models.HoldDecisions();
        var report = Service.ConsolidationReportAsync().WaitAsync(Guard);

        await _models.DecisionRequestsReached(2).WaitAsync(Guard);
        _models.ReleaseDecisions();
        await report;

        Assert.Equal(5, _models.DecisionCalls);
        Assert.InRange(_models.PeakDecisionConcurrency, 1, 2);
    }

    [Fact]
    public async Task Once_the_decision_model_cools_down_the_queued_judgments_are_not_sent()
    {
        Configure("consolidation:\n  maxConcurrency: 1\n");
        Write("回答尽量简短", "回复别太长", "别绕弯子");
        PickPersonaEntry();
        _models.ScriptDecision(ModelReply.Status(ModelDouble.DownStatus));

        var text = await Service.ConsolidationReportAsync().WaitAsync(Guard);

        Assert.Equal(1, _models.DecisionCalls);
        Assert.Contains("Not checked", Section(text, Removal));
    }

    [Fact]
    public async Task After_the_cooldown_the_next_consolidation_asks_the_model_again()
    {
        Configure("consolidation:\n  maxConcurrency: 1\n");
        Write("回答尽量简短", "回复别太长", "别绕弯子");
        PickPersonaEntry();
        _models.ScriptDecision(ModelReply.Status(ModelDouble.DownStatus));
        await Service.ConsolidationReportAsync().WaitAsync(Guard);
        var callsWhileCooling = _models.DecisionCalls;

        _home.Clock.Elapse(TimeSpan.FromSeconds(30));
        var text = await Service.ConsolidationReportAsync().WaitAsync(Guard);

        Assert.True(_models.DecisionCalls > callsWhileCooling);
        Assert.DoesNotContain("Not checked", Section(text, Removal));
    }

    [Fact]
    public async Task A_later_category_is_not_starved_when_an_earlier_one_runs_out_of_the_budget()
    {
        Configure($"consolidation:\n  budgetSeconds: {Budget.TotalSeconds}\n");
        Write("回答尽量简短");
        await CreatePage("alpha", "数据库升级", "内容一");
        await CreatePage("beta", "数据库迁移", "内容二");
        PickPersonaEntry();
        var first = await StartConsolidationAsync();
        await first;

        EditPage("alpha", "内容一", "内容三");
        EditPage("beta", "内容二", "内容四");
        var asked = _models.DecisionCalls + 2;
        var embedded = _models.EmbeddingCalls + 3;
        _models.HoldDecisions();
        var report = await StartConsolidationAsync();
        await _models.DecisionRequestsReached(asked).WaitAsync(Guard);
        await _models.EmbeddingRequestsReached(embedded).WaitAsync(Guard);
        _home.Clock.Elapse(Budget);
        var text = await report;

        Assert.Contains("2 changed page(s) not checked yet", Section(text, "Suspected duplicate pages"));
        Assert.Contains("they ran out before all of them finished", text);

        _models.ReleaseDecisions();
        await Service.JudgmentCompletion.WaitAsync(Guard);
        var complete = await Service.ConsolidationReportAsync().WaitAsync(Guard);

        Assert.Contains("回答尽量简短", Section(complete, Removal));
        Assert.DoesNotContain("not checked yet", Section(complete, Removal));
    }
}
