namespace TacitMemory.Core.Tests;

/// <summary>
/// The probe through the facade: one request per configured model, no retry and no cooldown, the built-in English text
/// only, and the result each kind of answer gives. The probe's time limit is a timer on <see cref="TestHome.Clock"/>, which
/// each test moves; nothing waits in real time except the guard on each await.
/// </summary>
public sealed class ProbeTests : IDisposable
{
    /// <summary>A bound on each await: a probe that waits on a timer the test never moves fails here instead of hanging.</summary>
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);

    /// <summary>The time one probe may take, as the probe itself is specified.</summary>
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(20);

    private readonly TestHome _home = new();
    private readonly ModelDouble _models = new();

    public ProbeTests() => _home.InitReady(models: _models);

    public void Dispose()
    {
        _models.Dispose();
        _home.Dispose();
    }

    private FixedTime Clock => _home.Clock;

    private TacitConfig Config => _home.Tacit().LoadConfig();

    /// <summary>Replaces the config's models section with <paramref name="models"/>; the rest of the config is left as it is.</summary>
    private void UseModels(string models) =>
        File.WriteAllText(Path.Combine(_home.TacitHome, "config.yaml"), models.ReplaceLineEndings("\n"));

    private Task<IReadOnlyList<ProbeResult>> Probe(params ModelRole[] roles) =>
        _home.Tacit().ProbeAsync(Config, roles).WaitAsync(Guard);

    [Fact]
    public async Task A_model_that_answers_in_the_generic_protocol_succeeds()
    {
        var results = await Probe(ModelRole.Embedding, ModelRole.Reranker, ModelRole.Decision);

        Assert.Equal(3, results.Count);
        Assert.All(results, result => Assert.Equal(ProbeOutcome.Succeeded, result.Outcome));
        Assert.All(results, result => Assert.Equal(200, result.StatusCode));
        Assert.Equal(1, _models.EmbeddingCalls);
        Assert.Equal(1, _models.RerankCalls);
        Assert.Equal(1, _models.DecisionCalls);
    }

    [Fact]
    public async Task A_cloudflare_reranker_and_decision_model_succeed_and_the_reranker_sends_no_model_name()
    {
        UseModels(_models.ModelsYaml(rerankerCloudflare: true, decisionCloudflare: true));

        var results = await Probe(ModelRole.Reranker, ModelRole.Decision);

        Assert.All(results, result => Assert.Equal(ProbeOutcome.Succeeded, result.Outcome));
        var rerank = Assert.Single(_models.RerankRequests);
        Assert.Equal("/ai/run/" + ModelDouble.CloudflareRerankModel, rerank.Path);
        Assert.False(rerank.Body.ContainsKey("model"));
        Assert.Single(_models.DecisionRequests);
    }

    [Fact]
    public async Task A_client_error_fails_with_its_status_and_the_first_500_characters_of_its_body()
    {
        var body = new string('a', 300) + new string('b', 300);
        _models.ScriptEmbedding(ModelReply.StatusWithBody(401, body));

        var result = (await Probe(ModelRole.Embedding)).Single();

        Assert.Equal(ProbeOutcome.Failed, result.Outcome);
        Assert.Equal(ProbeFailure.HttpStatus, result.Failure);
        Assert.Equal(401, result.StatusCode);
        Assert.Equal(body[..500], result.ResponseBody);
    }

    [Fact]
    public async Task A_server_error_is_sent_once_and_not_retried()
    {
        _models.ScriptEmbedding(ModelReply.StatusWithBody(500, "upstream exploded"));

        var result = (await Probe(ModelRole.Embedding)).Single();

        Assert.Equal(ProbeFailure.HttpStatus, result.Failure);
        Assert.Equal(500, result.StatusCode);
        Assert.Equal("upstream exploded", result.ResponseBody);
        Assert.Equal(1, _models.EmbeddingCalls);
    }

    [Fact]
    public async Task A_success_status_with_the_wrong_shape_fails_as_a_wrong_format()
    {
        _models.ScriptEmbedding(ModelReply.Body("{}"));

        var result = (await Probe(ModelRole.Embedding)).Single();

        Assert.Equal(ProbeOutcome.Failed, result.Outcome);
        Assert.Equal(ProbeFailure.WrongFormat, result.Failure);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("{}", result.ResponseBody);
        Assert.Equal(1, _models.EmbeddingCalls);
    }

    [Fact]
    public async Task A_reranker_that_scores_only_some_documents_is_a_wrong_format()
    {
        _models.ScriptRerank(ModelReply.Body("{\"results\":[{\"index\":0,\"relevance_score\":0.5}]}"));

        var result = (await Probe(ModelRole.Reranker)).Single();

        Assert.Equal(ProbeFailure.WrongFormat, result.Failure);
    }

    [Fact]
    public async Task A_cloudflare_answer_that_is_not_successful_is_a_wrong_format()
    {
        _models.CloudflareReportsFailure = true;
        UseModels(_models.ModelsYaml(rerankerCloudflare: true));

        var result = (await Probe(ModelRole.Reranker)).Single();

        Assert.Equal(ProbeOutcome.Failed, result.Outcome);
        Assert.Equal(ProbeFailure.WrongFormat, result.Failure);
        Assert.Equal(200, result.StatusCode);
        Assert.Single(_models.RerankRequests);
    }

    [Fact]
    public async Task A_model_that_does_not_answer_in_time_times_out_after_twenty_seconds()
    {
        _models.ScriptEmbedding(ModelReply.Hang());

        var probe = Probe(ModelRole.Embedding);
        await Clock.NextTimerAsync(Limit);
        await _models.EmbeddingRequestsReached(1);
        Clock.Elapse(Limit);
        var result = (await probe).Single();

        Assert.Equal(ProbeOutcome.Failed, result.Outcome);
        Assert.Equal(ProbeFailure.Timeout, result.Failure);
        Assert.Null(result.StatusCode);
        Assert.Equal(Limit, result.Elapsed);
        Assert.Equal(1, _models.EmbeddingCalls);
    }

    [Fact]
    public async Task A_dropped_connection_fails_as_a_network_error()
    {
        _models.ScriptEmbedding(ModelReply.Drop());

        var result = (await Probe(ModelRole.Embedding)).Single();

        Assert.Equal(ProbeOutcome.Failed, result.Outcome);
        Assert.Equal(ProbeFailure.Network, result.Failure);
        Assert.Null(result.StatusCode);
        Assert.Null(result.ResponseBody);
        Assert.Equal(1, _models.EmbeddingCalls);
    }

    [Fact]
    public async Task A_model_the_config_does_not_name_is_unconfigured_and_never_called()
    {
        UseModels($"models:\n  decision: {{ url: '{_models.DecisionUrl}', model: double-decision }}\n");

        var results = await Probe(ModelRole.Embedding, ModelRole.Reranker, ModelRole.Decision);

        Assert.Equal(ProbeOutcome.Unconfigured, results[0].Outcome);
        Assert.Equal(ProbeOutcome.Unconfigured, results[1].Outcome);
        Assert.Equal(ProbeOutcome.Succeeded, results[2].Outcome);
        Assert.Equal(TimeSpan.Zero, results[0].Elapsed);
        Assert.Equal(0, _models.EmbeddingCalls);
        Assert.Equal(0, _models.RerankCalls);
    }

    [Fact]
    public async Task The_request_carries_only_the_built_in_text_and_no_memory_content()
    {
        const string marker = "MEMORY-MARKER-ZQX";
        Directory.CreateDirectory(Path.Combine(_home.DefaultMemory, "pages"));
        File.WriteAllText(Path.Combine(_home.DefaultMemory, "pages", "note.md"), marker);

        await Probe(ModelRole.Embedding, ModelRole.Reranker, ModelRole.Decision);

        var sent = _models.EmbeddingRequests.Select(request => request.Body.ToJsonString())
            .Concat(_models.RerankRequests.Select(request => request.Body.ToJsonString()))
            .Concat(_models.DecisionRequests.Select(request => request.Body.ToJsonString()))
            .ToList();
        Assert.Equal(3, sent.Count);
        Assert.All(sent, body => Assert.DoesNotContain(marker, body));
    }

    [Fact]
    public async Task A_failed_probe_does_not_put_a_running_service_model_in_cooldown()
    {
        using var service = _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);
        _models.ScriptEmbedding(ModelReply.Status(503));

        var result = (await Probe(ModelRole.Embedding)).Single();

        Assert.Equal(ProbeFailure.HttpStatus, result.Failure);
        Assert.Equal(1, _models.EmbeddingCalls);
        Assert.DoesNotContain("Unavailable auxiliary models", await service.ServerInstructionsAsync());

        // The service still calls the model at once: the probe left no cooldown behind.
        var recall = await service.RecallAsync(new RecallRequest { Query = "部署" }).WaitAsync(Guard);
        Assert.Equal(2, _models.EmbeddingCalls);
        Assert.DoesNotContain("embedding model is unavailable", recall.Text);
    }

    [Fact]
    public async Task A_key_echoed_in_an_answer_is_not_shown()
    {
        UseModels(_models.ModelsYaml(embeddingKey: "embed-secret-value"));
        _models.ScriptEmbedding(ModelReply.StatusWithBody(401, "bad key embed-secret-value"));

        var result = (await Probe(ModelRole.Embedding)).Single();

        Assert.Equal("bad key ***", result.ResponseBody);
    }
}
