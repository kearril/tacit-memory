using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using TacitMemory.Core;
using TacitMemory.Core.Retrieval;

namespace TacitMemory.Benchmarks;

internal sealed record RecallBenchmarkReport(
    DateTimeOffset GeneratedAt,
    string CorpusPath,
    string? EmbeddingModel,
    string? RerankerModel,
    RecallSummary Summary,
    StageFunnel Funnel,
    IReadOnlyList<CategorySummary> Categories,
    IReadOnlyList<QueryOutcome> Queries);

internal sealed record RecallSummary(
    int PositiveCount,
    int NegativeCount,
    double RecallAt5,
    double HitAt1Rate,
    double AllHitAt5Rate,
    double Mrr,
    int NegativeFalsePositives,
    double AbstentionRate,
    double P50Ms,
    double P95Ms);

internal sealed record CategorySummary(
    string Category,
    int PositiveCount,
    int NegativeCount,
    double RecallAt5,
    double Mrr,
    int NegativeFalsePositives);

internal sealed record StageFunnel(
    int PositiveCount,
    int FullTextHits,
    int VectorHits,
    int FusedHits,
    int RerankedHits,
    int MissedNotRecalled,
    int MissedBeyondCandidates,
    int MissedBelowThreshold,
    int MissedBeyondTopK,
    int MissedUnreadable);

internal sealed record ResultScoreSummary(
    int Rank,
    string Label,
    string Kind,
    bool IsTarget,
    double? RerankScore,
    double? CosineSimilarity,
    int? FullTextRank,
    int? VectorRank,
    double? Rrf);

internal sealed record QueryOutcome(
    string Id,
    string Category,
    string Query,
    bool Negative,
    int TargetCount,
    int HitCountAt5,
    double RecallAt5,
    int? FirstHitRank,
    double ReciprocalRank,
    int ResultCount,
    bool InFullText,
    bool InVector,
    bool InFused,
    bool InReranked,
    string? MissFate,
    double ElapsedMs,
    double? EmbeddingMs,
    double? RerankerMs,
    IReadOnlyList<string> ReturnedTop,
    IReadOnlyList<ResultScoreSummary>? TopScores = null);

internal static class RecallEvaluation
{
    private const string FateNotRecalled = "not_recalled";
    private const string FateBeyondCandidates = "beyond_candidates";
    private const string FateBelowThreshold = "below_threshold";
    private const string FateBelowRelativeFloor = "below_relative_floor";
    private const string FateBeyondActivityCap = "beyond_activity_cap";
    private const string FateBeyondTopK = "beyond_top_k";
    private const string FateUnreadable = "unreadable";
    private const string FateFalsePositive = "false_positive";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static QueryOutcome EvaluateQuery(QueryCase query, RecallTraceReport trace, double elapsedMs)
    {
        var returnedTop = trace.Results.Take(5).Select(DescribeResult).ToList();
        var topScores = BuildTopScores(query, trace);
        if (query.Negative)
        {
            return new QueryOutcome(
                query.Id, query.Category, query.Query, Negative: true,
                TargetCount: 0, HitCountAt5: 0, RecallAt5: 0,
                FirstHitRank: null, ReciprocalRank: 0,
                ResultCount: trace.Results.Count,
                InFullText: false, InVector: false, InFused: false, InReranked: false,
                MissFate: trace.Results.Count > 0 ? FateFalsePositive : null,
                ElapsedMs: elapsedMs,
                EmbeddingMs: trace.Embedding.Milliseconds,
                RerankerMs: trace.Reranker.Milliseconds,
                ReturnedTop: returnedTop,
                TopScores: topScores);
        }

        var top5 = trace.Results.Take(5).ToList();
        var hitCountAt5 = query.Targets.Count(target => top5.Any(target.MatchesResult));
        int? firstHitRank = null;
        foreach (var result in trace.Results)
        {
            if (query.Targets.Any(target => target.MatchesResult(result)))
            {
                firstHitRank = result.Number;
                break;
            }
        }

        var recallAt5 = query.Targets.Count == 0 ? 0 : (double)hitCountAt5 / query.Targets.Count;
        var rr = firstHitRank is { } rank ? 1.0 / rank : 0.0;

        var matchingUnits = trace.Units
            .Where(unit => query.Targets.Any(target => target.MatchesUnit(unit)))
            .ToList();
        var candidates = (int)trace.Settings.Candidates.Value;
        var inFullText = matchingUnits.Any(unit => unit.FullTextRank is not null);
        var inVector = matchingUnits.Any(unit => unit.VectorRank is not null);
        var inFused = matchingUnits.Any(unit => unit.FusedRank <= candidates);
        // Stage 4 reflects whether a target reached the final shown results (UnitFate.Shown within configured topK).
        var inReranked = firstHitRank is not null;
        var missFate = inReranked ? null : ClassifyMiss(matchingUnits);

        return new QueryOutcome(
            query.Id, query.Category, query.Query, Negative: false,
            TargetCount: query.Targets.Count,
            HitCountAt5: hitCountAt5,
            RecallAt5: recallAt5,
            FirstHitRank: firstHitRank,
            ReciprocalRank: rr,
            ResultCount: trace.Results.Count,
            InFullText: inFullText,
            InVector: inVector,
            InFused: inFused,
            InReranked: inReranked,
            MissFate: missFate,
            ElapsedMs: elapsedMs,
            EmbeddingMs: trace.Embedding.Milliseconds,
            RerankerMs: trace.Reranker.Milliseconds,
            ReturnedTop: returnedTop,
            TopScores: topScores);
    }

