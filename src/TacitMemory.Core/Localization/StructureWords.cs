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
    public const string BelongsTo = "Belongs to: ";
    public const string Scope = "Scope: ";
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

    /// <summary>What follows a structure word before its value: <c>## Domain: network-proxy</c>, <c>Trigger: …</c>.</summary>
    public const string Separator = ": ";
}
