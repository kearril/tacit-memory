using TacitMemory.Core.Retrieval;

namespace TacitMemory.Core.Tests;

/// <summary>
/// Retrieval quality, two-layer universal gating, post-rerank adjustments, and 1-hop page graph expansion verified
/// through the Core facade (<c>Tools.RecallAsync</c> and <c>Browse.TraceRecallAsync</c>).
/// </summary>
public sealed class RetrievalEvolutionTests : IDisposable
{
    private readonly TestHome _home = new();
    private readonly ModelDouble _models = new();
    private MemoryService? _service;
    private DateOnly _day = TestHome.Today;

    public RetrievalEvolutionTests()
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

    private void GoTo(DateOnly target)
    {
        _home.Clock.Advance(TimeSpan.FromDays(target.DayNumber - _day.DayNumber));
        _day = target;
    }

    private async Task CreatePage(
        string page,
        string type,
        string title,
        string[]? related = null,
        string[]? supersedes = null,
        params (string Name, string Body)[] sections)
    {
        var result = await Service.Tools.WritePageAsync(new WritePageRequest
        {
            Op = ToolValues.WritePageOps.Create,
            Page = page,
            Type = type,
            Title = title,
            Aliases = [title + "别名", title + "又名"],
            Related = related,
            Supersedes = supersedes,
            Sections = sections.Select(section => new PageSectionInput(section.Name, section.Body)).ToList(),
        });
        Assert.False(result.IsError, result.Text);
    }

    private async Task WriteActivity(string text, string? workstream = null)
    {
        var result = await Service.Tools.WriteRecentAsync(new WriteRecentRequest { Text = text, Workstream = workstream });
        Assert.False(result.IsError, result.Text);
    }

    [Fact]
    public async Task Superseded_seed_pulls_active_replacement_via_graph_expansion_and_ranks_it_first()
    {
        await CreatePage("my-app/old-cache", ToolValues.PageTypes.Decision, "进程内缓存",
            sections: [("决定", "早期采用进程内字典缓存热点数据。")]);
        await CreatePage("my-app/new-cache", ToolValues.PageTypes.Decision, "共享 Valkey 集群",
            supersedes: ["my-app/old-cache"],
            sections: [("决定", "改用独立 Valkey 实例统一承接会话。")]);

        // Full-text only matches the old page's legacy wording; 1-hop graph expansion injects the active replacement.
        _models.EmbeddingDown = true;
        _models.Score = (_, doc) => doc.Contains("进程内", StringComparison.Ordinal) ? 0.90 : 0.30;

        var trace = await Service.Browse.TraceRecallAsync(new RecallTraceRequest("进程内字典缓存"));

        Assert.Equal(2, trace.Results.Count);
        Assert.Equal("my-app/new-cache", trace.Results[0].Page);
        Assert.Equal(PageType.Decision, trace.Results[0].PageType);
        Assert.False(trace.Results[0].Overturned);

        Assert.Equal("my-app/old-cache", trace.Results[1].Page);
        Assert.True(trace.Results[1].Overturned);
        Assert.Equal(["my-app/new-cache"], trace.Results[1].SupersededBy);

        var expandedUnit = trace.Units.First(unit => unit.Source == "my-app/new-cache" && unit.GraphOrigin == GraphOrigin.Supersedes);
        Assert.Equal(0.30, expandedUnit.RerankScore);
        Assert.True(expandedUnit.FinalScore > 0.75, $"Expected inherited score ~0.765, got {expandedUnit.FinalScore}");

        Assert.Contains("[1] page my-app/new-cache: 共享 Valkey 集群 (decision)\n", trace.Text);
        Assert.Contains("[2] page my-app/old-cache: 进程内缓存 (decision, overturned -> my-app/new-cache)\n", trace.Text);
        // Both pages are already shown, so neither repeats the other in a Links footer.
        Assert.DoesNotContain("Links:", trace.Text);
    }

    [Fact]
    public async Task Standalone_overturned_decision_can_still_rank_first_when_directly_queried()
    {
        await CreatePage("my-app/abandoned-proto", ToolValues.PageTypes.Decision, "弃用的自研协议",
            sections: [("决定", "尝试自研二进制帧协议。"), ("Overturned", "维护成本过高，已彻底放弃。")]);
        await CreatePage("my-app/other-note", ToolValues.PageTypes.Work, "协议压测记录",
            sections: [("产出", "记录协议基准数据。")]);

        _models.Score = (_, doc) => doc.Contains("维护成本", StringComparison.Ordinal) ? 0.95 : 0.40;

        var trace = await Service.Browse.TraceRecallAsync(new RecallTraceRequest("自研协议为什么放弃"));

        Assert.Equal("my-app/abandoned-proto", trace.Results[0].Page);
        Assert.True(trace.Results[0].Overturned);
        Assert.Contains("(decision, overturned)\n", trace.Text);
    }

