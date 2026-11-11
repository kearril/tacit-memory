using System.Text.RegularExpressions;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Persona;
using TacitMemory.Core.Recent;

namespace TacitMemory.Core.Consolidation;

/// <summary>
/// The "Persona evidence" category: every candidate of <c>candidates.md</c> with its scope, section, occurrence records and quotes,
/// whatever the baseline, and the source quotes of the activities added since the baseline. Both kinds are dated and listed
/// oldest first. Nothing here asks a model.
/// </summary>
internal static class PersonaEvidence
{
    /// <summary>A source comment under an activity: its inner text is the quote mark and the quote, in either language.</summary>
    private static readonly Regex SourceComment = new(
        @"^\s*<!--\s*(?<inner>(?:原话|quote)「.*」)\s*-->\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The category from the candidate review, which is null when <c>candidates.md</c> is not valid UTF-8.</summary>
    public static async Task<ConsolidationSection> ProduceAsync(ConsolidationContext context, CandidateReview? review, CancellationToken cancellationToken)
    {
        if (review is null)
            return new ConsolidationSection(ConsolidationTitles.PersonaEvidence, [], CandidateReview.NotReadable);

        var language = context.Config.Language;
        var entries = new List<(DateOnly Day, int Order, string Text)>();
        foreach (var judgment in review.Judgments)
        {
            var candidate = judgment.Candidate;
            var first = candidate.Occurrences.Count == 0 ? DateOnly.MinValue : candidate.Occurrences.Min(occurrence => occurrence.Day);
            var occurrences = candidate.Occurrences.Select(occurrence =>
                $"{occurrence.Day:yyyy-MM-dd} {(occurrence.Rejected ? StructureWords.Rejected.In(language) + " " : "")}{StructureWords.Quote.In(language)}「{occurrence.Quote}」");
            var text = $"- \"{candidate.Text}\"; scope: {candidate.Scope}; section: {PersonaDocument.HeadingOf(candidate.Section).In(language)}; occurrences: {string.Join("; ", occurrences)}.";
            entries.Add((first, 0, text));
        }

        foreach (var (activity, comment) in await NewActivitiesAsync(context, cancellationToken))
        {
            if (comment is null)
                continue;
            var workstream = activity.Workstream ?? "none";
            entries.Add((activity.Day, 1, $"- {activity.Day:yyyy-MM-dd} activity [{activity.Code}] workstream {workstream}: {comment}"));
        }

        var lines = entries
            .OrderBy(entry => entry.Day)
            .ThenBy(entry => entry.Order)
            .ThenBy(entry => entry.Text, StringComparer.Ordinal)
            .Select(entry => entry.Text)
            .ToList();
        return new ConsolidationSection(ConsolidationTitles.PersonaEvidence, lines);
    }

    /// <summary>
    /// The activities that are not in the baseline's month file, each with the inner text of its source comment, or null when it
    /// has none. An activity is new when its short code is missing from the file as the baseline commit has it; without a
    /// baseline, every activity is new.
    /// </summary>
    internal static async Task<List<(RecentFile.Activity Activity, string? Comment)>> NewActivitiesAsync(ConsolidationContext context, CancellationToken cancellationToken)
    {
        var result = new List<(RecentFile.Activity, string?)>();
        foreach (var (relative, full) in RecentFile.MonthFiles(context.Config.MemoryPath))
        {
            if (TextFile.Read(full) is not { } read)
                continue;
            var before = context.Baseline.Commit is { } commit ? await context.Git.ShowAsync(commit.Hash, relative, cancellationToken) : null;
            var known = before is null
                ? new HashSet<string>(StringComparer.Ordinal)
                : RecentFile.Activities(before).Select(activity => activity.Code).ToHashSet(StringComparer.Ordinal);

            var lines = read.Text.Split('\n');
            foreach (var activity in RecentFile.Activities(read.Text))
            {
                if (known.Contains(activity.Code))
                    continue;
                var match = activity.Line + 1 < lines.Length ? SourceComment.Match(lines[activity.Line + 1].TrimEnd('\r')) : null;
                result.Add((activity, match is { Success: true } ? match.Groups["inner"].Value : null));
            }
        }

        return result;
    }
}
