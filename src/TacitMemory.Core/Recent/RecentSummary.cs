using TacitMemory.Core.Injection;

namespace TacitMemory.Core.Recent;

/// <summary>
/// What the injected text says about recent activity, read from the memory repository: the activities of the last seven
/// days, newest first, all of them.
/// </summary>
/// <param name="Week">The activities of the last seven days, newest first.</param>
internal sealed record RecentSummary(IReadOnlyList<RecentFile.Activity> Week)
{
    /// <summary>How many days back, counting today, the summary covers.</summary>
    public const int WindowDays = 7;

    /// <summary>Reads every activity month file under <c>recent/</c> and keeps the activities of the window.</summary>
    public static RecentSummary Read(string memoryPath, DateOnly today)
    {
        var activities = new List<RecentFile.Activity>();
        foreach (var (_, file) in RecentFile.MonthFiles(memoryPath))
        {
            if (TextFile.Read(file) is { } read)
                activities.AddRange(RecentFile.Activities(read.Text));
        }

        var start = today.AddDays(1 - WindowDays);
        var week = activities
            .Where(activity => activity.Day >= start && activity.Day <= today)
            .Reverse()
            .OrderByDescending(activity => activity.Day)
            .ToList();

        return new RecentSummary(week);
    }
}
