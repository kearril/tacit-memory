using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace TacitMemory.Core.Models;

/// <summary>
/// A model that answers structured questions with probabilities. The questions are in English; the text they are
/// about stays as written. Each call returns <c>null</c> when the model cannot be reached or answers wrongly.
/// </summary>
internal interface IDecisionModel
{
    ModelHealth Health { get; }

    /// <summary>Probability of each option, by option key.</summary>
    Task<IReadOnlyDictionary<string, double>?> ChooseAsync(
        string state, string instructions, IReadOnlyDictionary<string, string> options, CancellationToken cancellationToken);

    /// <summary>Probability of each level, in the order given.</summary>
    Task<IReadOnlyList<double>?> ScoreAsync(
        string state, string instructions, IReadOnlyList<string> levels, CancellationToken cancellationToken);

    /// <summary>Probability that the statement described by <paramref name="whenTrue"/> holds.</summary>
    Task<double?> IsTrueAsync(
        string state, string instructions, string whenTrue, string whenFalse, CancellationToken cancellationToken);
}

/// <summary>TypeSafe's native decision API, <c>POST &lt;endpoint&gt;/systemone</c>.</summary>
internal sealed class JevDecisionModel(ModelClient client, DecisionModelEndpoint endpoint, ModelHealth health) : IDecisionModel
{
    private const string QuestionId = "q";

    public ModelHealth Health => health;

    public Task<IReadOnlyDictionary<string, double>?> ChooseAsync(
        string state, string instructions, IReadOnlyDictionary<string, string> options, CancellationToken cancellationToken)
    {
        var criteria = new JsonObject();
        foreach (var (key, text) in options)
            criteria[key] = text;
        return AskAsync<IReadOnlyDictionary<string, double>>(
            state, "choice", instructions, criteria, answer => ReadChoice(answer, options.Keys), cancellationToken);
    }

    public Task<IReadOnlyList<double>?> ScoreAsync(
        string state, string instructions, IReadOnlyList<string> levels, CancellationToken cancellationToken)
    {
        var criteria = new JsonArray(levels.Select(level => (JsonNode)level).ToArray());
        return AskAsync<IReadOnlyList<double>>(state, "score", instructions, criteria, answer => ReadScores(answer, levels), cancellationToken);
    }

    public async Task<double?> IsTrueAsync(
        string state, string instructions, string whenTrue, string whenFalse, CancellationToken cancellationToken)
    {
        var criteria = new JsonObject { ["true"] = whenTrue, ["false"] = whenFalse };
        var value = await AskAsync<JsonValue>(
            state, "noul", instructions, criteria, answer => answer["noul"] is JsonValue { } node && node.TryGetValue<double>(out _) ? node : null,
            cancellationToken);
        return value?.GetValue<double>();
    }

    /// <summary>The API key from the config or the environment variable it names; <c>null</c> when there is none.</summary>
    public static string? Key(DecisionModelEndpoint endpoint) =>
        (endpoint.ApiKey ?? (endpoint.ApiKeyEnv is { } name ? Environment.GetEnvironmentVariable(name) : null)) is { } key
        && !string.IsNullOrWhiteSpace(key) ? key : null;

    /// <summary>
    /// The one question asked, answered and read by <paramref name="read"/>, or <c>null</c> when the request fails.
    /// Without a key the model is disabled at startup and nothing is sent.
    /// </summary>
    private async Task<T?> AskAsync<T>(
        string state, string type, string instructions, JsonNode criteria, Func<JsonObject, T?> read, CancellationToken cancellationToken)
        where T : class
    {
        var key = Key(endpoint);
        if (key is null)
            return null;

        var body = new JsonObject
        {
            ["model"] = endpoint.Model,
            ["state"] = state,
            ["questions"] = new JsonObject
            {
                [QuestionId] = new JsonObject { ["type"] = type, ["instructions"] = instructions, ["criteria"] = criteria },
            },
        };
        return await client.SendAsync(
            health,
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, ModelUrls.Append(endpoint.Endpoint, "systemone"))
                {
                    Content = JsonContent.Create(body),
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                return request;
            },
            (response, token) => ReadAnswerAsync(response, read, token),
            cancellationToken);
    }

    private static async Task<T?> ReadAnswerAsync<T>(HttpResponseMessage response, Func<JsonObject, T?> read, CancellationToken cancellationToken)
        where T : class
    {
        var reply = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        return reply?["answers"]?[QuestionId] is JsonObject answer ? read(answer) : null;
    }

    /// <summary>The probability of every option; one missing or not a number is a wrong answer.</summary>
    private static IReadOnlyDictionary<string, double>? ReadChoice(JsonObject answer, IEnumerable<string> keys)
    {
        if (answer["probabilities"] is not JsonObject probabilities)
            return null;
        var result = new Dictionary<string, double>();
        foreach (var key in keys)
        {
            if (probabilities[key] is not JsonValue value || !value.TryGetValue<double>(out var probability))
                return null;
            result[key] = probability;
        }
        return result;
    }

    /// <summary>
    /// The probability of every level, as an array in order or an object keyed by "1", "2", … or by the level text;
    /// one missing or not a number is a wrong answer.
    /// </summary>
    private static IReadOnlyList<double>? ReadScores(JsonObject answer, IReadOnlyList<string> levels)
    {
        IEnumerable<JsonNode?>? nodes = answer["probabilities"] switch
        {
            JsonArray array => array,
            JsonObject map => Enumerable.Range(0, levels.Count)
                .Select(i => map[(i + 1).ToString(CultureInfo.InvariantCulture)] ?? map[levels[i]]),
            _ => null,
        };
        var list = nodes?.ToList();
        if (list is null || list.Count != levels.Count)
            return null;
        var scores = new double[list.Count];
        for (var i = 0; i < scores.Length; i++)
        {
            if (list[i] is not JsonValue value || !value.TryGetValue<double>(out scores[i]))
                return null;
        }
        return scores;
    }
}
