using System.Globalization;
using TacitMemory.Core.Configuration;
using TacitMemory.Core.Index;

namespace TacitMemory.Core.Retrieval;

/// <summary>A search as <c>recall</c> and the recall trace take it: every parameter as given, before it is checked.</summary>
internal sealed record SearchRequest(string? Query, string? Workstream, string? From, string? To, string? Kind, RetrievalOverrides Overrides);

/// <summary>What is wrong with one parameter of a search request.</summary>
internal enum SearchFault
{
    QueryMissing,
    DateInvalid,
    DatesReversed,
    KindInvalid,
    OutOfRange,
}

/// <summary>
/// One problem of a search request: the parameter at fault, what is wrong with it, the value as given and, for a value out
/// of range, the range the config accepts. The caller words it for its reader.
/// </summary>
internal sealed record SearchProblem(string Parameter, SearchFault Fault, string? Given = null, double Min = 0, double Max = 0);

/// <summary>
/// A search checked and normalized: the query as given, the filter with the workstream trimmed and lowercased, and the
/// retrieval values that replace the config's for this search alone. <c>recall</c> and the recall trace both check a request
/// here, so the same parameters are accepted or refused by both.
/// </summary>
internal sealed record RecallSearch(string Query, IndexFilter Filter, RetrievalOverrides Overrides)
{
    /// <summary>The search <paramref name="request"/> asks for, or every problem with it, in parameter order.</summary>
    public static (RecallSearch? Search, IReadOnlyList<SearchProblem> Problems) Check(SearchRequest request)
    {
        var problems = new List<SearchProblem>();
        if (string.IsNullOrWhiteSpace(request.Query))
            problems.Add(new SearchProblem("query", SearchFault.QueryMissing));
        var (from, to) = Dates(request.From, request.To, problems);

        var kind = string.IsNullOrWhiteSpace(request.Kind) ? null : request.Kind.Trim();
        if (kind is not (null or ToolValues.RecallKinds.Pages or ToolValues.RecallKinds.Activities))
            problems.Add(new SearchProblem("kind", SearchFault.KindInvalid, kind));

        InRange(request.Overrides.MinRerankScore, "minRerankScore", ConfigKeys.MinRerankScore, problems);
        InRange(request.Overrides.TopK, "topK", ConfigKeys.TopK, problems);
        InRange(request.Overrides.Candidates, "candidates", ConfigKeys.Candidates, problems);
        if (problems.Count > 0)
            return (null, problems);

        var workstream = string.IsNullOrWhiteSpace(request.Workstream) ? null : request.Workstream.Trim().ToLowerInvariant();
        var activities = kind switch { ToolValues.RecallKinds.Activities => true, ToolValues.RecallKinds.Pages => false, _ => (bool?)null };
        return (new RecallSearch(request.Query!, new IndexFilter(workstream, from, to, activities), request.Overrides), []);
    }

    /// <summary>The date range <paramref name="from"/> to <paramref name="to"/>, each written yyyy-MM-dd; a problem for each that is not, or when they are reversed.</summary>
    public static (DateOnly? From, DateOnly? To) Dates(string? from, string? to, List<SearchProblem> problems)
    {
        var first = Date(from, "from", problems);
        var last = Date(to, "to", problems);
        if (first > last)
            problems.Add(new SearchProblem("to", SearchFault.DatesReversed));
        return (first, last);
    }

    private static DateOnly? Date(string? raw, string name, List<SearchProblem> problems)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        if (DateOnly.TryParseExact(raw.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;
        problems.Add(new SearchProblem(name, SearchFault.DateInvalid, raw));
        return null;
    }

    /// <summary>Adds a problem when <paramref name="value"/> lies outside the range the config accepts for <paramref name="key"/>.</summary>
    private static void InRange(double? value, string name, ConfigEntry range, List<SearchProblem> problems)
    {
        if (value is { } given && (given < range.Min || given > range.Max))
            problems.Add(new SearchProblem(name, SearchFault.OutOfRange, null, range.Min!.Value, range.Max!.Value));
    }
}
