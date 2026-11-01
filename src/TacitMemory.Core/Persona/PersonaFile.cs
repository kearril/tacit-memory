using TacitMemory.Core.Injection;

namespace TacitMemory.Core.Persona;

/// <summary>
/// Writes entries into <c>persona.md</c> line by line, so every other byte stays as the user left it. Sections are found
/// by their headings in any language; a missing heading is restored in the standard order.
/// </summary>
internal static class PersonaFile
{
    /// <summary>
    /// Appends <paramref name="entry"/> after the last non-blank line of the last section of <paramref name="kind"/>. When
    /// that heading is missing, it is restored after the nearest earlier standard section, else before the nearest later
    /// one, else at the end of the file.
    /// </summary>
    /// <returns>The new text, and the index of the entry's first line in it.</returns>
    public static (string Text, int Line) Append(string text, PersonaSection kind, IReadOnlyList<string> entry, MemoryLanguage language)
    {
        var cr = TextFile.CarriageReturnOf(text);
        var lines = text.Split('\n').ToList();
        if (lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);

        var heading = KindHeading(lines, kind);
        if (heading >= 0)
        {
            var end = MarkdownSections.EndOfSection(lines, heading);
            lines.InsertRange(end, entry.Select(line => line + cr));
            return (string.Concat(lines.Select(line => line + "\n")), end);
        }

        var (at, before, after) = MissingHeading(lines, kind, "## " + PersonaDocument.HeadingOf(kind).In(language));
        lines.InsertRange(at, before.Concat(entry).Concat(after).Select(line => line + cr));
        return (string.Concat(lines.Select(line => line + "\n")), at + before.Count);
    }

    /// <summary>
    /// Replaces the entry's lines, from its first line through its last continuation line, with <paramref name="entry"/>;
    /// an empty entry removes it. Every other byte of the file is kept as it was.
    /// </summary>
    public static string ReplaceEntry(string text, PersonaItem item, IReadOnlyList<string> entry)
    {
        var cr = TextFile.CarriageReturnOf(text);
        var lines = text.Split('\n').ToList();
        lines.RemoveRange(item.Line, 1 + item.Continuation.Count);
        lines.InsertRange(item.Line, entry.Select(line => line + cr));
        return string.Join('\n', lines);
    }

    /// <summary>The index of the last heading of <paramref name="kind"/>, or -1 when there is none.</summary>
    private static int KindHeading(List<string> lines, PersonaSection kind) =>
        MarkdownSections.LastHeading(lines, title => PersonaDocument.KindOf(title) == kind);

    /// <summary>
    /// Where a missing heading goes: after the nearest earlier standard section, else before the nearest later one, else at
    /// the end of the file. Returns the index to insert at, the lines before the entry and the lines after it.
    /// </summary>
    private static (int At, List<string> Before, List<string> After) MissingHeading(List<string> lines, PersonaSection kind, string heading)
    {
        for (var earlier = (int)kind - 1; earlier >= 0; earlier--)
        {
            var earlierHeading = KindHeading(lines, (PersonaSection)earlier);
            if (earlierHeading >= 0)
                return Placed(lines, MarkdownSections.EndOfSection(lines, earlierHeading), heading, []);
        }

        for (var later = (int)kind + 1; later <= (int)PersonaSection.Profile; later++)
        {
            var laterHeading = KindHeading(lines, (PersonaSection)later);
            if (laterHeading >= 0)
                return Placed(lines, laterHeading, heading, [string.Empty]);
        }

        return Placed(lines, lines.Count, heading, []);
    }

    /// <summary>The heading placed at <paramref name="at"/>, with a blank line before it when the line above is not blank.</summary>
    private static (int At, List<string> Before, List<string> After) Placed(List<string> lines, int at, string heading, List<string> after)
    {
        var before = new List<string>();
        if (at > 0 && lines[at - 1].Trim().Length > 0)
            before.Add(string.Empty);
        before.Add(heading);
        return (at, before, after);
    }
}
