using System.Diagnostics;
using System.Globalization;
using System.Text;
using TacitMemory.Core.Index;
using TacitMemory.Core.Models;
using TacitMemory.Core.Pages;

namespace TacitMemory.Core.Retrieval;

/// <summary>Values that replace the retrieval config for one search; <c>null</c> keeps the configured value.</summary>
internal sealed record RetrievalOverrides(double? MinRerankScore = null, int? TopK = null, int? Candidates = null);

/// <summary>What became of a retrieval unit in a search.</summary>
public enum UnitFate
{
    /// <summary>The result the unit belongs to was shown.</summary>
    Shown,

    /// <summary>The unit passed every stage, but its result came after the first <c>topK</c> results.</summary>
    BeyondTopK,

    /// <summary>The reranker scored the unit below the minimum score.</summary>
    BelowThreshold,

    /// <summary>The unit ranked after the candidates kept by fusion, so the reranker never saw it.</summary>
    BeyondCandidates,

    /// <summary>The unit's result was among the first <c>topK</c>, but its page could no longer be read, so it was left out.</summary>
    Unreadable,
}

/// <summary>One retrieval unit as a search saw it, stage by stage. Ranks are 1-based.</summary>
/// <param name="FullTextRank">The unit's rank in the full-text search; <c>null</c> when that search did not return it.</param>
/// <param name="Bm25">The unit's BM25 value (lower is better); <c>null</c> when it was not returned or matched as a substring.</param>
/// <param name="VectorRank">The unit's rank in the vector search; <c>null</c> when that search did not return it.</param>
/// <param name="Distance">The unit's cosine distance to the query; <c>null</c> when the vector search did not return it.</param>
/// <param name="Rrf">The unit's reciprocal rank fusion score.</param>
/// <param name="FusedRank">The unit's rank after fusion.</param>
/// <param name="RerankScore">The reranker's score; <c>null</c> when the unit was beyond the candidates or the reranker did not score.</param>
/// <param name="Result">The number of the shown result the unit belongs to; <c>null</c> unless its fate is <see cref="UnitFate.Shown"/>.</param>
internal sealed record UnitTrace(
    IndexHit Hit,
    int? FullTextRank,
    double? Bm25,
    int? VectorRank,
    double? Distance,
    double Rrf,
    int FusedRank,
    double? RerankScore,
    UnitFate Fate,
    int? Result);

/// <summary>How one model took part in a search.</summary>
/// <param name="Configured">Whether the model is configured.</param>
/// <param name="Answered">Whether it answered; <c>false</c> when it is not configured, cooling down or failed.</param>
/// <param name="CoolingDown">Whether it was cooling down from an earlier failure, so it was not called.</param>
/// <param name="Elapsed">How long the call took; <c>null</c> when the model is not configured.</param>
internal sealed record ModelStage(bool Configured, bool Answered, bool CoolingDown, TimeSpan? Elapsed);

/// <summary>
/// A result as the agent sees it: its number and the units it is made of, best first. A page's result also has the page's
/// title and the sections the text shows.
/// </summary>
internal sealed record ShownResult(int Number, IReadOnlyList<IndexHit> Units, string? Title = null, IReadOnlyList<string>? Sections = null);

/// <summary>
/// A whole search, stage by stage: the values it ran with, how each model took part, every unit either search returned and
/// what became of it, the results shown, and the text <c>recall</c> answers with.
/// </summary>
/// <param name="Units">Every unit either search returned, in fused order.</param>
/// <param name="Text">The answer <c>recall</c> gives, notes included.</param>
/// <param name="Notes">The degradation notes appended to the text.</param>
internal sealed record RecallTrace(
    FullTextMethod FullText,
    ModelStage Embedding,
    ModelStage Reranker,
    double MinRerankScore,
    int TopK,
    int Candidates,
    IReadOnlyList<UnitTrace> Units,
    IReadOnlyList<ShownResult> Results,
    IReadOnlyList<string> Notes,
    string Text)
{
    /// <summary>The units of the shown results: <c>recall</c> counts a return for each.</summary>
    public IEnumerable<IndexHit> Returned => Results.SelectMany(result => result.Units);
}

