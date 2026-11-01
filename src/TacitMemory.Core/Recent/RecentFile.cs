using System.Globalization;
using System.Text.RegularExpressions;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Pages;

namespace TacitMemory.Core.Recent;

/// <summary>
/// The recent activity log: one Markdown file per month under <c>recent/</c>, with a <c>## yyyy-MM-dd</c> heading per
/// day and one <c>- </c> line per activity. Adding an activity touches only its own lines; every other byte stays.
/// </summary>
internal static class RecentFile
{
    /// <summary>The month file's path, relative to the memory repository, for the month of <paramref name="day"/>.</summary>
    public static string RelativePath(DateOnly day) => $"recent/{day:yyyy-MM}.md";

    /// <summary>
    /// Adds <paramref name="entry"/> under the day's heading, appending the heading at the end of the file when the day
    /// has none. The entry goes after the day's last non-blank line, so it stays inside the day's block.
    /// </summary>
    /// <returns>The new text, and the index of the entry's first line in it.</returns>
    public static (string Text, int Line) Insert(string text, DateOnly day, IReadOnlyList<string> entry)
    {
        var cr = TextFile.NewlineOf(text) == "\r\n" ? "\r" : "";
        var lines = text.Split('\n').ToList();
        if (lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);

        var heading = $"## {day:yyyy-MM-dd}";
        var headingAt = lines.FindIndex(line => line.TrimEnd('\r') == heading);
        int at;
        if (headingAt < 0)
        {
            lines.Add(heading + cr);
            at = lines.Count;
        }
        else
        {
            var end = headingAt + 1;
            while (end < lines.Count && !lines[end].StartsWith("## ", StringComparison.Ordinal))
                end++;
            at = headingAt + 1;
            for (var i = headingAt + 1; i < end; i++)
            {
                if (lines[i].Trim().Length > 0)
                    at = i + 1;
            }
        }

        lines.InsertRange(at, entry.Select(line => line + cr));
        return (string.Concat(lines.Select(line => line + "\n")), at);
    }

    /// <summary>
    /// The short code of the entry whose first line is <paramref name="line"/>. Codes are counted over every
    /// <c>- </c> line in the file, in file order, with the source comments left out.
    /// </summary>
    public static string CodeAt(string text, int line) => CodesByLine(text)[line];

    /// <summary>The short code of every <c>- </c> line, keyed by the line's index in the file.</summary>
    public static Dictionary<int, string> CodesByLine(string text)
    {
        var entries = text.Split('\n')
            .Select((content, index) => (Body: content.TrimEnd('\r'), Index: index))
            .Where(entry => entry.Body.StartsWith("- ", StringComparison.Ordinal))
            .ToList();
        var codes = ShortCode.For(entries.Select(entry => ShortCode.Normalize(entry.Body[2..])));
        return entries
            .Select((entry, position) => (Line: entry.Index, Code: codes[position]))
            .ToDictionary(item => item.Line, item => item.Code);
    }

    /// <summary>One activity: a <c>- </c> line under a <c>## yyyy-MM-dd</c> heading.</summary>
    /// <param name="Line">The line's index in the file.</param>
    /// <param name="Day">The day of the heading the line sits under.</param>
    /// <param name="Project">The project label, or <c>null</c> when the line has none.</param>
    /// <param name="Body">The activity text without the label.</param>
    /// <param name="Code">The activity's short code, as <see cref="CodeAt"/> gives it.</param>
    public sealed record Activity(int Line, DateOnly Day, string? Project, string Body, string Code);

    /// <summary>
    /// The activities of a month file, in file order. Lines before the first day heading, under other headings, or with
    /// an empty body are not activities.
    /// </summary>
    public static List<Activity> Activities(string text)
    {
        var codes = CodesByLine(text);
        var lines = text.Split('\n');
        var result = new List<Activity>();
        DateOnly? day = null;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].TrimEnd('\r');
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                day = DateOnly.TryParseExact(line[3..], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                    ? parsed
                    : null;
                continue;
            }

            if (day is not { } date || !line.StartsWith("- ", StringComparison.Ordinal))
                continue;
            var (project, body) = SplitLabel(line[2..]);
            if (body.Length > 0)
                result.Add(new Activity(index, date, project, body, codes[index]));
        }

        return result;
    }

    /// <summary>Whether a file name is a month file of the activity log: <c>yyyy-MM.md</c>.</summary>
    public static bool IsMonthFileName(string name) => Regex.IsMatch(name, @"^[0-9]{4}-[0-9]{2}\.md$");

    /// <summary>Splits a leading <c>[project] </c> label off an activity's text, when the label is a project name.</summary>
    private static (string? Project, string Body) SplitLabel(string content)
    {
        var close = content.IndexOf("] ", StringComparison.Ordinal);
        if (content.StartsWith('[') && close > 1 && PageId.IsProjectName(content[1..close]))
            return (content[1..close], content[(close + 2)..].Trim());
        return (null, content.Trim());
    }
}
