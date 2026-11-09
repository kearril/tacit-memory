namespace TacitMemory.Core.Tests;

public sealed class RerankTests : IDisposable
{
    private readonly TestHome _home = new();
    private readonly ModelDouble _models = new();
    private MemoryService? _service;

    public RerankTests()
    {
        _home.InitReady(models: _models);
    }

    public void Dispose()
    {
        _service?.Dispose();
        _models.Dispose();
        _home.Dispose();
    }

    private MemoryService Service => _service ??= _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

    private async Task Create(string page, string title, params PageSectionInput[] sections)
    {
        var result = await Service.WritePageAsync(new WritePageRequest
        {
            Op = "create",
            Type = ToolValues.PageTypes.Work,
            Page = page,
            Title = title,
            Aliases = [title + "别名", title + "又名"],
            Sections = sections,
        });
        Assert.False(result.IsError, result.Text);
    }

    private Task<ToolResult> Recall(string query) => Service.RecallAsync(new RecallRequest { Query = query });

    /// <summary>Replaces the config, which the service reads when it opens; call before the first use of <see cref="Service"/>.</summary>
    private void Configure(string retrieval) => File.WriteAllText(
        Path.Combine(_home.TacitHome, "config.yaml"),
        (_models.ModelsYaml(decisionKey: "test-key") + retrieval + "\n").ReplaceLineEndings("\n"));

    [Fact]
    public async Task Pages_the_reranker_scores_higher_come_first()
    {
        await Create("ops/health", "部署健康检查", new PageSectionInput("检查", "部署后检查健康状况。"));
        await Create("ops/rollback", "部署回滚方案", new PageSectionInput("回滚", "部署失败时先回滚到上一个版本。"));
        _models.Score = (_, document) => document.Contains("回滚", StringComparison.Ordinal) ? 0.9 : 0.2;

        var result = await Recall("部署");

        Assert.False(result.IsError, result.Text);
        Assert.True(result.Text.IndexOf("ops/rollback", StringComparison.Ordinal) < result.Text.IndexOf("ops/health", StringComparison.Ordinal), result.Text);
    }

    [Fact]
    public async Task Units_scored_below_the_default_floor_of_0_001_are_dropped()
    {
        await Create("ops/relevant", "部署流程", new PageSectionInput("步骤", "部署前先备份数据库。"));
        await Create("ops/noise", "部署杂谈", new PageSectionInput("闲聊", "部署这个词在这里只是顺口提到。"));
        _models.Score = (_, document) => document.Contains("备份", StringComparison.Ordinal) ? 0.5 : 0.0005;

        var result = await Recall("部署");

        Assert.Contains("ops/relevant", result.Text);
        Assert.DoesNotContain("ops/noise", result.Text);
    }

    [Fact]
    public async Task When_every_unit_is_below_the_floor_the_answer_is_no_relevant_memory()
    {
        await Create("ops/deploy", "部署流程", new PageSectionInput("步骤", "部署前先备份数据库。"));
        _models.Score = (_, _) => 0.0;

        var result = await Recall("部署");

        Assert.Equal("No relevant memory.", result.Text);
    }

    [Fact]
    public async Task A_floor_set_in_the_config_replaces_the_default()
    {
        Configure("retrieval: { minRerankScore: 0.5 }");
        await Create("ops/deploy", "部署流程", new PageSectionInput("步骤", "部署前先备份数据库。"));
        _models.Score = (_, _) => 0.3;

        var result = await Recall("部署");

        Assert.Equal("No relevant memory.", result.Text);
    }

    [Fact]
    public async Task Only_the_top_k_pages_by_rerank_order_are_returned()
    {
        Configure("retrieval: { topK: 1 }");
        await Create("ops/deploy", "部署流程", new PageSectionInput("步骤", "部署前先备份数据库。"));
        await Create("ops/first", "部署甲", new PageSectionInput("步骤", "部署甲的步骤。"));
        await Create("ops/second", "部署乙", new PageSectionInput("步骤", "部署乙的步骤。"));
        _models.Score = (_, document) => document.Contains("部署乙", StringComparison.Ordinal) ? 0.9 : 0.2;

        var result = await Recall("部署");

        Assert.Contains("[1] page ops/second", result.Text);
        Assert.DoesNotContain("[2] ", result.Text);
        Assert.DoesNotContain("ops/first", result.Text);
    }

    [Fact]
    public async Task Only_the_sections_scored_above_the_floor_are_shown()
    {
        await Create("ops/rollback", "部署回滚", new("回滚步骤", "部署失败时先回滚。"), new("无关", "部署的历史沿革。"));
        _models.Score = (_, document) => document.Contains("回滚", StringComparison.Ordinal) ? 0.9 : 0.0;

        var result = await Recall("部署");

        Assert.Contains("### 回滚步骤 [", result.Text);
        Assert.DoesNotContain("### 无关 [", result.Text);
    }

    [Fact]
    public async Task The_reranker_is_sent_each_section_as_its_name_and_body()
    {
        await Create("ops/rollback", "部署回滚", new PageSectionInput("回滚步骤", "部署失败时先回滚。"), new PageSectionInput("无关", "部署的历史沿革。"));

        await Recall("部署");

        Assert.Contains(_models.RerankedDocuments, document => document.StartsWith("回滚步骤\n", StringComparison.Ordinal) && document.Contains("先回滚", StringComparison.Ordinal));
    }

    [Fact]
    public async Task With_the_reranker_down_nothing_is_filtered_and_the_answer_says_so()
    {
        await Create("ops/rollback", "部署回滚", new PageSectionInput("回滚步骤", "部署失败时先回滚。"));
        _models.RerankDown = true;
        _models.Score = (_, _) => 0.0;

        var result = await Recall("部署");

        Assert.Contains("ops/rollback", result.Text);
        Assert.Contains("reranker model is unavailable", result.Text);
        Assert.DoesNotContain("No relevant memory.", result.Text);
    }

    [Fact]
    public async Task Filtering_resumes_once_the_reranker_is_back_after_its_cooldown()
    {
        await Create("ops/deploy", "部署流程", new PageSectionInput("步骤", "部署前先备份数据库。"));
        _models.RerankDown = true;
        await Recall("部署");

        _models.RerankDown = false;
        _models.Score = (_, _) => 0.0;
        _home.Clock.Advance(TimeSpan.FromMinutes(1));
        var result = await Recall("部署");

        Assert.Equal("No relevant memory.", result.Text);
    }
}
