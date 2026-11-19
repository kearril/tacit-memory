using TacitMemory.Core.Injection;
using TacitMemory.Core.Persona;
using TacitMemory.Core.Recent;

namespace TacitMemory.Core.Consolidation;

/// <summary>
/// The "Length hints" category: the persona entries and the activities added since the baseline that are longer than
/// <c>consolidation.hintEntryLength</c>, and one line when the resident block is longer than <c>consolidation.hintResidentLength</c>.
/// Hints only: each line states where and how long; whether to propose a shorter wording is left to the consolidation skill.
/// </summary>
internal static class LengthHints
{
    public static async Task<IReadOnlyList<string>> FindingsAsync(ConsolidationContext context, CancellationToken cancellationToken)
    {
        var config = context.Config;
        var entryLimit = config.Consolidation.HintEntryLength;
        var findings = new List<string>();

        var persona = PersonaDocument.Read(MemoryLayout.Full(config.MemoryPath, MemoryLayout.Persona));
        if (persona is not null)
        {
            foreach (var entry in persona.KnownEntries())
            {
                var length = TextLength.Of(entry.Entry.Text);
                if (length > entryLimit)
                    findings.Add($"- {MemoryLayout.Persona}, line {entry.Entry.Line + 1}: {length} characters (hint length {entryLimit}).");
            }
        }

        foreach (var (activity, _) in await context.NewActivitiesAsync(cancellationToken))
        {
            var length = TextLength.Of(activity.Body);
            if (length > entryLimit)
                findings.Add($"- {MemoryLayout.RecentMonth(activity.Day)}, line {activity.Line + 1}: {length} characters (hint length {entryLimit}).");
        }

        var residentLimit = config.Consolidation.HintResidentLength;
        if (persona is not null)
        {
            var block = ResidentBlock.Render(ResidentContent.Read(config, context.Today, persona), config.Prompts.Resident);
            if (block.Length > residentLimit)
                findings.Add($"- The resident block is {block.Length} characters, over the hint length of {residentLimit}.");
        }

        return findings;
    }
}
