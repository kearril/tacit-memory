using System.Net;
using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace TacitMemory.Core.Tests;

/// <summary>
/// The three auxiliary models on 127.0.0.1, answering from what each test sets: <c>/v1/embeddings</c>,
/// <c>/v1/rerank</c> and <c>/v1/systemone</c>. A model that is down answers 503 at once.
/// </summary>
internal sealed class ModelDouble : IDisposable
{
    private static readonly Lazy<ModelDouble> SharedQuiet = new(() => new ModelDouble(shared: true) { EmbeddingDown = true, RerankDown = true });

    private readonly WebApplication _app;
    private readonly bool _shared;
    private readonly ConcurrentQueue<string> _rerankedDocuments = new();
    private int _embeddingCalls;
    private int _rerankCalls;
    private int _decisionCalls;

    public ModelDouble()
        : this(shared: false)
    {
    }

    private ModelDouble(bool shared)
    {
        _shared = shared;
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        _app = builder.Build();
        _app.MapPost("/v1/embeddings", EmbeddingsAsync);
        _app.MapPost("/v1/rerank", RerankAsync);
        _app.MapPost("/v1/systemone", DecideAsync);
        _app.StartAsync().GetAwaiter().GetResult();
        Endpoint = new Uri($"http://127.0.0.1:{new Uri(_app.Urls.Single()).Port}/v1");
    }

    /// <summary>
    /// The default for tests that do not exercise the models: the embedding model and the reranker are down, and the
    /// decision model answers "none" to every choice, so writes are never duplicates.
    /// </summary>
    public static ModelDouble Quiet => SharedQuiet.Value;

    public Uri Endpoint { get; }

    public bool EmbeddingDown { get; set; }

    public bool RerankDown { get; set; }

    /// <summary>The relevance of a document to a query; by default every document is fully relevant.</summary>
    public Func<string, string, double> Score { get; set; } = (_, _) => 1.0;

    /// <summary>Every document the reranker was sent, in order.</summary>
    public IReadOnlyCollection<string> RerankedDocuments => _rerankedDocuments;

    public int RerankCalls => Volatile.Read(ref _rerankCalls);

    /// <summary>The vector for a text; by default a bag of its character pairs, so similar wording is near.</summary>
    public Func<string, float[]> Embed { get; set; } = BagOfPairs;

    public int EmbeddingCalls => Volatile.Read(ref _embeddingCalls);

    public bool DecisionDown { get; set; }

    /// <summary>Probabilities for a choice question, given the state and the options by key; by default the last option, "none".</summary>
    public Func<string, IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, double>> Choose { get; set; } =
        (_, options) => options.Keys.ToDictionary(key => key, key => key == options.Keys.Last() ? 1.0 : 0.0);

    public int DecisionCalls => Volatile.Read(ref _decisionCalls);

    /// <summary>The body and the Authorization header of the latest decision request.</summary>
    public (JsonObject Body, string? Authorization)? LastDecision { get; private set; }

    /// <summary>A <c>models</c> section pointing every model at this double; without a key the decision model has none.</summary>
    public string ModelsYaml(string embeddingModel = "double-embedding", string? decisionKey = null) => $$"""
        models:
          embedding: { endpoint: '{{Endpoint}}', model: {{embeddingModel}} }
          reranker: { endpoint: '{{Endpoint}}', model: double-reranker }
          decision: { endpoint: '{{Endpoint}}', model: double-decision, {{(decisionKey is null ? "apiKeyEnv: JEV_KEY" : $"apiKey: {decisionKey}")}} }

        """;

    public void Dispose()
    {
        if (_shared)
            return;
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public static float[] BagOfPairs(string text)
    {
        var vector = new float[64];
        for (var i = 0; i + 1 < text.Length; i++)
            vector[(int)((uint)HashCode.Combine(text[i], text[i + 1]) % 64)] += 1;
        var length = MathF.Sqrt(vector.Sum(value => value * value));
        return length == 0 ? [1, .. new float[63]] : vector.Select(value => value / length).ToArray();
    }

    private async Task<IResult> EmbeddingsAsync(HttpRequest request)
    {
        Interlocked.Increment(ref _embeddingCalls);
        if (EmbeddingDown)
            return Results.StatusCode(503);
        var body = await JsonNode.ParseAsync(request.Body);
        var inputs = body!["input"]!.AsArray().Select(node => node!.GetValue<string>()).ToList();
        var data = new JsonArray();
        for (var i = 0; i < inputs.Count; i++)
            data.Add(new JsonObject { ["index"] = i, ["embedding"] = new JsonArray(Embed(inputs[i]).Select(value => (JsonNode)value).ToArray()) });
        return Results.Json(new JsonObject { ["data"] = data });
    }

    private async Task<IResult> RerankAsync(HttpRequest request)
    {
        Interlocked.Increment(ref _rerankCalls);
        if (RerankDown)
            return Results.StatusCode(503);
        var body = await JsonNode.ParseAsync(request.Body);
        var query = body!["query"]!.GetValue<string>();
        var documents = body["documents"]!.AsArray().Select(node => node!.GetValue<string>()).ToList();
        foreach (var document in documents)
            _rerankedDocuments.Enqueue(document);
        // Like the real reranker: one result per document, best first, each naming its document by index.
        var results = documents
            .Select((document, index) => (index, score: Score(query, document)))
            .OrderByDescending(item => item.score)
            .Select(item => (JsonNode)new JsonObject { ["index"] = item.index, ["relevance_score"] = item.score });
        return Results.Json(new JsonObject { ["results"] = new JsonArray(results.ToArray()) });
    }

    private async Task<IResult> DecideAsync(HttpRequest request)
    {
        Interlocked.Increment(ref _decisionCalls);
        var body = (await JsonNode.ParseAsync(request.Body))!.AsObject();
        LastDecision = (body, request.Headers.Authorization.ToString());
        if (DecisionDown)
            return Results.StatusCode(503);
        var answers = new JsonObject();
        foreach (var (id, node) in body["questions"]!.AsObject())
        {
            var question = node!.AsObject();
            var state = body["state"]!.GetValue<string>();
            answers[id] = question["type"]!.GetValue<string>() switch
            {
                "choice" => Choice(state, question["criteria"]!.AsObject()),
                var type => throw new InvalidOperationException($"The double does not answer {type} questions."),
            };
        }

        return Results.Json(new JsonObject { ["model"] = "double-decision", ["answers"] = answers });
    }

    private JsonObject Choice(string state, JsonObject criteria)
    {
        var options = criteria.ToDictionary(option => option.Key, option => option.Value!.GetValue<string>());
        var probabilities = new JsonObject();
        foreach (var (key, probability) in Choose(state, options))
            probabilities[key] = probability;
        return new JsonObject { ["type"] = "choice", ["probabilities"] = probabilities };
    }
}
