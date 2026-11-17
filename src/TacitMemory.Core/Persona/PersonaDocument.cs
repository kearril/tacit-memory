using TacitMemory.Core.Injection;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Pages;

namespace TacitMemory.Core.Persona;

/// <summary>The four persona sections, in rendering order.</summary>
public enum PersonaSection
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
/// <see cref="Heading"/> is <c>null</c> for text before the first heading. Inside a scope block the headings are <c>### </c>.
/// </summary>
internal sealed record PersonaPart(PersonaSection? Kind, string? Heading, IReadOnlyList<PersonaItem> Items);

/// <summary>
/// A scope block of <c>persona.md</c>: a domain or a workstream, its <c>## </c> heading and, for a domain, its trigger line.
/// <see cref="Parts"/> are its <c>### </c> sections, then any items before the first of them (with a <c>null</c> heading).
/// <see cref="Trigger"/> is <c>null</c> when there is no trigger line, and empty when the line is blank.
/// </summary>
internal sealed record PersonaBlock(
    PersonaScope Scope,
    string Heading,
    int HeadingLine,
    string? Trigger,
    int? TriggerLine,
    IReadOnlyList<PersonaPart> Parts)
{
    /// <summary>The entries of every section of the block that is one of the four, in file order.</summary>
    public IReadOnlyList<PersonaItem> Entries => Parts.Where(part => part.Kind is not null).SelectMany(part => part.Items).Where(item => item.IsListItem).ToList();

    /// <summary>The entries of the block's section of <paramref name="kind"/>, in file order.</summary>
    public IReadOnlyList<PersonaItem> EntriesOf(PersonaSection kind) =>
        Parts.Where(part => part.Kind == kind).SelectMany(part => part.Items).Where(item => item.IsListItem).ToList();
}

/// <summary>A list entry with the scope and the section it belongs to.</summary>
internal sealed record ScopedEntry(PersonaScope Scope, PersonaSection Kind, PersonaItem Item);

/// <summary>
/// <c>persona.md</c> parsed leniently: global sections are found by their <c>## </c> headings, scope blocks by
/// <c>## Domain: </c> and <c>## Workstream: </c>, and items by top-level <c>- </c> lines with their indented
/// continuation lines. Nothing the user wrote is dropped: unknown sections, off-format lines and text before the first
/// heading are kept as they are.
/// </summary>
internal sealed record PersonaDocument(IReadOnlyList<PersonaPart> Parts, IReadOnlyList<PersonaBlock> Blocks)
{
    private static readonly (PersonaSection Kind, string Word)[] Headings =
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
        var lines = text.Split('\n');
        var chunks = TopLevelChunks(lines);
        var parts = new List<PersonaPart>();
        var blocks = new List<PersonaBlock>();

        foreach (var chunk in chunks)
        {
            if (chunk.Heading is not null && PersonaScope.OfHeading(chunk.Heading) is { } scope)
            {
                blocks.Add(ParseBlock(lines, chunk, scope));
                continue;
            }

            var items = ItemsOf(lines, chunk.From, chunk.To, skip: null);
            if (chunk.Heading is not null || items.Count > 0)
                parts.Add(new PersonaPart(chunk.Heading is null ? null : KindOf(chunk.Heading), chunk.Heading, items));
        }

