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

    /// <summary>The unit passed every stage, but <c>topK</c> results were shown before its result was reached.</summary>
    BeyondTopK,

    /// <summary>The unit's group fell below the relative-to-best score cutoff of the top shown result.</summary>
    BelowRelativeFloor,

    /// <summary>The activity or summary group exceeded the activity slot cap while detail pages qualified.</summary>
    BeyondActivityCap,

    /// <summary>The unit scored below the minimum relevance floor.</summary>
    BelowThreshold,

    /// <summary>The unit ranked after the candidates kept by fusion, so the reranker never saw it.</summary>
    BeyondCandidates,

    /// <summary>The unit's page could no longer be read when its result was reached, so it was left out and the next result took its place.</summary>
    Unreadable,
}

/// <summary>How a retrieval unit entered the candidate pool.</summary>
public enum GraphOrigin
{
    /// <summary>Matched directly by full-text and/or vector search.</summary>
    None,

    /// <summary>Injected by chasing active replacements of a superseded seed page.</summary>
    Supersedes,

    /// <summary>Injected as a 1-hop <c>related</c> neighbor of a seed page.</summary>
    Related,
}

/// <summary>One retrieval unit as a search saw it, stage by stage. Ranks are 1-based.</summary>
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
    int? Result,
    GraphOrigin GraphOrigin = GraphOrigin.None,
    double? FinalScore = null,
    bool Overturned = false);

/// <summary>How one model took part in a search.</summary>
internal sealed record ModelStage(bool Configured, bool Answered, bool CoolingDown, TimeSpan? Elapsed);

/// <summary>
/// A result as the agent sees it: its number and the units it is made of, best first. A page's result also carries its
/// title, shown sections, type, overturned state and active replacement IDs.
/// </summary>
internal sealed record ShownResult(
    int Number,
    IReadOnlyList<IndexHit> Units,
    string? Title = null,
    IReadOnlyList<string>? Sections = null,
    string? PageType = null,
    bool Overturned = false,
    IReadOnlyList<string>? SupersededBy = null);