    [Fact]
    public async Task Related_1hop_expansion_injects_linked_neighbor_and_respects_filters_and_links_footer()
    {
        await CreatePage("my-app/gateway-timeout", ToolValues.PageTypes.Incident, "网关超时排查",
            sections: [("现象", "入口网关间歇性返回 504。")]);
        await CreatePage("my-app/pool-tuning", ToolValues.PageTypes.Decision, "连接池参数规范",
            related: ["my-app/gateway-timeout"],
            sections: [("决定", "上游最大连接数保持 64 并发。")]);
        await CreatePage("other-app/cross-ws", ToolValues.PageTypes.Entity, "跨主线依赖",
            related: ["my-app/gateway-timeout"],
            sections: [("现状", "另一条主线的上游服务。")]);

        // Only gateway-timeout matches directly; 1-hop related expansion injects pool-tuning while workstream filter excludes cross-ws.
        _models.EmbeddingDown = true;
        _models.Score = (_, doc) =>
            doc.Contains("504", StringComparison.Ordinal) ? 0.95
            : doc.Contains("连接池", StringComparison.Ordinal) ? 0.80
            : 0.75;

        // Filtered to my-app: pool-tuning is expanded via related, cross-ws is excluded by workstream filter.
        var trace = await Service.Browse.TraceRecallAsync(new RecallTraceRequest("网关 504", Workstream: "my-app"));
        Assert.Contains(trace.Results, result => result.Page == "my-app/gateway-timeout");
        Assert.Contains(trace.Results, result => result.Page == "my-app/pool-tuning");
        Assert.DoesNotContain(trace.Units, unit => unit.Source == "other-app/cross-ws");
        Assert.Contains(trace.Units, unit => unit.Source == "my-app/pool-tuning" && unit.GraphOrigin == GraphOrigin.Related);
        // Cross-ws was not shown, so it appears in gateway-timeout's Links footer.
        Assert.Contains("Links: related other-app/cross-ws (entity)", trace.Text);
    }

    [Fact]
    public async Task General_search_applies_activity_prior_and_recency_decay_while_pages_never_decay_and_kind_activities_skips_both()
    {
        await CreatePage("my-app/old-decision", ToolValues.PageTypes.Decision, "缓存架构定型",
            sections: [("决定", "统一采用共享缓存架构。")]);
        await WriteActivity("调整共享缓存架构的监控阈值。", workstream: "my-app");

        // Advance clock by 180 days (two half-lives) and add a fresh activity.
        GoTo(_day.AddDays(180));
        await WriteActivity("复盘共享缓存架构的容量规划。", workstream: "my-app");

        // Identical raw rerank score for every unit.
        _models.Score = (_, _) => 0.90;

        var general = await Service.Browse.TraceRecallAsync(new RecallTraceRequest("共享缓存架构"));
        Assert.Equal(3, general.Results.Count);
        Assert.Equal("page", general.Results[0].Kind);
        Assert.Equal("activity", general.Results[1].Kind);
        Assert.Contains("容量规划", general.Results[1].Text);
        Assert.Equal("activity", general.Results[2].Kind);
        Assert.Contains("监控阈值", general.Results[2].Text);

        var pageUnit = general.Units.First(unit => unit.Source == "my-app/old-decision" && unit.Section == "决定");
        var freshUnit = general.Units.First(unit => unit.Text.Contains("容量规划", StringComparison.Ordinal));
        var staleUnit = general.Units.First(unit => unit.Text.Contains("监控阈值", StringComparison.Ordinal));
        Assert.Equal(0.90, pageUnit.FinalScore!.Value, precision: 6);
        Assert.Equal(0.90 * 0.80, freshUnit.FinalScore!.Value, precision: 6);
        Assert.Equal(0.90 * 0.80 * (0.70 + 0.30 * 0.25), staleUnit.FinalScore!.Value, precision: 4);

        // Explicit kind=activities disables type prior and recency decay.
        var activitiesOnly = await Service.Browse.TraceRecallAsync(new RecallTraceRequest("共享缓存架构", Kind: ToolValues.RecallKinds.Activities));
        Assert.All(activitiesOnly.Units, unit => Assert.Equal(0.90, unit.FinalScore!.Value, precision: 6));
    }

