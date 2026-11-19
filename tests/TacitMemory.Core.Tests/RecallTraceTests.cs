using TacitMemory.Core.Index;
using TacitMemory.Core.Retrieval;

namespace TacitMemory.Core.Tests;

public sealed class RecallTraceTests : IDisposable
{
    private readonly TestHome _home = new();
    private readonly ModelDouble _models = new();
    private MemoryService? _service;

    public RecallTraceTests()
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
        var result = await Service.Tools.WritePageAsync(new WritePageRequest
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

    private Task<RecallTraceReport> Trace(string query, RetrievalOverrides? overrides = null) =>
        Service.Browse.TraceRecallAsync(new RecallTraceRequest(query, MinRerankScore: overrides?.MinRerankScore, TopK: overrides?.TopK, Candidates: overrides?.Candidates));

    private static TraceUnit Section(RecallTraceReport trace, string page, string section) =>
        trace.Units.Single(unit => unit.Source == page && unit.Section == section);

    [Fact]
    public async Task A_page_that_cannot_be_read_is_skipped_and_the_next_result_takes_its_place()
    {
        // The index is not told of the deletion, so it still returns the page that is gone.
        _home.UseManualSources();
        await Create("ops/rollback", "部署回滚", new PageSectionInput("回滚步骤", "部署失败时先回滚。"));
        await Create("ops/health", "部署健康检查", new PageSectionInput("检查", "部署后检查健康状况。"));
        await Create("ops/noise", "部署杂谈", new PageSectionInput("闲聊", "部署这个词只是顺口提到。"));
        _models.Score = (_, document) =>
            document.Contains("回滚", StringComparison.Ordinal) ? 0.9
            : document.Contains("健康", StringComparison.Ordinal) ? 0.5
            : 0.2;
        File.Delete(Path.Combine(_home.DefaultMemory, "pages", "ops", "rollback.md"));

        var trace = await Trace("部署", new RetrievalOverrides(TopK: 2));

        Assert.Equal(UnitFate.Unreadable, Section(trace, "ops/rollback", "回滚步骤").Fate);
        Assert.Equal(1, Section(trace, "ops/health", "检查").Result);
        Assert.Equal(2, Section(trace, "ops/noise", "闲聊").Result);
        Assert.Equal(2, trace.Results.Count);
        Assert.Contains("2 result(s)", trace.Text);
    }

    [Fact]
    public async Task Each_unit_is_shown_beyond_top_k_or_below_the_threshold_and_sections_of_one_page_share_its_result()
    {
        await Create("ops/rollback", "部署回滚", new("回滚步骤", "部署失败时先回滚。"), new("回滚记录", "部署回滚做过两次。"));
        await Create("ops/health", "部署健康检查", new PageSectionInput("检查", "部署后检查健康状况。"));
        await Create("ops/noise", "部署杂谈", new PageSectionInput("闲聊", "部署这个词只是顺口提到。"));
        _models.Score = (_, document) =>
            document.Contains("回滚", StringComparison.Ordinal) ? 0.9
            : document.Contains("健康", StringComparison.Ordinal) ? 0.5
            : 0.0;

        var trace = await Trace("部署", new RetrievalOverrides(TopK: 1));

        Assert.Equal(UnitFate.Shown, Section(trace, "ops/rollback", "回滚步骤").Fate);
        Assert.Equal(1, Section(trace, "ops/rollback", "回滚步骤").Result);
        Assert.Equal(1, Section(trace, "ops/rollback", "回滚记录").Result);
        Assert.Equal(UnitFate.BeyondTopK, Section(trace, "ops/health", "检查").Fate);
        Assert.Null(Section(trace, "ops/health", "检查").Result);
        Assert.Equal(UnitFate.BelowThreshold, Section(trace, "ops/noise", "闲聊").Fate);
        Assert.Equal(0.0, Section(trace, "ops/noise", "闲聊").RerankScore);
        var result = Assert.Single(trace.Results);
        Assert.Equal("ops/rollback", result.Page);
        Assert.Equal(1, trace.Settings.TopK.Value);
        Assert.Equal(FullTextMethod.Phrase, trace.FullText);
        Assert.All(trace.Units.Where(unit => unit.FullTextRank is not null), unit => Assert.NotNull(unit.Bm25));
        Assert.All(trace.Units.Where(unit => unit.VectorRank is not null), unit => Assert.NotNull(unit.Distance));
        Assert.Equal(Enumerable.Range(1, trace.Units.Count), trace.Units.Select(unit => unit.FusedRank));
    }

    [Fact]
    public async Task Units_fused_after_the_candidates_are_never_reranked()
    {
        // The vector search favours the health page, which full text ranks last, so the two searches disagree and their union
        // is longer than the candidates fusion keeps.
        _models.Embed = text => text.Contains("健康", StringComparison.Ordinal) || text == "部署" ? [1f, 0f] : [0f, 1f];
        await Create("ops/rollback", "部署回滚", new("回滚步骤", "部署失败时先回滚。"), new("回滚记录", "部署回滚做过两次。"));
        await Create("ops/health", "部署健康检查", new PageSectionInput("检查", "部署后检查健康状况。"));

        var trace = await Trace("部署", new RetrievalOverrides(Candidates: 2));

        Assert.Equal(2, trace.Settings.Candidates.Value);
        var beyond = trace.Units.Where(unit => unit.Fate == UnitFate.BeyondCandidates).ToList();
        Assert.NotEmpty(beyond);
        Assert.All(beyond, unit => Assert.True(unit.FusedRank > 2 && unit.RerankScore is null));
        Assert.All(trace.Units.Where(unit => unit.FusedRank <= 2), unit => Assert.NotNull(unit.RerankScore));
    }

    [Fact]
    public async Task The_trace_text_is_what_recall_answers()
    {
        await Create("ops/rollback", "部署回滚", new PageSectionInput("回滚步骤", "部署失败时先回滚。"));
        await Create("ops/health", "部署健康检查", new PageSectionInput("检查", "部署后检查健康状况。"));

        var trace = await Trace("部署");
        var recall = await Service.Tools.RecallAsync(new RecallRequest { Query = "部署" });

        Assert.Equal(recall.Text, trace.Text);
    }

    [Theory]
    [InlineData(null, null, null, null)]
    [InlineData("ops", null, null, null)]
    [InlineData(null, null, null, "activities")]
    [InlineData(null, null, null, "pages")]
    [InlineData(null, "2026-11-01", "2026-11-30", null)]
    public async Task A_trace_shows_the_results_and_text_recall_answers_for_the_same_parameters(string? workstream, string? from, string? to, string? kind)
    {
        await Create("ops/rollback", "部署回滚", new("回滚步骤", "部署失败时先回滚。"), new("回滚记录", "部署回滚做过两次。"));
        await Create("web/deploy", "前端部署", new PageSectionInput("步骤", "部署前端前先构建。"));
        Assert.False((await Service.Tools.WriteRecentAsync(new WriteRecentRequest { Text = "部署流程改为先备份再上线。", Workstream = "ops" })).IsError);

        var report = await Service.Browse.TraceRecallAsync(new RecallTraceRequest("部署", workstream, from, to, kind));
        var recall = await Service.Tools.RecallAsync(new RecallRequest { Query = "部署", Workstream = workstream, From = from, To = to, Kind = kind });

        Assert.Equal(recall.Text, report.Text);
        Assert.Equal(Enumerable.Range(1, report.Results.Count), report.Results.Select(result => result.Number));
        foreach (var result in report.Results)
        {
            var marker = result.Kind == "page" ? $"[{result.Number}] page {result.Page}: {result.Title}" : $"[{result.Number}] activity {result.Date} [{result.Code}]";
            Assert.Contains(marker, recall.Text);
        }

        Assert.Equal(report.Results.Count, report.Counts.Results);
        Assert.Equal(report.Units.Count, report.Counts.Fused);
    }

    [Fact]
    public async Task A_trace_request_with_several_problems_is_refused_with_every_one()
    {
        var refusal = await Assert.ThrowsAsync<TacitException>(() => Service.Browse.TraceRecallAsync(
            new RecallTraceRequest(" ", "Bad Name", "2026-13-01", "2026-11-01", "persona", MinRerankScore: 2, TopK: 0, Candidates: 3)));

        Assert.Equal(TacitError.RequestInvalid, refusal.Error);
        Assert.Equal(["query", "from", "kind", "minRerankScore", "topK"], refusal.Problems!.Select(problem => problem.Path));
        Assert.Contains("0 到 1", refusal.Problems!.Single(problem => problem.Path == "minRerankScore").Message);
    }

    [Theory]
    [InlineData("2026-13-01", null, null, "from")]
    [InlineData(null, "11/30", null, "to")]
    [InlineData("2026-11-30", "2026-11-01", null, "to")]
    [InlineData(null, null, "everything", "kind")]
    public async Task Recall_and_the_trace_refuse_the_same_parameter(string? from, string? to, string? kind, string parameter)
    {
        var recall = await Service.Tools.RecallAsync(new RecallRequest { Query = "部署", From = from, To = to, Kind = kind });
        var refusal = await Assert.ThrowsAsync<TacitException>(() => Service.Browse.TraceRecallAsync(new RecallTraceRequest("部署", null, from, to, kind)));

        Assert.True(recall.IsError);
        Assert.Contains(parameter, recall.Text.Split('\n')[0]);
        Assert.Equal(parameter, Assert.Single(refusal.Problems!).Path);
    }

    [Fact]
    public async Task A_workstream_name_recall_searches_with_is_traced_as_recall_searches_with_it()
    {
        await Create("ops/rollback", "部署回滚", new PageSectionInput("回滚步骤", "部署失败时先回滚。"));

        var report = await Service.Browse.TraceRecallAsync(new RecallTraceRequest("部署", "Bad Name"));
        var recall = await Service.Tools.RecallAsync(new RecallRequest { Query = "部署", Workstream = "Bad Name" });

        Assert.False(recall.IsError, recall.Text);
        Assert.Equal(recall.Text, report.Text);
    }

    [Fact]
    public async Task A_degraded_trace_reports_the_notes_recall_appends()
    {
        await Create("ops/rollback", "部署回滚", new PageSectionInput("回滚步骤", "部署失败时先回滚。"));
        _models.EmbeddingDown = true;
        _models.RerankDown = true;

        var report = await Service.Browse.TraceRecallAsync(new RecallTraceRequest("部署"));
        var recall = await Service.Tools.RecallAsync(new RecallRequest { Query = "部署" });

        Assert.Equal(2, report.Notes.Count);
        Assert.All(report.Notes, note => Assert.Contains(note, recall.Text));
    }

    [Fact]
    public async Task Replaced_values_give_what_recall_gives_with_the_same_values_in_the_config()
    {
        await Create("ops/rollback", "部署回滚", new("回滚步骤", "部署失败时先回滚。"), new("回滚记录", "部署回滚做过两次。"));
        await Create("ops/health", "部署健康检查", new PageSectionInput("检查", "部署后检查健康状况。"));
        await Create("web/deploy", "前端部署", new PageSectionInput("步骤", "部署前端前先构建。"));
        _models.Score = (_, document) => document.Contains("回滚", StringComparison.Ordinal) ? 0.9 : document.Contains("健康", StringComparison.Ordinal) ? 0.4 : 0.1;

        var report = await Service.Browse.TraceRecallAsync(new RecallTraceRequest("部署", MinRerankScore: 0.3, TopK: 1, Candidates: 3));
        _service!.Dispose();
        _service = null;
        File.WriteAllText(
            Path.Combine(_home.TacitHome, "config.yaml"),
            (_models.ModelsYaml() + "retrieval: { minRerankScore: 0.3, topK: 1, candidates: 3 }\n").ReplaceLineEndings("\n"));
        var recall = await Service.Tools.RecallAsync(new RecallRequest { Query = "部署" });

        Assert.Equal(recall.Text, report.Text);
        Assert.Contains("ops/rollback", recall.Text);
        Assert.DoesNotContain("ops/health", recall.Text);
    }

    [Fact]
    public async Task Replaced_values_are_reported_beside_the_configured_ones()
    {
        await Create("ops/rollback", "部署回滚", new PageSectionInput("回滚步骤", "部署失败时先回滚。"));

        var report = await Service.Browse.TraceRecallAsync(new RecallTraceRequest("部署", TopK: 2));

        Assert.Equal(2, report.Settings.TopK.Value);
        Assert.NotEqual(report.Settings.TopK.Value, report.Settings.TopK.Configured);
        Assert.Equal(report.Settings.Candidates.Configured, report.Settings.Candidates.Value);
        Assert.Equal(0.001, report.Settings.MinRerankScore.Configured);
    }

    [Fact]
    public async Task A_reranker_failure_in_a_trace_cools_it_down_for_the_next_recall()
    {
        await Create("ops/rollback", "部署回滚", new PageSectionInput("回滚步骤", "部署失败时先回滚。"));
        _models.RerankDown = true;
        await Service.Browse.TraceRecallAsync(new RecallTraceRequest("部署"));
        _models.RerankDown = false;
        var calls = _models.RerankCalls;

        var recall = await Service.Tools.RecallAsync(new RecallRequest { Query = "部署" });
        var report = await Service.Browse.TraceRecallAsync(new RecallTraceRequest("部署"));

        Assert.Equal(calls, _models.RerankCalls);
        Assert.Contains("reranker model is unavailable", recall.Text);
        Assert.True(report.Reranker.CoolingDown);
        Assert.False(report.Reranker.Answered);
    }

    [Fact]
    public async Task A_single_character_query_is_matched_through_the_full_text_index_with_bm25_values()
    {
        await Create("ops/rollback", "部署回滚", new PageSectionInput("回滚步骤", "部署失败时先回滚。"));
        _models.EmbeddingDown = true;

        var trace = await Trace("滚");

        Assert.Equal(FullTextMethod.Phrase, trace.FullText);
        var matched = trace.Units.Where(unit => unit.FullTextRank is not null).ToList();
        Assert.NotEmpty(matched);
        Assert.All(matched, unit => Assert.NotNull(unit.Bm25));
    }

    [Fact]
    public async Task With_the_embedding_model_down_only_full_text_runs_and_the_note_matches_recall()
    {
        await Create("ops/rollback", "部署回滚", new PageSectionInput("回滚步骤", "部署失败时先回滚。"));
        _models.EmbeddingDown = true;

        var trace = await Trace("部署");
        var recall = await Service.Tools.RecallAsync(new RecallRequest { Query = "部署" });

        Assert.True(trace.Embedding.Configured);
        Assert.False(trace.Embedding.Answered);
        Assert.NotNull(trace.Embedding.Milliseconds);
        Assert.All(trace.Units, unit => Assert.Null(unit.VectorRank));
        Assert.Equal([RecallPipeline.EmbeddingDown], trace.Notes);
        Assert.Equal(recall.Text, trace.Text);
    }

    [Fact]
    public async Task With_the_reranker_down_the_fusion_order_is_final_and_nothing_is_below_the_threshold()
    {
        await Create("ops/rollback", "部署回滚", new PageSectionInput("回滚步骤", "部署失败时先回滚。"));
        await Create("ops/health", "部署健康检查", new PageSectionInput("检查", "部署后检查健康状况。"));
        _models.RerankDown = true;

        var trace = await Trace("部署");
        var recall = await Service.Tools.RecallAsync(new RecallRequest { Query = "部署" });

        Assert.False(trace.Reranker.Answered);
        Assert.All(trace.Units, unit => Assert.Null(unit.RerankScore));
        Assert.DoesNotContain(trace.Units, unit => unit.Fate == UnitFate.BelowThreshold);
        Assert.Equal([RecallPipeline.RerankerDown], trace.Notes);
        Assert.Equal(recall.Text, trace.Text);
    }

    [Fact]
    public async Task A_trace_counts_no_return_where_recall_does()
    {
        await Create("ops/rollback", "部署回滚", new PageSectionInput("回滚步骤", "部署失败时先回滚。"));

        await Trace("部署");
        Assert.All(await Service.PageCountsAsync(), entry => Assert.Equal(0, entry.Value));

        await Service.Tools.RecallAsync(new RecallRequest { Query = "部署" });
        Assert.InRange((await Service.PageCountsAsync())["ops/rollback"], 1, long.MaxValue);
    }

    [Fact]
    public async Task Browsing_a_brief_and_a_page_counts_nothing_while_recall_still_counts()
    {
        await Create("ops/rollback", "部署回滚", new PageSectionInput("回滚步骤", "部署失败时先回滚。"));

        var brief = await Service.Browse.WorkstreamBriefAsync("ops");
        var page = await Service.Browse.PageAsync("ops", "rollback");

        Assert.Equal("ops/rollback", Assert.Single(brief.Groups.SelectMany(group => group.Pages)).Id);
        Assert.Equal("部署回滚", page.Title);
        Assert.All(await Service.PageCountsAsync(), entry => Assert.Equal(0, entry.Value));

        await Service.Tools.RecallAsync(new RecallRequest { Workstream = "ops" });
        Assert.Equal(1, (await Service.PageCountsAsync())["ops/rollback"]);
        await Service.Tools.RecallAsync(new RecallRequest { Page = "ops/rollback" });
        Assert.Equal(2, (await Service.PageCountsAsync())["ops/rollback"]);
    }
}
