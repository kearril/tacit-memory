namespace TacitMemory.Core.Tests;

/// <summary>
/// The consolidation's suspected duplicate pages through the facade: only the pages changed since the last consolidation are judged,
/// each against the nearest pages of its project, as a new page is judged live. A sure match and an unsure one are
/// listed with their tiers, a rejection is not, and a pair is listed once.
/// </summary>
public sealed class ConsolidationDuplicatePageTests : IDisposable
{
    /// <summary>A bound on each await: a wait for a timer the test never moves fails here instead of hanging.</summary>
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(2);

    private const string Section = "Suspected duplicate pages";

    private const string Db = "数据库升级后扩展丢失";

    private const string Pg = "升级后搜不到中文";

    private const string Deploy = "部署超时";

    private const string Other = "部署回滚方案";

    private readonly TestHome _home = new();
    private readonly ModelDouble _models = new();
    private readonly List<(string Page, string Neighbour, double Probability)> _answers = [];
    private MemoryService? _service;
    private int _callsAtCheckpoint;

    public ConsolidationDuplicatePageTests()
    {
        _models.Choose = Answered;
    }

    public void Dispose()
    {
        _models.ReleaseDecisions();
        _service?.Dispose();
        _models.Dispose();
        _home.Dispose();
    }

    private string Memory => _home.DefaultMemory;

    private MemoryService Service => _service ??= _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

    /// <summary>The decision requests made since the last consolidation commit; the live writes before it are not counted.</summary>
    private int AskedSinceCheckpoint => _models.DecisionCalls - _callsAtCheckpoint;

    private void Configure(string extra = "") => _home.InitReady(extra, _models);

    private string PageFile(string page) => Path.Combine(Memory, "pages", page + ".md");

    private Task<ToolResult> Create(string page, string title, string body = "升级后中文搜索没有结果。") => Service.WritePageAsync(new WritePageRequest
    {
        Op = "create",
        Page = page,
        Title = title,
        Aliases = [title + "的坑", title + "问题"],
        Sections = [new("现象", body)],
    });

    /// <summary>Changes the text of a page by hand, without a commit.</summary>
    private void Edit(string page, string from, string to)
    {
        var path = PageFile(page);
        File.WriteAllText(path, File.ReadAllText(path).Replace(from, to, StringComparison.Ordinal));
    }

    /// <summary>
    /// Records a consolidation commit, which is what the next consolidation counts changes from. Like the consolidation, it commits every
    /// change made so far, so the pages edited by hand become part of the baseline.
    /// </summary>
    private void CheckpointCommit(string message)
    {
        TestHome.Git(Memory, "add", "-A");
        TestHome.Git(Memory, "-c", "user.name=tacit", "-c", "user.email=tacit@localhost", "commit", "--allow-empty", "-m", message);
        _callsAtCheckpoint = _models.DecisionCalls;
    }

    /// <summary>
    /// Makes the decision model answer the question of the page titled <paramref name="page"/> with
    /// <paramref name="probability"/> for the option of the page titled <paramref name="neighbour"/>; every other
    /// question is answered "none of these".
    /// </summary>
    private void Answer(string page, string neighbour, double probability) => _answers.Add((page, neighbour, probability));

    private IReadOnlyDictionary<string, double> Answered(string state, IReadOnlyDictionary<string, string> options)
    {
        var none = options.Keys.Last();
        var answer = _answers.FirstOrDefault(a => state.Contains($"Title: {a.Page}\n", StringComparison.Ordinal));
        return options.Keys.ToDictionary(key => key, key =>
        {
            if (answer.Page is null)
                return key == none ? 1.0 : 0.0;
            if (key == none)
                return 1 - answer.Probability;
            return options[key].Contains(answer.Neighbour, StringComparison.Ordinal) ? answer.Probability : 0.0;
        });
    }

    private async Task<string> Consolidation() => await Service.ConsolidationReportAsync().WaitAsync(Guard);

    /// <summary>The text of the duplicate pages category: from its heading to the next heading, or to the end.</summary>
    private static string SectionOf(string text)
    {
        var start = text.IndexOf($"### {Section}\n", StringComparison.Ordinal);
        Assert.True(start >= 0, $"No category \"{Section}\" in the report.");
        var body = text[(start + Section.Length + 5)..];
        var next = body.IndexOf("\n### ", StringComparison.Ordinal);
        return next < 0 ? body : body[..next];
    }

    /// <summary>The findings of the category: its lines that start with a dash.</summary>
    private static string[] FindingsOf(string text) =>
        SectionOf(text).Split('\n').Where(line => line.StartsWith("- ", StringComparison.Ordinal)).ToArray();

