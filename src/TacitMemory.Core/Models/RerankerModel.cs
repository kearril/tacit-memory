using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace TacitMemory.Core.Models;

/// <summary>
/// The reranker. By the generic protocol, Cohere/Jina Rerank: <c>model</c>, <c>query</c> and <c>documents</c> go to the
/// configured URL, and <c>results[].index</c> and <c>results[].relevance_score</c> come back. Cloudflare Workers AI takes
/// <c>query</c> and <c>contexts[].text</c> with no model name, and answers <c>result.response[].id</c> and
/// <c>score</c> inside its envelope. Either way every document gets exactly one score.
/// </summary>
internal sealed class RerankerModel(ModelClient client, ModelConfig? config, ModelHealth health)
{
    /// <summary>Whether the model is configured. An unconfigured model is never called and never cools down.</summary>
    public bool Configured => config is not null;

    public ModelHealth Health => health;

    /// <summary>
    /// One relevance score per document in input order, or <c>null</c> when the model is not configured, cannot be reached or
    /// answers wrongly. An unconfigured model answers at once, without a request.
    /// </summary>
    public async Task<double[]?> ScoreAsync(string query, IReadOnlyList<string> documents, CancellationToken cancellationToken)
    {
        if (documents.Count == 0)
            return [];
        if (config is not { } settings)
            return null;

        return await client.SendAsync(
            health,
            () => ModelRequest.Post(settings, RequestBody(settings, query, documents)),
            (response, token) => ReadScoresAsync(settings.Provider, response, documents.Count, token),
            cancellationToken);
    }

    private static object RequestBody(ModelConfig settings, string query, IReadOnlyList<string> documents) => settings.Provider switch
    {
        ModelProvider.Cloudflare => new CloudflareRequest(query, documents.Select(text => new Context(text)).ToList()),
        _ => new Request(settings.Model, query, documents),
    };

    private static async Task<double[]?> ReadScoresAsync(ModelProvider provider, HttpResponseMessage response, int count, CancellationToken cancellationToken)
    {
        if (provider == ModelProvider.Cloudflare)
        {
            var answer = (await response.Content.ReadFromJsonAsync<CloudflareEnvelope<CloudflareResult>>(cancellationToken))?.Answer;
            return Scores((answer?.Response ?? []).Select(item => (item?.Id, item?.Score)), count);
        }

        var body = await response.Content.ReadFromJsonAsync<Response>(cancellationToken);
        return Scores((body?.Results ?? []).Select(item => (item?.Index, item?.RelevanceScore)), count);
    }

    /// <summary>
    /// One score per document, each document named by exactly one index in range. A missing, repeated or out-of-range
    /// index, or a missing score, is a wrong answer.
    /// </summary>
    private static double[]? Scores(IEnumerable<(int? Index, double? Score)> items, int count)
    {
        var scores = new double[count];
        var scored = new bool[count];
        foreach (var (index, score) in items)
        {
            if (index is not { } i || i < 0 || i >= count || scored[i] || score is not { } value)
                return null;
            scores[i] = value;
            scored[i] = true;
        }
        return scored.Any(done => !done) ? null : scores;
    }

    private sealed record Request(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("documents")] IReadOnlyList<string> Documents);

    private sealed record Response([property: JsonPropertyName("results")] IReadOnlyList<Item>? Results);

    private sealed record Item(
        [property: JsonPropertyName("index")] int? Index,
        [property: JsonPropertyName("relevance_score")] double? RelevanceScore);

    private sealed record CloudflareRequest(
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("contexts")] IReadOnlyList<Context> Contexts);

    private sealed record Context([property: JsonPropertyName("text")] string Text);

    private sealed record CloudflareResult([property: JsonPropertyName("response")] IReadOnlyList<CloudflareItem>? Response);

    private sealed record CloudflareItem(
        [property: JsonPropertyName("id")] int? Id,
        [property: JsonPropertyName("score")] double? Score);
}
