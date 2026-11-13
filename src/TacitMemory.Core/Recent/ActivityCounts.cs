using System.Globalization;
using TacitMemory.Core.Consolidation;
using TacitMemory.Core.Git;
using TacitMemory.Core.Injection;

namespace TacitMemory.Core.Recent;

/// <summary>Counts the activity log per day for the dashboard's heatmap. Reads only; never locks or writes.</summary>
internal static class ActivityCounts
{
    /// <summary>How many weeks the window spans, the week of today included.</summary>
    public const int Weeks = 53;

    /// <summary>
    /// The counts of the window that ends today. A month file that is not valid UTF-8 or cannot be read is skipped and
    /// named; a history that cannot be read leaves the consolidation days empty and says so. Neither fails the call.
    /// </summary>
    public static async Task<ActivityReport> ReadAsync(TacitConfig config, DateOnly today, CancellationToken cancellationToken)
    {
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var from = monday.AddDays(-7 * (Weeks - 1));

        var activities = new List<RecentFile.Activity>();
        var skipped = new List<string>();
        foreach (var (relative, full) in RecentFile.MonthFiles(config.MemoryPath))
        {
            if (!Overlaps(Path.GetFileNameWithoutExtension(full), from, today))
                continue;
            if (ReadText(full) is { } text)
                activities.AddRange(RecentFile.Activities(text));
            else
                skipped.Add(relative);
        }

        var days = activities
            .Where(activity => activity.Day >= from && activity.Day <= today)
            .GroupBy(activity => activity.Day)
            .OrderBy(group => group.Key)
            .Select(group => new ActivityDay(
                group.Key,
                group.Where(activity => activity.Workstream is not null)
                    .GroupBy(activity => activity.Workstream!, StringComparer.Ordinal)
                    .Select(workstream => new WorkstreamCount(workstream.Key, workstream.Count()))
                    .OrderByDescending(count => count.Count)
                    .ThenBy(count => count.Workstream, StringComparer.Ordinal)
                    .ToList(),
                group.Count(activity => activity.Workstream is null)))
            .ToList();

        var (consolidations, unreadable) = await ConsolidationDaysAsync(config, from, today, cancellationToken);
        return new ActivityReport(from, today, days, consolidations, skipped, unreadable);
    }

    /// <summary>Whether the month a month file is named for (<c>yyyy-MM</c>) has a day in the window.</summary>
    private static bool Overlaps(string month, DateOnly from, DateOnly to)
    {
        if (!DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var first))
            return false;
        return first <= to && first.AddMonths(1) > from;
    }

    /// <summary>The file's text, or <c>null</c> when it is not valid UTF-8 or cannot be read at all.</summary>
    private static string? ReadText(string path)
    {
        try
        {
            return TextFile.Read(path)?.Text;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The days of the window with a consolidation commit, judged as the status report's last consolidation is.</summary>
    private static async Task<(IReadOnlyList<DateOnly> Days, bool Unreadable)> ConsolidationDaysAsync(
        TacitConfig config, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        try
        {
            var history = await ConsolidationBaseline.HistoryAsync(new GitCli(config.MemoryPath, config.Language), cancellationToken);
            var days = history
                .Where(ConsolidationBaseline.IsConsolidation)
                .Select(commit => commit.Day)
                .Where(day => day >= from && day <= to)
                .Distinct()
                .Order()
                .ToList();
            return (days, false);
        }
        catch (TacitException e) when (e.Error == TacitError.GitFailed)
        {
            return ([], true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return ([], true);
        }
    }
}
