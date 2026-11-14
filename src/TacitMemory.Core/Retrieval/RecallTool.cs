using System.Globalization;
using System.Text;
using TacitMemory.Core.Index;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Models;
using TacitMemory.Core.Pages;
using TacitMemory.Core.Persona;
using TacitMemory.Core.Tools;

namespace TacitMemory.Core.Retrieval;

/// <summary>
/// The <c>recall</c> tool: reads one page by ID, reads the persona or its domain rules, returns a workstream's brief when given only a
/// workstream, or searches pages and activities by full text and by vector, merges the two with reciprocal rank fusion, reranks the
/// merged units and returns the best sections, merged per page. The persona is never searched. It never changes a file.
/// </summary>
internal sealed class RecallTool(TacitConfig config, MemoryIndex index, PageTools pages, EmbeddingModel embedding, RerankerModel reranker, VectorSync vectors)
{
    private readonly WorkstreamBrief _brief = new(config, index);

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

    public async Task<ToolResult> RunAsync(RecallRequest request, CancellationToken cancellationToken)
    {
        var hasQuery = !string.IsNullOrWhiteSpace(request.Query);
        var hasPage = !string.IsNullOrWhiteSpace(request.Page);
        var hasDomain = !string.IsNullOrWhiteSpace(request.Domain);
        var unknown = ToolText.UnknownParameters(request.UnknownParameters);
        var kind = request.Kind?.Trim();
        if (kind is ToolValues.RecallKinds.Persona or ToolValues.RecallKinds.Rules)
            return ReadStructured(request, kind, unknown);
        if (!hasQuery && !hasPage && !string.IsNullOrWhiteSpace(request.Workstream))
            return WorkstreamBriefCall(request, unknown);
        if (hasPage && !hasQuery && !hasDomain)
            return unknown.Count == 0 ? pages.Read(request, index) : ToolText.Refuse(unknown, ParametersStep);

        var problems = new List<string>(unknown);
        var queryOrPage = hasQuery == hasPage;
        if (queryOrPage)
            problems.Add(hasQuery ? "Give query or page, not both." : "Missing parameter query or page.");
        var from = Date(request.From, "from", problems);
        var to = Date(request.To, "to", problems);
        if (from > to)
            problems.Add("from is later than to.");
        if (kind is { Length: > 0 } and not (ToolValues.RecallKinds.Pages or ToolValues.RecallKinds.Activities))
            problems.Add($"Unknown kind {ToolText.Quote(kind)}; use pages, activities, rules or persona.");
        if (hasDomain)
            problems.Add("Parameter domain applies only to kind=rules.");
        if (problems.Count > 0)
        {
            var nextStep = unknown.Count > 0 ? ParametersStep
                : queryOrPage ? "Call recall again with query (what to look for, in the memory language) or page (a page ID to read in full)."
                : "Fix these and call recall again.";
            return ToolText.Refuse(problems, nextStep);
        }

        var workstream = string.IsNullOrWhiteSpace(request.Workstream) ? null : request.Workstream.Trim().ToLowerInvariant();
        var activities = kind switch { ToolValues.RecallKinds.Activities => true, ToolValues.RecallKinds.Pages => false, _ => (bool?)null };
        var filter = new IndexFilter(workstream, from, to, activities);
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

    private string PersonaPath => Path.Combine(config.MemoryPath, PersonaFiles.Persona);

    private static ToolResult NotUtf8() => ToolText.Refuse(
        $"{PersonaFiles.Persona} is not valid UTF-8, so it cannot be read.",
        $"Save {PersonaFiles.Persona} as UTF-8, then call recall again with the same kind.");

    /// <summary>
    /// <c>recall</c> with only <c>workstream</c>: the workstream's brief. It takes <c>from</c> and <c>to</c> as its date range, and
    /// refuses <c>kind</c>, which has no meaning for a brief.
    /// </summary>
    private ToolResult WorkstreamBriefCall(RecallRequest request, List<string> unknown)
    {
        var problems = new List<string>(unknown);
        var from = Date(request.From, "from", problems);
        var to = Date(request.To, "to", problems);
        if (from > to)
            problems.Add("from is later than to.");
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

        return ToolText.Ok(_brief.Compose(workstream, from, to));
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
        // Pages group by source. Each activity and each summary is a result of its own, so two of one month file stay apart.
        var results = hits.GroupBy(GroupKey).Take(config.Retrieval.TopK).ToList();
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
                var label = sample.Workstream.Length > 0 ? $", workstream {sample.Workstream}" : "";
                text.Append(CultureInfo.InvariantCulture, $"\n[{shownResults}] activity {sample.Date} [{sample.Code}]{label}\n{sample.Text}\n");
                continue;
            }

            if (sample.Kind == UnitKinds.Summary)
            {
                returned.AddRange(result);
                shownResults++;
                text.Append(CultureInfo.InvariantCulture, $"\n[{shownResults}] summary, {sample.Section} starting {sample.Date}\n{sample.Text}\n");
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

    /// <summary>Hits of one activity or summary are one result each; the hits of a page's sections share the page's result.</summary>
    private static (string Source, string? Code, string? Text) GroupKey(IndexHit hit) =>
        hit.Kind is UnitKinds.Activity or UnitKinds.Summary ? (hit.Source, hit.Code, hit.Text) : (hit.Source, null, null);

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
