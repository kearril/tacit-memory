using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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
internal sealed class JevDecisionModel(HttpClient http, DecisionModelEndpoint endpoint, ModelHealth health) : IDecisionModel
{
    private const string QuestionId = "q";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public ModelHealth Health => health;

    public async Task<IReadOnlyDictionary<string, double>?> ChooseAsync(
        string state, string instructions, IReadOnlyDictionary<string, string> options, CancellationToken cancellationToken)
    {
        var criteria = new JsonObject();
        foreach (var (key, text) in options)
            criteria[key] = text;
        var answer = await AskAsync(state, "choice", instructions, criteria, cancellationToken);
        if (answer?["probabilities"] is not JsonObject probabilities)
            return Fail();
        var result = options.Keys.ToDictionary(key => key, key => probabilities[key]?.GetValue<double>() ?? 0);
        return Succeed<IReadOnlyDictionary<string, double>>(result);
    }

    public async Task<IReadOnlyList<double>?> ScoreAsync(
        string state, string instructions, IReadOnlyList<string> levels, CancellationToken cancellationToken)
    {
        var answer = await AskAsync(state, "score", instructions, new JsonArray(levels.Select(level => (JsonNode)level).ToArray()), cancellationToken);
        var probabilities = answer?["probabilities"] switch
        {
            JsonArray array => array.Select(node => node?.GetValue<double>() ?? 0).ToList(),
            JsonObject map => Enumerable.Range(0, levels.Count)
                .Select(i => (map[(i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)] ?? map[levels[i]])?.GetValue<double>() ?? 0)
                .ToList(),
            _ => null,
        };
        return probabilities is { } list && list.Count == levels.Count ? Succeed<IReadOnlyList<double>>(list) : FailList();
    }

    public async Task<double?> IsTrueAsync(
        string state, string instructions, string whenTrue, string whenFalse, CancellationToken cancellationToken)
    {
        var answer = await AskAsync(state, "noul", instructions, new JsonObject { ["true"] = whenTrue, ["false"] = whenFalse }, cancellationToken);
        if (answer?["noul"] is not JsonValue value || !value.TryGetValue<double>(out var probability))
        {
            Fail();
            return null;
        }

        health.Succeeded();
        return probability;
    }

    /// <summary>The answer object of the one question asked, or <c>null</c> after marking the model unavailable.</summary>
    private async Task<JsonObject?> AskAsync(string state, string type, string instructions, JsonNode criteria, CancellationToken cancellationToken)
    {
        if (!health.ShouldTry)
            return null;
        var key = endpoint.ApiKey ?? (endpoint.ApiKeyEnv is { } name ? Environment.GetEnvironmentVariable(name) : null);
        if (string.IsNullOrWhiteSpace(key))
        {
            health.Failed();
            return null;
        }

        var body = new JsonObject
        {
            ["model"] = endpoint.Model,
            ["state"] = state,
            ["questions"] = new JsonObject
            {
                [QuestionId] = new JsonObject { ["type"] = type, ["instructions"] = instructions, ["criteria"] = criteria },
            },
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, ModelUrls.Append(endpoint.Endpoint, "systemone"))
            {
                Content = JsonContent.Create(body),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var response = await http.SendAsync(request, timeout.Token);
            response.EnsureSuccessStatusCode();
            var reply = await response.Content.ReadFromJsonAsync<JsonObject>(timeout.Token);
            if (reply?["answers"]?[QuestionId] is JsonObject answer)
                return answer;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException
                                      && !cancellationToken.IsCancellationRequested)
        {
            // Unreachable, slow or malformed: unavailable, below.
        }

        health.Failed();
        return null;
    }

    private IReadOnlyDictionary<string, double>? Fail()
    {
        health.Failed();
        return null;
    }

    private IReadOnlyList<double>? FailList()
    {
        health.Failed();
        return null;
    }

    private T Succeed<T>(T value)
    {
        health.Succeeded();
        return value;
    }
}
