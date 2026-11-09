using TacitMemory.Core.Consolidation;
using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Persona;

/// <summary>
/// The format and rule problems of <c>persona.md</c>, read from its lenient parse. Entries are never dropped by the
/// parse, so every problem here is a line the parser kept as it was written.
/// </summary>
internal static class PersonaFormatCheck
{
    /// <summary>Finds the problems in <paramref name="text"/>; the file must already be valid UTF-8.</summary>
    public static IReadOnlyList<FormatIssue> Check(string text, TacitConfig config)
    {
        var document = PersonaDocument.Parse(text);
        var issues = new List<FormatIssue>();
        var counts = new Dictionary<PersonaSection, int>();

        foreach (var part in document.Parts)
        {
            if (part.Kind is not { } kind)
            {
                if (part.Heading is null)
                {
                    foreach (var item in part.Items)
                        issues.Add(Issue(item, "Unrecognized content", "Text before the first section is not part of any section."));
                }
                else
                {
                    issues.Add(new FormatIssue("", Section(part.Heading), "Unrecognized section",
                        "This heading is not one of the four persona sections, so its entries are not read.", FormatHandling.FixDirectly));
                }

                continue;
            }

            counts[kind] = counts.GetValueOrDefault(kind) + part.Items.Count(item => item.IsListItem);
            foreach (var item in part.Items)
            {
                if (!item.IsListItem)
                    issues.Add(Issue(item, "Unrecognized content", "This line is not a list item, so it is not an entry."));
                else
                    CheckEntry(item, kind, config, issues);
            }
        }

        foreach (var (kind, count) in counts)
        {
            var limit = LimitOf(kind, config);
            if (count > limit)
                issues.Add(new FormatIssue("", Section(StructureWordOf(kind).In(config.Language)), "Section over limit",
                    $"The section has {count} entries; the limit is {limit}. The user decides which to remove.", FormatHandling.AskUser));
        }

        CheckBlocks(document, config, issues);
        return issues;
    }

    /// <summary>The problems of the scope blocks: their names, their trigger lines, their sections and their limits.</summary>
    private static void CheckBlocks(PersonaDocument document, TacitConfig config, List<FormatIssue> issues)
    {
        var seen = new HashSet<PersonaScope>();
        foreach (var block in document.Blocks)
        {
            var section = Section(block.Heading);
            var headingLocation = $"line {block.HeadingLine + 1}";
            if (!PersonaScope.IsValidName(block.Scope.Name))
                issues.Add(new FormatIssue("", section, "Invalid scope name",
                    "The name must use only lowercase letters, digits, '.', '_' and '-', and must not start with '.' or '_'.", FormatHandling.FixDirectly));
            else if (!seen.Add(block.Scope))
                issues.Add(new FormatIssue("", section, "Duplicate scope block",
                    "This scope already has a block above. Merge the two blocks into one.", FormatHandling.FixDirectly));

            if (block.Scope.Kind == PersonaScopeKind.Domain)
                CheckTrigger(block, headingLocation, config, issues);

            foreach (var part in block.Parts)
            {
                if (part.Kind is not { } kind)
                {
                    foreach (var item in part.Items)
                    {
                        if (part.Heading is not null)
                            continue;
                        if (item.IsListItem)
                            issues.Add(Issue(item, "Entry outside a section",
                                "This entry is not under a ### section heading, so it is not read.", FormatHandling.FixDirectly));
                        else
                            issues.Add(Issue(item, "Unrecognized content", "This line is not a list item, so it is not an entry."));
                    }

                    if (part.Heading is not null)
                        issues.Add(new FormatIssue("", Section(part.Heading), "Unrecognized section",
                            "This heading is not one of the four persona sections, so its entries are not read.", FormatHandling.FixDirectly));
                    continue;
                }

                foreach (var item in part.Items)
                {
                    if (!item.IsListItem)
                        issues.Add(Issue(item, "Unrecognized content", "This line is not a list item, so it is not an entry."));
                    else
                        CheckEntry(item, kind, config, issues);
                }
            }

            if (block.Scope.Kind == PersonaScopeKind.Domain && block.Entries.Count > config.Limits.DomainEntries)
                issues.Add(new FormatIssue("", section, "Domain over limit",
                    $"The domain has {block.Entries.Count} entries; the limit is {config.Limits.DomainEntries}. The user decides which to remove.",
                    FormatHandling.AskUser));
        }

        var domains = document.Blocks.Count(block => block.Scope.Kind == PersonaScopeKind.Domain);
        if (domains > config.Limits.Domains)
            issues.Add(new FormatIssue("", "domains", "Too many domains",
                $"The persona has {domains} domains; the limit is {config.Limits.Domains}. The user decides which to merge or remove.",
                FormatHandling.AskUser));
    }

    /// <summary>A domain needs one trigger line, no longer than the trigger limit.</summary>
    private static void CheckTrigger(PersonaBlock block, string headingLocation, TacitConfig config, List<FormatIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(block.Trigger))
        {
            issues.Add(new FormatIssue("", headingLocation, "Domain without trigger",
                "The domain has no trigger line under its heading, so the resident index cannot show when it applies. Add one line: trigger: when the domain applies.",
                FormatHandling.FixDirectly));
            return;
        }

        var length = PersonaTools.Length(block.Trigger);
        if (length > config.Limits.TriggerLength)
            issues.Add(new FormatIssue("", $"line {block.TriggerLine + 1}", "Trigger too long",
                $"The trigger has {length} characters; the limit is {config.Limits.TriggerLength}.", FormatHandling.FixDirectly));
    }

    private static void CheckEntry(PersonaItem item, PersonaSection kind, TacitConfig config, List<FormatIssue> issues)
    {
        if (kind == PersonaSection.RedLines && !PersonaTools.HasStructureWord(item.Text, StructureWords.Instead))
            issues.Add(Issue(item, "Red line without instead",
                "A red line must say what to do instead: add \"改为\" (or \"instead\") followed by the alternative.", FormatHandling.FixDirectly));

        if (kind == PersonaSection.WorkingRules && !item.Text.Contains('→'))
            issues.Add(Issue(item, "Working rule without →",
                "A working rule must have the form situation → action, with → between them.", FormatHandling.FixDirectly));

        var length = PersonaTools.Length(item.Text);
        if (length > config.Limits.ItemLength)
            issues.Add(Issue(item, "Entry too long",
                $"The entry has {length} characters; the limit is {config.Limits.ItemLength}.", FormatHandling.FixDirectly));
    }

    private static FormatIssue Issue(PersonaItem item, string kind, string description, FormatHandling handling = FormatHandling.FixDirectly) =>
        new("", $"line {item.Line + 1}", kind, description, handling);

    private static string Section(string name) => $"section \"{name}\"";

    private static Localized StructureWordOf(PersonaSection kind) => PersonaDocument.HeadingOf(kind);

    private static int LimitOf(PersonaSection kind, TacitConfig config) => kind switch
    {
        PersonaSection.RedLines => config.Limits.Persona.RedLines,
        PersonaSection.WorkingRules => config.Limits.Persona.WorkingRules,
        PersonaSection.Preferences => config.Limits.Persona.Preferences,
        _ => config.Limits.Persona.Profile,
    };
}
