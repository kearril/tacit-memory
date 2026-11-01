using TacitMemory.Core.Injection;
using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Persona;

/// <summary>
/// Writes candidates into <c>candidates.md</c> line by line, so every other byte stays as the user left it. Sections are
/// found by the headings <see cref="CandidatesDocument"/> reads, in any language. A missing candidates heading is restored
/// before the rejection list, or at the end of the file when there is none.
/// </summary>
internal static class CandidatesFile
{
    /// <summary>
    /// Appends a candidate at the end of the candidates section: its text as a <c>###</c> heading, the section it belongs to,
    /// and its first occurrence. Returns the new text.
    /// </summary>
    public static string AppendCandidate(
        string existing, string title, PersonaSection section, DateOnly day, string source, MemoryLanguage language)
    {
        var cr = TextFile.CarriageReturnOf(existing);
        var lines = existing.Split('\n').ToList();
        if (lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);

        var block = new List<string>
        {
            $"### {title}",
            $"{StructureWords.BelongsTo.In(language)}{PersonaDocument.HeadingOf(section).In(language)}",
            Occurrence(day, source, language),
        };

        var candidates = MarkdownSections.LastHeading(lines, CandidatesDocument.IsCandidatesHeading);
        if (candidates >= 0)
        {
            var at = MarkdownSections.EndOfSection(lines, candidates);
            lines.InsertRange(at, new[] { string.Empty }.Concat(block).Select(line => line + cr));
        }
        else
        {
            var rejection = MarkdownSections.LastHeading(lines, CandidatesDocument.IsRejectionListHeading);
            var at = rejection >= 0 ? rejection : lines.Count;
            var restored = new List<string>();
            if (at > 0 && lines[at - 1].Trim().Length > 0)
                restored.Add(string.Empty);
            restored.Add("## " + StructureWords.Candidates.In(language));
            restored.Add(string.Empty);
            restored.AddRange(block);
            if (rejection >= 0)
                restored.Add(string.Empty);
            lines.InsertRange(at, restored.Select(line => line + cr));
        }

        return string.Concat(lines.Select(line => line + "\n"));
    }

    /// <summary>
    /// Appends an occurrence record to the candidate titled <paramref name="title"/> in the candidates section, after its
    /// last occurrence. Returns the new text, or <c>null</c> when no such candidate of <paramref name="section"/> exists.
    /// </summary>
    public static string? AppendOccurrence(
        string existing, string title, PersonaSection section, DateOnly day, string source, MemoryLanguage language)
    {
        var cr = TextFile.CarriageReturnOf(existing);
        var lines = existing.Split('\n').ToList();
        if (lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);

        // Mirrors CandidatesDocument.Parse: the first belongs-to line of a block names its section.
        var inCandidates = false;
        for (var start = 0; start < lines.Count; start++)
        {
            var line = lines[start].TrimEnd();
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                inCandidates = CandidatesDocument.IsCandidatesHeading(line[3..].Trim());
                continue;
            }

            if (!inCandidates || !line.StartsWith("### ", StringComparison.Ordinal) || line[4..].Trim() != title)
                continue;

            var end = start + 1;
            while (end < lines.Count && !lines[end].TrimEnd().StartsWith("## ", StringComparison.Ordinal) && !lines[end].TrimEnd().StartsWith("### ", StringComparison.Ordinal))
                end++;

            PersonaSection? belongsTo = null;
            var belongsAt = -1;
            var lastOccurrence = -1;
            for (var i = start + 1; i < end; i++)
            {
                var body = lines[i].TrimEnd();
                if (belongsAt < 0 && CandidatesDocument.BelongsToOf(body) is { } named)
                {
                    belongsTo = named;
                    belongsAt = i;
                }

                if (CandidatesDocument.OccurrenceOf(body) is not null)
                    lastOccurrence = i;
            }

            if (belongsTo != section)
                continue;

            var at = (lastOccurrence >= 0 ? lastOccurrence : belongsAt) + 1;
            lines.Insert(at, Occurrence(day, source, language) + cr);
            return string.Concat(lines.Select(line => line + "\n"));
        }

        return null;
    }

    /// <summary>An occurrence record: the day and the user's own words in the memory language.</summary>
    private static string Occurrence(DateOnly day, string source, MemoryLanguage language) =>
        $"- {day:yyyy-MM-dd} {StructureWords.Quote.In(language)}「{source}」";
}
