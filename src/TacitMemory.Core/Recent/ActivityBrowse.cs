using System.Globalization;
using TacitMemory.Core.Formats;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Recent;

/// <summary>The activity log for the memory page: its months, and one month by day. Counts nothing.</summary>
internal static class ActivityBrowse
{
    public static ActivityMonths Months(string memoryPath, DateOnly today) =>
        new([.. RecentFile.MonthFiles(memoryPath).Select(file => Path.GetFileNameWithoutExtension(file.FullPath)).Reverse()],
            today.ToString("yyyy-MM", CultureInfo.InvariantCulture));

    /// <summary>The month <paramref name="month"/>, written yyyy-MM.</summary>
    /// <exception cref="TacitException">
    /// <see cref="TacitError.RequestInvalid"/> when it is not written yyyy-MM; <see cref="TacitError.MemoryNotFound"/> when
    /// it has no file and is not the current month.
    /// </exception>
    public static ActivityMonthView Month(TacitConfig config, DateOnly today, string month)
    {
        if (month.Length != 7
            || !DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var first))
            throw new TacitException(TacitError.RequestInvalid, ManagementApiMessages.ActivityMonthInvalid.Format(config.Language, month));

        var relative = MemoryLayout.RecentMonth(first);
        var path = MemoryLayout.Full(config.MemoryPath, relative);
        if (!File.Exists(path))
        {
            if (first.Year != today.Year || first.Month != today.Month)
                throw new TacitException(TacitError.MemoryNotFound, ManagementApiMessages.ActivityMonthNotFound.Format(config.Language, month));
            return new ActivityMonthView(month, MemoryFileReport.Of(config, relative), [], []);
        }

        var text = TextFile.Read(path)?.Text ?? "";
        var lines = text.Split('\n');
        var days = RecentFile.Activities(text)
            .GroupBy(activity => activity.Day)
            .OrderBy(group => group.Key)
            .Select(group => new ActivityDayView(Day(group.Key), [.. group.Select(activity => Item(activity, lines))]))
            .ToList();
        var summaries = SummaryLines.All(text)
            .Select(summary => new ActivitySummaryView(
                summary.Kind == SummaryKind.Week ? "week" : "month", summary.Label, Day(summary.First), Day(summary.Last), summary.Body))
            .ToList();
        return new ActivityMonthView(month, MemoryFileReport.Of(config, relative), days, summaries);
    }

    /// <summary>An activity with the indented lines under it.</summary>
    private static ActivityItemView Item(RecentFile.Activity activity, string[] lines)
    {
        var continuation = new List<string>();
        for (var index = activity.Line + 1; index < lines.Length; index++)
        {
            var line = lines[index].TrimEnd('\r');
            if (!line.StartsWith(' ') && !line.StartsWith('\t'))
                break;
            continuation.Add(line);
        }

        return new ActivityItemView(activity.Code, activity.Workstream, activity.Body, continuation);
    }

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
