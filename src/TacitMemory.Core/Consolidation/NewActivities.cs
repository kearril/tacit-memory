using System.Text.RegularExpressions;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Recent;

namespace TacitMemory.Core.Consolidation;

/// <summary>
/// The activities added since the baseline, each with the inner text of its source comment, or null when it has none. An
/// activity is new when its short code is missing from its month file as the baseline commit has it; without a baseline,
/// every activity is new. Read once per report through <see cref="ConsolidationContext.NewActivitiesAsync"/>.
/// </summary>
internal static class NewActivities
{
    /// <summary>A source comment under an activity: its inner text is the quote mark and the quote.</summary>
    private static readonly Regex SourceComment = new(
        @"^\s*<!--\s*(?<inner>quote「.*」)\s*-->\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static async Task<IReadOnlyList<(RecentFile.Activity Activity, string? Comment)>> ReadAsync(ConsolidationContext context, CancellationToken cancellationToken)
    {
        var result = new List<(RecentFile.Activity Activity, string? Comment)>();
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
