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
    public static string CodeAt(string text, int line)
    {
        var entries = text.Split('\n')
            .Select((content, index) => (Body: content.TrimEnd('\r'), Index: index))
            .Where(entry => entry.Body.StartsWith("- ", StringComparison.Ordinal))
            .ToList();
        var codes = ShortCode.For(entries.Select(entry => ShortCode.Normalize(entry.Body[2..])));
        return codes[entries.FindIndex(entry => entry.Index == line)];
    }

    /// <summary>The activities recorded under <paramref name="day"/>'s heading, with their short codes; codes are counted over the whole file, as <see cref="CodeAt"/> does.</summary>
    public static IReadOnlyList<RecentEntry> EntriesOn(string text, DateOnly day)
    {
        var heading = $"## {day:yyyy-MM-dd}";
        var lines = text.Split('\n');
        var entries = new List<(int Line, string Body, bool OnDay)>();
        var onDay = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var content = lines[i].TrimEnd('\r');
            if (content.StartsWith("## ", StringComparison.Ordinal))
                onDay = content == heading;
            else if (content.StartsWith("- ", StringComparison.Ordinal))
                entries.Add((i, content[2..], onDay));
        }

        var codes = ShortCode.For(entries.Select(entry => ShortCode.Normalize(entry.Body)));
        return entries
            .Select((entry, index) => new RecentEntry(entry.Line, codes[index], entry.Body))
            .Where((_, index) => entries[index].OnDay)
            .ToList();
    }

    /// <summary>
    /// Rewrites the entry whose first line is <paramref name="line"/> to <paramref name="entry"/>, in place. A
    /// <paramref name="source"/> replaces the source comment under it; without one the comment stays as it is.
    /// Every other line is kept byte for byte.
    /// </summary>
    /// <returns>The new text.</returns>
    public static string Replace(string text, int line, string entry, string? source)
    {
        var cr = TextFile.NewlineOf(text) == "\r\n" ? "\r" : "";
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

/// <summary>An activity in the log: the line of its entry, its short code, and its text with the project label.</summary>
internal sealed record RecentEntry(int Line, string Code, string Body);
