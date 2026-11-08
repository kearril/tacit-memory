using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace TacitMemory.Core.Models;

/// <summary>
/// The embedding model, speaking the OpenAI Embeddings protocol: <c>model</c> and <c>input</c> go to the configured URL,
/// and <c>data[].index</c> and <c>data[].embedding</c> come back.
/// </summary>
internal sealed class EmbeddingModel(ModelClient client, ModelConfig config, ModelHealth health)
{
    /// <summary>Texts sent in one request.</summary>
    public const int BatchSize = 32;

    public string Name => config.Model;

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
                () => ModelRequest.Post(config, new Request(config.Model, batch)),
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
