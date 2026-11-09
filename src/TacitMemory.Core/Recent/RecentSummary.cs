using TacitMemory.Core.Injection;
using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Recent;

/// <summary>
/// What the injected text says about recent activity, read from the memory repository: the activities of the last seven
/// days (newest first, at most seven), the project labels of every activity, and the lines of the recent weeks, which the
/// resident block shows.
/// </summary>
/// <param name="Week">The activities of the last seven days, newest first.</param>
/// <param name="ProjectLabels">The project labels of every activity, distinct, in file order.</param>
/// <param name="RecentWeeks">The lines of the latest completed weeks that have anything in them, newest first; each is <c>- 2026-W43：…</c>.</param>
internal sealed record RecentSummary(
    IReadOnlyList<RecentFile.Activity> Week, IReadOnlyList<string> ProjectLabels, IReadOnlyList<string> RecentWeeks)
{
    /// <summary>How many days back, counting today, the summary covers.</summary>
    public const int WindowDays = 7;

    /// <summary>How many activities the summary lists at most.</summary>
    public const int ListedActivities = 7;

    /// <summary>How many completed weeks the resident block lists at most.</summary>
    public const int ListedWeeks = 3;

    /// <summary>Reads every activity month file under <c>recent/</c>, with its summary lines.</summary>
    public static RecentSummary Read(string memoryPath, DateOnly today, MemoryLanguage language)
    {
        var activities = new List<RecentFile.Activity>();
        var summaries = new List<Summary>();
        foreach (var (_, file) in RecentFile.MonthFiles(memoryPath))
        {
            if (TextFile.Read(file) is { } read)
            {
                activities.AddRange(RecentFile.Activities(read.Text));
                summaries.AddRange(SummaryLines.All(read.Text));
            }
        }

        var start = today.AddDays(1 - WindowDays);
        var week = activities
            .Where(activity => activity.Day >= start && activity.Day <= today)
            .Reverse()
            .OrderByDescending(activity => activity.Day)
            .Take(ListedActivities)
            .ToList();
        var labels = activities.Select(activity => activity.Project).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        var weeks = WeekLines(activities, summaries, start, language);

        return new RecentSummary(week, labels, weeks);
    }

    /// <summary>
    /// The lines of the latest completed weeks that hold an activity or a summary. A week is completed once its Sunday is
    /// before <paramref name="windowStart"/>. A week with a summary line shows the summary; otherwise it shows its activity
    /// count, the projects its activities name, and how many of them have no project.
    /// </summary>
    private static List<string> WeekLines(
        IReadOnlyList<RecentFile.Activity> activities, IReadOnlyList<Summary> summaries, DateOnly windowStart, MemoryLanguage language)
    {
        var weeks = activities.Select(activity => SummaryLines.WeekOf(activity.Day))
            .Concat(summaries.Where(summary => summary.Kind == SummaryKind.Week).Select(summary => (summary.Label, summary.First, summary.Last)))
            .Where(week => week.Last < windowStart)
            .DistinctBy(week => week.Label)
            .OrderByDescending(week => week.First)
            .Take(ListedWeeks);

        var lines = new List<string>();
        foreach (var week in weeks)
        {
            var summary = summaries.FirstOrDefault(candidate => candidate.Kind == SummaryKind.Week && candidate.Label == week.Label);
            if (summary is not null)
            {
                lines.Add($"- {week.Label}：{summary.Body}");
                continue;
            }

            var covered = activities.Where(activity => activity.Day >= week.First && activity.Day <= week.Last).ToList();
            var projects = covered.Select(activity => activity.Project).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            var other = covered.Count(activity => activity.Project is null);
            var text = ResidentMessages.Activities(language, covered.Count)
                + (projects.Count == 0 ? "" : ResidentMessages.WeekProjects.Format(language, string.Join(ResidentMessages.NameSeparator.In(language), projects)))
                + (other == 0 ? "" : ResidentMessages.WeekOther.Format(language, other));
            lines.Add($"- {week.Label}：{text}");
        }
        return lines;
    }
}
