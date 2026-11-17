using System.Globalization;
using System.Text.RegularExpressions;
using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Recent;

/// <summary>Whether a summary covers an ISO week or a calendar month.</summary>
internal enum SummaryKind
{
    Week,
    Month,
}

/// <summary>
/// One summary line of the <c>## Summary</c> section of a month file: <c>- 2026-W45：...</c> for a week or
/// <c>- 2026-11：...</c> for a month. The colon after the label may be full-width or ASCII.
/// </summary>
/// <param name="Line">The line's index in the file.</param>
/// <param name="Kind">Whether the summary covers a week or a month.</param>
/// <param name="Label">The period's label: <c>2026-W45</c> for a week, <c>2026-11</c> for a month.</param>
/// <param name="First">The period's first day: the Monday of a week, the first day of a month.</param>
/// <param name="Last">The period's last day: the Sunday of a week, the last day of a month.</param>
/// <param name="Body">The summary's one line of text.</param>
internal sealed record Summary(int Line, SummaryKind Kind, string Label, DateOnly First, DateOnly Last, string Body)
{
    /// <summary>The summary as the index holds it: its label, the full-width colon and its text.</summary>
    public string Text => $"{Label}：{Body}";
}

/// <summary>
/// The summary section of a month file: its heading, which lines in it are summaries, and what each one covers. A
/// summary is not an activity: it is not under a date heading, takes no short code and is not compared for the same topic.
/// </summary>
internal static partial class SummaryLines
{
    /// <summary>The shape of a summary line before its period is checked: its kind, its numbers and its text.</summary>
    /// <param name="Kind">Week or month.</param>
    /// <param name="Year">The year, as written.</param>
    /// <param name="Number">The week number, or the month number.</param>
    /// <param name="Body">The text after the colon, trimmed and not empty.</param>
    public sealed record Shape(SummaryKind Kind, int Year, int Number, string Body);

    /// <summary>Whether a line is the summary section's heading.</summary>
    public static bool IsHeading(string line) =>
        line.TrimStart().StartsWith("## ", StringComparison.Ordinal) && StructureWords.Is(line.Trim()[3..].Trim(), StructureWords.Summary);

    /// <summary>The index of the summary heading, or -1 when the file has none.</summary>
    public static int HeadingIndex(IReadOnlyList<string> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (IsHeading(lines[i]))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// For each line, whether it lies in the summary section: from the heading to the next <c>## </c> heading, the
    /// heading included.
    /// </summary>
    public static bool[] InSection(IReadOnlyList<string> lines)
    {
        var mask = new bool[lines.Count];
        var inside = false;
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].StartsWith("## ", StringComparison.Ordinal))
                inside = IsHeading(lines[i]);
            mask[i] = inside;
        }

        return mask;
    }

    /// <summary>The shape of a summary line (the whole line, with its <c>- </c>), or null when it is not one.</summary>
    public static Shape? ShapeOf(string line)
    {
        var content = line.TrimEnd('\r');
        if (WeekLine().Match(content) is { Success: true } week && week.Groups["body"].Value.Trim() is { Length: > 0 } weekBody)
        {
            return new Shape(
                SummaryKind.Week,
                int.Parse(week.Groups["year"].Value, CultureInfo.InvariantCulture),
                int.Parse(week.Groups["number"].Value, CultureInfo.InvariantCulture),
                weekBody);
        }

        if (MonthLine().Match(content) is { Success: true } month && month.Groups["body"].Value.Trim() is { Length: > 0 } monthBody)
        {
            return new Shape(
                SummaryKind.Month,
                int.Parse(month.Groups["year"].Value, CultureInfo.InvariantCulture),
                int.Parse(month.Groups["number"].Value, CultureInfo.InvariantCulture),
                monthBody);
        }

        return null;
    }

    /// <summary>
    /// The period a shape names, or null when it does not exist: the week must be one the ISO year has (52 or 53), the
    /// month must be 1 to 12, and the year must be 1 to 9998 so the period's last day is still a date.
    /// </summary>
    public static (string Label, DateOnly First, DateOnly Last)? PeriodOf(Shape shape)
    {
        if (shape.Year is < 1 or > 9998)
            return null;

        var year = shape.Year.ToString("0000", CultureInfo.InvariantCulture);
        if (shape.Kind == SummaryKind.Week)
        {
            if (shape.Number is < 1 || shape.Number > ISOWeek.GetWeeksInYear(shape.Year))
                return null;
            var monday = DateOnly.FromDateTime(ISOWeek.ToDateTime(shape.Year, shape.Number, DayOfWeek.Monday));
            return ($"{year}-W{shape.Number:00}", monday, monday.AddDays(6));
        }

        if (shape.Number is < 1 or > 12)
            return null;
        var first = new DateOnly(shape.Year, shape.Number, 1);
        return ($"{year}-{shape.Number:00}", first, new DateOnly(shape.Year, shape.Number, DateTime.DaysInMonth(shape.Year, shape.Number)));
    }

    /// <summary>The summary a line holds, or null when the line is not a summary line of a real period.</summary>
    public static Summary? Parse(int line, string text)
    {
        if (ShapeOf(text) is not { } shape || PeriodOf(shape) is not { } period)
            return null;
        return new Summary(line, shape.Kind, period.Label, period.First, period.Last, shape.Body);
    }

    /// <summary>Every valid summary in a month file, in file order. Lines outside the summary section are not summaries.</summary>
    public static List<Summary> All(string text)
    {
        var lines = text.Split('\n');
        var inSection = InSection(lines);
        var result = new List<Summary>();
        for (var index = 0; index < lines.Length; index++)
        {
            if (inSection[index] && Parse(index, lines[index].TrimEnd('\r')) is { } summary)
                result.Add(summary);
        }

        return result;
    }

    /// <summary>
    /// The ISO 8601 week a day falls in (Monday to Sunday; the week belongs to the year that holds its Thursday): its
    /// label, its Monday and its Sunday.
    /// </summary>
    public static (string Label, DateOnly First, DateOnly Last) WeekOf(DateOnly day)
    {
        var date = day.ToDateTime(TimeOnly.MinValue);
        var year = ISOWeek.GetYear(date);
        var week = ISOWeek.GetWeekOfYear(date);
        var monday = DateOnly.FromDateTime(ISOWeek.ToDateTime(year, week, DayOfWeek.Monday));
        return ($"{year:0000}-W{week:00}", monday, monday.AddDays(6));
    }

    /// <summary>The label of the calendar month a day falls in: <c>2026-11</c>.</summary>
    public static string MonthLabelOf(DateOnly day) => day.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>The month a week's summary is written in: the month of the week's Thursday, as its first day.</summary>
    public static DateOnly FileMonthOfWeek(DateOnly monday)
    {
        var thursday = monday.AddDays(3);
        return new DateOnly(thursday.Year, thursday.Month, 1);
    }

    [GeneratedRegex(@"^- (?<year>\d{4})-W(?<number>\d{2})[：:](?<body>.*)$")]
    private static partial Regex WeekLine();

    [GeneratedRegex(@"^- (?<year>\d{4})-(?<number>\d{2})[：:](?<body>.*)$")]
    private static partial Regex MonthLine();
}
