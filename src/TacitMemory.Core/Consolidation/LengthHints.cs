using TacitMemory.Core.Injection;
using TacitMemory.Core.Persona;
using TacitMemory.Core.Recent;

namespace TacitMemory.Core.Consolidation;

/// <summary>
/// The "Length hints" category: the persona entries and the activities added since the baseline that are longer than
/// <c>consolidation.hintItemLength</c>, and one line when the resident block is longer than <c>consolidation.hintResidentLength</c>.
/// Hints only: nothing is refused, and the consolidation proposes shortening the listed items for the user to confirm.
/// </summary>
internal static class LengthHints
{
    public static async Task<IReadOnlyList<string>> FindingsAsync(ConsolidationContext context, CancellationToken cancellationToken)
    {
        var config = context.Config;
        var itemLimit = config.Consolidation.HintItemLength;
        var findings = new List<string>();

        var persona = PersonaDocument.Read(Path.Combine(config.MemoryPath, PersonaFiles.Persona));
        if (persona is not null)
        {
            foreach (var entry in persona.KnownEntries())
            {
                var length = TextLength.Of(entry.Item.Text);
                if (length > itemLimit)
                    findings.Add($"- {PersonaFiles.Persona}, line {entry.Item.Line + 1}: {length} characters (hint length {itemLimit}); propose a shorter wording.");
            }
        }

        foreach (var (activity, _) in await context.NewActivitiesAsync(cancellationToken))
        {
            var length = TextLength.Of(activity.Body);
            if (length > itemLimit)
                findings.Add($"- {RecentFile.RelativePath(activity.Day)}, line {activity.Line + 1}: {length} characters (hint length {itemLimit}); propose a shorter wording.");
        }

        var residentLimit = config.Consolidation.HintResidentLength;
        if (persona is not null)
        {
            var block = ResidentBlock.Render(ResidentContent.Read(config, context.Today, persona), config.Prompts.Resident);
            if (block.Length > residentLimit)
                findings.Add($"- The resident block is {block.Length} characters, over the hint length of {residentLimit}; propose shortening its persona entries or workstream lines.");
        }

        return findings;
    }
}
