using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace TacitMemory.Core.Models;

/// <summary>
/// A model that answers multiple-choice questions with probabilities. The questions are in English; the text they are
/// about stays as written. Each call returns <c>null</c> when the model cannot be reached or answers wrongly.
/// </summary>
internal interface IDecisionModel
{
    /// <summary>Whether the model is configured. An unconfigured model is never called and never cools down.</summary>
    bool Configured { get; }

    ModelHealth Health { get; }

    /// <summary>Probability of each option, by option key; <c>null</c> when the model is not configured or cannot answer.</summary>
    Task<IReadOnlyDictionary<string, double>?> ChooseAsync(
        string state, string instructions, IReadOnlyDictionary<string, string> options, CancellationToken cancellationToken);
}

/// <summary>
/// The System One protocol, one choice question per request: <c>model</c>, <c>state</c> and <c>questions</c> go to the
/// configured URL, and the probability of each option is read from <c>answers</c>. TypeSafe's native decision API
/// speaks it. Cloudflare Workers AI takes the same request and returns the same answer inside its <c>result</c>.
/// </summary>
internal sealed class SystemOneDecisionModel(ModelClient client, ModelConfig? config, ModelHealth health) : IDecisionModel
{
    private const string QuestionId = "q";

    public bool Configured => config is not null;

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
    /// The one question asked, answered and read by <paramref name="read"/>, or <c>null</c> when the model is not configured
    /// or the request fails.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, double>?> AskAsync(
        string state, string instructions, JsonNode criteria, Func<JsonObject, IReadOnlyDictionary<string, double>?> read, CancellationToken cancellationToken)
    {
        if (config is not { } settings)
            return null;

        var body = new JsonObject
        {
            ["model"] = settings.Model,
            ["state"] = state,
            ["questions"] = new JsonObject
            {
                [QuestionId] = new JsonObject { ["type"] = "choice", ["instructions"] = instructions, ["criteria"] = criteria },
            },
        };
        return await client.SendAsync(
            health,
            () => ModelRequest.Post(settings, body),
            (response, token) => ReadAnswerAsync(settings.Provider, response, read, token),
            cancellationToken);
    }

    /// <summary>
    /// The answer to the question, or <c>null</c> when the reply has none. Cloudflare's reply is read from its
    /// <c>result</c>; a reply that is not successful, or has no result, has none.
    /// </summary>
    private static async Task<IReadOnlyDictionary<string, double>?> ReadAnswerAsync(
        ModelProvider provider, HttpResponseMessage response, Func<JsonObject, IReadOnlyDictionary<string, double>?> read, CancellationToken cancellationToken)
    {
        var reply = provider == ModelProvider.Cloudflare
            ? (await response.Content.ReadFromJsonAsync<CloudflareEnvelope<JsonObject>>(cancellationToken))?.Answer
            : await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
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
