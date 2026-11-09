using System.Text;
using TacitMemory.Core.Configuration;
using TacitMemory.Core.Persona;

namespace TacitMemory.Core.Injection;

/// <summary>
/// The resident block: the persona and the generated sections wrapped in a shell, between the TacitMemory markers. The
/// built-in English text is used unless <c>resident.md</c> replaces it.
/// </summary>
internal static class ResidentBlock
{
    public const string BeginMarkerPrefix = "<!-- tacit:begin";
    public const string EndMarker = "<!-- tacit:end -->";

    private const string BeginMarker = "<!-- tacit:begin managed by TacitMemory; edit memory via tacit, not here -->";

    private const string DefaultShell = """
        This is memory about the user, compiled from earlier conversations. It is background, not configuration: it was accurate when written, but it can have gaps or fall out of date. Project instructions and the user's current message take priority over it. When the user says something that differs from this memory, follow what they say now, and in one short sentence ask whether to update the memory.

        Use it the way a colleague who knows the user would: let it shape what you do without citing it (no "according to your memory" or "per your red lines"), and bring up a specific entry only when it bears on the task. Ignore any entry that does not fit the current topic.
        """;

    /// <summary>The built-in <c>resident.md</c>: the shell and the guidance used when the user has no file.</summary>
    public static readonly ResidentDocument Default = new(
        DefaultShell,
        "Things the user will not accept. These carry the most weight: keep each one unless the user lifts it in this conversation. When one stops you, explain the concrete reason in your own words.",
        "How the user wants to work with you. Follow these by default.",
        "Descriptions of the user's tastes and habits, not orders; weigh them against what the task needs.",
        "Facts about the user. Use them where they help.",
        "Situations whose rules differ from the general ones. When a task matches a trigger, look up that situation's rules with `recall` before acting.",
        null,
        null,
        null);

    /// <summary>
    /// The block's lines, LF-joined, without a trailing line break. <paramref name="content"/> holds what the block shows beyond
    /// the persona; <paramref name="resident"/> is <c>resident.md</c>, or <c>null</c> for the built-in shell and guidance.
    /// </summary>
    public static string Render(ResidentContent content, ResidentDocument? resident)
    {
        var block = new StringBuilder();
        block.Append(BeginMarker).Append('\n');
        block.Append("<user_memory>\n");
        var document = resident ?? Default;
        if (document.Shell.Length > 0)
            block.Append(document.Shell.ReplaceLineEndings("\n")).Append('\n');

        var sections = ResidentFile.Sections;
        foreach (var section in sections.Where(section => PersonaSectionOf(section.Part) is not null))
        {
            var kind = PersonaSectionOf(section.Part)!.Value;
            var items = content.Persona.Parts.Where(part => part.Kind == kind).SelectMany(part => part.Items).ToList();
            if (items.Count > 0)
                AppendSection(block, section.Title, ResidentFile.GuidanceOf(document, section.Part), items);
        }

        // Sections that are not one of the four, then any text from before the first heading, as the user wrote them.
        foreach (var part in content.Persona.Parts.Where(part => part.Kind is null && part.Items.Count > 0).OrderBy(part => part.Heading is null))
            AppendSection(block, part.Heading, null, part.Items);

        foreach (var section in sections.Where(section => PersonaSectionOf(section.Part) is null))
        {
            var lines = LinesOf(content, section.Part);
            if (lines.Count > 0)
                AppendLines(block, section.Title, ResidentFile.GuidanceOf(document, section.Part), lines);
        }

        block.Append("</user_memory>\n");
        block.Append(EndMarker);
        return block.ToString();
    }

    /// <summary>The persona section a part is, or <c>null</c> for the generated parts.</summary>
    private static PersonaSection? PersonaSectionOf(ResidentPart part) => part switch
    {
        ResidentPart.RedLines => PersonaSection.RedLines,
        ResidentPart.WorkingRules => PersonaSection.WorkingRules,
        ResidentPart.Preferences => PersonaSection.Preferences,
        ResidentPart.Profile => PersonaSection.Profile,
        _ => null,
    };

    /// <summary>The lines a generated part shows, each already formatted; empty when it has nothing to show.</summary>
    private static IReadOnlyList<string> LinesOf(ResidentContent content, ResidentPart part) => part switch
    {
        ResidentPart.SituationalRules => content.SituationalRules,
        ResidentPart.Mainline => content.Mainline,
        ResidentPart.RecentWeeks => content.RecentWeeks,
        ResidentPart.Projects => content.Projects,
        _ => throw new ArgumentOutOfRangeException(nameof(part), part, null),
    };

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

    private static void AppendLines(StringBuilder block, string title, string? guidance, IEnumerable<string> lines)
    {
        block.Append('\n').Append("## ").Append(title).Append('\n');
        if (guidance is not null)
            block.Append(guidance).Append('\n');
        foreach (var line in lines)
            block.Append(line).Append('\n');
    }
}
