using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace TacitMemory.Core.Models;

/// <summary>The reranker behind an OpenAI-compatible <c>/rerank</c> endpoint, scoring documents against one query.</summary>
internal sealed class RerankerModel(HttpClient http, ModelEndpoint endpoint, ModelHealth health)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public ModelHealth Health => health;

    /// <summary>One relevance score per document in input order, or <c>null</c> when the model cannot be reached or answers wrongly.</summary>
    public async Task<double[]?> ScoreAsync(string query, IReadOnlyList<string> documents, CancellationToken cancellationToken)
    {
        if (documents.Count == 0)
            return [];
        if (!health.ShouldTry)
            return null;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            using var response = await http.PostAsJsonAsync(
                ModelUrls.Append(endpoint.Endpoint, "rerank"), new Request(endpoint.Model, query, documents), timeout.Token);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<Response>(timeout.Token);
            var scores = new double[documents.Count];
            var scored = new bool[documents.Count];
            foreach (var item in body?.Results ?? [])
            {
                if (item.Index is not { } index || index < 0 || index >= documents.Count || scored[index]
                    || item.RelevanceScore is not { } score)
                    throw new HttpRequestException("The rerank response does not score each document once.");
                scores[index] = score;
                scored[index] = true;
            }
            if (scored.Any(done => !done))
                throw new HttpRequestException("The rerank response does not score each document once.");

            health.Succeeded();
            return scores;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException
                                      && !cancellationToken.IsCancellationRequested)
        {
            health.Failed();
            return null;
        }
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