/// <summary>
/// A whole search, stage by stage: the values it ran with, how each model took part, every unit either search returned and
/// what became of it, the results shown, and the text <c>recall</c> answers with.
/// </summary>
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
/// The search behind <c>recall</c> and the recall trace: full text and vector search, reciprocal rank fusion, bounded
/// 1-hop page-graph expansion, reranking, white-box post-rerank adjustments, two-layer gating, grouping into results
/// and the text the agent reads. It counts nothing and never changes a file; the caller decides whether the shown units
/// count as returned.
/// </summary>
internal sealed class RecallPipeline(
    TacitConfig config,
    MemoryIndex index,
    EmbeddingModel embedding,
    RerankerModel reranker,
    VectorSync vectors,
    Func<DateOnly> today)
{
    /// <summary>The answer when nothing matches; also a plain result, not an error.</summary>
    public const string NoMemory = "No relevant memory.";

    public const string EmbeddingDown =
        "Note: the embedding model is unavailable, so these results come from full-text search only and may miss differently worded memories.";

    /// <summary>Added when the reranker cannot score; the fusion order is kept and no minimum score applies.</summary>
    public const string RerankerDown =
        "Note: the reranker model is unavailable, so these results keep the fusion order and no minimum score applies.";

    /// <summary>The minimum rerank score when the config sets none; units scored below it are not shown.</summary>
    internal const double DefaultMinRerankScore = 0.20;

    /// <summary>How many sections a page that matched only on its title or aliases shows.</summary>
    private const int PageLevelSections = 2;

    /// <summary>The usual constant of reciprocal rank fusion; it damps the weight of the very top ranks.</summary>
    private const int RrfK = 60;

    /// <summary>Maximum number of distinct seed pages taken from top fused hits for 1-hop graph expansion.</summary>
    private const int MaxSeedPages = 5;

    /// <summary>Maximum number of units injected by active-replacement chasing.</summary>
    private const int MaxSupersedesUnits = 8;

    /// <summary>Maximum number of units injected from 1-hop <c>related</c> neighbors.</summary>
    private const int MaxRelatedUnits = 6;

    /// <summary>Fraction of a superseded page's score inherited by its active replacement.</summary>
    private const double ReplacementInheritanceFactor = 0.85;

    /// <summary>Mild demotion applied to effectively overturned or superseded pages.</summary>
    private const double OverturnedMultiplier = 0.70;

    /// <summary>Type prior applied to activities and summaries in general searches.</summary>
    private const double ActivityTypePrior = 0.80;

    /// <summary>Floor of the gentle recency factor for old activities and summaries.</summary>
    private const double ActivityRecencyFloor = 0.70;

    /// <summary>Decaying portion of the activity recency factor (floor + span = 1.0 on day 0).</summary>
    private const double ActivityRecencySpan = 0.30;

    /// <summary>Half-life in days of the gentle recency decay for activities and summaries.</summary>
    private const double ActivityHalfLifeDays = 90.0;

    /// <summary>Relative-to-best score cutoff applied to result groups after the top result when reranked.</summary>
    private const double RelativeCutoffRatio = 0.30;

    /// <summary>Maximum number of neighbor entries listed in a page's <c>Links:</c> footer.</summary>
    private const int MaxLinkHints = 5;

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
        var (allUnits, keptEntries) = ExpandCandidates(fused, candidates, filter);
        var kept = keptEntries.Select(entry => entry.Hit).ToList();

        var rerankerCooling = reranker.Configured && reranker.Health.CoolingDown;
        clock.Restart();
        var scores = await reranker.ScoreAsync(query, kept.Select(unit => unit.Passage).ToList(), cancellationToken);
        var rerankerStage = new ModelStage(reranker.Configured, scores is not null, rerankerCooling, reranker.Configured ? clock.Elapsed : null);
        if (scores is null && reranker.Configured)
            notes.Add(RerankerDown);

        var allPageIds = allUnits.Where(unit => !IsActivityUnit(unit.Hit)).Select(unit => unit.Hit.Source).Distinct(StringComparer.Ordinal).ToList();
        var pageInfos = index.PageInfos(allPageIds);
        var activeReplacements = index.ActiveReplacementsOf(allPageIds);

        var rawScoreOf = new Dictionary<IndexHit, double>();
        var finalScoreOf = new Dictionary<IndexHit, double>();
        var currentDay = today();
        var generalSearch = filter.Activities is null;
        List<IndexHit> passedUnits;

        if (scores is null)
        {
            var rrfByUnit = keptEntries.ToDictionary(entry => entry.Hit, entry => entry.Rrf);
            var workingRrf = new Dictionary<IndexHit, double>(rrfByUnit);
            ApplyReplacementInheritance(kept, rrfByUnit, workingRrf, activeReplacements, floor: 0.0, factor: 0.95);
            foreach (var entry in keptEntries)
                finalScoreOf[entry.Hit] = workingRrf[entry.Hit] * AdjustmentMultiplier(entry.Hit, pageInfos, generalSearch, currentDay);
            passedUnits = kept.OrderByDescending(unit => finalScoreOf[unit]).ToList();
        }
        else
        {
            var workingScoreOf = new Dictionary<IndexHit, double>(kept.Count);
            for (var i = 0; i < kept.Count; i++)
            {
                rawScoreOf[kept[i]] = scores[i];
                workingScoreOf[kept[i]] = scores[i];
            }

            ApplyReplacementInheritance(kept, rawScoreOf, workingScoreOf, activeReplacements, floor, ReplacementInheritanceFactor);

            foreach (var unit in kept)
                finalScoreOf[unit] = workingScoreOf[unit] * AdjustmentMultiplier(unit, pageInfos, generalSearch, currentDay);

            passedUnits = kept
                .Where(unit => finalScoreOf[unit] >= floor)
                .OrderByDescending(unit => finalScoreOf[unit])
                .ToList();
        }

        var grouped = passedUnits
            .GroupBy(GroupKey)
            .Select(group => new CandidateGroup(group.Key, [.. group], group.Max(unit => finalScoreOf[unit])))
            .OrderByDescending(group => group.Score)
            .ToList();

        var (text, results, unreadable, belowRelative, beyondActivityCap) = SelectAndFormat(
            grouped, topK, scores is not null, generalSearch, pageInfos, activeReplacements);
        if (notes.Count > 0)
            text += "\n\n" + string.Join('\n', notes);

        var resultOf = results.SelectMany(result => result.Units.Select(unit => (unit, result.Number))).ToDictionary(pair => pair.unit, pair => pair.Number);
        var passedSet = passedUnits.ToHashSet();
        var keptSet = kept.ToHashSet();
        var traces = allUnits.Select((unit, position) =>
        {
            var fate = resultOf.ContainsKey(unit.Hit) ? UnitFate.Shown
                : unreadable.Contains(unit.Hit) ? UnitFate.Unreadable
                : belowRelative.Contains(unit.Hit) ? UnitFate.BelowRelativeFloor
                : beyondActivityCap.Contains(unit.Hit) ? UnitFate.BeyondActivityCap
                : passedSet.Contains(unit.Hit) ? UnitFate.BeyondTopK
                : keptSet.Contains(unit.Hit) ? UnitFate.BelowThreshold
                : UnitFate.BeyondCandidates;
            var overturned = !IsActivityUnit(unit.Hit) && pageInfos.TryGetValue(unit.Hit.Source, out var info) && info.Overturned;
            return new UnitTrace(
                unit.Hit,
                unit.FullTextRank,
                unit.Bm25,
                unit.VectorRank,
                unit.Distance,
                unit.Rrf,
                position + 1,
                rawScoreOf.TryGetValue(unit.Hit, out var raw) ? raw : null,
                fate,
                resultOf.TryGetValue(unit.Hit, out var number) ? number : null,
                unit.GraphOrigin,
                finalScoreOf.TryGetValue(unit.Hit, out var final) ? final : null,
                overturned);
        }).ToList();

        return new RecallTrace(fullText.Method, embeddingStage, rerankerStage, floor, topK, candidates, traces, results, notes, text.TrimEnd());
    }

    /// <summary>A unit after fusion (and optional 1-hop graph expansion), with what each search said about it.</summary>
    private sealed record FusedUnit(
        IndexHit Hit,
        int? FullTextRank,
        double? Bm25,
        int? VectorRank,
        double? Distance,
        double Rrf,
        GraphOrigin GraphOrigin = GraphOrigin.None);

    private sealed record CandidateGroup((string Source, string? Code, string? Text) Key, List<IndexHit> Units, double Score);

    private sealed record LoadedGroup(CandidateGroup Group, PageId? PageId, PageDocument? Document);

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
    /// Selects the top <paramref name="candidates"/> fused units and runs bounded 1-hop graph expansion from up to
    /// <see cref="MaxSeedPages"/> distinct seed pages: chasing superseded seeds to their active replacements and pulling
    /// bounded 1-hop <c>related</c> neighbors under the active <paramref name="filter"/>.
    /// </summary>
    private (List<FusedUnit> AllUnits, List<FusedUnit> Kept) ExpandCandidates(List<FusedUnit> fused, int candidates, IndexFilter filter)
    {
        var allUnits = new List<FusedUnit>(fused);
        var kept = fused.Take(candidates).ToList();
        if (filter.Activities == true || kept.Count == 0)
            return (allUnits, kept);

        var seedPages = new List<string>(MaxSeedPages);
        var seedRrf = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var entry in kept)
        {
            if (IsActivityUnit(entry.Hit))
                continue;
            if (!seedRrf.ContainsKey(entry.Hit.Source))
            {
                if (seedPages.Count < MaxSeedPages)
                {
                    seedPages.Add(entry.Hit.Source);
                    seedRrf[entry.Hit.Source] = entry.Rrf;
                }
            }
            else if (entry.Rrf > seedRrf[entry.Hit.Source])
            {
                seedRrf[entry.Hit.Source] = entry.Rrf;
            }
        }

        if (seedPages.Count == 0)
            return (allUnits, kept);

        var allIndexById = new Dictionary<long, int>(allUnits.Count);
        for (var i = 0; i < allUnits.Count; i++)
            allIndexById[allUnits[i].Hit.Id] = i;
        var keptIds = kept.Select(entry => entry.Hit.Id).ToHashSet();

        void Inject(IndexHit hit, GraphOrigin origin, double fallbackRrf)
        {
            if (!keptIds.Add(hit.Id))
                return;
            if (allIndexById.TryGetValue(hit.Id, out var existingIndex))
            {
                var promoted = allUnits[existingIndex] with { GraphOrigin = origin, Rrf = Math.Max(allUnits[existingIndex].Rrf, fallbackRrf) };
                allUnits[existingIndex] = promoted;
                kept.Add(promoted);
            }
            else
            {
                var added = new FusedUnit(hit, null, null, null, null, fallbackRrf, origin);
                allIndexById[hit.Id] = allUnits.Count;
                allUnits.Add(added);
                kept.Add(added);
            }
        }

        // 1. Active replacement chasing for superseded seed pages.
        var replacementsBySeed = index.ActiveReplacementsOf(seedPages);
        if (replacementsBySeed.Count > 0)
        {
            var replacementRrf = new Dictionary<string, double>(StringComparer.Ordinal);
            var orderedReplacements = new List<string>();
            foreach (var seed in seedPages)
            {
                if (!replacementsBySeed.TryGetValue(seed, out var targets))
                    continue;
                var inheritedRrf = seedRrf[seed] * 0.95;
                foreach (var target in targets)
                {
                    if (!replacementRrf.TryGetValue(target, out var existing))
                    {
                        orderedReplacements.Add(target);
                        replacementRrf[target] = inheritedRrf;
                    }
                    else if (inheritedRrf > existing)
                    {
                        replacementRrf[target] = inheritedRrf;
                    }
                }
            }

            var replacementUnitsByPage = index.UnitsForPages(orderedReplacements, filter)
                .GroupBy(hit => hit.Source, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
            var addedCount = 0;
            foreach (var target in orderedReplacements)
            {
                if (addedCount >= MaxSupersedesUnits)
                    break;
                if (!replacementUnitsByPage.TryGetValue(target, out var units))
                    continue;
                foreach (var hit in units)
                {
                    if (addedCount >= MaxSupersedesUnits)
                        break;
                    if (keptIds.Contains(hit.Id))
                        continue;
                    Inject(hit, GraphOrigin.Supersedes, replacementRrf[target]);
                    addedCount++;
                }
            }
        }

        // 2. Bounded 1-hop related neighbor expansion (only pages not already present in the candidate pool).
        var keptPages = kept
            .Where(entry => !IsActivityUnit(entry.Hit))
            .Select(entry => entry.Hit.Source)
            .ToHashSet(StringComparer.Ordinal);
        var relatedPages = index.RelatedNeighborsOf(seedPages)
            .Where(neighbor => !keptPages.Contains(neighbor))
            .ToList();
        if (relatedPages.Count > 0)
        {
            var minDirectRrf = kept.Min(entry => entry.Rrf);
            var relatedFallbackRrf = minDirectRrf * 0.5;
            var relatedUnitsByPage = index.UnitsForPages(relatedPages, filter)
                .GroupBy(hit => hit.Source, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
            var addedCount = 0;
            foreach (var neighbor in relatedPages)
            {
                if (addedCount >= MaxRelatedUnits)
                    break;
                if (!relatedUnitsByPage.TryGetValue(neighbor, out var units))
                    continue;

                var picked = new List<IndexHit>(2);
                var clue = units.FirstOrDefault(unit => unit.Kind == UnitKinds.Clue);
                if (clue is not null)
                    picked.Add(clue);
                var firstSection = units.FirstOrDefault(unit => unit.Kind == UnitKinds.Section);
                if (firstSection is not null)
                    picked.Add(firstSection);

                foreach (var hit in picked)
                {
                    if (addedCount >= MaxRelatedUnits)
                        break;
                    if (keptIds.Contains(hit.Id))
                        continue;
                    Inject(hit, GraphOrigin.Related, relatedFallbackRrf);
                    addedCount++;
                }
            }
        }

        return (allUnits, kept);
    }

    /// <summary>
    /// When a superseded page cleared the raw relevance floor, lets its active replacement inherit a floor score on its
    /// best unit so queries phrased in legacy vocabulary surface the active design ahead of the overturned page.
    /// </summary>
    private static void ApplyReplacementInheritance(
        List<IndexHit> kept,
        Dictionary<IndexHit, double> baseScoreOf,
        Dictionary<IndexHit, double> workingScoreOf,
        IReadOnlyDictionary<string, IReadOnlyList<string>> activeReplacements,
        double floor,
        double factor)
    {
        if (activeReplacements.Count == 0)
            return;

        var bestRawByPage = new Dictionary<string, double>(StringComparer.Ordinal);
        var bestUnitByPage = new Dictionary<string, IndexHit>(StringComparer.Ordinal);
        foreach (var unit in kept)
        {
            if (IsActivityUnit(unit))
                continue;
            var raw = baseScoreOf[unit];
            if (!bestRawByPage.TryGetValue(unit.Source, out var currentBest) || raw > currentBest)
            {
                bestRawByPage[unit.Source] = raw;
                bestUnitByPage[unit.Source] = unit;
            }
        }

        foreach (var (oldPage, replacements) in activeReplacements)
        {
            if (!bestRawByPage.TryGetValue(oldPage, out var scoreA) || scoreA < floor)
                continue;
            var inherited = scoreA * factor;
            foreach (var replacement in replacements)
            {
                if (bestUnitByPage.TryGetValue(replacement, out var bestUnit) && inherited > workingScoreOf[bestUnit])
                    workingScoreOf[bestUnit] = inherited;
            }
        }
    }

    private static double AdjustmentMultiplier(
        IndexHit unit,
        IReadOnlyDictionary<string, PageGraphInfo> pageInfos,
        bool generalSearch,
        DateOnly currentDay)
    {
        if (!IsActivityUnit(unit))
            return pageInfos.TryGetValue(unit.Source, out var info) && info.Overturned ? OverturnedMultiplier : 1.0;

        if (!generalSearch)
            return 1.0;

        return ActivityTypePrior * ActivityRecencyFactor(unit.Date, currentDay);
    }

    private static double ActivityRecencyFactor(string? dateText, DateOnly currentDay)
    {
        if (dateText is null || !DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return 1.0;
        var ageDays = Math.Max(0, currentDay.DayNumber - date.DayNumber);
        return ActivityRecencyFloor + ActivityRecencySpan * Math.Exp(-Math.Log(2) * ageDays / ActivityHalfLifeDays);
    }

    /// <summary>
    /// Applies Layer 2 gating (relative-to-best cutoff when reranked, and activity slot cap in general searches when
    /// pages qualify), then renders the final results with type/overturned headers and concise <c>Links:</c> hints.
    /// </summary>
    private (
        string Text,
        List<ShownResult> Results,
        HashSet<IndexHit> Unreadable,
        HashSet<IndexHit> BelowRelativeFloor,
        HashSet<IndexHit> BeyondActivityCap)
        SelectAndFormat(
            IReadOnlyList<CandidateGroup> groups,
            int topK,
            bool rerankerAnswered,
            bool generalSearch,
            IReadOnlyDictionary<string, PageGraphInfo> pageInfos,
            IReadOnlyDictionary<string, IReadOnlyList<string>> activeReplacements)
    {
        var unreadable = new HashSet<IndexHit>();
        var belowRelative = new HashSet<IndexHit>();
        var beyondActivityCap = new HashSet<IndexHit>();
        var readable = new List<LoadedGroup>(groups.Count);

        foreach (var group in groups)
        {
            var sample = group.Units[0];
            if (IsActivityUnit(sample))
            {
                readable.Add(new LoadedGroup(group, null, null));
                continue;
            }

            if (!PageId.TryParse(sample.Source, out var id, out _) || PageDocument.TryLoad(config.MemoryPath, id) is not { } document)
            {
                unreadable.UnionWith(group.Units);
                continue;
            }

            readable.Add(new LoadedGroup(group, id, document));
        }

        var relativePassed = new List<LoadedGroup>(readable.Count);
        if (readable.Count > 0)
        {
            var bestScore = readable[0].Group.Score;
            var relativeThreshold = bestScore * RelativeCutoffRatio;
            for (var i = 0; i < readable.Count; i++)
            {
                var item = readable[i];
                // Only apply multiplicative relative cutoff on positive scores so negative-logit rerankers do not invert the threshold.
                if (rerankerAnswered && bestScore > 0 && i > 0 && item.Group.Score < relativeThreshold)
                {
                    belowRelative.UnionWith(item.Group.Units);
                    continue;
                }

                relativePassed.Add(item);
            }
        }

        var applyActivityCap = generalSearch && relativePassed.Any(item => item.Document is not null);
        var maxActivities = Math.Max(1, topK / 2);
        var shownActivities = 0;
        var selected = new List<LoadedGroup>(Math.Min(topK, relativePassed.Count));
        foreach (var item in relativePassed)
        {
            if (selected.Count == topK)
                break;
            if (applyActivityCap && item.Document is null && shownActivities >= maxActivities)
            {
                beyondActivityCap.UnionWith(item.Group.Units);
                continue;
            }

            selected.Add(item);
            if (item.Document is null)
                shownActivities++;
        }

        var shownPageIds = selected.Where(item => item.PageId is not null).Select(item => item.PageId!.ToString()).ToHashSet(StringComparer.Ordinal);
        var directLinks = index.DirectLinksOf(shownPageIds);

        var text = new StringBuilder();
        var results = new List<ShownResult>(selected.Count);
        foreach (var item in selected)
        {
            var group = item.Group;
            var sample = group.Units[0];
            if (sample.Kind == UnitKinds.Activity)
            {
                results.Add(new ShownResult(results.Count + 1, [.. group.Units]));
                var label = sample.Workstream.Length > 0 ? $", workstream {sample.Workstream}" : "";
                var machine = sample.Section is null ? "" : $" ({sample.Section})";
                text.Append(CultureInfo.InvariantCulture, $"\n[{results.Count}] activity {sample.Date}{machine} [{sample.Code}]{label}\n{sample.Text}\n");
                continue;
            }

            if (sample.Kind == UnitKinds.Summary)
            {
                results.Add(new ShownResult(results.Count + 1, [.. group.Units]));
                text.Append(CultureInfo.InvariantCulture, $"\n[{results.Count}] summary, {sample.Section} starting {sample.Date}\n{sample.Text}\n");
                continue;
            }

            var id = item.PageId!;
            var pageKey = id.ToString();
            var document = item.Document!;
            var matched = group.Units.Select(hit => hit.Section).OfType<string>().ToHashSet(StringComparer.Ordinal);
            var sections = matched.Count > 0
                ? document.Sections.Where(section => matched.Contains(section.Name)).ToList()
                : document.Sections.Take(PageLevelSections).ToList();
            var others = document.Sections.Except(sections).Select(section => section.Name).ToList();
            var changed = index.SectionChanges(id);

            var info = pageInfos.GetValueOrDefault(pageKey);
            var pageType = info?.PageType ?? (PageDocument.IsPageType(document.Header.Type) ? document.Header.Type : null);
            var overturned = info?.Overturned ?? document.HasOverturnedSection;
            var replacements = activeReplacements.GetValueOrDefault(pageKey) ?? [];

            results.Add(new ShownResult(
                results.Count + 1,
                [.. group.Units],
                document.Header.Title,
                [.. sections.Select(section => section.Name)],
                pageType,
                overturned,
                replacements));

            var annotation = FormatPageAnnotation(pageType, overturned, replacements);
            text.Append(CultureInfo.InvariantCulture, $"\n[{results.Count}] page {id}: {document.Header.Title}{annotation}\n");
            if (document.Header.First is { } first && document.Header.Last is { } last)
                text.Append(CultureInfo.InvariantCulture, $"Dates: {first:yyyy-MM-dd} to {last:yyyy-MM-dd}\n");
            foreach (var section in sections)
                text.Append(CultureInfo.InvariantCulture, $"### {section.Name} [{section.Code}]{SectionDates.Note(changed, section.Name)}\n{section.Body}\n");
            if (others.Count > 0)
                text.Append(CultureInfo.InvariantCulture, $"Other sections: {string.Join(", ", others)}\n");

            if (FormatLinksFooter(directLinks.GetValueOrDefault(pageKey), shownPageIds) is { } linksLine)
                text.Append(CultureInfo.InvariantCulture, $"Links: {linksLine}\n");
        }

        var body = results.Count == 0
            ? NoMemory
            : $"{results.Count} result(s). Short codes in brackets identify sections for write_page and activities for write_recent; read a whole page with recall page=<page ID>.\n{text}";
        return (body, results, unreadable, belowRelative, beyondActivityCap);
    }

    private static string FormatPageAnnotation(string? pageType, bool overturned, IReadOnlyList<string> replacements)
    {
        var tags = new List<string>(2);
        if (pageType is not null)
            tags.Add(pageType);
        if (overturned)
            tags.Add(replacements.Count > 0 ? $"overturned -> {string.Join(", ", replacements)}" : "overturned");
        return tags.Count == 0 ? "" : $" ({string.Join(", ", tags)})";
    }

    private static string? FormatLinksFooter(IReadOnlyList<PageNeighbor>? neighbors, HashSet<string> shownPageIds)
    {
        if (neighbors is null || neighbors.Count == 0)
            return null;
        var entries = new List<string>(MaxLinkHints);
        foreach (var neighbor in neighbors)
        {
            if (shownPageIds.Contains(neighbor.Page))
                continue;
            var meta = new List<string>(2);
            if (neighbor.PageType is not null)
                meta.Add(neighbor.PageType);
            if (neighbor.Overturned)
                meta.Add("overturned");
            var suffix = meta.Count == 0 ? "" : $" ({string.Join(", ", meta)})";
            var directionLabel = neighbor.Direction switch
            {
                PageLinkDirection.SupersededBy => "superseded by",
                PageLinkDirection.Supersedes => "supersedes",
                _ => "related",
            };
            entries.Add($"{directionLabel} {neighbor.Page}{suffix}");
            if (entries.Count == MaxLinkHints)
                break;
        }

        return entries.Count == 0 ? null : string.Join("; ", entries);
    }

    private static bool IsActivityUnit(IndexHit hit) => hit.Kind is UnitKinds.Activity or UnitKinds.Summary;

    /// <summary>Hits of one activity or summary are one result each; the hits of a page's sections share the page's result.</summary>
    private static (string Source, string? Code, string? Text) GroupKey(IndexHit hit) =>
        IsActivityUnit(hit) ? (hit.Source, hit.Code, hit.Text) : (hit.Source, null, null);
}