    private static List<ResultScoreSummary> BuildTopScores(QueryCase query, RecallTraceReport trace)
    {
        var unitsByResult = trace.Units
            .Where(unit => unit.Result is not null)
            .GroupBy(unit => unit.Result!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());
        var summaries = new List<ResultScoreSummary>();
        foreach (var result in trace.Results.Take(5))
        {
            unitsByResult.TryGetValue(result.Number, out var units);
            var bestRerank = units?.Where(u => u.RerankScore is not null).Select(u => u.RerankScore!.Value).DefaultIfEmpty().Max();
            var hasRerank = units?.Any(u => u.RerankScore is not null) == true;
            var bestDist = units?.Where(u => u.Distance is not null).Select(u => u.Distance!.Value).DefaultIfEmpty().Min();
            var hasDist = units?.Any(u => u.Distance is not null) == true;
            var bestFt = units?.Where(u => u.FullTextRank is not null).Select(u => u.FullTextRank!.Value).DefaultIfEmpty().Min();
            var hasFt = units?.Any(u => u.FullTextRank is not null) == true;
            var bestVec = units?.Where(u => u.VectorRank is not null).Select(u => u.VectorRank!.Value).DefaultIfEmpty().Min();
            var hasVec = units?.Any(u => u.VectorRank is not null) == true;
            var bestRrf = units?.Max(u => u.Rrf);
            var isTarget = !query.Negative && query.Targets.Any(target => target.MatchesResult(result));
            summaries.Add(new ResultScoreSummary(
                result.Number,
                DescribeResult(result),
                result.Kind,
                isTarget,
                hasRerank ? bestRerank : null,
                hasDist ? 1.0 - bestDist : null,
                hasFt ? bestFt : null,
                hasVec ? bestVec : null,
                bestRrf));
        }

        return summaries;
    }

    public static RecallBenchmarkReport Summarize(
        string corpusPath,
        string? embeddingModel,
        string? rerankerModel,
        IReadOnlyList<QueryOutcome> outcomes)
    {
        var positives = outcomes.Where(outcome => !outcome.Negative).ToList();
        var negatives = outcomes.Where(outcome => outcome.Negative).ToList();
        var latencies = outcomes.Select(outcome => outcome.ElapsedMs).Order().ToList();

        var summary = new RecallSummary(
            PositiveCount: positives.Count,
            NegativeCount: negatives.Count,
            RecallAt5: positives.Count == 0 ? 0 : positives.Average(outcome => outcome.RecallAt5),
            HitAt1Rate: positives.Count == 0 ? 0 : positives.Average(outcome => outcome.FirstHitRank == 1 ? 1.0 : 0.0),
            AllHitAt5Rate: positives.Count == 0 ? 0 : positives.Average(outcome => outcome.HitCountAt5 == outcome.TargetCount ? 1.0 : 0.0),
            Mrr: positives.Count == 0 ? 0 : positives.Average(outcome => outcome.ReciprocalRank),
            NegativeFalsePositives: negatives.Count(outcome => outcome.ResultCount > 0),
            AbstentionRate: negatives.Count == 0 ? 1.0 : negatives.Average(outcome => outcome.ResultCount == 0 ? 1.0 : 0.0),
            P50Ms: Percentile(latencies, 0.50),
            P95Ms: Percentile(latencies, 0.95));

        var funnel = new StageFunnel(
            PositiveCount: positives.Count,
            FullTextHits: positives.Count(outcome => outcome.InFullText),
            VectorHits: positives.Count(outcome => outcome.InVector),
            FusedHits: positives.Count(outcome => outcome.InFused),
            RerankedHits: positives.Count(outcome => outcome.InReranked),
            MissedNotRecalled: positives.Count(outcome => outcome.MissFate == FateNotRecalled),
            MissedBeyondCandidates: positives.Count(outcome => outcome.MissFate == FateBeyondCandidates),
            MissedBelowThreshold: positives.Count(outcome => outcome.MissFate == FateBelowThreshold),
            MissedBeyondTopK: positives.Count(outcome => outcome.MissFate == FateBeyondTopK),
            MissedUnreadable: positives.Count(outcome => outcome.MissFate == FateUnreadable));

        var categories = outcomes
            .GroupBy(outcome => outcome.Category, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var catPos = group.Where(outcome => !outcome.Negative).ToList();
                var catNeg = group.Where(outcome => outcome.Negative).ToList();
                return new CategorySummary(
                    group.Key,
                    catPos.Count,
                    catNeg.Count,
                    catPos.Count == 0 ? 0 : catPos.Average(outcome => outcome.RecallAt5),
                    catPos.Count == 0 ? 0 : catPos.Average(outcome => outcome.ReciprocalRank),
                    catNeg.Count(outcome => outcome.ResultCount > 0));
            })
            .ToList();

