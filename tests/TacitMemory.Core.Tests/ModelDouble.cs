using System.Net;
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
    private static readonly Lazy<ModelDouble> SharedDown = new(() => new ModelDouble(shared: true) { EmbeddingDown = true });

    private readonly WebApplication _app;
    private readonly bool _shared;
    private int _embeddingCalls;

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
        _app.StartAsync().GetAwaiter().GetResult();
        Endpoint = new Uri($"http://127.0.0.1:{new Uri(_app.Urls.Single()).Port}/v1");
    }

    /// <summary>A double whose models are all down; the default for tests that do not exercise the models.</summary>
    public static ModelDouble Down => SharedDown.Value;

    public Uri Endpoint { get; }

    public bool EmbeddingDown { get; set; }

    /// <summary>The vector for a text; by default a bag of its character pairs, so similar wording is near.</summary>
    public Func<string, float[]> Embed { get; set; } = BagOfPairs;

    public int EmbeddingCalls => Volatile.Read(ref _embeddingCalls);

    /// <summary>A <c>models</c> section pointing every model at this double.</summary>
    public string ModelsYaml(string embeddingModel = "double-embedding") => $$"""
        models:
          embedding: { endpoint: '{{Endpoint}}', model: {{embeddingModel}} }
          reranker: { endpoint: '{{Endpoint}}', model: double-reranker }
          decision: { endpoint: '{{Endpoint}}', model: double-decision, apiKeyEnv: JEV_KEY }

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
}
