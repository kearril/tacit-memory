using TacitMemory.Core.Injection;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Recent;

namespace TacitMemory.Core.Consolidation;

/// <summary>
/// The activities added since the baseline, each with the quote of its source comment written <c>quote「…」</c>, or null when
/// it has none. An activity is new when its short code is missing from its month file as the baseline commit has it; without
/// a baseline, every activity is new. Read once per report through <see cref="ConsolidationContext.NewActivitiesAsync"/>.
/// </summary>
internal static class NewActivities
{
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
                var quote = RecentFile.CommentAt(lines, activity.Line + 1)?.Quote;
                result.Add((activity, quote is null ? null : $"{StructureWords.Quote}「{quote}」"));
            }
        }

        return result;
    }
}
