using TacitMemory.Core.Injection;
using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Persona;

/// <summary>
/// Writes entries into <c>persona.md</c> line by line, so every other byte stays as the user left it. Sections are found
/// by their headings; a missing heading is restored in the standard order. Scope blocks follow the global
/// sections: domains first, then workstreams, each by name.
/// </summary>
internal static class PersonaFile
{
    /// <summary>
    /// Appends <paramref name="entry"/> after the last non-blank line of the last global section of <paramref name="kind"/>.
    /// When that heading is missing, it is restored after the nearest earlier standard section, else before the nearest later
    /// one, else before the first scope block, else at the end of the file.
    /// </summary>
    /// <returns>The new text, and the index of the entry's first line in it.</returns>
    public static (string Text, int Line) Append(string text, PersonaSection kind, IReadOnlyList<string> entry)
    {
        var cr = TextFile.CarriageReturnOf(text);
        var lines = Lines(text);

        var heading = KindHeading(lines, kind);
        if (heading >= 0)
        {
            var end = MarkdownSections.EndOfSection(lines, heading);
            lines.InsertRange(end, entry.Select(line => line + cr));
            return (Joined(lines), end);
        }

        var block = FirstBlockHeading(lines);
        var (at, before, after) = MissingHeading(
            lines,
            kind,
            "## " + PersonaDocument.HeadingOf(kind),
            section => KindHeading(lines, section),
            fallback: block >= 0 ? block : lines.Count,
            fallbackAfter: block >= 0 ? [string.Empty] : []);
        lines.InsertRange(at, before.Concat(entry).Concat(after).Select(line => line + cr));
        return (Joined(lines), at + before.Count);
    }

    /// <summary>
    /// Appends <paramref name="entry"/> to the section of <paramref name="kind"/> in the block of <paramref name="scope"/>. A
    /// missing block is created with <paramref name="trigger"/> as its trigger line when that is given; a missing section of an
    /// existing block is restored in the standard order inside the block.
    /// </summary>
    /// <returns>The new text, and the index of the entry's first line in it.</returns>
    public static (string Text, int Line) AppendToBlock(
        string text, PersonaScope scope, string? trigger, PersonaSection kind, IReadOnlyList<string> entry)
    {
        var cr = TextFile.CarriageReturnOf(text);
        var lines = Lines(text);

        var block = BlockHeading(lines, scope);
        if (block < 0)
            return CreateBlock(lines, scope, trigger, kind, entry, cr);

        var end = NextHeading(lines, block + 1);
        var sub = SubHeading(lines, block, end, kind);
        if (sub >= 0)
        {
            var at = MarkdownSections.EndOfSection(lines, sub);
            lines.InsertRange(at, entry.Select(line => line + cr));
            return (Joined(lines), at);
        }

        var (sectionAt, before, after) = MissingHeading(
            lines,
            kind,
            "### " + PersonaDocument.HeadingOf(kind),
            section => SubHeading(lines, block, end, section),
            fallback: MarkdownSections.EndOfSection(lines, block),
            fallbackAfter: []);
        lines.InsertRange(sectionAt, before.Concat(entry).Concat(after).Select(line => line + cr));
        return (Joined(lines), sectionAt + before.Count);
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

    /// <summary>
    /// Creates the block of <paramref name="scope"/>: its heading, its trigger line for a domain, the section heading and the
    /// entry. It goes before the first existing block that sorts after it, else at the end of the file.
    /// </summary>
    private static (string Text, int Line) CreateBlock(
        List<string> lines, PersonaScope scope, string? trigger, PersonaSection kind, IReadOnlyList<string> entry, string cr)
    {
        List<string> block = ["## " + scope.Heading];
        if (trigger is not null)
            block.Add(StructureWords.Field(StructureWords.Trigger, trigger));
        block.Add("### " + PersonaDocument.HeadingOf(kind));
        block.AddRange(entry);

        var next = NextBlockHeading(lines, scope);
        int at;
        List<string> before;
        List<string> after;
        if (next >= 0)
        {
            at = next;
            before = next > 0 && lines[next - 1].Trim().Length > 0 ? [string.Empty] : [];
            after = [string.Empty];
        }
        else
        {
            at = lines.Count;
            before = lines.Count > 0 && lines[^1].Trim().Length > 0 ? [string.Empty] : [];
            after = [];
        }

        lines.InsertRange(at, before.Concat(block).Concat(after).Select(line => line + cr));
        return (Joined(lines), at + before.Count + block.Count - entry.Count);
    }

    /// <summary>The lines of a file without its final line break, which <see cref="Joined"/> restores.</summary>
    private static List<string> Lines(string text)
    {
        var lines = text.Split('\n').ToList();
        if (lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    private static string Joined(List<string> lines) => string.Concat(lines.Select(line => line + "\n"));

    /// <summary>The index of the last global <c>## </c> heading of <paramref name="kind"/>, or -1 when there is none.</summary>
    private static int KindHeading(List<string> lines, PersonaSection kind) =>
        MarkdownSections.LastHeading(lines, title => PersonaDocument.KindOf(title) == kind && PersonaScope.OfHeading(title) is null);

    /// <summary>The index of the first <c>## </c> heading that names a scope block, or -1 when there is none.</summary>
    private static int FirstBlockHeading(List<string> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].StartsWith("## ", StringComparison.Ordinal) && PersonaScope.OfHeading(lines[i][3..].Trim()) is not null)
                return i;
        }

        return -1;
    }

