using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace TacitMemory.Core.Models;

/// <summary>The embedding model behind an OpenAI-compatible <c>/embeddings</c> endpoint.</summary>
internal sealed class EmbeddingModel(ModelClient client, ModelEndpoint endpoint, ModelHealth health)
{
    /// <summary>Texts sent in one request.</summary>
    public const int BatchSize = 32;

    public string Name => endpoint.Model;

    public ModelHealth Health => health;

    /// <summary>One vector per text, or <c>null</c> when the model cannot be reached or answers wrongly.</summary>
    public async Task<float[][]?> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        if (texts.Count == 0)
            return [];

        var vectors = new List<float[]>(texts.Count);
        foreach (var batch in texts.Chunk(BatchSize))
        {
            var answer = await client.SendAsync(
                health,
                () => new HttpRequestMessage(HttpMethod.Post, ModelUrls.Append(endpoint.Endpoint, "embeddings"))
                {
                    Content = JsonContent.Create(new Request(endpoint.Model, batch)),
                },
                (response, token) => ReadVectorsAsync(response, batch.Length, token),
                cancellationToken);
            if (answer is null)
                return null;
            vectors.AddRange(answer);
        }

        if (vectors.Any(vector => vector.Length != vectors[0].Length))
        {
            health.Failed();
            return null;
        }
        return [.. vectors];
    }

    private static async Task<List<float[]>?> ReadVectorsAsync(HttpResponseMessage response, int count, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadFromJsonAsync<Response>(cancellationToken);
        var data = body?.Data?.OrderBy(item => item.Index).Select(item => item.Embedding).ToList();
        if (data is null || data.Count != count || data.Any(vector => vector is not { Length: > 0 } || vector.Length != data[0]!.Length))
            return null;
        return data!;
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
