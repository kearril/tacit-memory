using TacitMemory.Core.Injection;

namespace TacitMemory.Core.Recent;

/// <summary>A completed period that has activities but no summary line yet, with the activities it covers.</summary>
/// <param name="Kind">Whether the period is a week or a month.</param>
/// <param name="Label">The period's label, as a summary line would write it.</param>
/// <param name="First">The period's first day.</param>
/// <param name="Last">The period's last day.</param>
/// <param name="Activities">The activities dated within the period, in file order.</param>
internal sealed record UnsummarizedPeriod(
    SummaryKind Kind, string Label, DateOnly First, DateOnly Last, IReadOnlyList<RecentFile.Activity> Activities);

/// <summary>
/// The deterministic questions the summaries raise: which periods are complete, and which of them still lack a summary
/// line. A period is complete once its last day is before today, so a week ends on its Sunday and a month on its last
/// day; a period without activities never needs a summary.
/// </summary>
internal static class SummaryCalendar
{
    /// <summary>The completed periods of the activity log that have activities and no summary line, oldest first.</summary>
    public static IReadOnlyList<UnsummarizedPeriod> Unsummarized(string memoryPath, DateOnly today)
    {
        var activities = new List<RecentFile.Activity>();
        var summaries = new List<Summary>();
        foreach (var (_, file) in RecentFile.MonthFiles(memoryPath))
        {
            if (TextFile.Read(file) is not { } read)
                continue;
            activities.AddRange(RecentFile.Activities(read.Text));
            summaries.AddRange(SummaryLines.All(read.Text));
        }

        var summarized = summaries.Select(summary => summary.Label).ToHashSet(StringComparer.Ordinal);
        var periods = new Dictionary<string, (SummaryKind Kind, DateOnly First, DateOnly Last)>(StringComparer.Ordinal);
        foreach (var activity in activities)
        {
            var week = SummaryLines.WeekOf(activity.Day);
            periods.TryAdd(week.Label, (SummaryKind.Week, week.First, week.Last));
            var month = SummaryLines.MonthLabelOf(activity.Day);
            var monthFirst = new DateOnly(activity.Day.Year, activity.Day.Month, 1);
            periods.TryAdd(month, (SummaryKind.Month, monthFirst, monthFirst.AddMonths(1).AddDays(-1)));
        }

        var result = new List<UnsummarizedPeriod>();
        foreach (var (label, period) in periods.OrderBy(item => item.Value.First).ThenBy(item => item.Key, StringComparer.Ordinal))
        {
            if (period.Last >= today || summarized.Contains(label))
                continue;
            var covered = activities.Where(activity => activity.Day >= period.First && activity.Day <= period.Last).ToList();
            if (covered.Count > 0)
                result.Add(new UnsummarizedPeriod(period.Kind, label, period.First, period.Last, covered));
        }

        return result;
    }
}
