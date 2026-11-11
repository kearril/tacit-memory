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
        Background on the user from earlier sessions. It may be dated: project instructions come first; when the user says otherwise, follow them and ask in one sentence whether to update memory. Apply it the way a colleague who knows the user would: silently, mentioning an entry only when it bears on the task.
        """;

    /// <summary>The built-in <c>resident.md</c>: the shell and the guidance used when the user has no file.</summary>
    public static readonly ResidentDocument Default = new(
        DefaultShell,
        "Hold each unless the user lifts it in this conversation; when one stops you, give the reason.",
        null,
        "Tastes to weigh against the task.",
        null,
        "Each line is a trigger; when it matches, read the rules with `recall kind=rules domain=<label>`.",
        "Each line gives a workstream's background first, then its recent progress. Only consolidation updates the recent progress, so it may lag behind the activities; the activities win.");

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
        ResidentPart.Workstreams => content.Workstreams,
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
