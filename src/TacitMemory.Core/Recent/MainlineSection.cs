using TacitMemory.Core.Injection;
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

    /// <summary>The items under the mainline heading in the memory repository's recent.md; empty when there is no file.</summary>
    public static IReadOnlyList<string> ReadItems(string memoryPath) =>
        Items(TextFile.Read(Path.Combine(memoryPath, RelativePath))?.Text ?? "");

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
}
