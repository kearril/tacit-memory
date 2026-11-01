using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace TacitMemory.Core.Models;

/// <summary>The reranker behind an OpenAI-compatible <c>/rerank</c> endpoint, scoring documents against one query.</summary>
internal sealed class RerankerModel(ModelClient client, ModelEndpoint endpoint, ModelHealth health)
{
    public ModelHealth Health => health;

    /// <summary>One relevance score per document in input order, or <c>null</c> when the model cannot be reached or answers wrongly.</summary>
    public async Task<double[]?> ScoreAsync(string query, IReadOnlyList<string> documents, CancellationToken cancellationToken)
    {
        if (documents.Count == 0)
            return [];

        return await client.SendAsync(
            health,
            () => new HttpRequestMessage(HttpMethod.Post, ModelUrls.Append(endpoint.Endpoint, "rerank"))
            {
                Content = JsonContent.Create(new Request(endpoint.Model, query, documents)),
            },
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
