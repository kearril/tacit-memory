namespace TacitMemory.Core.Consolidation;

/// <summary>
/// The titles of the six report categories, in English. The steps of the tacit-consolidate skill refer to them by these
/// names, and the consolidation report resource uses them as headings, so they are defined once.
/// </summary>
internal static class ConsolidationTitles
{
    public const string DuplicatePages = "Suspected duplicate pages";

    public const string ConflictingEntries = "Conflicting persona entries";

    public const string ConfirmableCandidates = "Confirmable persona candidates";

    public const string RedundantCandidates = "Persona candidates to remove";

    public const string FormatViolations = "Format and rule violations";

    public const string StaleMainline = "Stale mainline";
}
