using System.Globalization;
using TacitMemory.Core.Index;
using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Retrieval;

/// <summary>
/// The recall trace of the management API: checks a request as <c>recall</c> checks one, and turns the
/// pipeline's stages into the report the Web interface reads.
/// </summary>
internal static class RecallTraceRun
{
    /// <summary>The search <paramref name="request"/> asks for, or every problem with it at once, in the memory language.</summary>
    /// <exception cref="TacitException"><see cref="TacitError.RequestInvalid"/> with one problem per parameter at fault.</exception>
    public static RecallSearch Check(RecallTraceRequest request, MemoryLanguage language)
    {
        var (search, faults) = RecallSearch.Check(new SearchRequest(
            request.Query, request.Workstream, request.From, request.To, request.Kind,
            new RetrievalOverrides(request.MinRerankScore, request.TopK, request.Candidates)));
        if (search is not null)
            return search;
        var problems = faults.Select(fault => new ConfigProblem(fault.Parameter, Describe(fault, language))).ToList();
        throw new TacitException(TacitError.RequestInvalid, string.Join(" ", problems.Select(problem => problem.Message))) { Problems = problems };
    }

    /// <summary>A problem of the request, as the Web interface shows it beside the field.</summary>
    private static string Describe(SearchProblem problem, MemoryLanguage language) => problem.Fault switch
    {
        SearchFault.QueryMissing => RequestMessages.TraceQueryMissing.In(language),
        SearchFault.DateInvalid => RequestMessages.TraceDateInvalid.Format(language, problem.Given!.Trim()),
        SearchFault.DatesReversed => RequestMessages.TraceDatesReversed.In(language),
        SearchFault.KindInvalid => RequestMessages.TraceKindInvalid.Format(language, problem.Given),
        _ => RequestMessages.TraceValueOutOfRange.Format(
            language, problem.Parameter, problem.Min.ToString(CultureInfo.InvariantCulture), problem.Max.ToString(CultureInfo.InvariantCulture)),
    };

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

}