    /// <summary>Three pages of one project: two on the same matter, one on another.</summary>
    private async Task CreateDbPgAndDeploy()
    {
        Configure();
        await Create("tacit-memory/db-upgrade", Db);
        await Create("tacit-memory/pg-search", Pg, body: "PGroonga 没装。");
        await Create("tacit-memory/deploy", Deploy, body: "部署后检查健康状况。");
    }

    [Fact]
    public async Task Only_pages_changed_since_the_last_consolidation_are_judged_including_uncommitted_edits()
    {
        await CreateDbPgAndDeploy();
        CheckpointCommit("consolidate: first pass");
        Answer(Deploy, Db, 0.95);

        Edit("tacit-memory/deploy", "部署后检查健康状况。", "部署后先看日志再重启。");
        var report = await Consolidation();

        Assert.Equal(1, AskedSinceCheckpoint);
        var findings = FindingsOf(report);
        Assert.Single(findings);
        Assert.Contains("tacit-memory/deploy", findings[0]);
        Assert.Contains("tacit-memory/db-upgrade", findings[0]);
    }

    [Fact]
    public async Task A_page_the_consolidation_has_seen_is_not_reported_again_until_it_changes()
    {
        await CreateDbPgAndDeploy();
        CheckpointCommit("consolidate: first pass");
        Answer(Deploy, Db, 0.95);
        Edit("tacit-memory/deploy", "部署后检查健康状况。", "部署后先看日志再重启。");
        Assert.Single(FindingsOf(await Consolidation()));
        CheckpointCommit("consolidate: second pass");

        var unchanged = await Consolidation();

        Assert.Empty(FindingsOf(unchanged));
        Assert.Contains("None.", SectionOf(unchanged));
        Assert.Equal(0, AskedSinceCheckpoint);

        Edit("tacit-memory/deploy", "部署后先看日志再重启。", "部署后先看日志，再看指标。");
        var changedAgain = FindingsOf(await Consolidation());
        Assert.Single(changedAgain);
        Assert.Contains("tacit-memory/deploy", changedAgain[0]);
    }

    [Fact]
    public async Task A_pair_of_two_changed_pages_is_listed_once()
    {
        await CreateDbPgAndDeploy();
        Answer(Db, Pg, 0.95);
        Answer(Pg, Db, 0.95);
        Edit("tacit-memory/db-upgrade", "升级后中文搜索没有结果。", "升级后中文搜索没有结果，扩展丢失。");
        Edit("tacit-memory/pg-search", "PGroonga 没装。", "PGroonga 没有随升级重装。");

        var findings = FindingsOf(await Consolidation());

        Assert.Single(findings);
        Assert.Contains("tacit-memory/db-upgrade", findings[0]);
        Assert.Contains("tacit-memory/pg-search", findings[0]);
    }

    [Fact]
    public async Task A_sure_match_is_listed_as_certain_an_unsure_one_as_uncertain_and_a_rejection_is_not_listed()
    {
        Configure();
        await Create("tacit-memory/db-upgrade", Db);
        await Create("tacit-memory/pg-search", Pg);
        await Create("tacit-memory/deploy", Deploy, body: "部署后检查健康状况。");
        await Create("tacit-memory/rollback", Other);
        CheckpointCommit("consolidate: first pass");
        Answer(Db, Pg, 0.95);
        Answer(Deploy, Other, 0.6);
        Answer(Other, Db, 0.1);
        Edit("tacit-memory/db-upgrade", "升级后中文搜索没有结果。", "升级后中文搜索没有结果，扩展丢失。");
        Edit("tacit-memory/deploy", "部署后检查健康状况。", "部署后先看日志再重启。");
        Edit("tacit-memory/rollback", "升级后中文搜索没有结果。", "回滚前先备份。");

        var findings = FindingsOf(await Consolidation());

        Assert.Equal(2, findings.Length);
        var sure = Assert.Single(findings, line => line.Contains("tacit-memory/db-upgrade", StringComparison.Ordinal));
        Assert.Contains("tacit-memory/pg-search", sure);
        Assert.DoesNotContain("uncertain", sure);
        var unsure = Assert.Single(findings, line => line.Contains("tacit-memory/deploy", StringComparison.Ordinal));
        Assert.Contains("tacit-memory/rollback", unsure);
        Assert.Contains("uncertain", unsure);
        Assert.DoesNotContain("tacit-memory/rollback", string.Join('\n', findings.Where(line => line != unsure)));
    }

