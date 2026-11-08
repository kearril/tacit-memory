namespace TacitMemory.Core.Tests;

/// <summary>
/// The requests the three auxiliary models send, as <see cref="ModelDouble"/> receives them: each goes to its configured URL
/// exactly as written, with no path appended; it carries the fields its protocol names; it sends <c>Authorization: Bearer</c>
/// when the model has a key and no authentication header otherwise.
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
    private void Configure(string? embeddingKey = null, string? rerankerKey = null, string decisionKey = "test-key") => File.WriteAllText(
        Path.Combine(_home.TacitHome, "config.yaml"),
        _models.ModelsYaml(embeddingKey: embeddingKey, rerankerKey: rerankerKey, decisionKey: decisionKey).ReplaceLineEndings("\n"));

    private async Task CreatePage(string page, string title, string body)
    {
        var result = await Service.WritePageAsync(new WritePageRequest
        {
            Op = "create",
            Page = page,
            Title = title,
            Aliases = [title + "别名", title + "又名"],
            Sections = [new("现象", body)],
        });
        Assert.False(result.IsError, result.Text);
    }

    /// <summary>Writes one page and recalls, so the embedding model and the reranker are both called.</summary>
    private async Task WriteAndRecall()
    {
        await CreatePage("ops/deploy", "部署流程", "部署前先备份数据库。");
        await Service.RecallAsync(new RecallRequest { Query = "部署" });
    }

    /// <summary>Writes a second page in the same project, which makes the decision model judge whether it repeats the first.</summary>
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
}
