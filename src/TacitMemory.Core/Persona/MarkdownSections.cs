namespace TacitMemory.Core.Persona;

/// <summary>
/// Line-level helpers for the Markdown files the persona layer writes: the <c>## </c> headings and where a section ends.
/// Shared by <see cref="PersonaFile"/> and <see cref="CandidatesFile"/>.
/// </summary>
internal static class MarkdownSections
{
    /// <summary>
    /// The index of the last <c>## </c> heading whose text, without its marker and trimmed, <paramref name="isHeading"/>
    /// accepts; -1 when there is none.
    /// </summary>
    public static int LastHeading(IReadOnlyList<string> lines, Func<string, bool> isHeading)
    {
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (lines[i].StartsWith("## ", StringComparison.Ordinal) && isHeading(lines[i][3..].Trim()))
                return i;
        }

        return -1;
    }

    /// <summary>The index just after the last non-blank line of the section whose heading is at <paramref name="heading"/>.</summary>
    public static int EndOfSection(IReadOnlyList<string> lines, int heading)
    {
        var end = heading + 1;
        while (end < lines.Count && !lines[end].StartsWith("## ", StringComparison.Ordinal))
            end++;

        var at = heading + 1;
        for (var i = heading + 1; i < end; i++)
        {
            if (lines[i].Trim().Length > 0)
                at = i + 1;
        }

        return at;
    }
}