    [Fact]
    public async Task A_changed_page_with_no_other_page_in_its_project_is_not_asked_about_pages_of_other_projects()
    {
        Configure();
        await Create("other-app/db-upgrade", Db);
        await Create("tacit-memory/pg-search", Pg);
        CheckpointCommit("consolidate: first pass");
        Answer(Pg, Db, 0.95);
        Edit("tacit-memory/pg-search", "升级后中文搜索没有结果。", "升级后中文搜索没有结果，扩展丢失。");

        var report = await Consolidation();

        Assert.Equal(0, AskedSinceCheckpoint);
        Assert.Contains("None.", SectionOf(report));
    }

    [Fact]
    public async Task A_deleted_page_is_not_judged_and_is_not_offered_as_a_neighbour()
    {
        await CreateDbPgAndDeploy();
        CheckpointCommit("consolidate: first pass");
        Answer(Deploy, Db, 0.95);
        File.Delete(PageFile("tacit-memory/db-upgrade"));
        File.Delete(PageFile("tacit-memory/pg-search"));
        Edit("tacit-memory/deploy", "部署后检查健康状况。", "部署后先看日志再重启。");

        var report = await Consolidation();

        Assert.Equal(0, AskedSinceCheckpoint);
        Assert.Empty(FindingsOf(report));
    }

    [Fact]
    public async Task A_rejected_pair_is_not_listed()
    {
        await CreateDbPgAndDeploy();
        CheckpointCommit("consolidate: first pass");
        Answer(Deploy, Db, 0.1);
        Edit("tacit-memory/deploy", "部署后检查健康状况。", "部署后先看日志再重启。");

        var report = await Consolidation();

        Assert.Equal(1, AskedSinceCheckpoint);
        Assert.Empty(FindingsOf(report));
        Assert.Contains("None.", SectionOf(report));
    }

    [Fact]
    public async Task Without_a_consolidation_or_init_commit_every_page_counts_as_changed()
    {
        Configure();
        TestHome.Git(Memory, "-c", "user.name=tacit", "-c", "user.email=tacit@localhost", "commit", "--amend", "--allow-empty", "-m", "setup memory");
        await Create("tacit-memory/db-upgrade", Db);
        await Create("tacit-memory/pg-search", Pg);
        Answer(Db, Pg, 0.95);

        var findings = FindingsOf(await Consolidation());

        Assert.Single(findings);
        Assert.Contains("tacit-memory/db-upgrade", findings[0]);
        Assert.Contains("tacit-memory/pg-search", findings[0]);
    }

    [Fact]
    public async Task Without_the_decision_model_the_changed_pages_are_not_checked()
    {
        await CreateDbPgAndDeploy();
        CheckpointCommit("consolidate: first pass");
        Edit("tacit-memory/deploy", "部署后检查健康状况。", "部署后先看日志再重启。");
        _models.DecisionDown = true;

        var report = await Consolidation();

        Assert.Contains("Not checked", SectionOf(report));
        Assert.Empty(FindingsOf(report));
    }

    [Fact]
    public async Task Without_the_embedding_model_the_changed_pages_are_not_checked_and_no_decision_is_asked()
    {
        await CreateDbPgAndDeploy();
        CheckpointCommit("consolidate: first pass");
        Edit("tacit-memory/deploy", "部署后检查健康状况。", "部署后先看日志再重启。");
        _models.EmbeddingDown = true;

        var report = await Consolidation();

        Assert.Equal(0, AskedSinceCheckpoint);
        Assert.Contains("Not checked", SectionOf(report));
        Assert.Empty(FindingsOf(report));
    }

    [Fact]
    public async Task When_the_budget_runs_out_the_unanswered_pages_are_counted_as_not_checked()
    {
        Configure($"consolidation:\n  budgetSeconds: {Budget.TotalSeconds}\n");
        await Create("tacit-memory/db-upgrade", Db);
        await Create("tacit-memory/pg-search", Pg);
        CheckpointCommit("consolidate: first pass");
        Edit("tacit-memory/db-upgrade", "升级后中文搜索没有结果。", "升级后中文搜索没有结果，扩展丢失。");
        _models.HoldDecisions();

        var report = Service.ConsolidationReportAsync().WaitAsync(Guard);
        await _home.Clock.NextTimerAsync(Budget).WaitAsync(Guard);
        await _models.DecisionRequestsReached(_callsAtCheckpoint + 1).WaitAsync(Guard);
        _home.Clock.Elapse(Budget);
        var text = await report;

        Assert.Contains("not checked yet", SectionOf(text));
        Assert.Empty(FindingsOf(text));
    }
}