    [Fact]
    public async Task Layer1_default_floor_0_20_rejects_noise_and_layer2_relative_cutoff_prunes_weak_tails_but_keeps_multihop()
    {
        await CreatePage("my-app/primary", ToolValues.PageTypes.Decision, "日志归档策略",
            sections: [("决定", "日志按日轮转归档。")]);
        await CreatePage("my-app/secondary", ToolValues.PageTypes.Work, "日志压缩任务",
            sections: [("产出", "归档日志采用 zstd 压缩。")]);
        await CreatePage("my-app/tail", ToolValues.PageTypes.Work, "日志顺带提及",
            sections: [("产出", "某次顺带提到日志一词。")]);
        await CreatePage("my-app/below-floor", ToolValues.PageTypes.Work, "日志低分噪声",
            sections: [("产出", "日志噪声低于默认阈值。")]);

        _models.Score = (_, doc) =>
            doc.Contains("按日轮转", StringComparison.Ordinal) ? 0.95
            : doc.Contains("zstd", StringComparison.Ordinal) ? 0.70
            : doc.Contains("顺带提到", StringComparison.Ordinal) ? 0.25
            : 0.15;

        var trace = await Service.Browse.TraceRecallAsync(new RecallTraceRequest("日志"));

        // Primary (0.95) and Secondary (0.70 >= 0.30 * 0.95 = 0.285) are kept.
        Assert.Equal(["my-app/primary", "my-app/secondary"], trace.Results.Select(r => r.Page));
        // Tail (0.25) passed Layer 1 (0.20) but fell below Layer 2 relative cutoff (0.285).
        Assert.Equal(UnitFate.BelowRelativeFloor, trace.Units.Single(u => u.Source == "my-app/tail" && u.Section == "产出").Fate);
        // Below-floor (0.15) fell below Layer 1 default floor (0.20).
        Assert.Equal(UnitFate.BelowThreshold, trace.Units.Single(u => u.Source == "my-app/below-floor" && u.Section == "产出").Fate);
        Assert.Equal(0.20, trace.Settings.MinRerankScore.Configured);
    }

    [Fact]
    public async Task Activity_slot_cap_reserves_slots_for_pages_in_general_search_but_not_when_only_activities_match()
    {
        await CreatePage("my-app/page-one", ToolValues.PageTypes.Decision, "构建加速方案甲",
            sections: [("决定", "启用增量构建缓存甲。")]);
        await CreatePage("my-app/page-two", ToolValues.PageTypes.Decision, "构建加速方案乙",
            sections: [("决定", "启用并行编译乙。")]);

        for (var i = 1; i <= 4; i++)
        {
            if (i > 1)
                GoTo(_day.AddDays(1));
            await WriteActivity($"第{i}次调整构建加速脚本。", workstream: "my-app");
        }

        // Activities score slightly higher than pages even after 0.80 prior (e.g., 1.0 * 0.80 = 0.80 vs 0.75).
        _models.Score = (_, doc) => doc.Contains("调整构建加速", StringComparison.Ordinal) ? 1.0 : 0.75;

        // General search (topK=5): max 2 activities, leaving room for both detail pages.
        var general = await Service.Browse.TraceRecallAsync(new RecallTraceRequest("构建加速", TopK: 5));
        Assert.Equal(2, general.Results.Count(r => r.Kind == "activity"));
        Assert.Equal(2, general.Results.Count(r => r.Kind == "page"));
        Assert.Equal(2, general.Units.Count(u => u.Fate == UnitFate.BeyondActivityCap));

        // Explicit kind=activities: no cap, all 4 activities shown.
        var activitiesOnly = await Service.Browse.TraceRecallAsync(new RecallTraceRequest("构建加速", Kind: ToolValues.RecallKinds.Activities, TopK: 5));
        Assert.Equal(4, activitiesOnly.Results.Count);
        Assert.DoesNotContain(activitiesOnly.Units, u => u.Fate == UnitFate.BeyondActivityCap);

        // General search when zero pages qualify: no cap, all 4 activities shown.
        _models.Score = (_, doc) => doc.Contains("调整构建加速", StringComparison.Ordinal) ? 0.90 : 0.05;
        var noPages = await Service.Browse.TraceRecallAsync(new RecallTraceRequest("构建加速", TopK: 5));
        Assert.Equal(4, noPages.Results.Count);
        Assert.DoesNotContain(noPages.Units, u => u.Fate == UnitFate.BeyondActivityCap);
    }

