using System.Globalization;
using System.Text.RegularExpressions;
using TacitMemory.Core.Index;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Pages;
using TacitMemory.Core.Persona;
using TacitMemory.Core.Tools;

namespace TacitMemory.Core.Retrieval;

/// <summary>
/// The <c>recall</c> tool: reads one page by ID, reads the persona or its domain rules, returns a workstream's brief when given only a
/// workstream, or searches pages and activities by full text and by vector, merges the two with reciprocal rank fusion, reranks the
/// merged units and returns the best sections, merged per page. The persona is never searched. It never changes a file.
/// </summary>
internal sealed class RecallTool(TacitConfig config, MemoryIndex index, MemoryReader reader)
{

    /// <summary>The answer when nothing matches; also a plain result, not an error.</summary>
    public const string NoMemory = RecallPipeline.NoMemory;

    /// <summary>Every parameter <c>recall</c> takes, in the order the next step lists them.</summary>
    private static readonly string[] Parameters = ["query", "page", "workstream", "from", "to", "kind", "domain"];

    /// <summary>The next step when the call gave a parameter the schema does not declare.</summary>
    private static readonly string ParametersStep =
        $"Call recall again with only these parameters: {string.Join(", ", Parameters)}; give exactly one of query or page, unless kind is persona or rules.";

    /// <summary>The next step for <c>kind=persona</c> or <c>kind=rules</c> when the call combined it with something it does not take.</summary>
    private static readonly Dictionary<string, string> StructuredStep = new()
    {
        [ToolValues.RecallKinds.Persona] = "Call recall again with kind=persona alone to read the whole persona, or with page alone to read a page.",
        [ToolValues.RecallKinds.Rules] = "Call recall again with kind=rules, and domain set to one domain label or left out for every domain.",
    };

    /// <summary>What a call asks for, decided from its parameters alone: <c>brief</c>, <c>page</c>, <c>persona</c>, <c>rules</c> or <c>search</c>.</summary>
    public static string ModeOf(RecallRequest request)
    {
        var hasQuery = !string.IsNullOrWhiteSpace(request.Query);
        var hasPage = !string.IsNullOrWhiteSpace(request.Page);
        if (request.Kind?.Trim() is ToolValues.RecallKinds.Persona or ToolValues.RecallKinds.Rules)
            return request.Kind.Trim();
        if (!hasQuery && !hasPage && !string.IsNullOrWhiteSpace(request.Workstream))
            return Modes.Brief;
        if (hasPage && !hasQuery && string.IsNullOrWhiteSpace(request.Domain))
            return Modes.Page;
        return Modes.Search;
    }

    /// <summary>The modes of <see cref="ModeOf"/> that are not a kind.</summary>
    public static class Modes
    {
        public const string Brief = "brief";
        public const string Page = "page";
        public const string Search = "search";
    }

