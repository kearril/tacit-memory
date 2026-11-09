using System.Globalization;
using System.Text;
using TacitMemory.Core.Index;
using TacitMemory.Core.Models;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Pages;
using TacitMemory.Core.Tools;

namespace TacitMemory.Core.Retrieval;

/// <summary>
/// The <c>recall</c> tool: reads one page by ID, or searches pages and activities by full text and by vector, merges the two with
/// reciprocal rank fusion, reranks the merged units and returns the best sections, merged per page. It never changes a file.
/// </summary>
internal sealed class RecallTool(TacitConfig config, MemoryIndex index, PageTools pages, EmbeddingModel embedding, RerankerModel reranker, VectorSync vectors)
{
    /// <summary>The answer when nothing matches; also a plain result, not an error.</summary>
    public const string NoMemory = "No relevant memory.";

    public const string EmbeddingDown =
        "Note: the embedding model is unavailable, so these results come from full-text search only and may miss differently worded memories.";

    /// <summary>Added when the reranker cannot score; the fusion order is kept and no minimum score applies.</summary>
    public const string RerankerDown =
        "Note: the reranker model is unavailable, so these results keep the fusion order and no minimum score applies.";

    /// <summary>The minimum rerank score when the config sets none; units scored below it are not shown.</summary>
    private const double DefaultMinRerankScore = 0.001;

    /// <summary>How many sections a page that matched only on its title or aliases shows.</summary>
    private const int PageLevelSections = 2;

    /// <summary>The usual constant of reciprocal rank fusion; it damps the weight of the very top ranks.</summary>
    private const int RrfK = 60;

    /// <summary>Every parameter <c>recall</c> takes, in the order the next step lists them.</summary>
    private static readonly string[] Parameters = ["query", "page", "project", "from", "to", "kind"];

    /// <summary>The next step when the call gave a parameter the schema does not declare.</summary>
    private static readonly string ParametersStep =
        $"Call recall again with only these parameters: {string.Join(", ", Parameters)}; give exactly one of query or page.";

    public async Task<ToolResult> RunAsync(RecallRequest request, CancellationToken cancellationToken)
    {
        var hasQuery = !string.IsNullOrWhiteSpace(request.Query);
        var hasPage = !string.IsNullOrWhiteSpace(request.Page);
        var unknown = ToolText.UnknownParameters(request.UnknownParameters);
        if (hasPage && !hasQuery)
            return unknown.Count == 0 ? pages.Read(request, index) : ToolText.Refuse(unknown, ParametersStep);

        var problems = new List<string>(unknown);
        var queryOrPage = hasQuery == hasPage;
        if (queryOrPage)
            problems.Add(hasQuery ? "Give query or page, not both." : "Missing parameter query or page.");
        var from = Date(request.From, "from", problems);
        var to = Date(request.To, "to", problems);
        if (from > to)
            problems.Add("from is later than to.");
        var kind = request.Kind?.Trim();
        if (kind is { Length: > 0 } and not (ToolValues.RecallKinds.Pages or ToolValues.RecallKinds.Activities))
            problems.Add($"Unknown kind {ToolText.Quote(kind)}; use pages or activities.");
        if (problems.Count > 0)
        {
            var nextStep = unknown.Count > 0 ? ParametersStep
                : queryOrPage ? "Call recall again with query (what to look for, in the memory language) or page (a page ID to read in full)."
                : "Fix these and call recall again.";
            return ToolText.Refuse(problems, nextStep);
        }

        var project = string.IsNullOrWhiteSpace(request.Project) ? null : request.Project.Trim().ToLowerInvariant();
        var activities = kind switch { ToolValues.RecallKinds.Activities => true, ToolValues.RecallKinds.Pages => false, _ => (bool?)null };
        var filter = new IndexFilter(project, from, to, activities);
        var candidates = config.Retrieval.Candidates;
        var fullText = index.Search(request.Query!, filter, candidates);

        IReadOnlyList<IndexHit> nearest = [];
        var notes = new List<string>();
        var queryVector = await embedding.EmbedAsync([request.Query!], cancellationToken);
        if (queryVector is null)
        {
            if (embedding.Configured)
                notes.Add(EmbeddingDown);
        }
        else
        {
            await vectors.PrepareSearchAsync(queryVector[0].Length, cancellationToken);
            nearest = index.SearchVector(queryVector[0], filter, candidates);
        }

        var ranked = await RerankAsync(request.Query!, Fuse([fullText, nearest], candidates), notes, cancellationToken);
        var (text, shown) = Format(ranked);
        index.RecordReturns(shown.Select(hit => hit.Id).ToList());
        return ToolText.Ok(notes.Count == 0 ? text : text + "\n\n" + string.Join('\n', notes));
    }

