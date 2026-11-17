using System.Globalization;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Recent;
using TacitMemory.Core.Workstreams;

namespace TacitMemory.Core.Consolidation;

/// <summary>
/// The material of the summary categories in part A: the completed ISO weeks and calendar months that have activities but no
/// summary line yet, and the workstream material. Everything is read from the memory repository as it is now and nothing is
/// written: the agent writes the summaries and the workstream entries itself.
/// </summary>
internal static class SummaryMaterial
{
    /// <summary>The group of the activities that have no workstream label.</summary>
    private const string OtherGroup = "Other";

    /// <summary>Each completed week that lacks a summary, oldest first, with all of its activities grouped by workstream.</summary>
    public static ConsolidationSection Weeks(ConsolidationContext context) =>
        new(ConsolidationTitles.WeeksToSummarize,
            SummaryCalendar.Unsummarized(context.Config.MemoryPath, context.Today)
                .Where(period => period.Kind == SummaryKind.Week)
                .Select(WeekFinding)
                .ToList());

    /// <summary>Each completed month that lacks a summary, oldest first, with the week summaries written in its file.</summary>
    public static ConsolidationSection Months(ConsolidationContext context)
    {
        var summaries = ReadSummaries(context.Config.MemoryPath);
        var findings = new List<string>();
        foreach (var period in SummaryCalendar.Unsummarized(context.Config.MemoryPath, context.Today).Where(period => period.Kind == SummaryKind.Month))
        {
            var lines = new List<string>
            {
                $"- {period.Label} ({Day(period.First)} to {Day(period.Last)}): write its summary line in {MemoryLayout.RecentMonth(period.First)}. Week summaries written in that month:",
            };
            var weeks = summaries
                .Where(summary => summary.Kind == SummaryKind.Week && SummaryLines.FileMonthOfWeek(summary.First) == period.First)
                .OrderBy(summary => summary.First)
                .ToList();
            if (weeks.Count == 0)
                lines.Add("  - None yet.");
            else
                lines.AddRange(weeks.Select(week => $"  - {week.Text}"));
            findings.Add(string.Join('\n', lines));
        }

        return new ConsolidationSection(ConsolidationTitles.MonthsToSummarize, findings);
    }

    /// <summary>
    /// The established workstreams that have activities since the baseline, the in-progress week included, each with its
    /// current entry and those activities, oldest first. A workstream without new activity is not listed. With no established
    /// workstream the section says so in one line.
    /// </summary>
    public static async Task<ConsolidationSection> WorkstreamAsync(ConsolidationContext context, CancellationToken cancellationToken)
    {
        var established = WorkstreamFile.Established(context.Config.MemoryPath);
        if (established.Count == 0)
            return new ConsolidationSection(ConsolidationTitles.WorkstreamMaterial, ["No workstream is established in workstreams.md."]);

        var fresh = (await context.NewActivitiesAsync(cancellationToken))
            .Select(item => item.Activity)
            .Where(activity => activity.Workstream is not null)
            .ToList();
        var findings = new List<string>();
        foreach (var entry in established)
        {
            var activities = fresh
                .Where(activity => activity.Workstream == entry.Name)
                .OrderBy(activity => activity.Day)
                .ThenBy(activity => activity.Line)
                .ToList();
            if (activities.Count == 0)
                continue;

            var lines = new List<string>
            {
                $"- {entry.Name}",
                $"  - background: {entry.Background}",
                $"  - recent: {entry.Recent ?? "none yet"}",
                "  - activities since the last consolidation:",
            };
            lines.AddRange(activities.Select(activity => $"    - {Day(activity.Day)} {activity.Body}"));
            findings.Add(string.Join('\n', lines));
        }

        return new ConsolidationSection(ConsolidationTitles.WorkstreamMaterial, findings);
    }

    /// <summary>
    /// One week as a finding: its label and dates, the month file its summary goes into, and its activities grouped by workstream,
    /// the workstreams in name order and the activities that have none last. Each activity is its text as the log holds it.
    /// </summary>
    private static string WeekFinding(UnsummarizedPeriod period)
    {
        var lines = new List<string>
        {
            $"- {period.Label} ({Day(period.First)} to {Day(period.Last)}): write its summary line in {MemoryLayout.RecentMonth(SummaryLines.FileMonthOfWeek(period.First))}. Activities ({period.Activities.Count}):",
        };
        var groups = period.Activities
            .GroupBy(activity => activity.Workstream)
            .OrderBy(group => group.Key is null ? 1 : 0)
            .ThenBy(group => group.Key, StringComparer.Ordinal);
        foreach (var group in groups)
        {
            lines.Add($"  - {group.Key ?? OtherGroup}:");
            lines.AddRange(group.Select(activity => $"    - {Day(activity.Day)} {activity.Body}"));
        }

        return string.Join('\n', lines);
    }

    /// <summary>Every summary line of every month file, in file order.</summary>
    private static List<Summary> ReadSummaries(string memoryPath)
    {
        var summaries = new List<Summary>();
        foreach (var (_, file) in RecentFile.MonthFiles(memoryPath))
        {
            if (TextFile.Read(file) is { } read)
                summaries.AddRange(SummaryLines.All(read.Text));
        }

        return summaries;
    }

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