    /// <summary>
    /// What the log adds for a call: its mode, the filters given, and for a search its query, with each run of whitespace
    /// as one space so the event stays on one line, and the number of results when the search ran.
    /// </summary>
    public static string LogDetails(RecallRequest request, int? results, MemoryLanguage language)
    {
        var mode = ModeOf(request);
        var details = new List<string> { LogMessages.RecallMode.Format(language, mode) };
        var filters = new (string Name, string? Value)[]
        {
            ("workstream", request.Workstream), ("from", request.From), ("to", request.To), ("kind", request.Kind), ("domain", request.Domain),
        }.Where(filter => !string.IsNullOrWhiteSpace(filter.Value)).Select(filter => $"{filter.Name}={OneLine(filter.Value!)}").ToList();
        if (filters.Count > 0)
            details.Add(LogMessages.RecallFilters.Format(language, string.Join(' ', filters)));
        if (mode == Modes.Search && !string.IsNullOrWhiteSpace(request.Query))
            details.Add(LogMessages.RecallQuery.Format(language, OneLine(request.Query)));
        if (results is { } count)
            details.Add(LogMessages.RecallResults.Format(language, count));
        return string.Concat(details.Select(detail => LogMessages.ToolDetail.Format(language, detail)));
    }

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.CultureInvariant);

    private static string OneLine(string text) => Whitespace.Replace(text.Trim(), " ");

    /// <returns>The answer, and for a search that ran, how many results it shows.</returns>
    public async Task<(ToolResult Result, int? Results)> RunAsync(RecallRequest request, CancellationToken cancellationToken)
    {
        var unknown = ToolText.UnknownParameters(request.UnknownParameters);
        switch (ModeOf(request))
        {
            case ToolValues.RecallKinds.Persona or ToolValues.RecallKinds.Rules:
                return (ReadStructured(request, request.Kind!.Trim(), unknown), null);
            case Modes.Brief:
                return (WorkstreamBriefCall(request, unknown), null);
            case Modes.Page:
                return (unknown.Count == 0 ? ReadPage(request.Page!) : ToolText.Refuse(unknown, ParametersStep), null);
        }

        var hasQuery = !string.IsNullOrWhiteSpace(request.Query);
        var hasPage = !string.IsNullOrWhiteSpace(request.Page);
        var problems = new List<string>(unknown);
        var queryOrPage = hasQuery == hasPage;
        if (queryOrPage)
            problems.Add(hasQuery ? "Give query or page, not both." : "Missing parameter query or page.");
        var (search, faults) = RecallSearch.Check(new SearchRequest(request.Query, request.Workstream, request.From, request.To, request.Kind?.Trim(), new RetrievalOverrides()));
        // A missing query is said above, together with the page it can be traded for.
        problems.AddRange(faults.Where(fault => fault.Fault != SearchFault.QueryMissing).Select(Describe));
        if (!string.IsNullOrWhiteSpace(request.Domain))
            problems.Add("Parameter domain applies only to kind=rules.");
        if (problems.Count > 0)
        {
            var nextStep = unknown.Count > 0 ? ParametersStep
                : queryOrPage ? "Call recall again with query (what to look for, in the memory language) or page (a page ID to read in full)."
                : "Fix these and call recall again.";
            return (ToolText.Refuse(problems, nextStep), null);
        }

        var trace = await reader.SearchAsync(search!, Usage.Counted, cancellationToken);
        return (ToolText.Ok(trace.Text), trace.Results.Count);
    }

    /// <summary>A problem of the search request, as the agent reads it.</summary>
    private static string Describe(SearchProblem problem) => problem.Fault switch
    {
        SearchFault.DateInvalid => $"{problem.Parameter} {ToolText.Quote(problem.Given!)} must be a date written YYYY-MM-DD.",
        SearchFault.DatesReversed => "from is later than to.",
        SearchFault.KindInvalid => $"Unknown kind {ToolText.Quote(problem.Given!)}; use pages, activities, rules or persona.",
        _ => $"Parameter {problem.Parameter} is not valid.",
    };

    /// <summary>A whole page, with the date each section last changed.</summary>
    private ToolResult ReadPage(string raw)
    {
        if (!PageId.TryParse(raw, out var id, out var problem))
            return ToolText.Refuse(problem, "Call recall again with a valid page ID.");
        var read = reader.Page(id, Usage.Counted);
        return read.Refusal ?? ToolText.Ok(PageTools.Format(id, read.Document!, index.SectionChanges(id)));
    }

    /// <summary>
    /// <c>kind=persona</c> and <c>kind=rules</c>: they read the persona directly, so they take neither a page nor a date or
    /// workstream filter; <c>query</c> is ignored. <c>domain</c> goes only with <c>rules</c>.
    /// </summary>
    private ToolResult ReadStructured(RecallRequest request, string kind, List<string> unknown)
    {
        var domain = string.IsNullOrWhiteSpace(request.Domain) ? null : request.Domain.Trim();
        var problems = new List<string>(unknown);
        if (!string.IsNullOrWhiteSpace(request.Page))
            problems.Add($"Parameter page cannot be combined with kind={kind}.");
        if (domain is not null && kind == ToolValues.RecallKinds.Persona)
            problems.Add("Parameter domain applies only to kind=rules.");
        if (!string.IsNullOrWhiteSpace(request.Workstream))
            problems.Add($"Parameter workstream does not apply to kind={kind}.");
        if (!string.IsNullOrWhiteSpace(request.From))
            problems.Add($"Parameter from does not apply to kind={kind}.");
        if (!string.IsNullOrWhiteSpace(request.To))
            problems.Add($"Parameter to does not apply to kind={kind}.");
        if (problems.Count > 0)
            return ToolText.Refuse(problems, unknown.Count > 0 ? ParametersStep : StructuredStep[kind]);

        return kind == ToolValues.RecallKinds.Persona ? ReadPersona() : ReadRules(domain);
    }

    /// <summary>The whole persona with its short codes, or <see cref="NoMemory"/> when it holds no entry.</summary>
    private ToolResult ReadPersona()
    {
        if (PersonaDocument.Read(PersonaPath) is not { } persona)
            return NotUtf8();
        return ToolText.Ok(PersonaReading.Whole(persona) ?? NoMemory);
    }

    /// <summary>
    /// The rules of one domain, or of every domain in label order. A domain the persona does not hold is refused with the labels
    /// it holds.
    /// </summary>
    private ToolResult ReadRules(string? domain)
    {
        if (PersonaDocument.Read(PersonaPath) is not { } persona)
            return NotUtf8();

        var domains = persona.Blocks
            .Where(block => block.Scope.Kind == PersonaScopeKind.Domain)
            .OrderBy(block => block.Scope.Name, StringComparer.Ordinal)
            .ToList();
        if (domain is not null)
        {
            var chosen = domains.FirstOrDefault(block => block.Scope.Name == domain);
            if (chosen is null)
            {
                var labels = domains.Select(block => block.Scope.Name).ToList();
                return labels.Count == 0
                    ? ToolText.Refuse(
                        $"The persona has no domains, so it has no domain {ToolText.Quote(domain)}.",
                        "Call recall again without domain to read the rules of every domain, or with kind=persona to read the whole persona.")
                    : ToolText.Refuse(
                        $"Domain {ToolText.Quote(domain)} is not in the persona; its domains are: {string.Join(", ", labels)}.",
                        "Call recall again with kind=rules and domain set to one of these labels, or without domain for all of them.");
            }

            domains = [chosen];
        }

        return ToolText.Ok(PersonaReading.Domains(persona, domains) ?? NoMemory);
    }

    private string PersonaPath => MemoryLayout.Full(config.MemoryPath, MemoryLayout.Persona);

    private static ToolResult NotUtf8() => ToolText.Refuse(
        $"{MemoryLayout.Persona} is not valid UTF-8, so it cannot be read.",
        $"Save {MemoryLayout.Persona} as UTF-8, then call recall again with the same kind.");

    /// <summary>
    /// <c>recall</c> with only <c>workstream</c>: the workstream's brief. It takes <c>from</c> and <c>to</c> as its date range, and
    /// refuses <c>kind</c>, which has no meaning for a brief.
    /// </summary>
    private ToolResult WorkstreamBriefCall(RecallRequest request, List<string> unknown)
    {
        var problems = new List<string>(unknown);
        var faults = new List<SearchProblem>();
        var (from, to) = RecallSearch.Dates(request.From, request.To, faults);
        problems.AddRange(faults.Select(Describe));
        var raw = request.Workstream!.Trim();
        var workstream = raw.ToLowerInvariant();
        if (workstream != PageId.Topics && !PageId.IsWorkstreamName(workstream))
            problems.Add($"Workstream {ToolText.Quote(raw)} may only use a-z, 0-9, '.', '_' and '-', and must not start with '.' or '_' (except {PageId.Topics}).");
        if (!string.IsNullOrWhiteSpace(request.Kind))
            problems.Add("kind does not apply to a workstream brief.");
        if (!string.IsNullOrWhiteSpace(request.Domain))
            problems.Add("Parameter domain applies only to kind=rules.");
        if (problems.Count > 0)
        {
            var nextStep = unknown.Count > 0 ? ParametersStep
                : "Call recall again with workstream alone for the workstream brief, or with query and kind to search the workstream.";
            return ToolText.Refuse(problems, nextStep);
        }

        return ToolText.Ok(reader.Brief(workstream, from, to, Usage.Counted)?.Text ?? NoMemory);
    }
}
