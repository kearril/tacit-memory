using TacitMemory.Core.Localization;
using TacitMemory.Core.Pages;

namespace TacitMemory.Core.Recent;

/// <summary>
/// The mainline: the top-level <c>- </c> items under the <c>## 近期主线</c> (or <c>## Mainline</c>) heading of
/// <c>recent.md</c>. Only that list is read or rewritten; every other line stays as it is.
/// </summary>
internal static class MainlineSection
{
    /// <summary>The file, relative to the memory repository, that holds the mainline.</summary>
    public const string RelativePath = "recent.md";

    /// <summary>The version code of a mainline: the short code of its normalized items, one per line.</summary>
    public static string VersionOf(IReadOnlyList<string> items) => ShortCode.Of(ShortCode.Normalize(string.Join("\n", items)));

    /// <summary>The items under the mainline heading, in order; empty when the heading is missing.</summary>
    public static IReadOnlyList<string> Items(string text)
    {
        var lines = Lines(text);
        var (heading, end) = Section(lines);
        if (heading < 0)
            return [];

        var items = new List<string>();
        for (var i = heading + 1; i < end; i++)
        {
            if (IsItem(lines[i]))
                items.Add(lines[i][2..].Trim());
        }

        return items;
    }

    /// <summary>
    /// The text with the items under the heading replaced by <paramref name="items"/>. The new items take the place of
    /// the first old item; indented lines under an old item go with it. When there is no heading, one is appended in
    /// <paramref name="heading"/>.
    /// </summary>
    public static string Replace(string text, IReadOnlyList<string> items, string heading, string newline)
    {
        var lines = text.Split('\n').ToList();
        var endsWithNewline = lines[^1].Length == 0;
        if (endsWithNewline)
            lines.RemoveAt(lines.Count - 1);

        var cr = newline == "\r\n" ? "\r" : "";
        var newLines = items.Select(item => "- " + item + cr).ToList();
        var (headingAt, end) = Section(lines.Select(line => line.TrimEnd('\r')).ToList());
        if (headingAt < 0)
        {
            lines.Add($"## {heading}{cr}");
            lines.AddRange(newLines);
            return Join(lines, endsWithNewline);
        }

        var hasItems = Items(text).Count > 0;
        var output = new List<string>();
        var placed = false;
        for (var i = 0; i < lines.Count; i++)
        {
            if (i > headingAt && i < end && IsItem(lines[i].TrimEnd('\r')))
            {
                if (!placed)
                {
                    output.AddRange(newLines);
                    placed = true;
                }

                while (i + 1 < end && IsContinuation(lines[i + 1].TrimEnd('\r')))
                    i++;
                continue;
            }

            output.Add(lines[i]);
            if (i == headingAt && !hasItems)
            {
                output.AddRange(newLines);
                placed = true;
            }
        }

        return Join(output, endsWithNewline);
    }

    /// <summary>The heading line in <paramref name="language"/>, without the <c>## </c> prefix.</summary>
    public static string HeadingIn(MemoryLanguage language) => StructureWords.Mainline.In(language);

    private static List<string> Lines(string text) => text.Split('\n').Select(line => line.TrimEnd('\r')).ToList();

    /// <summary>The index of the mainline heading (or -1) and the index where its section ends.</summary>
    private static (int Heading, int End) Section(List<string> lines)
    {
        var headings = new[] { $"## {StructureWords.Mainline.Zh}", $"## {StructureWords.Mainline.En}" };
        var heading = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            if (headings.Contains(lines[i].Trim()))
            {
                heading = i;
                break;
            }
        }

        if (heading < 0)
            return (-1, lines.Count);

        var end = heading + 1;
        while (end < lines.Count && !lines[end].StartsWith("## ", StringComparison.Ordinal))
            end++;
        return (heading, end);
    }

    private static bool IsItem(string line) => line.StartsWith("- ", StringComparison.Ordinal);

    private static bool IsContinuation(string line) =>
        line.Length > 0 && (line[0] == ' ' || line[0] == '\t') && line.Trim().Length > 0;

    private static string Join(List<string> lines, bool endsWithNewline) =>
        string.Join("\n", lines) + (endsWithNewline ? "\n" : "");
}
