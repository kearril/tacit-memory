using System.Text;
using TacitMemory.Core.Index;
using TacitMemory.Core.Models;
using TacitMemory.Core.Pages;

namespace TacitMemory.Core.Decisions;

/// <summary>What the duplicate check found: the page that already records the matter, or a note for the agent.</summary>
internal sealed record DuplicateCheck(PageId? Duplicate, string? Note);

/// <summary>
/// Before a page is created, asks whether a page of the same workstream already records the same matter: the nearest
/// pages by embedding, plus "none of these", go to the decision model as a choice question. The consolidation asks the same
/// question about changed pages, so the question and its options are built by the members shared below.
/// </summary>
internal sealed class DuplicatePageCheck(TacitConfig config, MemoryIndex index, EmbeddingModel embedding, VectorSync vectors, ChoiceJudge judge)
{
    /// <summary>Pages offered besides "none of these"; the decision model takes at most 10 options.</summary>
    public const int OtherOptions = 9;

    public const string DecisionDown =
        "Note: the decision model is unavailable, so whether this page repeats an existing one was not checked.";

    public const string EmbeddingDown =
        "Note: the embedding model is unavailable, so whether this page repeats an existing one was not checked.";

    /// <summary>The question's instructions, the same for a new page and for a changed one.</summary>
    public const string Instructions =
        "The state is a new page for a personal memory. Each option is an existing page of the same workstream. "
        + "Choose the option that describes exactly the same issue, bug, decision or subject as the new page. "
        + "If the options only share a product area or component but describe a different matter, choose the none option.";

    /// <summary>The text of the "none of these" option, the same for a new page and for a changed one.</summary>
    public const string NoneText = "None of the existing pages describes the same matter as the new page.";

    /// <summary>Longest description of one candidate.</summary>
    private const int OptionLength = 250;

    /// <summary>Characters taken from the start of each section when summarizing a candidate page.</summary>
    private const int SectionStartLength = 80;

    public async Task<DuplicateCheck> RunAsync(PageId id, PageHeader header, IReadOnlyList<PageSectionInput> sections, CancellationToken cancellationToken)
    {
        var embeddingFailed = false;
        var outcome = await judge.AskAsync(
            Neighbours.Elsewhere,
            async token =>
            {
                if (!index.HasOtherPagesInWorkstream(id))
                    return PreparedChoice<PageId>.Nothing;
                var vector = await embedding.EmbedAsync([string.Join('\n', [header.Title, .. header.Aliases])], token);
                if (vector is null)
                {
                    embeddingFailed = true;
                    return new PreparedChoice<PageId>(null, ChoiceJudge.NoAnswerReason(embedding.Configured));
                }
                await vectors.PrepareSearchAsync(vector[0].Length, token);
                var options = Describe(config.MemoryPath, Nearest(index, id, vector[0]))
                    .Select(described => new ChoiceOption<PageId>(described.Text, described.Page))
                    .ToList();
                return options.Count == 0
                    ? PreparedChoice<PageId>.Nothing
                    : new PreparedChoice<PageId>(new ChoiceQuestion<PageId>(State(header, sections), Instructions, NoneText, options), null);
            },
            config.Decision.SamePage ?? Judgment.SamePage,
            cancellationToken);
        return new DuplicateCheck(outcome.Certain, ChoiceJudge.NoteOf(outcome.Reason, embeddingFailed, EmbeddingDown, DecisionDown));
    }

    /// <summary>The new page as the decision model reads it: labels in English, content as written.</summary>
    public static string State(PageHeader header, IReadOnlyList<PageSectionInput> sections)
    {
        var text = new StringBuilder();
        text.Append("Title: ").Append(header.Title).Append('\n');
        text.Append("Aliases: ").Append(string.Join("; ", header.Aliases)).Append('\n');
        foreach (var section in sections)
            text.Append("\n## ").Append(section.Name).Append('\n').Append(section.Body.Trim()).Append('\n');
        return text.ToString();
    }

    /// <summary>
    /// The nearest pages of the same workstream as <paramref name="id"/> by <paramref name="vector"/>: at most
    /// <see cref="OtherOptions"/>, never the page itself, nearest first.
    /// </summary>
    public static IReadOnlyList<PageId> Nearest(MemoryIndex index, PageId id, float[] vector) =>
        index.SearchVector(vector, new IndexFilter(id.Workstream, null, null), int.MaxValue)
            .Select(scored => scored.Hit.Source)
            .Where(page => page != id.ToString())
            .Distinct()
            .Take(OtherOptions)
            .Select(page => PageId.TryParse(page, out var parsed, out _) ? parsed : null)
            .OfType<PageId>()
            .ToList();

    /// <summary>The pages as option texts, in order; a page whose file cannot be read is left out.</summary>
    public static IReadOnlyList<(PageId Page, string Text)> Describe(string memoryPath, IEnumerable<PageId> pages)
    {
        var described = new List<(PageId Page, string Text)>();
        foreach (var page in pages)
            if (DescribeOne(memoryPath, page) is { } text)
                described.Add((page, text));
        return described;
    }

    /// <summary>One candidate page in a line: title, aliases and the start of each section.</summary>
    private static string? DescribeOne(string memoryPath, PageId page)
    {
        if (PageDocument.TryLoad(memoryPath, page) is not { } document)
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
