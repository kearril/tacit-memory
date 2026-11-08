using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace TacitMemory.Core.Models;

/// <summary>
/// The reranker, speaking the Cohere/Jina Rerank protocol: <c>model</c>, <c>query</c> and <c>documents</c> go to the
/// configured URL, and <c>results[].index</c> and <c>results[].relevance_score</c> come back.
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
            () => ModelRequest.Post(settings, new Request(settings.Model, query, documents)),
            (response, token) => ReadScoresAsync(response, documents.Count, token),
            cancellationToken);
    }

    private static async Task<double[]?> ReadScoresAsync(HttpResponseMessage response, int count, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadFromJsonAsync<Response>(cancellationToken);
        var scores = new double[count];
        var scored = new bool[count];
        foreach (var item in body?.Results ?? [])
        {
            if (item.Index is not { } index || index < 0 || index >= count || scored[index] || item.RelevanceScore is not { } score)
                return null;
            scores[index] = score;
            scored[index] = true;
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
}
