namespace TacitMemory.Core.Tests;

/// <summary>
/// The requests the three auxiliary models send, as <see cref="ModelDouble"/> receives them: each goes to its configured URL
/// exactly as written, with no path appended; it carries the fields its protocol names; it sends <c>Authorization: Bearer</c>
/// when the model has a key and no authentication header otherwise. A reranker or decision model with the Cloudflare
/// provider sends Cloudflare's request, and its answer is read from the envelope's <c>result</c>.
/// </summary>
public sealed class ModelProtocolTests : IDisposable
{
    private readonly TestHome _home = new();
    private readonly ModelDouble _models = new();
    private MemoryService? _service;

    public ModelProtocolTests()
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

    /// <summary>Replaces the config, which the service reads when it opens; call before the first use of <see cref="Service"/>.</summary>
    private void Configure(
        string? embeddingKey = null, string? rerankerKey = null, string decisionKey = "test-key",
        bool embeddingCloudflare = false, bool rerankerCloudflare = false, bool decisionCloudflare = false) => File.WriteAllText(
        Path.Combine(_home.TacitHome, "config.yaml"),
        _models.ModelsYaml(
            embeddingKey: embeddingKey,
            rerankerKey: rerankerKey,
            decisionKey: decisionKey,
            embeddingCloudflare: embeddingCloudflare,
            rerankerCloudflare: rerankerCloudflare,
            decisionCloudflare: decisionCloudflare).ReplaceLineEndings("\n"));

    private Task<ToolResult> WritePage(string page, string title, string body) => Service.Tools.WritePageAsync(new WritePageRequest
    {
        Op = "create",
        Type = ToolValues.PageTypes.Work,
        Page = page,
        Title = title,
        Aliases = [title + "别名", title + "又名"],
        Sections = [new("现象", body)],
    });

    private async Task CreatePage(string page, string title, string body)
    {
        var result = await WritePage(page, title, body);
        Assert.False(result.IsError, result.Text);
    }

    /// <summary>Writes one page and recalls, so the embedding model and the reranker are both called.</summary>
    private async Task WriteAndRecall()
    {
        await CreatePage("ops/deploy", "部署流程", "部署前先备份数据库。");
        await Service.Tools.RecallAsync(new RecallRequest { Query = "部署" });
    }

    /// <summary>Writes a second page in the same workstream, which makes the decision model judge whether it repeats the first.</summary>
    private async Task WriteSimilarPage()
    {
        await CreatePage("ops/deploy-again", "部署流程的问题", "部署后中文搜索没有结果。");
    }

    [Fact]
    public async Task Embedding_requests_go_to_the_configured_url_as_written()
    {
        Configure();

        await WriteAndRecall();

        Assert.NotEmpty(_models.EmbeddingRequests);
        Assert.All(_models.EmbeddingRequests, request => Assert.Equal(_models.EmbeddingUrl.AbsolutePath, request.Path));
    }

    [Fact]
    public async Task Reranker_requests_go_to_the_configured_url_as_written()
    {
        Configure();

        await WriteAndRecall();

        Assert.NotEmpty(_models.RerankRequests);
        Assert.All(_models.RerankRequests, request => Assert.Equal(_models.RerankUrl.AbsolutePath, request.Path));
    }

    [Fact]
    public async Task Decision_requests_go_to_the_configured_url_as_written()
    {
        Configure();

        await WriteAndRecall();
        await WriteSimilarPage();

        Assert.NotEmpty(_models.DecisionRequests);
        Assert.All(_models.DecisionRequests, request => Assert.Equal(_models.DecisionUrl.AbsolutePath, request.Path));
    }

