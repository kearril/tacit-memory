using TacitMemory.Core.Persona;

namespace TacitMemory.Core.Consolidation;

/// <summary>
/// The deterministic domain checks listed under "Domain hygiene" that are not format problems: a domain with only one entry.
/// </summary>
internal static class DomainHygiene
{
    /// <summary>One finding per domain of <c>persona.md</c> that holds exactly one entry; empty when the persona cannot be read.</summary>
    public static IReadOnlyList<string> Findings(TacitConfig config)
    {
        var persona = PersonaDocument.Read(MemoryLayout.Full(config.MemoryPath, MemoryLayout.Persona));
        if (persona is null)
            return [];
        return persona.Blocks
            .Where(block => block.Scope.Kind == PersonaScopeKind.Domain && block.Entries.Count == 1)
            .Select(block => $"- {block.Scope} has only 1 entry.")
            .ToList();
    }
}
