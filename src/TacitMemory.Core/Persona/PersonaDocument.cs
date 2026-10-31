using TacitMemory.Core.Injection;
using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Persona;

/// <summary>The four persona sections, in rendering order.</summary>
internal enum PersonaSection
{
    RedLines,
    WorkingRules,
    Preferences,
    Profile,
}

/// <summary>
/// One persona entry: a top-level <c>- </c> item with its indented continuation lines, or, when
/// <see cref="IsListItem"/> is false, a line that does not follow the format and is kept as written.
/// </summary>
internal sealed record PersonaItem(string Text, IReadOnlyList<string> Continuation, bool IsListItem)
{
    /// <summary>The continuation lines that are not source comments (<c>&lt;!-- … --&gt;</c>).</summary>
    public IEnumerable<string> ContinuationWithoutComments => Continuation.Where(line => !IsComment(line));

    public static bool IsComment(string line) =>
        line.TrimStart().StartsWith("<!--", StringComparison.Ordinal) && line.TrimEnd().EndsWith("-->", StringComparison.Ordinal);
}

/// <summary>
/// A section of <c>persona.md</c>. <see cref="Kind"/> is <c>null</c> for headings that are not one of the four, and
/// <see cref="Heading"/> is <c>null</c> for text before the first heading.
/// </summary>
internal sealed record PersonaPart(PersonaSection? Kind, string? Heading, IReadOnlyList<PersonaItem> Items);

/// <summary>
/// <c>persona.md</c> parsed leniently: sections are found by <c>## </c> headings in any language, items by top-level
/// <c>- </c> lines with their indented continuation lines. Nothing the user wrote is dropped: unknown sections,
/// off-format lines and text before the first heading are kept as they are.
/// </summary>
internal sealed record PersonaDocument(IReadOnlyList<PersonaPart> Parts)
{
    private static readonly (PersonaSection Kind, Localized Word)[] Headings =
    [
        (PersonaSection.RedLines, StructureWords.RedLines),
        (PersonaSection.WorkingRules, StructureWords.WorkingRules),
        (PersonaSection.Preferences, StructureWords.Preferences),
        (PersonaSection.Profile, StructureWords.Profile),
    ];

    /// <summary>Reads <paramref name="path"/>; <c>null</c> when it is not valid UTF-8. A missing file is an empty persona.</summary>
    public static PersonaDocument? Read(string path) => TextFile.Read(path) is var (text, _) ? Parse(text) : null;

    public static PersonaDocument Parse(string text)
    {
        var parts = new List<PersonaPart>();
        string? heading = null;
        List<PersonaItem> items = [];
        PersonaItem? item = null;
        List<string> continuation = [];

        void EndItem()
        {
            if (item is not null)
                items.Add(item with { Continuation = continuation });
            item = null;
        }

        void EndPart()
        {
            EndItem();
            if (heading is not null || items.Count > 0)
                parts.Add(new PersonaPart(heading is null ? null : KindOf(heading), heading, items));
        }

        foreach (var raw in text.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                EndPart();
                heading = line[3..].Trim();
                items = [];
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                EndItem();
                item = new PersonaItem(line[2..].Trim(), [], IsListItem: true);
                continuation = [];
            }
            else if (line.Length > 0 && char.IsWhiteSpace(line[0]) && item is not null)
            {
                continuation.Add(line);
            }
            else if (line.Length > 0 && !PersonaItem.IsComment(line))
            {
                EndItem();
                item = new PersonaItem(line, [], IsListItem: false);
                continuation = [];
            }
            else
            {
                EndItem();
            }
        }

        EndPart();
        return new PersonaDocument(parts);
    }

    private static PersonaSection? KindOf(string heading)
    {
        foreach (var (kind, word) in Headings)
        {
            if (string.Equals(heading, word.Zh, StringComparison.Ordinal) || string.Equals(heading, word.En, StringComparison.OrdinalIgnoreCase))
                return kind;
        }

        return null;
    }
}
