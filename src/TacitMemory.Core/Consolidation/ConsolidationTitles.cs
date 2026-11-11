namespace TacitMemory.Core.Consolidation;

/// <summary>
/// The titles of the consolidation report, in English: its two parts and their categories. The steps of the tacit-consolidate
/// skill refer to them by these names, and the consolidation report resource uses them as headings, so they are defined once.
/// </summary>
internal static class ConsolidationTitles
{
    /// <summary>Part A: what the agent does itself, without asking the user.</summary>
    public const string AutomaticPart = "A. Automatic";

    /// <summary>Part B: what the agent gathers into one numbered list for the user.</summary>
    public const string ProposalPart = "B. For proposals";

    public const string FormatViolations = "Format and rule violations";

    public const string RedundantCandidates = "Persona candidates to remove";

    public const string WeeksToSummarize = "Weeks to summarize";

    public const string MonthsToSummarize = "Months to summarize";

    public const string MainlineMaterial = "Mainline material";

    /// <summary>Part B: the unestablished workstream names, the topic pages sharing a prefix and the unlabeled activities.</summary>
    public const string WorkstreamProposals = "Workstream proposal material";

    public const string PersonaEvidence = "Persona evidence";

    public const string IncidentLessons = "Incident lessons";

    public const string ChangedPages = "Changed pages";

    public const string DuplicatePages = "Suspected duplicate pages";

    public const string ConflictingEntries = "Conflicting persona entries";

    /// <summary>One line per domain of the persona that holds exactly one entry.</summary>
    public const string DomainHygiene = "Domain hygiene";

    /// <summary>The items and the resident block longer than the hint lengths in the config; hints only, nothing is refused.</summary>
    public const string LengthHints = "Length hints";
}
