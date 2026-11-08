using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace TacitMemory.Core.Models;

/// <summary>
/// A model that answers multiple-choice questions with probabilities. The questions are in English; the text they are
/// about stays as written. Each call returns <c>null</c> when the model cannot be reached or answers wrongly.
/// </summary>
internal interface IDecisionModel
{
    ModelHealth Health { get; }

    /// <summary>Probability of each option, by option key.</summary>
    Task<IReadOnlyDictionary<string, double>?> ChooseAsync(
        string state, string instructions, IReadOnlyDictionary<string, string> options, CancellationToken cancellationToken);
}

/// <summary>
/// The implementation of the System One protocol, <c>POST &lt;endpoint&gt;/systemone</c>, one choice question per request.
/// TypeSafe's native decision API is one service that speaks it.
/// </summary>
internal sealed class SystemOneDecisionModel(ModelClient client, DecisionModelEndpoint endpoint, ModelHealth health) : IDecisionModel
{
    private const string QuestionId = "q";

    public ModelHealth Health => health;

    public Task<IReadOnlyDictionary<string, double>?> ChooseAsync(
        string state, string instructions, IReadOnlyDictionary<string, string> options, CancellationToken cancellationToken)
    {
        var criteria = new JsonObject();
        foreach (var (key, text) in options)
            criteria[key] = text;
        return AskAsync(state, instructions, criteria, answer => ReadChoice(answer, options.Keys), cancellationToken);
    }

    /// <summary>
    /// The one question asked, answered and read by <paramref name="read"/>, or <c>null</c> when the request fails.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, double>?> AskAsync(
        string state, string instructions, JsonNode criteria, Func<JsonObject, IReadOnlyDictionary<string, double>?> read, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["model"] = endpoint.Model,
            ["state"] = state,
            ["questions"] = new JsonObject
            {
                [QuestionId] = new JsonObject { ["type"] = "choice", ["instructions"] = instructions, ["criteria"] = criteria },
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
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.ApiKey.Value);
                return request;
            },
            (response, token) => ReadAnswerAsync(response, read, token),
            cancellationToken);
    }

    private static async Task<IReadOnlyDictionary<string, double>?> ReadAnswerAsync(
        HttpResponseMessage response, Func<JsonObject, IReadOnlyDictionary<string, double>?> read, CancellationToken cancellationToken)
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
}
