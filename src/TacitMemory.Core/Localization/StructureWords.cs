namespace TacitMemory.Core.Localization;

/// <summary>
/// The fixed words that give memory files and <c>rules.md</c> their structure. They are English whatever the memory
/// language, so a memory has the same format on every machine; only the content is written in the memory language.
/// Parsers accept these forms only: a word written in another language is content.
/// </summary>
internal static class StructureWords
{
    public const string RedLines = "Red lines";
    public const string WorkingRules = "Working rules";
    public const string Preferences = "Preferences";
    public const string Profile = "Profile";
    public const string Candidates = "Candidates";
    public const string RejectionList = "Rejection list";
    public const string Summary = "Summary";
    public const string BelongsTo = "Belongs to";
    public const string Scope = "Scope";
    public const string Quote = "quote";
    public const string Rejected = "rejected";
    public const string WorthRecording = "Worth recording";
    public const string NotRecorded = "Not recorded";
    public const string Leftovers = "Leftovers";
    public const string Overturned = "Overturned";
    public const string Domain = "Domain";
    public const string Workstream = "Workstream";
    public const string Background = "Background";
    public const string Recent = "Recent";
    public const string Trigger = "Trigger";
    public const string Lesson = "Lesson";
    public const string Machine = "Machine";

    /// <summary>What follows a structure word before its value: <c>## Domain: network-proxy</c>, <c>Trigger: …</c>.</summary>
    public const string Separator = ": ";

    /// <summary>Whether a heading or name is <paramref name="word"/>, ignoring case.</summary>
    public static bool Is(string text, string word) => string.Equals(text, word, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The text after <paramref name="word"/> and its colon, trimmed; null when the line does not start with the word,
    /// ignoring case, followed by a colon. Spaces may come between the word and the colon.
    /// </summary>
    public static string? After(string line, string word)
    {
        if (!line.StartsWith(word, StringComparison.OrdinalIgnoreCase))
            return null;
        var rest = line[word.Length..].TrimStart();
        return rest.StartsWith(':') ? rest[1..].Trim() : null;
    }

    /// <summary>A field line as written: the word, the separator, then the value.</summary>
    public static string Field(string word, string value) => word + Separator + value;
}
