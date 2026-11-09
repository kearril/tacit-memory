using System.Text;

namespace TacitMemory.Core.Persona;

/// <summary>
/// What <c>recall</c> returns for the persona: the whole persona, or the rules of some of its domains. Every entry carries its
/// short code, the one <c>write_persona</c> takes; source comments are left out. Nothing here is searched.
/// </summary>
internal static class PersonaReading
{
    /// <summary>
    /// The whole persona: the four global sections, then each domain by label, then each project by name. Null when the persona
    /// holds no entry at all.
    /// </summary>
    public static string? Whole(PersonaDocument persona, LimitsConfig limits)
    {
        if (persona.KnownEntries().Count == 0)
            return null;

        var codes = persona.CodesByLine();
        var sections = Enum.GetValues<PersonaSection>().Select(kind => GlobalSection(persona, kind, codes, LimitOf(limits.Persona, kind)));
        var blocks = persona.Blocks
            .OrderBy(block => block.Scope, Comparer<PersonaScope>.Create(PersonaScope.Compare))
            .Select(block => Block(block, limits, codes));
        return string.Join("\n\n", sections.Concat(blocks));
    }

    /// <summary>
    /// The domains in <paramref name="domains"/>, in the order given, each with its trigger and entries. Null when they hold no
    /// entry at all.
    /// </summary>
    public static string? Domains(PersonaDocument persona, IReadOnlyList<PersonaBlock> domains, LimitsConfig limits)
    {
        if (domains.All(block => block.Entries.Count == 0))
            return null;

        var codes = persona.CodesByLine();
        return string.Join("\n\n", domains.Select(block => Block(block, limits, codes)));
    }

    /// <summary>One global section: its heading with the entry count and the limit, then its entries.</summary>
    private static string GlobalSection(PersonaDocument persona, PersonaSection kind, IReadOnlyDictionary<int, string> codes, int limit)
    {
        var entries = persona.Entries(kind);
        return Lines($"{PersonaDocument.HeadingOf(kind).En}, {entries.Count} of {limit} entries:", entries, codes);
    }

    /// <summary>
    /// One scope block. A domain's heading carries its count, its limit and its trigger; a project's carries its count only. Each
    /// of the four sections that has entries follows under its own name, in the order of the sections.
    /// </summary>
    private static string Block(PersonaBlock block, LimitsConfig limits, IReadOnlyDictionary<int, string> codes)
    {
        var count = block.Entries.Count;
        var heading = block.Scope.Kind == PersonaScopeKind.Domain
            ? $"Domain {block.Scope.Name}, {count} of {limits.DomainEntries} entries. Trigger: {TriggerText(block.Trigger)}"
            : $"Project {block.Scope.Name}, {count} {(count == 1 ? "entry" : "entries")}:";

        var text = new StringBuilder(heading);
        foreach (var kind in Enum.GetValues<PersonaSection>())
        {
            var entries = block.EntriesOf(kind);
            if (entries.Count > 0)
                text.Append('\n').Append(Lines($"{PersonaDocument.HeadingOf(kind).En}:", entries, codes));
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

    private static int LimitOf(PersonaLimits limits, PersonaSection kind) => kind switch
    {
        PersonaSection.RedLines => limits.RedLines,
        PersonaSection.WorkingRules => limits.WorkingRules,
        PersonaSection.Preferences => limits.Preferences,
        _ => limits.Profile,
    };
}
