using TacitMemory.Core.Formats;
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

            foreach (var item in part.Items.Where(item => !item.IsListItem))
                issues.Add(Issue(item, "Unrecognized content", "This line is not a list item, so it is not an entry."));
        }

        CheckBlocks(document, config, issues);
        return issues;
    }

    /// <summary>The problems of the scope blocks: their names, their trigger lines and their sections.</summary>
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
                CheckTrigger(block, headingLocation, issues);

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

                foreach (var item in part.Items.Where(item => !item.IsListItem))
                    issues.Add(Issue(item, "Unrecognized content", "This line is not a list item, so it is not an entry."));
            }
        }
    }

    /// <summary>A domain needs one trigger line.</summary>
    private static void CheckTrigger(PersonaBlock block, string headingLocation, List<FormatIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(block.Trigger))
            issues.Add(new FormatIssue("", headingLocation, "Domain without trigger",
                "The domain has no trigger line under its heading, so the resident index cannot show when it applies. Add one line: trigger: when the domain applies.",
                FormatHandling.FixDirectly));
    }

    private static FormatIssue Issue(PersonaItem item, string kind, string description, FormatHandling handling = FormatHandling.FixDirectly) =>
        new("", $"line {item.Line + 1}", kind, description, handling);

    private static string Section(string name) => $"section \"{name}\"";
}
