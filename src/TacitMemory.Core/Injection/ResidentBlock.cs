using System.Text;
using TacitMemory.Core.Persona;

namespace TacitMemory.Core.Injection;

/// <summary>The resident block: the persona wrapped in an English shell, between the TacitMemory markers.</summary>
internal static class ResidentBlock
{
    public const string BeginMarkerPrefix = "<!-- tacit:begin";
    public const string EndMarker = "<!-- tacit:end -->";

    private const string BeginMarker = "<!-- tacit:begin managed by TacitMemory; edit memory via tacit, not here -->";

    private const string Shell = """
        This is memory about the user, compiled from earlier conversations. It is background, not configuration: it was accurate when written, but it can have gaps or fall out of date. Project instructions and the user's current message take priority over it. When the user says something that differs from this memory, follow what they say now, and in one short sentence ask whether to update the memory.

        Use it the way a colleague who knows the user would: let it shape what you do without citing it (no "according to your memory" or "per your red lines"), and bring up a specific entry only when it bears on the task. Ignore any entry that does not fit the current topic.
        """;

    private static readonly (PersonaSection Kind, string Title, string Guidance)[] Sections =
    [
        (PersonaSection.RedLines, "Red lines",
            "Things the user will not accept. These carry the most weight: keep each one unless the user lifts it in this conversation. When one stops you, explain the concrete reason in your own words."),
        (PersonaSection.WorkingRules, "Working rules", "How the user wants to work with you. Follow these by default."),
        (PersonaSection.Preferences, "Preferences",
            "Descriptions of the user's tastes and habits, not orders; weigh them against what the task needs."),
        (PersonaSection.Profile, "Profile", "Facts about the user. Use them where they help."),
    ];

    /// <summary>The block's lines, LF-joined, without a trailing line break.</summary>
    public static string Render(PersonaDocument persona)
    {
        var block = new StringBuilder();
        block.Append(BeginMarker).Append('\n');
        block.Append("<user_memory>\n");
        block.Append(Shell.ReplaceLineEndings("\n")).Append('\n');

        foreach (var (kind, title, guidance) in Sections)
        {
            var items = persona.Parts.Where(part => part.Kind == kind).SelectMany(part => part.Items).ToList();
            if (items.Count > 0)
                AppendSection(block, title, guidance, items);
        }

        // Sections that are not one of the four, then any text from before the first heading, as the user wrote them.
        foreach (var part in persona.Parts.Where(part => part.Kind is null && part.Items.Count > 0).OrderBy(part => part.Heading is null))
            AppendSection(block, part.Heading, null, part.Items);

        block.Append("</user_memory>\n");
        block.Append(EndMarker);
        return block.ToString();
    }

    private static void AppendSection(StringBuilder block, string? title, string? guidance, IEnumerable<PersonaItem> items)
    {
        block.Append('\n');
        if (title is not null)
            block.Append("## ").Append(title).Append('\n');
        if (guidance is not null)
            block.Append(guidance).Append('\n');
        foreach (var item in items)
        {
            block.Append(item.IsListItem ? "- " : "").Append(item.Text).Append('\n');
            foreach (var line in item.ContinuationWithoutComments)
                block.Append(line).Append('\n');
        }
    }
}
