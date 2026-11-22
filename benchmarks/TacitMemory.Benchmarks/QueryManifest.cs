using TacitMemory.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TacitMemory.Benchmarks;

/// <summary>The YAML query set loaded from <c>queries.yaml</c> inside a corpus directory.</summary>
internal sealed class QueryManifest
{
    public List<QueryCase> Queries { get; set; } = [];

    public static QueryManifest Load(string path)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
        var text = File.ReadAllText(path);
        return deserializer.Deserialize<QueryManifest>(text) ?? new QueryManifest();
    }
}

/// <summary>One annotated benchmark query with optional recall filters, retrieval overrides and expected targets.</summary>
internal sealed class QueryCase
{
    public string Id { get; set; } = "";
    public string Category { get; set; } = "general";
    public string Query { get; set; } = "";
    public string? Workstream { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
    public string? Kind { get; set; }
    public int? TopK { get; set; }
    public int? Candidates { get; set; }
    public double? MinRerankScore { get; set; }
    public bool Negative { get; set; }
    public List<QueryTarget> Targets { get; set; } = [];
}

/// <summary>Indexed corpus inventory used to verify that every target in a query manifest exists.</summary>
internal sealed record CorpusInventory(
    IReadOnlyDictionary<string, HashSet<string>> PageSections,
    IReadOnlyList<(string Date, string Code, string Text)> Activities,
    IReadOnlyList<(string Date, string Text)> Summaries);

/// <summary>One expected target of a positive query: a page, a page section, an activity (by code or date+text) or a summary.</summary>
internal sealed class QueryTarget
{
    public string? Page { get; set; }
    public string? Section { get; set; }
    public string? Activity { get; set; }
    public string? Summary { get; set; }
    public string? Code { get; set; }
    public string? Contains { get; set; }

    private TargetKind Kind =>
        !string.IsNullOrEmpty(Page) ? TargetKind.Page
        : !string.IsNullOrEmpty(Summary) ? TargetKind.Summary
        : !string.IsNullOrEmpty(Activity) || !string.IsNullOrEmpty(Code) ? TargetKind.Activity
        : TargetKind.Invalid;

    public bool MatchesResult(TraceResult result) => Kind switch
    {
        TargetKind.Page => result.Kind == "page"
            && string.Equals(result.Page, Page, StringComparison.Ordinal)
            && (string.IsNullOrEmpty(Section) || result.Sections.Any(section => string.Equals(section.Name, Section, StringComparison.Ordinal))),
        TargetKind.Activity => result.Kind == "activity" && MatchesActivity(result.Date, result.Code, result.Text),
        TargetKind.Summary => result.Kind == "summary" && MatchesSummary(result.Date, result.Text),
        _ => false,
    };

    public bool MatchesUnit(TraceUnit unit) => Kind switch
    {
        TargetKind.Page => string.Equals(unit.Source, Page, StringComparison.Ordinal)
            && (string.IsNullOrEmpty(Section)
                ? unit.Kind is "section" or "clue"
                : unit.Kind == "section" && string.Equals(unit.Section, Section, StringComparison.Ordinal)),
        TargetKind.Activity => unit.Kind == "activity" && MatchesActivity(unit.Date, unit.Code, unit.Text),
        TargetKind.Summary => unit.Kind == "summary" && MatchesSummary(unit.Date, unit.Text),
        _ => false,
    };

    public string? ValidateAgainst(string queryId, CorpusInventory inventory) => Kind switch
    {
        TargetKind.Page => !inventory.PageSections.TryGetValue(Page!, out var sections)
            ? $"Query '{queryId}' references missing page '{Page}'."
            : !string.IsNullOrEmpty(Section) && !sections.Contains(Section)
                ? $"Query '{queryId}' references missing section '{Section}' in page '{Page}'."
                : null,
        TargetKind.Activity => inventory.Activities.Any(item => MatchesActivity(item.Date, item.Code, item.Text))
            ? null
            : $"Query '{queryId}' references missing activity {Describe()}.",
        TargetKind.Summary => inventory.Summaries.Any(item => MatchesSummary(item.Date, item.Text))
            ? null
            : $"Query '{queryId}' references missing summary {Describe()}.",
        _ => $"Query '{queryId}' has an empty target entry.",
    };

    public string Describe() => Kind switch
    {
        TargetKind.Page => string.IsNullOrEmpty(Section) ? Page! : $"{Page}#{Section}",
        TargetKind.Activity => string.IsNullOrEmpty(Activity) ? $"activity:[{Code}]" : $"activity:{Activity}({Contains ?? Code})",
        TargetKind.Summary => $"summary:{Summary}({Contains})",
        _ => "(invalid)",
    };

    private bool MatchesActivity(string? date, string? code, string? text)
    {
        if (!string.IsNullOrEmpty(Activity) && !string.Equals(date, Activity, StringComparison.Ordinal))
            return false;
        if (!string.IsNullOrEmpty(Code) && !string.Equals(code, Code, StringComparison.Ordinal))
            return false;
        if (!string.IsNullOrEmpty(Contains) && (text is null || !text.Contains(Contains, StringComparison.Ordinal)))
            return false;
        return !string.IsNullOrEmpty(Code) || !string.IsNullOrEmpty(Contains);
    }

    private bool MatchesSummary(string? date, string? text) =>
        string.Equals(date, Summary, StringComparison.Ordinal)
        && !string.IsNullOrEmpty(Contains)
        && text is not null
        && text.Contains(Contains, StringComparison.Ordinal);

    private enum TargetKind
    {
        Invalid,
        Page,
        Activity,
        Summary,
    }
}
