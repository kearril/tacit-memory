using System.Globalization;
using TacitMemory.Core.Configuration;
using TacitMemory.Core.Index;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Pages;
using TacitMemory.Core.Tools;

namespace TacitMemory.Core.Retrieval;

/// <summary>
/// The recall trace of the management API: checks a request by <c>recall</c>'s rules and the config's ranges, and turns the
/// pipeline's stages into the report the Web interface reads.
/// </summary>
internal static class RecallTraceRun
{
    /// <summary>The search a request asks for.</summary>
    public sealed record Search(string Query, IndexFilter Filter, RetrievalOverrides Overrides);

    /// <summary>The search <paramref name="request"/> asks for, or every problem with it at once, in the memory language.</summary>
    /// <exception cref="TacitException"><see cref="TacitError.RequestInvalid"/> with one problem per parameter at fault.</exception>
    public static Search Check(RecallTraceRequest request, MemoryLanguage language)
    {
        var problems = new List<ConfigProblem>();
        if (string.IsNullOrWhiteSpace(request.Query))
            problems.Add(new ConfigProblem("query", ManagementApiMessages.TraceQueryMissing.In(language)));
        var from = Date(request.From, "from", language, problems);
        var to = Date(request.To, "to", language, problems);
        if (from > to)
            problems.Add(new ConfigProblem("to", ManagementApiMessages.TraceDatesReversed.In(language)));

        var kind = string.IsNullOrWhiteSpace(request.Kind) ? null : request.Kind.Trim();
        if (kind is not (null or ToolValues.RecallKinds.Pages or ToolValues.RecallKinds.Activities))
            problems.Add(new ConfigProblem("kind", ManagementApiMessages.TraceKindInvalid.Format(language, kind)));

        InRange(request.MinRerankScore, "minRerankScore", "retrieval.minRerankScore", language, problems);
        InRange(request.TopK, "topK", "retrieval.topK", language, problems);
        InRange(request.Candidates, "candidates", "retrieval.candidates", language, problems);
        if (problems.Count > 0)
            throw new TacitException(TacitError.RequestInvalid, string.Join(" ", problems.Select(problem => problem.Message))) { Problems = problems };

        var workstream = string.IsNullOrWhiteSpace(request.Workstream) ? null : request.Workstream.Trim().ToLowerInvariant();
        var activities = kind switch { ToolValues.RecallKinds.Activities => true, ToolValues.RecallKinds.Pages => false, _ => (bool?)null };
        return new Search(
            request.Query!,
            new IndexFilter(workstream, from, to, activities),
            new RetrievalOverrides(request.MinRerankScore, request.TopK, request.Candidates));
    }

    /// <summary>The report of a trace run under <paramref name="config"/>.</summary>
    public static RecallTraceReport Report(RecallTrace trace, TacitConfig config)
    {
        var retrieval = config.Retrieval;
        var settings = new TraceSettings(
            new TraceSetting(trace.MinRerankScore, retrieval.MinRerankScore ?? RecallPipeline.DefaultMinRerankScore),
            new TraceSetting(trace.TopK, retrieval.TopK),
            new TraceSetting(trace.Candidates, retrieval.Candidates));
        var fullText = trace.Units.Count(unit => unit.FullTextRank is not null);
        var vector = trace.Units.Count(unit => unit.VectorRank is not null);
        var reranked = trace.Units.Count(unit => unit.RerankScore is not null);
        var passed = trace.Units.Count(unit => unit.Fate is UnitFate.Shown or UnitFate.BeyondTopK or UnitFate.Unreadable);
        return new RecallTraceReport(
            settings,
            trace.FullText,
            Model(trace.Embedding),
            Model(trace.Reranker),
            new TraceCounts(fullText, vector, trace.Units.Count, reranked, passed, trace.Results.Count),
            [.. trace.Results.Select(Result)],
            trace.Text,
            trace.Notes,
            [.. trace.Units.Select(Unit)]);
    }

    private static TraceModel Model(ModelStage stage) =>
        new(stage.Configured, stage.Answered, stage.CoolingDown, stage.Elapsed?.TotalMilliseconds);

    private static TraceResult Result(ShownResult result)
    {
        var sample = result.Units[0];
        return sample.Kind switch
        {
            UnitKinds.Activity => new TraceResult(result.Number, "activity", null, null, sample.Date, sample.Code, null, sample.Workstream, [], sample.Text),
            UnitKinds.Summary => new TraceResult(result.Number, "summary", null, null, sample.Date, null, sample.Section, sample.Workstream, [], sample.Text),
            _ => new TraceResult(result.Number, "page", sample.Source, result.Title, null, null, null, sample.Workstream, result.Sections ?? [], null),
        };
    }

    private static TraceUnit Unit(UnitTrace unit) => new(
        unit.Hit.Kind,
        unit.Hit.Source,
        unit.Hit.Section,
        unit.Hit.Code,
        unit.Hit.Kind is UnitKinds.Activity or UnitKinds.Summary ? unit.Hit.Date : null,
        unit.Hit.Workstream,
        unit.Hit.Text,
        unit.FullTextRank,
        unit.Bm25,
        unit.VectorRank,
        unit.Distance,
        unit.Rrf,
        unit.FusedRank,
        unit.RerankScore,
        unit.Fate,
        unit.Result);

    private static DateOnly? Date(string? raw, string name, MemoryLanguage language, List<ConfigProblem> problems)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        if (DateOnly.TryParseExact(raw.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;
        problems.Add(new ConfigProblem(name, ManagementApiMessages.TraceDateInvalid.Format(language, raw.Trim())));
        return null;
    }

    /// <summary>Adds a problem when <paramref name="value"/> lies outside the range the config accepts for <paramref name="key"/>.</summary>
    private static void InRange(double? value, string name, string key, MemoryLanguage language, List<ConfigProblem> problems)
    {
        var range = ConfigSchema.Keys.Single(entry => entry.Path == key);
        if (value is { } given && (given < range.Min || given > range.Max))
        {
            problems.Add(new ConfigProblem(
                name,
                ManagementApiMessages.TraceValueOutOfRange.Format(
                    language, name, range.Min!.Value.ToString(CultureInfo.InvariantCulture), range.Max!.Value.ToString(CultureInfo.InvariantCulture))));
        }
    }
}