/// <summary>
/// The search behind <c>recall</c> and the recall trace: full text and vector search, reciprocal rank fusion, reranking with a
/// minimum score, grouping into results and the text the agent reads. It counts nothing and never changes a file; the caller
/// decides whether the shown units count as returned.
/// </summary>
internal sealed class RecallPipeline(TacitConfig config, MemoryIndex index, EmbeddingModel embedding, RerankerModel reranker, VectorSync vectors)
{
    /// <summary>The answer when nothing matches; also a plain result, not an error.</summary>
    public const string NoMemory = "No relevant memory.";

    public const string EmbeddingDown =
        "Note: the embedding model is unavailable, so these results come from full-text search only and may miss differently worded memories.";

    /// <summary>Added when the reranker cannot score; the fusion order is kept and no minimum score applies.</summary>
    public const string RerankerDown =
        "Note: the reranker model is unavailable, so these results keep the fusion order and no minimum score applies.";

    /// <summary>The minimum rerank score when the config sets none; units scored below it are not shown.</summary>
    internal const double DefaultMinRerankScore = 0.001;

    /// <summary>How many sections a page that matched only on its title or aliases shows.</summary>
    private const int PageLevelSections = 2;

    /// <summary>The usual constant of reciprocal rank fusion; it damps the weight of the very top ranks.</summary>
    private const int RrfK = 60;

    public async Task<RecallTrace> RunAsync(RecallSearch search, CancellationToken cancellationToken)
    {
        var (query, filter, overrides) = search;
        var candidates = overrides.Candidates ?? config.Retrieval.Candidates;
        var topK = overrides.TopK ?? config.Retrieval.TopK;
        var floor = overrides.MinRerankScore ?? config.Retrieval.MinRerankScore ?? DefaultMinRerankScore;
        var fullText = index.Search(query, filter, candidates);

        IReadOnlyList<ScoredHit> nearest = [];
        var notes = new List<string>();
        var embeddingCooling = embedding.Configured && embedding.Health.CoolingDown;
        var clock = Stopwatch.StartNew();
        var queryVector = await embedding.EmbedAsync([query], cancellationToken);
        var embeddingStage = new ModelStage(embedding.Configured, queryVector is not null, embeddingCooling, embedding.Configured ? clock.Elapsed : null);
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

        var fused = Fuse([fullText.Hits, nearest]);
        var kept = fused.Take(candidates).Select(unit => unit.Hit).ToList();
        var rerankerCooling = reranker.Configured && reranker.Health.CoolingDown;
        clock.Restart();
        var scores = await reranker.ScoreAsync(query, kept.Select(unit => unit.Passage).ToList(), cancellationToken);
        var rerankerStage = new ModelStage(reranker.Configured, scores is not null, rerankerCooling, reranker.Configured ? clock.Elapsed : null);
        var scoreOf = new Dictionary<IndexHit, double>();
        List<IndexHit> ranked;
        if (scores is null)
        {
            if (reranker.Configured)
                notes.Add(RerankerDown);
            ranked = kept;
        }
        else
        {
            for (var i = 0; i < kept.Count; i++)
                scoreOf[kept[i]] = scores[i];
            ranked = kept.Where(unit => scoreOf[unit] >= floor).OrderByDescending(unit => scoreOf[unit]).ToList();
        }

        // Pages group by source. Each activity and each summary is a result of its own, so two of one month file stay apart.
        var groups = ranked.GroupBy(GroupKey).ToList();
        var (text, results) = Format(groups.Take(topK).ToList());
        if (notes.Count > 0)
            text += "\n\n" + string.Join('\n', notes);

        var resultOf = results.SelectMany(result => result.Units.Select(unit => (unit, result.Number))).ToDictionary(pair => pair.unit, pair => pair.Number);
        var withinTopK = groups.Take(topK).SelectMany(group => group).ToHashSet();
        var passed = ranked.ToHashSet();
        var traces = fused.Select((unit, position) =>
        {
            var fate = resultOf.ContainsKey(unit.Hit) ? UnitFate.Shown
                : withinTopK.Contains(unit.Hit) ? UnitFate.Unreadable
                : passed.Contains(unit.Hit) ? UnitFate.BeyondTopK
                : position < candidates ? UnitFate.BelowThreshold
                : UnitFate.BeyondCandidates;
            return new UnitTrace(
                unit.Hit,
                unit.FullTextRank,
                unit.Bm25,
                unit.VectorRank,
                unit.Distance,
                unit.Rrf,
                position + 1,
                scoreOf.TryGetValue(unit.Hit, out var score) ? score : null,
                fate,
                resultOf.TryGetValue(unit.Hit, out var number) ? number : null);
        }).ToList();

        return new RecallTrace(fullText.Method, embeddingStage, rerankerStage, floor, topK, candidates, traces, results, notes, text.TrimEnd());
    }