        return new PersonaDocument(parts, blocks);
    }

    internal static PersonaSection? KindOf(string heading)
    {
        foreach (var (kind, word) in Headings)
        {
            if (StructureWords.Is(heading, word))
                return kind;
        }

        return null;
    }

    /// <summary>The structure word that heads a section of <paramref name="kind"/>.</summary>
    internal static string HeadingOf(PersonaSection kind) => Headings.Single(entry => entry.Kind == kind).Word;

    /// <summary>The list items of every global section of <paramref name="kind"/>, in file order.</summary>
    public IReadOnlyList<PersonaItem> Entries(PersonaSection kind) =>
        Parts.Where(part => part.Kind == kind).SelectMany(part => part.Items).Where(item => item.IsListItem).ToList();

    /// <summary>The first block of <paramref name="scope"/>; null when the file has none.</summary>
    public PersonaBlock? BlockOf(PersonaScope scope) => Blocks.FirstOrDefault(block => block.Scope == scope);

    /// <summary>
    /// The list items of the section of <paramref name="kind"/> in <paramref name="scope"/>: the global section for the global
    /// scope, the block's section otherwise.
    /// </summary>
    public IReadOnlyList<PersonaItem> EntriesIn(PersonaScope scope, PersonaSection kind) =>
        scope.IsGlobal ? Entries(kind) : BlockOf(scope)?.EntriesOf(kind) ?? [];

    /// <summary>Every list entry under a known section, in file order, with its scope.</summary>
    public IReadOnlyList<ScopedEntry> KnownEntries()
    {
        var global = Parts.Where(part => part.Kind is not null)
            .SelectMany(part => part.Items.Where(item => item.IsListItem).Select(item => new ScopedEntry(PersonaScope.Global, part.Kind!.Value, item)));
        var scoped = Blocks.SelectMany(block => block.Parts.Where(part => part.Kind is not null)
            .SelectMany(part => part.Items.Where(item => item.IsListItem).Select(item => new ScopedEntry(block.Scope, part.Kind!.Value, item))));
        return global.Concat(scoped).OrderBy(entry => entry.Item.Line).ToList();
    }

    /// <summary>
    /// The short code of every list item, keyed by the item's line. A code is the hash of the item's first line and its
    /// non-comment continuation lines; identical items get <c>-2</c>, <c>-3</c>… in file order. Scope blocks count too.
    /// </summary>
    public Dictionary<int, string> CodesByLine()
    {
        var items = Parts.SelectMany(part => part.Items)
            .Concat(Blocks.SelectMany(block => block.Parts.SelectMany(part => part.Items)))
            .Where(item => item.IsListItem)
            .OrderBy(item => item.Line)
            .ToList();
        var codes = ShortCode.For(items.Select(TextOf));
        return items.Select((item, index) => (Line: item.Line, Code: codes[index])).ToDictionary(entry => entry.Line, entry => entry.Code);
    }

    /// <summary>An item's text as its short code sees it: the first line, then the continuation lines that are not comments.</summary>
    private static string TextOf(PersonaItem item) =>
        ShortCode.Normalize(string.Join("\n", new[] { item.Text }.Concat(item.ContinuationWithoutComments.Select(line => line.Trim()))));

    /// <summary>A stretch of lines under one <c>## </c> heading (or before the first one, with a null heading).</summary>
    private sealed record Chunk(string? Heading, int HeadingLine, int From, int To);

    /// <summary>Splits the lines at every <c>## </c> heading.</summary>
    private static List<Chunk> TopLevelChunks(string[] lines)
    {
        var chunks = new List<Chunk>();
        string? heading = null;
        var headingLine = -1;
        var from = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
            if (!line.StartsWith("## ", StringComparison.Ordinal))
                continue;
            chunks.Add(new Chunk(heading, headingLine, from, i));
            heading = line[3..].Trim();
            headingLine = i;
            from = i + 1;
        }

        chunks.Add(new Chunk(heading, headingLine, from, lines.Length));
        return chunks;
    }

    /// <summary>
    /// Parses a scope block: its trigger line (the first non-blank line before its first <c>### </c> heading, when it reads
    /// as a trigger), then its <c>### </c> sections and the items before them.
    /// </summary>
    private static PersonaBlock ParseBlock(string[] lines, Chunk chunk, PersonaScope scope)
    {
        var subs = SubChunks(lines, chunk);
        var preamble = subs[0];
        string? trigger = null;
        int? triggerLine = null;
        for (var i = preamble.From; i < preamble.To; i++)
        {
            var line = lines[i].TrimEnd();
            if (line.Length == 0)
                continue;
            if (line[0] is not (' ' or '\t') && StructureWords.After(line, StructureWords.Trigger) is { } rest)
            {
                trigger = rest;
                triggerLine = i;
            }

            break;
        }

        var parts = new List<PersonaPart>();
        var items = ItemsOf(lines, preamble.From, preamble.To, triggerLine);
        if (items.Count > 0)
            parts.Add(new PersonaPart(null, null, items));
        foreach (var sub in subs.Skip(1))
            parts.Add(new PersonaPart(KindOf(sub.Heading!), sub.Heading, ItemsOf(lines, sub.From, sub.To, skip: null)));

        return new PersonaBlock(scope, lines[chunk.HeadingLine][3..].Trim(), chunk.HeadingLine, trigger, triggerLine, parts);
    }

    /// <summary>The block's preamble first (heading null), then one chunk per <c>### </c> heading, in file order.</summary>
    private static List<Chunk> SubChunks(string[] lines, Chunk block)
    {
        var chunks = new List<Chunk>();
        string? heading = null;
        var headingLine = -1;
        var from = block.From;
        for (var i = block.From; i < block.To; i++)
        {
            var line = lines[i].TrimEnd();
            if (!line.StartsWith("### ", StringComparison.Ordinal))
                continue;
            chunks.Add(new Chunk(heading, headingLine, from, i));
            heading = line[4..].Trim();
            headingLine = i;
            from = i + 1;
        }

        chunks.Add(new Chunk(heading, headingLine, from, block.To));
        return chunks;
    }

    /// <summary>
    /// The items of the lines in [<paramref name="from"/>, <paramref name="to"/>), each with its line index. The line at
    /// <paramref name="skip"/> is left out; a line that does not follow the format is kept as a non-list item.
    /// </summary>
    private static List<PersonaItem> ItemsOf(string[] lines, int from, int to, int? skip)
    {
        var items = new List<PersonaItem>();
        PersonaItem? item = null;
        var continuation = new List<string>();

        void EndItem()
        {
            if (item is not null)
                items.Add(item with { Continuation = continuation });
            item = null;
        }

        for (var i = from; i < to; i++)
        {
            var line = lines[i].TrimEnd();
            if (i == skip)
            {
                EndItem();
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                EndItem();
                item = new PersonaItem(line[2..].Trim(), [], IsListItem: true, Line: i);
                continuation = [];
            }
            else if (line.Length > 0 && char.IsWhiteSpace(line[0]) && item is not null)
            {
                continuation.Add(line);
            }
            else if (line.Length > 0 && !PersonaItem.IsComment(line))
            {
                EndItem();
                item = new PersonaItem(line, [], IsListItem: false, Line: i);
                continuation = [];
            }
            else
            {
                EndItem();
            }
        }

        EndItem();
        return items;
    }
}
