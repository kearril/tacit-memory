using System.Text;

namespace TacitMemory.Core.Persona;

/// <summary>
/// What <c>recall</c> returns for the persona: the whole persona, or the rules of some of its domains. Every entry carries its
/// short code, the one <c>write_persona</c> takes; source comments are left out. Nothing here is searched.
/// </summary>
internal static class PersonaReading
{
    /// <summary>
    /// The whole persona: the four global sections, then each domain by label, then each workstream by name. Null when the persona
    /// holds no entry at all.
    /// </summary>
    public static string? Whole(PersonaDocument persona)
    {
        if (persona.KnownEntries().Count == 0)
            return null;

        var codes = persona.CodesByLine();
        var sections = Enum.GetValues<PersonaSection>().Select(kind => GlobalSection(persona, kind, codes));
        var blocks = persona.Blocks
            .OrderBy(block => block.Scope, Comparer<PersonaScope>.Create(PersonaScope.Compare))
            .Select(block => Block(block, codes));
        return string.Join("\n\n", sections.Concat(blocks));
    }

    /// <summary>
    /// The domains in <paramref name="domains"/>, in the order given, each with its trigger and entries. Null when they hold no
    /// entry at all.
    /// </summary>
    public static string? Domains(PersonaDocument persona, IReadOnlyList<PersonaBlock> domains)
    {
        if (domains.All(block => block.Entries.Count == 0))
            return null;

        var codes = persona.CodesByLine();
        return string.Join("\n\n", domains.Select(block => Block(block, codes)));
    }

    /// <summary>One global section: its heading with the entry count, then its entries.</summary>
    private static string GlobalSection(PersonaDocument persona, PersonaSection kind, IReadOnlyDictionary<int, string> codes)
    {
        var entries = persona.Entries(kind);
        return Lines($"{PersonaDocument.HeadingOf(kind)}, {Count(entries.Count)}:", entries, codes);
    }

    /// <summary>
    /// One scope block. A domain's heading carries its count and its trigger; a workstream's carries its count only. Each
    /// of the four sections that has entries follows under its own name, in the order of the sections.
    /// </summary>
    private static string Block(PersonaBlock block, IReadOnlyDictionary<int, string> codes)
    {
        var count = block.Entries.Count;
        var heading = block.Scope.Kind == PersonaScopeKind.Domain
            ? $"Domain {block.Scope.Name}, {Count(count)}. Trigger: {TriggerText(block.Trigger)}"
            : $"Workstream {block.Scope.Name}, {Count(count)}:";

        var text = new StringBuilder(heading);
        foreach (var kind in Enum.GetValues<PersonaSection>())
        {
            var entries = block.EntriesOf(kind);
            if (entries.Count > 0)
                text.Append('\n').Append(Lines($"{PersonaDocument.HeadingOf(kind)}:", entries, codes));
        }

        return text.ToString();
    }

    /// <summary>A heading line, then one line per entry: its short code, its text and its continuation lines.</summary>
    private static string Lines(string heading, IEnumerable<PersonaItem> entries, IReadOnlyDictionary<int, string> codes)
    {
        var lines = new List<string> { heading };
        foreach (var entry in entries)
        {
            lines.Add($"- [{codes[entry.Line]}] {entry.Text}");
            lines.AddRange(entry.ContinuationWithoutComments);
        }

        return string.Join("\n", lines);
    }

    private static string TriggerText(string? trigger) => string.IsNullOrWhiteSpace(trigger) ? "none" : trigger;

    private static string Count(int count) => count == 1 ? "1 entry" : $"{count} entries";
}