    /// <summary>A unit after fusion, with what each search said about it.</summary>
    private sealed record FusedUnit(IndexHit Hit, int? FullTextRank, double? Bm25, int? VectorRank, double? Distance, double Rrf);

    /// <summary>Reciprocal rank fusion: each list adds 1 / (k + rank) to a unit's score. Every unit is kept, best first.</summary>
    private static List<FusedUnit> Fuse(IReadOnlyList<IReadOnlyList<ScoredHit>> lists)
    {
        var units = new Dictionary<IndexHit, FusedUnit>();
        for (var list = 0; list < lists.Count; list++)
        {
            for (var rank = 0; rank < lists[list].Count; rank++)
            {
                var (hit, score) = lists[list][rank];
                var unit = units.GetValueOrDefault(hit) ?? new FusedUnit(hit, null, null, null, null, 0);
                unit = list == 0 ? unit with { FullTextRank = rank + 1, Bm25 = score } : unit with { VectorRank = rank + 1, Distance = score };
                units[hit] = unit with { Rrf = unit.Rrf + 1.0 / (RrfK + rank + 1) };
            }
        }

        return units.Values.OrderByDescending(unit => unit.Rrf).ToList();
    }

    /// <summary>
    /// The text of the results, and the results it shows: every unit of a shown result counts as returned. A page shown only
    /// for its title or aliases counts its clue unit, not the sections it displays.
    /// </summary>
    private (string Text, List<ShownResult> Results) Format(IReadOnlyList<IGrouping<(string, string?, string?), IndexHit>> groups)
    {
        var text = new StringBuilder();
        var results = new List<ShownResult>();
        foreach (var result in groups)
        {
            var sample = result.First();
            if (sample.Kind == UnitKinds.Activity)
            {
                results.Add(new ShownResult(results.Count + 1, [.. result]));
                var label = sample.Workstream.Length > 0 ? $", workstream {sample.Workstream}" : "";
                text.Append(CultureInfo.InvariantCulture, $"\n[{results.Count}] activity {sample.Date} [{sample.Code}]{label}\n{sample.Text}\n");
                continue;
            }

            if (sample.Kind == UnitKinds.Summary)
            {
                results.Add(new ShownResult(results.Count + 1, [.. result]));
                text.Append(CultureInfo.InvariantCulture, $"\n[{results.Count}] summary, {sample.Section} starting {sample.Date}\n{sample.Text}\n");
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

            results.Add(new ShownResult(results.Count + 1, [.. result], document.Header.Title, [.. sections.Select(section => section.Name)]));
            text.Append(CultureInfo.InvariantCulture, $"\n[{results.Count}] page {id}: {document.Header.Title}\n");
            if (document.Header.First is { } first && document.Header.Last is { } last)
                text.Append(CultureInfo.InvariantCulture, $"Dates: {first:yyyy-MM-dd} to {last:yyyy-MM-dd}\n");
            foreach (var section in sections)
                text.Append(CultureInfo.InvariantCulture, $"### {section.Name} [{section.Code}]{SectionDates.Note(changed, section.Name)}\n{section.Body}\n");
            if (others.Count > 0)
                text.Append(CultureInfo.InvariantCulture, $"Other sections: {string.Join(", ", others)}\n");
        }

        var body = results.Count == 0
            ? NoMemory
            : $"{results.Count} result(s). Short codes in brackets identify sections for write_page and activities for write_recent; read a whole page with recall page=<page ID>.\n{text}";
        return (body, results);
    }

    /// <summary>Hits of one activity or summary are one result each; the hits of a page's sections share the page's result.</summary>
    private static (string Source, string? Code, string? Text) GroupKey(IndexHit hit) =>
        hit.Kind is UnitKinds.Activity or UnitKinds.Summary ? (hit.Source, hit.Code, hit.Text) : (hit.Source, null, null);
}
