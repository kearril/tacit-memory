namespace TacitMemory.Core.Models;

/// <summary>
/// The fixed English text a probe sends. It is written here rather than read from memory, so a probe never sends what the
/// memory holds. The decision question has two options, as the judgments do.
/// </summary>
internal static class ProbeInputs
{
    /// <summary>The one sentence an embedding probe embeds.</summary>
    public const string Sentence = "This is a connection check for the embedding model.";

    public const string RerankQuery = "How do I reset my password?";

    /// <summary>Two documents, one that answers the query and one that does not.</summary>
    public static readonly IReadOnlyList<string> RerankDocuments =
    [
        "Reset your password from the account settings page.",
        "The office kitchen is cleaned every Friday.",
    ];

    public const string DecisionState = "Statement: the sky is blue on a clear day.";

    public const string DecisionInstructions = "Choose the option that says whether the statement is true.";

    public static readonly IReadOnlyDictionary<string, string> DecisionOptions = new Dictionary<string, string>
    {
        ["yes"] = "The statement is true.",
        ["no"] = "The statement is false.",
    };
}
