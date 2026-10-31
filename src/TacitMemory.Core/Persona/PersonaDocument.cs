using System.Text;
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

/// <summary>One persona item: the top-level list line and its indented continuation lines.</summary>
internal sealed record PersonaItem(string Text, IReadOnlyList<string> Continuation)
{
    /// <summary>The continuation lines that are not source comments (<c>&lt;!-- … --&gt;</c>).</summary>
    public IEnumerable<string> ContinuationWithoutComments =>
        Continuation.Where(line => !(line.TrimStart().StartsWith("<!--", StringComparison.Ordinal) && line.EndsWith("-->", StringComparison.Ordinal)));
}

/// <summary>A section of <c>persona.md</c>; <see cref="Kind"/> is <c>null</c> for headings that are not one of the four.</summary>
internal sealed record PersonaPart(PersonaSection? Kind, string Heading, IReadOnlyList<PersonaItem> Items);

/// <summary>
/// <c>persona.md</c> parsed leniently: sections are found by <c>## </c> headings in any language, items by top-level
/// <c>- </c> lines with their indented continuation lines. Off-format items and unknown sections are kept as they are.
/// </summary>
internal sealed record PersonaDocument(IReadOnlyList<PersonaPart> Parts)
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly (PersonaSection Kind, Localized Word)[] Headings =
    [
        (PersonaSection.RedLines, StructureWords.RedLines),
        (PersonaSection.WorkingRules, StructureWords.WorkingRules),
        (PersonaSection.Preferences, StructureWords.Preferences),
        (PersonaSection.Profile, StructureWords.Profile),
    ];

    /// <summary>Reads <paramref name="path"/>; <c>null</c> when it is not valid UTF-8. A missing file is an empty persona.</summary>
    public static PersonaDocument? Read(string path)
    {
        if (!File.Exists(path))
            return new PersonaDocument([]);
        try
        {
            return Parse(StrictUtf8.GetString(File.ReadAllBytes(path)));
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    public static PersonaDocument Parse(string text)
    {
        var parts = new List<PersonaPart>();
        string? heading = null;
        List<PersonaItem> items = [];
        string? itemText = null;
        List<string> continuation = [];

        void EndItem()
        {
            if (itemText is not null)
                items.Add(new PersonaItem(itemText, continuation));
            itemText = null;
        }

        void EndPart()
        {
            EndItem();
            if (heading is not null)
                parts.Add(new PersonaPart(KindOf(heading), heading, items));
            heading = null;
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
            else if (line.StartsWith("- ", StringComparison.Ordinal) && heading is not null)
            {
                EndItem();
                itemText = line[2..].Trim();
                continuation = [];
            }
            else if (line.Length > 0 && char.IsWhiteSpace(line[0]) && itemText is not null)
            {
                continuation.Add(line);
            }
            else
            {
                // A blank line or stray text ends the item; stray text itself is not part of the persona.
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