    /// <summary>Reciprocal rank fusion: each list adds 1 / (k + rank) to a unit's score.</summary>
    private static List<IndexHit> Fuse(IReadOnlyList<IReadOnlyList<IndexHit>> lists, int limit)
    {
        var scores = new Dictionary<IndexHit, double>();
        foreach (var list in lists)
        {
            for (var rank = 0; rank < list.Count; rank++)
                scores[list[rank]] = scores.GetValueOrDefault(list[rank]) + 1.0 / (RrfK + rank + 1);
        }

        return scores.OrderByDescending(score => score.Value).Select(score => score.Key).Take(limit).ToList();
    }

    /// <summary>
    /// Orders the fused units by the reranker's score and drops those under the minimum score. When the reranker
    /// cannot score, the fusion order stays and nothing is dropped.
    /// </summary>
    private async Task<IReadOnlyList<IndexHit>> RerankAsync(
        string query, IReadOnlyList<IndexHit> units, List<string> notes, CancellationToken cancellationToken)
    {
        var scores = await reranker.ScoreAsync(query, units.Select(unit => unit.Text).ToList(), cancellationToken);
        if (scores is null)
        {
            if (reranker.Configured)
                notes.Add(RerankerDown);
            return units;
        }

        var floor = config.Retrieval.MinRerankScore ?? DefaultMinRerankScore;
        return units
            .Select((unit, index) => (Unit: unit, Score: scores[index]))
            .Where(scored => scored.Score >= floor)
            .OrderByDescending(scored => scored.Score)
            .Select(scored => scored.Unit)
            .ToList();
    }

    /// <summary>
    /// The text of the results, and the units it shows: every unit of a shown result counts as returned. A page shown only
    /// for its title or aliases counts its clue unit, not the sections it displays.
    /// </summary>
    private (string Text, List<IndexHit> Shown) Format(IReadOnlyList<IndexHit> hits)
    {
        // Pages group by source. Each activity is a result of its own, so two activities of one month file stay apart.
        var results = hits.GroupBy(hit => (hit.Source, Code: hit.Kind == UnitKinds.Activity ? hit.Code : null)).Take(config.Retrieval.TopK).ToList();
        var text = new StringBuilder();
        var shownResults = 0;
        var returned = new List<IndexHit>();
        foreach (var result in results)
        {
            var sample = result.First();
            if (sample.Kind == UnitKinds.Activity)
            {
                returned.AddRange(result);
                shownResults++;
                var label = sample.Project.Length > 0 ? $", project {sample.Project}" : "";
                text.Append(CultureInfo.InvariantCulture, $"\n[{shownResults}] activity {sample.Date} [{sample.Code}]{label}\n{sample.Text}\n");
                continue;
            }

            if (!PageId.TryParse(sample.Source, out var id, out _) || PageDocument.TryLoad(config.MemoryPath, id) is not { } document)
                continue;
            var matched = result.Select(hit => hit.Section).OfType<string>().ToHashSet(StringComparer.Ordinal);
            var sections = matched.Count > 0
                ? document.Sections.Where(section => matched.Contains(section.Name)).ToList()
                : document.Sections.Take(PageLevelSections).ToList();
            var others = document.Sections.Except(sections).Select(section => section.Name).ToList();
            var changed = index.SectionChanges(id);

            returned.AddRange(result);
            shownResults++;
            text.Append(CultureInfo.InvariantCulture, $"\n[{shownResults}] page {id}: {document.Header.Title}\n");
            if (document.Header.First is { } first && document.Header.Last is { } last)
                text.Append(CultureInfo.InvariantCulture, $"Dates: {first:yyyy-MM-dd} to {last:yyyy-MM-dd}\n");
            foreach (var section in sections)
                text.Append(CultureInfo.InvariantCulture, $"### {section.Name} [{section.Code}]{SectionDates.Note(changed, section.Name)}\n{section.Body}\n");
            if (others.Count > 0)
                text.Append(CultureInfo.InvariantCulture, $"Other sections: {string.Join(", ", others)}\n");
        }

        var body = shownResults == 0
            ? NoMemory
            : $"{shownResults} result(s). Short codes in brackets identify sections for write_page and activities for write_recent; read a whole page with recall page=<page ID>.\n{text}";
        return (body, returned);
    }

    private static DateOnly? Date(string? raw, string name, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        if (DateOnly.TryParseExact(raw.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;
        problems.Add($"{name} {ToolText.Quote(raw)} must be a date written YYYY-MM-DD.");
        return null;
    }
}