    [Fact]
    public async Task Degraded_mode_without_reranker_skips_rerank_gates_but_applies_overturned_demotion_and_replacement_chasing()
    {
        await CreatePage("my-app/old-auth", ToolValues.PageTypes.Decision, "Cookie 会话鉴权",
            sections: [("决定", "早期使用服务器端 Cookie 会话鉴权。")]);
        await CreatePage("my-app/new-auth", ToolValues.PageTypes.Decision, "JWT 无状态令牌",
            supersedes: ["my-app/old-auth"],
            sections: [("决定", "迁移到短期签名令牌鉴权。")]);

        _models.EmbeddingDown = true;
        _models.RerankDown = true;

        var trace = await Service.Browse.TraceRecallAsync(new RecallTraceRequest("Cookie 会话鉴权"));

        Assert.False(trace.Reranker.Answered);
        Assert.Equal("my-app/new-auth", trace.Results[0].Page);
        Assert.Equal("my-app/old-auth", trace.Results[1].Page);
        Assert.DoesNotContain(trace.Units, u => u.Fate is UnitFate.BelowThreshold or UnitFate.BelowRelativeFloor);
    }

    [Fact]
    public async Task Graph_expansion_respects_date_range_and_kind_filters_and_skips_pages_already_in_candidates()
    {
        await CreatePage("my-app/old-plan", ToolValues.PageTypes.Decision, "早期计划",
            sections: [("决定", "早期采用单机批处理脚本。")]);
        await CreatePage("my-app/already-matched", ToolValues.PageTypes.Work, "关联已命中页",
            related: ["my-app/old-plan"],
            sections: [("产出", "单机批处理脚本的压测指标。"), ("补充", "额外压测说明。")]);

        GoTo(_day.AddDays(30));
        await CreatePage("my-app/new-plan", ToolValues.PageTypes.Decision, "新版编排计划",
            supersedes: ["my-app/old-plan"],
            related: ["my-app/old-plan"],
            sections: [("决定", "改用新版编排引擎。")]);

        _models.EmbeddingDown = true;
        _models.Score = (_, _) => 0.85;

        // Date filter ending before new-plan was created must prevent supersedes expansion from leaking new-plan.
        var earlyOnly = await Service.Browse.TraceRecallAsync(new RecallTraceRequest(
            "单机批处理脚本",
            To: TestHome.Today.AddDays(10).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
        Assert.DoesNotContain(earlyOnly.Units, u => u.Source == "my-app/new-plan");

        // already-matched is already present in the direct candidate pool, so related expansion must not re-inject its other units.
        Assert.DoesNotContain(earlyOnly.Units, u => u.Source == "my-app/already-matched" && u.GraphOrigin == GraphOrigin.Related);

        // kind=activities disables page graph expansion completely.
        await WriteActivity("单机批处理脚本调整了日志级别。", workstream: "my-app");
        var activitiesOnly = await Service.Browse.TraceRecallAsync(new RecallTraceRequest("单机批处理脚本", Kind: ToolValues.RecallKinds.Activities));
        Assert.All(activitiesOnly.Units, u => Assert.Equal(GraphOrigin.None, u.GraphOrigin));
    }

    [Fact]
    public async Task Degraded_mode_still_promotes_active_replacement_already_present_with_weaker_rrf()
    {
        await CreatePage("my-app/legacy-queue", ToolValues.PageTypes.Decision, "旧队列轮询",
            sections: [("决定", "旧队列轮询机制每五秒扫描队列任务表。")]);
        await CreatePage("my-app/event-queue", ToolValues.PageTypes.Decision, "事件驱动队列",
            supersedes: ["my-app/legacy-queue"],
            sections: [("决定", "采用通道推送替代轮询。")]);

        _models.EmbeddingDown = true;
        _models.RerankDown = true;

        // Query hits legacy-queue strongly and event-queue weakly (both already in direct candidates).
        var trace = await Service.Browse.TraceRecallAsync(new RecallTraceRequest("旧队列轮询 轮询"));

        Assert.Equal("my-app/event-queue", trace.Results[0].Page);
        Assert.Equal("my-app/legacy-queue", trace.Results[1].Page);
    }

    [Fact]
    public async Task Zero_score_top_result_with_zero_floor_does_not_drop_tied_matches_via_relative_cutoff()
    {
        await CreatePage("my-app/first", ToolValues.PageTypes.Decision, "零分重排甲",
            sections: [("决定", "零分重排测试甲。")]);
        await CreatePage("my-app/second", ToolValues.PageTypes.Decision, "零分重排乙",
            sections: [("决定", "零分重排测试乙。")]);

        _models.Score = (_, _) => 0.0;

        var trace = await Service.Browse.TraceRecallAsync(new RecallTraceRequest("零分重排", MinRerankScore: 0.0));

        Assert.Equal(2, trace.Results.Count);
        Assert.DoesNotContain(trace.Units, u => u.Fate == UnitFate.BelowRelativeFloor);
    }
}