        return new RecallBenchmarkReport(
            DateTimeOffset.UtcNow,
            corpusPath,
            embeddingModel,
            rerankerModel,
            summary,
            funnel,
            categories,
            outcomes);
    }

    public static void PrintReport(TextWriter output, RecallBenchmarkReport report, RecallBenchmarkReport? baseline = null)
    {
        var s = report.Summary;
        output.WriteLine("=== Recall Benchmark Summary ===");
        output.WriteLine($"Corpus:    {report.CorpusPath}");
        output.WriteLine($"Models:    embedding={report.EmbeddingModel ?? "(none)"} | reranker={report.RerankerModel ?? "(none)"}");
        output.WriteLine(FormatMetric("Recall@5", $"{s.RecallAt5:P1}", baseline is null ? null : DeltaPercent(s.RecallAt5 - baseline.Summary.RecallAt5))
            + $" (hit@1={s.HitAt1Rate:P1}, all@5={s.AllHitAt5Rate:P1}, n={s.PositiveCount})");
        output.WriteLine(FormatMetric("MRR", $"{s.Mrr:F3}", baseline is null ? null : DeltaNumber(s.Mrr - baseline.Summary.Mrr, "F3")));
        output.WriteLine(FormatMetric("Neg FP", $"{s.NegativeFalsePositives}/{s.NegativeCount}", baseline is null ? null : DeltaInt(s.NegativeFalsePositives - baseline.Summary.NegativeFalsePositives))
            + $" (abstention={s.AbstentionRate:P1})");
        output.WriteLine($"Latency:   P50={s.P50Ms:F1} ms | P95={s.P95Ms:F1} ms");

        var f = report.Funnel;
        output.WriteLine();
        output.WriteLine("=== Stage Funnel (Positive Queries) ===");
        output.WriteLine($"FullText: {f.FullTextHits}/{f.PositiveCount} | Vector: {f.VectorHits}/{f.PositiveCount} | Fused: {f.FusedHits}/{f.PositiveCount} | Shown: {f.RerankedHits}/{f.PositiveCount}");
        output.WriteLine($"Misses:   not_recalled={f.MissedNotRecalled} | beyond_candidates={f.MissedBeyondCandidates} | below_threshold={f.MissedBelowThreshold} | beyond_top_k={f.MissedBeyondTopK} | unreadable={f.MissedUnreadable}");

        output.WriteLine();
        output.WriteLine("=== By Category ===");
        foreach (var cat in report.Categories)
        {
            if (cat.PositiveCount > 0)
                output.WriteLine($"  {cat.Category,-16} pos={cat.PositiveCount,-2} Recall@5={cat.RecallAt5:P1}  MRR={cat.Mrr:F3}");
            if (cat.NegativeCount > 0)
                output.WriteLine($"  {cat.Category,-16} neg={cat.NegativeCount,-2} FP={cat.NegativeFalsePositives}/{cat.NegativeCount}");
        }

        var failures = report.Queries.Where(query => (!query.Negative && query.HitCountAt5 < query.TargetCount) || (query.Negative && query.ResultCount > 0)).ToList();
        if (failures.Count > 0)
        {
            output.WriteLine();
            output.WriteLine($"=== Missed / Imperfect Queries ({failures.Count}) ===");
            foreach (var fail in failures)
            {
                var status = fail.Negative
                    ? $"{FateFalsePositive}(count={fail.ResultCount})"
                    : $"hit={fail.HitCountAt5}/{fail.TargetCount}, rank={fail.FirstHitRank?.ToString(CultureInfo.InvariantCulture) ?? "-"}, fate={fail.MissFate ?? "partial"}";
                output.WriteLine($"  [{fail.Id}] ({fail.Category}) {status} | {fail.Query}");
                if (fail.ReturnedTop.Count > 0)
                    output.WriteLine($"      returned: {string.Join(", ", fail.ReturnedTop)}");
            }
        }

        if (baseline is not null)
            PrintBaselineChanges(output, report, baseline);
    }

    public static double Percentile(IReadOnlyList<double> sorted, double quantile)
    {
        if (sorted.Count == 0)
            return 0;
        var index = Math.Clamp((int)Math.Ceiling(quantile * sorted.Count) - 1, 0, sorted.Count - 1);
        return sorted[index];
    }

    private static void PrintBaselineChanges(TextWriter output, RecallBenchmarkReport current, RecallBenchmarkReport baseline)
    {
        var previous = baseline.Queries.ToDictionary(query => query.Id, StringComparer.Ordinal);
        var changes = new List<string>();
        foreach (var now in current.Queries)
        {
            if (!previous.TryGetValue(now.Id, out var before))
                continue;
            if (now.Negative)
            {
                if (now.ResultCount != before.ResultCount && (now.ResultCount == 0 || before.ResultCount == 0))
                {
                    var direction = now.ResultCount == 0 ? "IMPROVED" : "REGRESSED";
                    changes.Add($"  {direction,-9} [{now.Id}] negative results {before.ResultCount} -> {now.ResultCount} | {now.Query}");
                }
                continue;
            }

            if (Math.Abs(now.RecallAt5 - before.RecallAt5) > 1e-6 || now.FirstHitRank != before.FirstHitRank)
            {
                var better = now.RecallAt5 > before.RecallAt5 || (Math.Abs(now.RecallAt5 - before.RecallAt5) <= 1e-6 && now.ReciprocalRank > before.ReciprocalRank);
                var direction = better ? "IMPROVED" : "REGRESSED";
                changes.Add(
                    $"  {direction,-9} [{now.Id}] recall {before.RecallAt5:P0}->{now.RecallAt5:P0}, rank {RankText(before.FirstHitRank)}->{RankText(now.FirstHitRank)} | {now.Query}");
            }
        }

        if (changes.Count == 0)
            return;
        output.WriteLine();
        output.WriteLine($"=== Baseline Diff ({changes.Count} changed) ===");
        foreach (var line in changes)
            output.WriteLine(line);
    }

    private static string ClassifyMiss(IReadOnlyList<TraceUnit> matchingUnits)
    {
        if (matchingUnits.Count == 0)
            return FateNotRecalled;
        var best = matchingUnits.MaxBy(unit => FateRank(unit.Fate).Order)!;
        return FateRank(best.Fate).Label;
    }

    private static (int Order, string Label) FateRank(UnitFate fate) => fate switch
    {
        UnitFate.BeyondTopK => (6, FateBeyondTopK),
        UnitFate.BeyondActivityCap => (5, FateBeyondActivityCap),
        UnitFate.BelowRelativeFloor => (4, FateBelowRelativeFloor),
        UnitFate.BelowThreshold => (3, FateBelowThreshold),
        UnitFate.BeyondCandidates => (2, FateBeyondCandidates),
        UnitFate.Unreadable => (1, FateUnreadable),
        _ => (0, FateNotRecalled),
    };

    private static string DescribeResult(TraceResult result) => result.Kind switch
    {
        "page" => $"{result.Page}({string.Join("+", result.Sections.Select(section => section.Name))})",
        "activity" => $"activity:{result.Date}[{result.Code}]",
        "summary" => $"summary:{result.Date}({result.Period})",
        _ => result.Kind,
    };

    private static string FormatMetric(string name, string value, string? delta) =>
        delta is null ? $"{name,-10} {value}" : $"{name,-10} {value} ({delta})";

    private static string DeltaPercent(double delta) =>
        delta >= 0 ? $"+{delta:P1}" : $"{delta:P1}";

    private static string DeltaNumber(double delta, string format) =>
        delta >= 0 ? $"+{delta.ToString(format, CultureInfo.InvariantCulture)}" : delta.ToString(format, CultureInfo.InvariantCulture);

    private static string DeltaInt(int delta) =>
        delta >= 0 ? $"+{delta.ToString(CultureInfo.InvariantCulture)}" : delta.ToString(CultureInfo.InvariantCulture);

    private static string RankText(int? rank) => rank?.ToString(CultureInfo.InvariantCulture) ?? "-";
}