    [Fact]
    public async Task Each_request_carries_the_fields_its_protocol_names()
    {
        Configure();

        await WriteAndRecall();
        await WriteSimilarPage();

        var embedding = _models.EmbeddingRequests.First();
        Assert.Equal("double-embedding", embedding.Body["model"]!.GetValue<string>());
        Assert.NotEmpty(embedding.Body["input"]!.AsArray());

        var rerank = _models.RerankRequests.First();
        Assert.Equal("double-reranker", rerank.Body["model"]!.GetValue<string>());
        Assert.False(string.IsNullOrEmpty(rerank.Body["query"]!.GetValue<string>()));
        Assert.NotEmpty(rerank.Body["documents"]!.AsArray());

        var decision = _models.DecisionRequests.Last();
        Assert.Equal("double-decision", decision.Body["model"]!.GetValue<string>());
        Assert.False(string.IsNullOrEmpty(decision.Body["state"]!.GetValue<string>()));
        Assert.Equal("choice", decision.Body["questions"]!.AsObject().Single().Value!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task Requests_of_models_with_a_key_carry_it_as_a_bearer_token()
    {
        Configure(embeddingKey: "emb-key", rerankerKey: "rer-key", decisionKey: "test-key");

        await WriteAndRecall();
        await WriteSimilarPage();

        Assert.NotEmpty(_models.EmbeddingRequests);
        Assert.All(_models.EmbeddingRequests, request => Assert.Equal("Bearer emb-key", request.Authorization));
        Assert.NotEmpty(_models.RerankRequests);
        Assert.All(_models.RerankRequests, request => Assert.Equal("Bearer rer-key", request.Authorization));
        Assert.NotEmpty(_models.DecisionRequests);
        Assert.All(_models.DecisionRequests, request => Assert.Equal("Bearer test-key", request.Authorization));
    }

    [Fact]
    public async Task Requests_of_models_without_a_key_carry_no_authentication_header()
    {
        Configure(decisionKey: string.Empty);

        await WriteAndRecall();
        await WriteSimilarPage();

        Assert.NotEmpty(_models.EmbeddingRequests);
        Assert.All(_models.EmbeddingRequests, request => Assert.Null(request.Authorization));
        Assert.NotEmpty(_models.RerankRequests);
        Assert.All(_models.RerankRequests, request => Assert.Null(request.Authorization));
        Assert.NotEmpty(_models.DecisionRequests);
        Assert.All(_models.DecisionRequests, request => Assert.Null(request.Authorization));
    }

    [Fact]
    public async Task Cloudflare_reranker_requests_carry_query_and_contexts_and_no_model()
    {
        Configure(rerankerCloudflare: true);

        await WriteAndRecall();

        Assert.NotEmpty(_models.RerankRequests);
        Assert.All(_models.RerankRequests, request => Assert.Equal(_models.CloudflareRerankUrl.AbsolutePath, request.Path));
        var body = _models.RerankRequests.First().Body;
        Assert.False(body.ContainsKey("model"));
        Assert.False(body.ContainsKey("documents"));
        Assert.False(string.IsNullOrEmpty(body["query"]!.GetValue<string>()));
        var contexts = body["contexts"]!.AsArray();
        Assert.NotEmpty(contexts);
        Assert.All(contexts, context => Assert.False(string.IsNullOrEmpty(context!["text"]!.GetValue<string>())));
    }

    [Fact]
    public async Task Recall_through_the_cloudflare_reranker_ranks_by_each_scored_id_and_drops_what_scores_below_the_floor()
    {
        Configure(rerankerCloudflare: true);
        await CreatePage("ops/health", "部署健康检查", "部署后检查健康状况。");
        await CreatePage("ops/rollback", "部署回滚方案", "部署失败时先回滚到上一个版本。");
        await CreatePage("ops/noise", "部署杂谈", "部署这个词在这里只是顺口提到。");
        _models.Score = (_, document) => document.Contains("回滚", StringComparison.Ordinal) ? 0.9
            : document.Contains("健康", StringComparison.Ordinal) ? 0.2 : 0.0005;

        var result = await Service.Tools.RecallAsync(new RecallRequest { Query = "部署" });

        Assert.False(result.IsError, result.Text);
        Assert.Contains("ops/rollback", result.Text);
        Assert.True(result.Text.IndexOf("ops/rollback", StringComparison.Ordinal) < result.Text.IndexOf("ops/health", StringComparison.Ordinal), result.Text);
        Assert.DoesNotContain("ops/noise", result.Text);
    }

    [Fact]
    public async Task Cloudflare_decision_requests_carry_the_system_one_body_and_its_answer_is_read_from_result()
    {
        Configure(decisionCloudflare: true);
        await CreatePage("ops/deploy", "部署流程", "部署前先备份数据库。");
        _models.Choose = (_, options) => options.ToDictionary(
            option => option.Key,
            option => option.Value.Contains("部署流程", StringComparison.Ordinal) ? 0.95 : option.Key == options.Keys.Last() ? 0.05 : 0.0);

        var result = await WritePage("ops/deploy-again", "部署流程的问题", "部署后中文搜索没有结果。");

        Assert.True(result.IsError);
        Assert.Contains("already records the same matter", result.Text);
        Assert.NotEmpty(_models.DecisionRequests);
        Assert.All(_models.DecisionRequests, request => Assert.Equal(_models.CloudflareDecisionUrl.AbsolutePath, request.Path));
        var body = _models.DecisionRequests.Last().Body;
        Assert.Equal("double-decision", body["model"]!.GetValue<string>());
        Assert.False(string.IsNullOrEmpty(body["state"]!.GetValue<string>()));
        Assert.Equal("choice", body["questions"]!.AsObject().Single().Value!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task Cloudflare_requests_of_models_with_a_key_carry_it_as_a_bearer_token()
    {
        Configure(rerankerKey: "rer-key", rerankerCloudflare: true, decisionCloudflare: true);

        await WriteAndRecall();
        await WriteSimilarPage();

        Assert.NotEmpty(_models.RerankRequests);
        Assert.All(_models.RerankRequests, request => Assert.Equal("Bearer rer-key", request.Authorization));
        Assert.NotEmpty(_models.DecisionRequests);
        Assert.All(_models.DecisionRequests, request => Assert.Equal("Bearer test-key", request.Authorization));
    }

    [Fact]
    public async Task The_embedding_model_with_the_cloudflare_provider_sends_the_generic_request()
    {
        Configure(embeddingCloudflare: true);

        await WriteAndRecall();

        Assert.NotEmpty(_models.EmbeddingRequests);
        Assert.All(_models.EmbeddingRequests, request =>
        {
            Assert.Equal(_models.EmbeddingUrl.AbsolutePath, request.Path);
            Assert.Equal("double-embedding", request.Body["model"]!.GetValue<string>());
            Assert.NotEmpty(request.Body["input"]!.AsArray());
        });
    }

    [Fact]
    public async Task A_cloudflare_reranker_refusal_is_called_once_and_filtering_is_skipped()
    {
        Configure(rerankerCloudflare: true);
        await CreatePage("ops/deploy", "部署流程", "部署前先备份数据库。");
        _models.CloudflareReportsFailure = true;
        var before = _models.RerankCalls;

        var result = await Service.Tools.RecallAsync(new RecallRequest { Query = "部署" });

        Assert.Equal(before + 1, _models.RerankCalls);
        Assert.Contains("ops/deploy", result.Text);
        Assert.Contains("the reranker model is unavailable", result.Text);
    }

    [Theory]
    [InlineData("{\"success\":false,\"errors\":[],\"messages\":[]}")]
    [InlineData("{\"success\":true,\"errors\":[],\"messages\":[]}")]
    [InlineData("{\"success\":true,\"result\":{\"response\":[{\"id\":99,\"score\":0.9}]}}")]
    [InlineData("{\"success\":true,\"result\":{\"response\":[{\"id\":0,\"score\":0.9},{\"id\":0,\"score\":0.8}]}}")]
    [InlineData("{\"success\":true,\"result\":{\"response\":[]}}")]
    public async Task A_cloudflare_reranker_reply_that_does_not_score_each_document_once_is_called_once_and_skipped(string reply)
    {
        Configure(rerankerCloudflare: true);
        await CreatePage("ops/deploy", "部署流程", "部署前先备份数据库。");
        _models.ScriptRerank(ModelReply.Body(reply));
        var before = _models.RerankCalls;

        var result = await Service.Tools.RecallAsync(new RecallRequest { Query = "部署" });

        Assert.Equal(before + 1, _models.RerankCalls);
        Assert.Contains("the reranker model is unavailable", result.Text);
    }

    [Fact]
    public async Task A_cloudflare_decision_refusal_is_called_once_and_the_model_is_unavailable()
    {
        Configure(decisionCloudflare: true);
        await CreatePage("ops/deploy", "部署流程", "部署前先备份数据库。");
        _models.CloudflareReportsFailure = true;
        var before = _models.DecisionCalls;

        var result = await WritePage("ops/deploy-again", "部署流程的问题", "部署后中文搜索没有结果。");

        Assert.Equal(before + 1, _models.DecisionCalls);
        Assert.False(result.IsError, result.Text);
        Assert.Contains("the decision model is unavailable", result.Text);
    }

    [Theory]
    [InlineData("{\"success\":false,\"errors\":[],\"messages\":[]}")]
    [InlineData("{\"success\":true,\"errors\":[],\"messages\":[]}")]
    [InlineData("{\"success\":true,\"result\":{\"model\":\"clef-flash\"}}")]
    public async Task A_cloudflare_decision_reply_without_an_answer_is_called_once_and_the_model_is_unavailable(string reply)
    {
        Configure(decisionCloudflare: true);
        await CreatePage("ops/deploy", "部署流程", "部署前先备份数据库。");
        _models.ScriptDecision(ModelReply.Body(reply));
        var before = _models.DecisionCalls;

        var result = await WritePage("ops/deploy-again", "部署流程的问题", "部署后中文搜索没有结果。");

        Assert.Equal(before + 1, _models.DecisionCalls);
        Assert.False(result.IsError, result.Text);
        Assert.Contains("the decision model is unavailable", result.Text);
    }
}
