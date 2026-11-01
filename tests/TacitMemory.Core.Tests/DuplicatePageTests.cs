namespace TacitMemory.Core.Tests;

public sealed class DuplicatePageTests : IDisposable
{
    private readonly TestHome _home = new();
    private readonly ModelDouble _models = new();
    private MemoryService? _service;

    public DuplicatePageTests() => Configure();

    public void Dispose()
    {
        _service?.Dispose();
        _models.Dispose();
        _home.Dispose();
    }

    private MemoryService Service => _service ??= _home.Tacit().OpenService(_ => { });

    private void Configure(string extra = "", string? decisionKey = "test-key")
    {
        _home.InitReady(models: _models);
        File.WriteAllText(Path.Combine(_home.TacitHome, "config.yaml"), (_models.ModelsYaml(decisionKey: decisionKey) + extra).ReplaceLineEndings("\n"));
    }

    private Task<ToolResult> Create(string page, string title, string body = "升级后中文搜索没有结果。") => Service.WritePageAsync(new WritePageRequest
    {
        Op = "create",
        Page = page,
        Title = title,
        Aliases = [title + "的坑", title + "问题"],
        Sections = [new("现象", body)],
    });

    /// <summary>Makes the decision model pick the option that mentions <paramref name="title"/> with <paramref name="probability"/>.</summary>
    private void Pick(string title, double probability) =>
        _models.Choose = (_, options) => options.ToDictionary(
            option => option.Key,
            option => option.Value.Contains(title, StringComparison.Ordinal) ? probability
                : option.Key == options.Keys.Last() ? 1 - probability
                : 0.0);

    private bool Exists(string page) => File.Exists(Path.Combine(_home.DefaultMemory, "pages", page + ".md"));

    [Fact]
    public async Task A_page_the_decision_model_is_sure_repeats_another_is_refused_and_points_to_it()
    {
        await Create("tacit-memory/db-upgrade", "数据库升级后扩展丢失");
        Pick("数据库升级后扩展丢失", 0.95);

        var result = await Create("tacit-memory/pg-search", "升级后搜不到中文");

        Assert.True(result.IsError);
        Assert.StartsWith("Refused: Page tacit-memory/db-upgrade already records the same matter.", result.Text);
        Assert.Contains("Next step: Update tacit-memory/db-upgrade instead", result.Text);
        Assert.Matches(@"Related:\ntacit-memory/db-upgrade: 数据库升级后扩展丢失\n- 现象 \[[0-9a-f]{6}\]", result.Text);
        Assert.False(Exists("tacit-memory/pg-search"));
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(0.2)]
    public async Task A_page_the_decision_model_is_not_sure_about_is_created(double probability)
    {
        await Create("tacit-memory/db-upgrade", "数据库升级后扩展丢失");
        Pick("数据库升级后扩展丢失", probability);

        var result = await Create("tacit-memory/pg-search", "升级后搜不到中文");

        Assert.False(result.IsError, result.Text);
        Assert.True(Exists("tacit-memory/pg-search"));
    }

    [Fact]
    public async Task Configured_thresholds_replace_the_built_in_ones()
    {
        Configure("decision:\n  samePage: { certain: 0.99, unlikely: 0.1 }\n");
        await Create("tacit-memory/db-upgrade", "数据库升级后扩展丢失");
        Pick("数据库升级后扩展丢失", 0.95);

        var result = await Create("tacit-memory/pg-search", "升级后搜不到中文");

        Assert.False(result.IsError, result.Text);
    }

    [Fact]
    public async Task Only_pages_of_the_same_project_are_offered_and_a_lone_page_skips_the_question()
    {
        await Create("other-app/db-upgrade", "数据库升级后扩展丢失");

        var lone = await Create("tacit-memory/pg-search", "升级后搜不到中文");
        Assert.False(lone.IsError, lone.Text);
        Assert.Equal(0, _models.DecisionCalls);

        await Create("tacit-memory/deploy", "部署超时");
        var options = new List<string>();
        _models.Choose = (_, offered) =>
        {
            options.AddRange(offered.Values);
            return offered.Keys.ToDictionary(key => key, key => key == offered.Keys.Last() ? 1.0 : 0.0);
        };
        await Create("tacit-memory/db-again", "数据库升级又出问题");

        Assert.Contains(options, option => option.Contains("升级后搜不到中文", StringComparison.Ordinal));
        Assert.DoesNotContain(options, option => option.Contains("数据库升级后扩展丢失", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_question_is_in_english_about_the_page_as_written_and_carries_the_key()
    {
        await Service.WritePageAsync(new WritePageRequest
        {
            Op = "create",
            Page = "tacit-memory/db-upgrade",
            Title = "数据库升级后扩展丢失",
            Aliases = ["数据库升级后扩展丢失的坑", "数据库升级后扩展丢失问题"],
            Sections = [new("现象", "升级后中文搜索没有结果。"), new("解决", "重建扩展并重跑索引。")],
        });

        await Create("tacit-memory/pg-search", "升级后搜不到中文", body: "PGroonga 没装。");

        var (body, authorization) = _models.LastDecision!.Value;
        Assert.Equal("Bearer test-key", authorization);
        Assert.Equal("double-decision", body["model"]!.GetValue<string>());
        var question = body["questions"]!.AsObject().Single().Value!;
        Assert.Equal("choice", question["type"]!.GetValue<string>());
        Assert.StartsWith("The state is a new page", question["instructions"]!.GetValue<string>());
        Assert.Contains("Title: 升级后搜不到中文", body["state"]!.GetValue<string>());
        Assert.Contains("PGroonga 没装。", body["state"]!.GetValue<string>());
        var criteria = question["criteria"]!.AsObject();
        Assert.InRange(criteria.Count, 2, 10);
        Assert.Contains("现象: 升级后中文搜索没有结果。; 解决: 重建扩展并重跑索引。", criteria["p1"]!.GetValue<string>());
    }

    [Fact]
    public async Task Without_the_decision_model_the_page_is_created_with_a_note()
    {
        await Create("tacit-memory/db-upgrade", "数据库升级后扩展丢失");
        _models.DecisionDown = true;

        var result = await Create("tacit-memory/pg-search", "升级后搜不到中文");

        Assert.False(result.IsError, result.Text);
        Assert.Contains("decision model is unavailable", result.Text);
        Assert.True(Exists("tacit-memory/pg-search"));
    }

    [Fact]
    public async Task A_missing_key_environment_variable_makes_the_decision_model_unavailable()
    {
        Configure(decisionKey: null);
        await Create("tacit-memory/db-upgrade", "数据库升级后扩展丢失");

        var result = await Create("tacit-memory/pg-search", "升级后搜不到中文");

        Assert.False(result.IsError, result.Text);
        Assert.Contains("decision model is unavailable", result.Text);
        Assert.Equal(0, _models.DecisionCalls);
    }
}
