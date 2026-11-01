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
}
