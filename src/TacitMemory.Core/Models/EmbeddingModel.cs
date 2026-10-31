using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace TacitMemory.Core.Models;

/// <summary>The embedding model behind an OpenAI-compatible <c>/embeddings</c> endpoint.</summary>
internal sealed class EmbeddingModel(HttpClient http, ModelEndpoint endpoint, ModelHealth health)
{
    /// <summary>Texts sent in one request.</summary>
    public const int BatchSize = 32;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public string Name => endpoint.Model;

    public ModelHealth Health => health;

    /// <summary>One vector per text, or <c>null</c> when the model cannot be reached or answers wrongly.</summary>
    public async Task<float[][]?> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        if (texts.Count == 0)
            return [];
        if (!health.ShouldTry)
            return null;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            var vectors = new List<float[]>(texts.Count);
            foreach (var batch in texts.Chunk(BatchSize))
            {
                using var response = await http.PostAsJsonAsync(
                    ModelUrls.Append(endpoint.Endpoint, "embeddings"), new Request(endpoint.Model, batch), timeout.Token);
                response.EnsureSuccessStatusCode();
                var body = await response.Content.ReadFromJsonAsync<Response>(timeout.Token);
                var data = body?.Data?.OrderBy(item => item.Index).Select(item => item.Embedding).ToList();
                if (data is null || data.Count != batch.Length || data.Any(vector => vector is not { Length: > 0 } || vector.Length != data[0]!.Length))
                    throw new HttpRequestException("The embedding response does not hold one vector per input.");
                vectors.AddRange(data!);
            }

            if (vectors.Any(vector => vector.Length != vectors[0].Length))
                throw new HttpRequestException("The embedding vectors differ in length.");
            health.Succeeded();
            return [.. vectors];
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
        [property: JsonPropertyName("input")] IReadOnlyList<string> Input);

    private sealed record Response([property: JsonPropertyName("data")] IReadOnlyList<Item>? Data);

    private sealed record Item(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("embedding")] float[]? Embedding);
}

internal static class ModelUrls
{
    /// <summary><paramref name="endpoint"/> with <paramref name="path"/> appended, whether or not it ends with a slash.</summary>
    public static Uri Append(Uri endpoint, string path) => new(endpoint.AbsoluteUri.TrimEnd('/') + "/" + path);
}