    /// <summary>The index of the first block heading of <paramref name="scope"/>, or -1 when there is none.</summary>
    private static int BlockHeading(List<string> lines, PersonaScope scope)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].StartsWith("## ", StringComparison.Ordinal) && PersonaScope.OfHeading(lines[i][3..].Trim()) == scope)
                return i;
        }

        return -1;
    }

    /// <summary>The index of the first block heading that sorts after <paramref name="scope"/>, or -1 when there is none.</summary>
    private static int NextBlockHeading(List<string> lines, PersonaScope scope)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].StartsWith("## ", StringComparison.Ordinal)
                && PersonaScope.OfHeading(lines[i][3..].Trim()) is { } other
                && PersonaScope.Compare(scope, other) < 0)
                return i;
        }

        return -1;
    }

    /// <summary>The index of the next <c>## </c> heading at or after <paramref name="from"/>, or the line count when there is none.</summary>
    private static int NextHeading(List<string> lines, int from)
    {
        var at = from;
        while (at < lines.Count && !lines[at].StartsWith("## ", StringComparison.Ordinal))
            at++;
        return at;
    }

    /// <summary>The last <c>### </c> heading of <paramref name="kind"/> between the block heading and <paramref name="end"/>; -1 when none.</summary>
    private static int SubHeading(List<string> lines, int block, int end, PersonaSection kind)
    {
        for (var i = end - 1; i > block; i--)
        {
            if (lines[i].StartsWith("### ", StringComparison.Ordinal) && PersonaDocument.KindOf(lines[i][4..].Trim()) == kind)
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Where a missing heading goes: after the nearest earlier standard section, else before the nearest later one, else at
    /// <paramref name="fallback"/>. Returns the index to insert at, the lines before the entry and the lines after it.
    /// </summary>
    private static (int At, List<string> Before, List<string> After) MissingHeading(
        List<string> lines, PersonaSection kind, string heading, Func<PersonaSection, int> headingOf, int fallback, List<string> fallbackAfter)
    {
        for (var earlier = (int)kind - 1; earlier >= 0; earlier--)
        {
            var earlierHeading = headingOf((PersonaSection)earlier);
            if (earlierHeading >= 0)
                return Placed(lines, MarkdownSections.EndOfSection(lines, earlierHeading), heading, []);
        }

        for (var later = (int)kind + 1; later <= (int)PersonaSection.Profile; later++)
        {
            var laterHeading = headingOf((PersonaSection)later);
            if (laterHeading >= 0)
                return Placed(lines, laterHeading, heading, [string.Empty]);
        }

        return Placed(lines, fallback, heading, fallbackAfter);
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
