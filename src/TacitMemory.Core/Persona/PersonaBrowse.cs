using System.Globalization;
using TacitMemory.Core.Formats;
using TacitMemory.Core.Injection;

namespace TacitMemory.Core.Persona;

/// <summary>
/// The persona and its candidates for the memory page, read from the files as they are now, with the problems
/// consolidation reports for them.
/// </summary>
internal static class PersonaBrowse
{
    public static PersonaReport Read(TacitConfig config)
    {
        var personaPath = MemoryLayout.Full(config.MemoryPath, MemoryLayout.Persona);
        var candidatesPath = MemoryLayout.Full(config.MemoryPath, MemoryLayout.Candidates);
        var persona = PersonaDocument.Read(personaPath) ?? new PersonaDocument([], []);
        var candidates = TextFile.Read(candidatesPath) is var (text, _) ? CandidatesDocument.Parse(text) : new CandidatesDocument([], []);
        var codes = persona.CodesByLine();

        var blocks = persona.Blocks.OrderBy(block => block.Scope, Comparer<PersonaScope>.Create(PersonaScope.Compare)).ToList();
        return new PersonaReport(
            MemoryFileReport.Of(config, MemoryLayout.Persona),
            Sections(persona.Parts, codes, everyKind: true),
            [.. blocks.Where(block => block.Scope.Kind == PersonaScopeKind.Domain).Select(block => Block(block, codes))],
            [.. blocks.Where(block => block.Scope.Kind == PersonaScopeKind.Workstream).Select(block => Block(block, codes))],
            MemoryFileReport.Of(config, MemoryLayout.Candidates),
            [.. candidates.Candidates.Select(Candidate)],
            [.. candidates.RejectionList.Select(Candidate)]);
    }

    /// <summary>
    /// The sections of the four kinds in their order, an entry of a kind written under two headings merged into one, then
    /// the other sections. With <paramref name="everyKind"/>, a kind the file has no section for is listed empty.
    /// </summary>
    private static List<PersonaSectionView> Sections(IReadOnlyList<PersonaPart> parts, IReadOnlyDictionary<int, string> codes, bool everyKind)
    {
        var sections = new List<PersonaSectionView>();
        foreach (var kind in Enum.GetValues<PersonaSection>())
        {
            var ofKind = parts.Where(part => part.Kind == kind).ToList();
            if (ofKind.Count > 0 || everyKind)
                sections.Add(new PersonaSectionView(kind, ofKind.FirstOrDefault()?.Heading, [.. ofKind.SelectMany(part => part.Items).Select(item => Entry(item, codes))]));
        }

        sections.AddRange(parts.Where(part => part.Kind is null).Select(part => new PersonaSectionView(null, part.Heading, [.. part.Items.Select(item => Entry(item, codes))])));
        return sections;
    }

    private static PersonaBlockView Block(PersonaBlock block, IReadOnlyDictionary<int, string> codes) =>
        new(block.Scope.Name, block.HeadingLine + 1, string.IsNullOrWhiteSpace(block.Trigger) ? null : block.Trigger.Trim(), BlockSections(block, codes));

    /// <summary>A scope block's sections as the persona view gives them; <paramref name="codes"/> are the persona's codes by line.</summary>
    internal static List<PersonaSectionView> BlockSections(PersonaBlock block, IReadOnlyDictionary<int, string> codes) =>
        Sections(block.Parts, codes, everyKind: false);

    private static PersonaEntryView Entry(PersonaItem item, IReadOnlyDictionary<int, string> codes) =>
        new(item.Line + 1, item.IsListItem ? codes[item.Line] : null, item.Text, [.. item.Continuation.Select(line => line.Trim())]);

    private static CandidateView Candidate(PersonaCandidate candidate) =>
        new(candidate.Text, candidate.Section, candidate.Scope.ToString(),
            [.. candidate.Occurrences.Select(occurrence => new CandidateOccurrenceView(occurrence.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), occurrence.Quote, occurrence.Rejected))]);
}
