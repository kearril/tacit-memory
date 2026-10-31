using System.Globalization;
using System.Text;
using TacitMemory.Core.Index;
using TacitMemory.Core.Models;
using TacitMemory.Core.Pages;

namespace TacitMemory.Core.Decisions;

/// <summary>What the duplicate check found: the page that already records the matter, or a note for the agent.</summary>
internal sealed record DuplicateCheck(PageId? Duplicate, string? Note);

/// <summary>
/// Before a page is created, asks whether a page of the same project already records the same matter: the nearest
/// pages by embedding, plus "none of these", go to the decision model as a choice question.
/// </summary>
internal sealed class DuplicatePageCheck(TacitConfig config, PageIndex index, EmbeddingModel embedding, VectorSync vectors, IDecisionModel decision)
{
    /// <summary>Candidate pages offered besides "none of these"; the decision model takes at most 10 options.</summary>
    public const int Candidates = 9;

    public const string DecisionDown =
        "Note: the decision model is unavailable, so whether this page repeats an existing one was not checked.";

    public const string EmbeddingDown =
        "Note: the embedding model is unavailable, so whether this page repeats an existing one was not checked.";

    private const string None = "none";

    private const string Instructions =
        "The state is a new page for a personal memory. Each option is an existing page of the same project. "
        + "Choose the option that describes exactly the same issue, bug, decision or subject as the new page. "
        + "If the options only share a product area or component but describe a different matter, choose the none option.";

    private const string NoneText = "None of the existing pages describes the same matter as the new page.";

    /// <summary>Longest description of one candidate.</summary>
    private const int OptionLength = 250;

    /// <summary>Characters taken from the start of each section when summarizing a candidate page.</summary>
    private const int SectionStartLength = 80;

    public async Task<DuplicateCheck> RunAsync(PageId id, PageHeader header, IReadOnlyList<PageSectionInput> sections, CancellationToken cancellationToken)
    {
        if (!index.HasOtherPagesInProject(id))
            return new DuplicateCheck(null, null);

        var vector = await embedding.EmbedAsync([string.Join('\n', [header.Title, .. header.Aliases])], cancellationToken);
        if (vector is null)
            return new DuplicateCheck(null, EmbeddingDown);

        await vectors.PrepareSearchAsync(vector[0].Length, cancellationToken);
        var nearest = index.SearchVector(vector[0], new IndexFilter(id.Project, null, null), int.MaxValue)
            .Select(hit => hit.PageId)
            .Where(page => page != id.ToString())
            .Distinct()
            .Take(Candidates)
            .Select(page => PageId.TryParse(page, out var parsed, out _) ? parsed : null)
            .OfType<PageId>()
            .ToList();
        if (nearest.Count == 0)
            return new DuplicateCheck(null, null);

        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        var pages = new Dictionary<string, PageId>(StringComparer.Ordinal);
        foreach (var page in nearest)
        {
            if (Describe(page) is not { } description)
                continue;
            var key = "p" + (pages.Count + 1).ToString(CultureInfo.InvariantCulture);
            options[key] = description;
            pages[key] = page;
        }

        options[None] = NoneText;
        var probabilities = await decision.ChooseAsync(State(header, sections), Instructions, options, cancellationToken);
        if (probabilities is null)
            return new DuplicateCheck(null, DecisionDown);

        var verdict = Judgment.Choice(probabilities, None, config.Decision.SamePage ?? Judgment.SamePage);
        return verdict.Tier == Tier.Certain ? new DuplicateCheck(pages[verdict.Option!], null) : new DuplicateCheck(null, null);
    }

    /// <summary>The new page as the decision model reads it: labels in English, content as written.</summary>
    private static string State(PageHeader header, IReadOnlyList<PageSectionInput> sections)
    {
        var text = new StringBuilder();
        text.Append("Title: ").Append(header.Title).Append('\n');
        text.Append("Aliases: ").Append(string.Join("; ", header.Aliases)).Append('\n');
        foreach (var section in sections)
            text.Append("\n## ").Append(section.Name).Append('\n').Append(section.Body.Trim()).Append('\n');
        return text.ToString();
    }

    /// <summary>One candidate page in a line: title, aliases and the start of each section.</summary>
    private string? Describe(PageId page)
    {
        if (PageDocument.TryLoad(config.MemoryPath, page) is not { } document)
            return null;
        var sections = string.Join("; ", document.Sections.Select(section =>
        {
            var body = section.Body.ReplaceLineEndings(" ").Trim();
            return $"{section.Name}: {(body.Length <= SectionStartLength ? body : body[..SectionStartLength])}";
        }));
        var text = $"Title: {document.Header.Title}; Aliases: {string.Join("; ", document.Header.Aliases)}; Sections: {sections}";
        return text.Length <= OptionLength ? text : text[..OptionLength];
    }
}
