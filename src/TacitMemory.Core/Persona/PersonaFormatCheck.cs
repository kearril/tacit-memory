using TacitMemory.Core.Checkup;
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

        return issues;
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
