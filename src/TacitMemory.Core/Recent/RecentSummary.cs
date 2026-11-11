using TacitMemory.Core.Injection;
using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Recent;

/// <summary>
/// What the injected text says about recent activity, read from the memory repository: the activities of the last seven
/// days (newest first, all of them), the workstream labels of every activity, and the lines of the recent weeks, which the
/// resident block shows.
/// </summary>
/// <param name="Week">The activities of the last seven days, newest first.</param>
/// <param name="WorkstreamLabels">The workstream labels of every activity, distinct, in file order.</param>
/// <param name="RecentWeeks">The lines of the latest completed weeks that have anything in them, newest first; each is <c>- 2026-W43：…</c>.</param>
internal sealed record RecentSummary(
    IReadOnlyList<RecentFile.Activity> Week, IReadOnlyList<string> WorkstreamLabels, IReadOnlyList<string> RecentWeeks)
{
    /// <summary>How many days back, counting today, the summary covers.</summary>
    public const int WindowDays = 7;

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
            .ToList();
        var labels = activities.Select(activity => activity.Workstream).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        var weeks = WeekLines(activities, summaries, start, language);

        return new RecentSummary(week, labels, weeks);
    }

    /// <summary>
    /// The lines of the latest completed weeks that hold an activity or a summary. A week is completed once its Sunday is
    /// before <paramref name="windowStart"/>. A week with a summary line shows the summary; otherwise it shows its activity
    /// count, the workstreams its activities name, and how many of them have no workstream.
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
            var workstreams = covered.Select(activity => activity.Workstream).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            var other = covered.Count(activity => activity.Workstream is null);
            var text = ResidentMessages.Activities(language, covered.Count)
                + (workstreams.Count == 0 ? "" : ResidentMessages.WeekWorkstreams.Format(language, string.Join(ResidentMessages.NameSeparator.In(language), workstreams)))
                + (other == 0 ? "" : ResidentMessages.WeekOther.Format(language, other));
            lines.Add($"- {week.Label}：{text}");
        }
        return lines;
    }
}
