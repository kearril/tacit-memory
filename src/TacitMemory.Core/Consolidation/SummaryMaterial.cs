using System.Globalization;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Recent;

namespace TacitMemory.Core.Consolidation;

/// <summary>
/// The material of the summary categories in part A: the completed ISO weeks and calendar months that have activities but no
/// summary line yet, and the mainline material. Everything is read from the memory repository as it is now and nothing is
/// written: the agent writes the summaries and the mainline itself.
/// </summary>
internal static class SummaryMaterial
{
    /// <summary>The group of the activities that have no project label, in the memory language.</summary>
    private static readonly Localized OtherGroup = new("其他", "Other");

    /// <summary>How many of the latest weekly summaries the mainline material shows.</summary>
    private const int MainlineWeeks = 4;

    /// <summary>Each completed week that lacks a summary, oldest first, with all of its activities grouped by project.</summary>
    public static ConsolidationSection Weeks(ConsolidationContext context) =>
        new(ConsolidationTitles.WeeksToSummarize,
            SummaryCalendar.Unsummarized(context.Config.MemoryPath, context.Today)
                .Where(period => period.Kind == SummaryKind.Week)
                .Select(period => WeekFinding(context.Config.Language, period))
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
                $"- {period.Label} ({Day(period.First)} to {Day(period.Last)}): write its summary line in {RecentFile.RelativePath(period.First)}. Week summaries written in that month:",
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

    /// <summary>The latest weekly summaries, newest first, and the mainline as recent.md holds it now.</summary>
    public static ConsolidationSection Mainline(ConsolidationContext context)
    {
        var memory = context.Config.MemoryPath;
        var latest = ReadSummaries(memory)
            .Where(summary => summary.Kind == SummaryKind.Week)
            .OrderByDescending(summary => summary.First)
            .Take(MainlineWeeks)
            .ToList();

        var lines = new List<string> { "Latest weekly summaries, newest first:" };
        if (latest.Count == 0)
            lines.Add("- None yet.");
        else
            lines.AddRange(latest.Select(summary => $"- {summary.Text}"));

        var items = MainlineSection.ReadItems(memory);
        if (items.Count == 0)
        {
            lines.Add("The mainline is empty; write it from the weekly summaries above.");
        }
        else
        {
            lines.Add("Mainline in recent.md:");
            lines.AddRange(items.Select(item => $"- {item}"));
        }

        return new ConsolidationSection(ConsolidationTitles.MainlineMaterial, [string.Join('\n', lines)]);
    }

    /// <summary>
    /// One week as a finding: its label and dates, the month file its summary goes into, and its activities grouped by project,
    /// the projects in name order and the activities that have none last. Each activity is its text as the log holds it.
    /// </summary>
    private static string WeekFinding(MemoryLanguage language, UnsummarizedPeriod period)
    {
        var lines = new List<string>
        {
            $"- {period.Label} ({Day(period.First)} to {Day(period.Last)}): write its summary line in {RecentFile.RelativePath(SummaryLines.FileMonthOfWeek(period.First))}. Activities ({period.Activities.Count}):",
        };
        var groups = period.Activities
            .GroupBy(activity => activity.Project)
            .OrderBy(group => group.Key is null ? 1 : 0)
            .ThenBy(group => group.Key, StringComparer.Ordinal);
        foreach (var group in groups)
        {
            lines.Add($"  - {group.Key ?? OtherGroup.In(language)}:");
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
