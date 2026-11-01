using TacitMemory.Core.Injection;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Pages;

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
internal sealed record PersonaItem(string Text, IReadOnlyList<string> Continuation, bool IsListItem, int Line)
{
    /// <summary>The continuation lines that are not source comments (<c>&lt;!-- … --&gt;</c>).</summary>
    public IEnumerable<string> ContinuationWithoutComments => Continuation.Where(line => !IsComment(line));

    /// <summary>The text the judgment index holds for this entry: its first line and its non-comment continuation lines.</summary>
    public string JudgmentText => Text + string.Concat(ContinuationWithoutComments.Select(line => "\n" + line));

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

        foreach (var (raw, number) in text.Split('\n').Select((content, index) => (content, index)))
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
                item = new PersonaItem(line[2..].Trim(), [], IsListItem: true, Line: number);
                continuation = [];
            }
            else if (line.Length > 0 && char.IsWhiteSpace(line[0]) && item is not null)
            {
                continuation.Add(line);
            }
            else if (line.Length > 0 && !PersonaItem.IsComment(line))
            {
                EndItem();
                item = new PersonaItem(line, [], IsListItem: false, Line: number);
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

    internal static PersonaSection? KindOf(string heading)
    {
        foreach (var (kind, word) in Headings)
        {
            if (string.Equals(heading, word.Zh, StringComparison.Ordinal) || string.Equals(heading, word.En, StringComparison.OrdinalIgnoreCase))
                return kind;
        }

        return null;
    }

    /// <summary>The structure word that heads a section of <paramref name="kind"/>.</summary>
    internal static Localized HeadingOf(PersonaSection kind) => Headings.Single(entry => entry.Kind == kind).Word;

    /// <summary>The list items of every section of <paramref name="kind"/>, in file order.</summary>
    public IReadOnlyList<PersonaItem> Entries(PersonaSection kind) =>
        Parts.Where(part => part.Kind == kind).SelectMany(part => part.Items).Where(item => item.IsListItem).ToList();

    /// <summary>
    /// The short code of every list item, keyed by the item's line. A code is the hash of the item's first line and its
    /// non-comment continuation lines; identical items get <c>-2</c>, <c>-3</c>… in file order.
    /// </summary>
    public Dictionary<int, string> CodesByLine()
    {
        var items = Parts.SelectMany(part => part.Items).Where(item => item.IsListItem).ToList();
        var codes = ShortCode.For(items.Select(TextOf));
        return items.Select((item, index) => (Line: item.Line, Code: codes[index])).ToDictionary(entry => entry.Line, entry => entry.Code);
    }

    /// <summary>An item's text as its short code sees it: the first line, then the continuation lines that are not comments.</summary>
    private static string TextOf(PersonaItem item) =>
        ShortCode.Normalize(string.Join("\n", new[] { item.Text }.Concat(item.ContinuationWithoutComments.Select(line => line.Trim()))));
}
