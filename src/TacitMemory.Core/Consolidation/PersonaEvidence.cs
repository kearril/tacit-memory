using TacitMemory.Core.Injection;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Persona;
using TacitMemory.Core.Recent;

namespace TacitMemory.Core.Consolidation;

/// <summary>
/// The "Persona evidence" category: every candidate of <c>candidates.md</c> with its scope, section, occurrence records and quotes,
/// whatever the baseline (capped), then the source quotes of the activities added since the baseline (all of them). Each kind is
/// listed oldest first. Nothing here asks a model.
/// </summary>
internal static class PersonaEvidence
{
    /// <summary>The category from the candidate review, which is null when <c>candidates.md</c> is not valid UTF-8.</summary>
    public static async Task<ConsolidationSection> ProduceAsync(ConsolidationContext context, CandidateReview? review, CancellationToken cancellationToken)
    {
        if (review is null)
            return new ConsolidationSection(ConsolidationTitles.PersonaEvidence, [], CandidateReview.NotReadable);

        var candidates = new List<(DateOnly Day, string Text)>();
        foreach (var judgment in review.Judgments)
        {
            var candidate = judgment.Candidate;
            var first = candidate.Occurrences.Count == 0 ? DateOnly.MinValue : candidate.Occurrences.Min(occurrence => occurrence.Day);
            var occurrences = candidate.Occurrences.Select(occurrence =>
                $"{occurrence.Day:yyyy-MM-dd} {(occurrence.Rejected ? StructureWords.Rejected + " " : "")}{StructureWords.Quote}「{occurrence.Quote}」");
            var text = $"- \"{candidate.Text}\"; scope: {candidate.Scope}; section: {PersonaDocument.HeadingOf(candidate.Section)}; occurrences: {string.Join("; ", occurrences)}.";
            candidates.Add((first, text));
        }

        var quotes = new List<(DateOnly Day, string Text)>();
        foreach (var (activity, comment) in await context.NewActivitiesAsync(cancellationToken))
        {
            if (comment is null)
                continue;
            var workstream = activity.Workstream ?? "none";
            quotes.Add((activity.Day, $"- {activity.Day:yyyy-MM-dd} activity [{activity.Code}] workstream {workstream}: {comment}"));
        }

        return new ConsolidationSection(ConsolidationTitles.PersonaEvidence, Ordered(candidates), Increments: Ordered(quotes));
    }

    private static List<string> Ordered(IEnumerable<(DateOnly Day, string Text)> entries) =>
        [.. entries.OrderBy(entry => entry.Day).ThenBy(entry => entry.Text, StringComparer.Ordinal).Select(entry => entry.Text)];
}
