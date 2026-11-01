using TacitMemory.Core.Injection;

namespace TacitMemory.Core.Recent;

/// <summary>
/// What the server instructions say about recent activity, read from the memory repository: the mainline items, the
/// activities of the last seven days (newest first, at most seven), and the project labels of every activity.
/// </summary>
internal sealed record RecentSummary(
    IReadOnlyList<string> Mainline, IReadOnlyList<RecentFile.Activity> Week, IReadOnlyList<string> ProjectLabels)
{
    /// <summary>How many days back, counting today, the summary covers.</summary>
    public const int WindowDays = 7;

    /// <summary>How many activities the summary lists at most.</summary>
    public const int ListedActivities = 7;

    /// <summary>Reads <c>recent.md</c> and every activity month file under <c>recent/</c>.</summary>
    public static RecentSummary Read(string memoryPath, DateOnly today)
    {
        var mainline = MainlineSection.Items(TextFile.Read(Path.Combine(memoryPath, MainlineSection.RelativePath))?.Text ?? "");

        var folder = Path.Combine(memoryPath, "recent");
        var activities = new List<RecentFile.Activity>();
        if (Directory.Exists(folder))
        {
            foreach (var file in Directory.GetFiles(folder, "*.md").Order(StringComparer.Ordinal))
            {
                if (!RecentFile.IsMonthFileName(Path.GetFileName(file)) || TextFile.Read(file) is not { } read)
                    continue;
                activities.AddRange(RecentFile.Activities(read.Text));
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

        return new RecentSummary(mainline, week, labels);
    }
}
