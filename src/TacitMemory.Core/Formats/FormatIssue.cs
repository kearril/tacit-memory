namespace TacitMemory.Core.Formats;

/// <summary>How a format problem is handled: the agent fixes it directly, asks the user, or only reports it.</summary>
internal enum FormatHandling
{
    FixDirectly,
    AskUser,
    HintOnly,
}

/// <summary>
/// A format or rule problem found while a memory file is read again for the report. The parser that finds it does not
/// know the file's path, so <see cref="File"/> is empty until the producer fills it in. <see cref="Location"/> is
/// <c>line N</c> (1-based) or the name of the section it is in; <see cref="Kind"/> is a short English label.
/// </summary>
internal sealed record FormatIssue(string File, string Location, string Kind, string Description, FormatHandling Handling)
{
    /// <summary>The finding the report lists: where the problem is, what kind it is, what is wrong and how to handle it.</summary>
    public string ToFinding() => $"- {File}, {Location} ({Kind}): {Description} {HandlingPhrase(Handling)}";

    private static string HandlingPhrase(FormatHandling handling) => handling switch
    {
        FormatHandling.FixDirectly => "Fix directly.",
        FormatHandling.AskUser => "Ask the user.",
        FormatHandling.HintOnly => "Hint only, no change needed.",
        _ => throw new ArgumentOutOfRangeException(nameof(handling), handling, null),
    };
}
