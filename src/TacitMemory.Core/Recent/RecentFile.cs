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
    /// <summary>The folder, relative to the memory repository, that holds the month files of the activity log.</summary>
    public const string Folder = "recent";

    /// <summary>The month file's path, relative to the memory repository, for the month of <paramref name="day"/>.</summary>
    public static string RelativePath(DateOnly day) => $"{Folder}/{day:yyyy-MM}.md";

    /// <summary>
    /// Adds <paramref name="entry"/> under the day's heading, appending the heading at the end of the file when the day
    /// has none. The entry goes after the day's last non-blank line, so it stays inside the day's block.
    /// </summary>
    /// <returns>The new text, and the index of the entry's first line in it.</returns>
    public static (string Text, int Line) Insert(string text, DateOnly day, IReadOnlyList<string> entry)
    {
        var cr = TextFile.CarriageReturnOf(text);
        var lines = text.Split('\n').ToList();
        if (lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);

        var heading = $"## {day:yyyy-MM-dd}";
        var headingAt = lines.FindIndex(line => line.TrimEnd('\r') == heading);
        int at;
        if (headingAt < 0 && SummaryLines.HeadingIndex(lines) is var summaryAt and >= 0)
        {
            // The new day goes above the summary section, which stays at the end of the file, with a blank line between.
            var before = summaryAt;
            while (before > 0 && lines[before - 1].Trim().Length == 0)
                before--;
            var block = new List<string>();
            if (before > 0)
                block.Add(cr);
            block.Add(heading + cr);
            at = before + block.Count;
            block.AddRange(entry.Select(line => line + cr));
            if (before == summaryAt)
                block.Add(cr);
            lines.InsertRange(before, block);
            return (string.Concat(lines.Select(line => line + "\n")), at);
        }

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

    /// <summary>
    /// The short code of every <c>- </c> line, keyed by the line's index in the file. Summary lines take no code.
    /// </summary>
    private static Dictionary<int, string> CodesByLine(string text)
    {
        var lines = text.Split('\n');
        var inSummary = SummaryLines.InSection(lines);
        var entries = lines
            .Select((content, index) => (Body: content.TrimEnd('\r'), Index: index))
            .Where(entry => !inSummary[entry.Index] && entry.Body.StartsWith("- ", StringComparison.Ordinal))
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
    public sealed record Activity(int Line, DateOnly Day, string? Project, string Body, string Code)
    {
        /// <summary>The activity's text with its project label in front, as the log shows it.</summary>
        public string Labeled => Label(Project) + Body;
    }

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

    /// <summary>The <c>[project] </c> label in front of an activity's text, or nothing when it has no project.</summary>
    public static string Label(string? project) => project is null ? "" : $"[{project}] ";

    /// <summary>
    /// The month files of the activity log under <c>recent/</c>, as the relative and full path of each, in name order.
    /// The mainline file is not one. Empty when the folder is missing.
    /// </summary>
    public static List<(string RelativePath, string FullPath)> MonthFiles(string memoryPath)
    {
        var folder = Path.Combine(memoryPath, Folder);
        if (!Directory.Exists(folder))
            return [];

        return Directory.EnumerateFiles(folder, "*.md", SearchOption.TopDirectoryOnly)
            .Where(file => IsMonthFileName(Path.GetFileName(file)))
            .OrderBy(file => Path.GetFileName(file), StringComparer.Ordinal)
            .Select(file => ($"{Folder}/{Path.GetFileName(file)}", file))
            .ToList();
    }

    /// <summary>Splits a leading <c>[project] </c> label off an activity's text, when the label is a project name.</summary>
    private static (string? Project, string Body) SplitLabel(string content)
    {
        var close = content.IndexOf("] ", StringComparison.Ordinal);
        if (content.StartsWith('[') && close > 1 && PageId.IsProjectName(content[1..close]))
            return (content[1..close], content[(close + 2)..].Trim());
        return (null, content.Trim());
    }

    /// <summary>The activities recorded under <paramref name="day"/>'s heading, in file order.</summary>
    public static List<Activity> ActivitiesOn(string text, DateOnly day) =>
        Activities(text).Where(activity => activity.Day == day).ToList();

    /// <summary>
    /// Rewrites the entry whose first line is <paramref name="line"/> to <paramref name="entry"/>, in place. A
    /// <paramref name="source"/> replaces the source comment under it; without one the comment stays as it is.
    /// Every other line is kept byte for byte.
    /// </summary>
    /// <returns>The new text.</returns>
    public static string Replace(string text, int line, string entry, string? source)
    {
        var cr = TextFile.CarriageReturnOf(text);
        var lines = text.Split('\n').ToList();
        var span = source is not null && IsSourceComment(lines, line + 1) ? 2 : 1;
        IReadOnlyList<string> replacement = source is null ? [entry] : [entry, source];
        lines.RemoveRange(line, span);
        lines.InsertRange(line, replacement.Select(content => content + cr));
        return string.Join('\n', lines);
    }

    private static bool IsSourceComment(IReadOnlyList<string> lines, int index) =>
        index < lines.Count && lines[index].TrimEnd('\r').StartsWith("  <!--", StringComparison.Ordinal);
}
