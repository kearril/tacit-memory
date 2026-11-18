using TacitMemory.Core.Index;
using TacitMemory.Core.Retrieval;

namespace TacitMemory.Core;

/// <summary>
/// A recall trace to run: the parameters <c>recall</c> takes for a search, and three optional values that replace the
/// retrieval config for this search alone.
/// </summary>
/// <param name="Query">What to look for, as <c>recall</c> takes it.</param>
/// <param name="Workstream">Only units of this workstream; <c>null</c> or empty for every workstream.</param>
/// <param name="From">The earliest date, written yyyy-MM-dd; <c>null</c> or empty for no lower bound.</param>
/// <param name="To">The latest date, written yyyy-MM-dd; <c>null</c> or empty for no upper bound.</param>
/// <param name="Kind"><c>pages</c> or <c>activities</c> to search only those; <c>null</c> or empty for both.</param>
/// <param name="MinRerankScore">Replaces <c>retrieval.minRerankScore</c> for this search; <c>null</c> keeps it.</param>
/// <param name="TopK">Replaces <c>retrieval.topK</c> for this search; <c>null</c> keeps it.</param>
/// <param name="Candidates">Replaces <c>retrieval.candidates</c> for this search; <c>null</c> keeps it.</param>
public sealed record RecallTraceRequest(
    string? Query,
    string? Workstream = null,
    string? From = null,
    string? To = null,
    string? Kind = null,
    double? MinRerankScore = null,
    int? TopK = null,
    int? Candidates = null);

/// <summary>
/// A recall trace: the search <c>recall</c> would run for the same parameters, stage by stage. It counts no return and
/// changes no file.
/// </summary>
/// <param name="Settings">The three retrieval values the search ran with, each beside the configured one.</param>
/// <param name="FullText">How the full-text search matched the query.</param>
/// <param name="Counts">How many units each stage held.</param>
/// <param name="Results">The results <c>recall</c> shows, in its order.</param>
/// <param name="Text">The text the agent receives from <c>recall</c>, word for word.</param>
/// <param name="Notes">The degradation notes <c>recall</c> appends to that text, word for word; empty when nothing degraded.</param>
/// <param name="Units">Every unit either search returned, in fused order, with what became of it.</param>
public sealed record RecallTraceReport(
    TraceSettings Settings,
    FullTextMethod FullText,
    TraceModel Embedding,
    TraceModel Reranker,
    TraceCounts Counts,
    IReadOnlyList<TraceResult> Results,
    string Text,
    IReadOnlyList<string> Notes,
    IReadOnlyList<TraceUnit> Units);

/// <summary>The three retrieval values of a trace; a value differs from its configured one when the request replaced it.</summary>
public sealed record TraceSettings(TraceSetting MinRerankScore, TraceSetting TopK, TraceSetting Candidates);

/// <summary>A retrieval value: what the search ran with, and what the config sets (the built-in default when it sets none).</summary>
public sealed record TraceSetting(double Value, double Configured);

/// <summary>How one auxiliary model took part in a trace.</summary>
/// <param name="Configured">Whether the model is configured; an unconfigured model is never called.</param>
/// <param name="Answered">Whether it answered; when a configured model did not, the search degraded as <c>recall</c> says.</param>
/// <param name="CoolingDown">Whether it was skipped because it is cooling down from an earlier failure.</param>
/// <param name="Milliseconds">How long the call took; <c>null</c> when the model is not configured.</param>
public sealed record TraceModel(bool Configured, bool Answered, bool CoolingDown, double? Milliseconds);

/// <summary>How many units each stage of a trace held.</summary>
/// <param name="FullText">Units the full-text search returned.</param>
/// <param name="Vector">Units the vector search returned.</param>
/// <param name="Fused">Distinct units of both searches after fusion.</param>
/// <param name="Reranked">Units the reranker scored: the candidates fusion kept, or none when it did not answer.</param>
/// <param name="Passed">Units that reached grouping: those at or above the minimum score, or every candidate when the reranker did not answer.</param>
/// <param name="Results">Results shown.</param>
public sealed record TraceCounts(int FullText, int Vector, int Fused, int Reranked, int Passed, int Results);

/// <summary>A result <c>recall</c> shows.</summary>
/// <param name="Number">The result's number in the text, from 1.</param>
/// <param name="Kind"><c>page</c>, <c>activity</c> or <c>summary</c>.</param>
/// <param name="Page">The page ID of a page's result; <c>null</c> otherwise.</param>
/// <param name="Title">The page's title; <c>null</c> for an activity or a summary.</param>
/// <param name="Date">The activity's day, or the first day of the summary's period; <c>null</c> for a page.</param>
/// <param name="Code">The activity's short code; <c>null</c> otherwise.</param>
/// <param name="Period"><c>week</c> or <c>month</c> for a summary; <c>null</c> otherwise.</param>
/// <param name="Workstream">The workstream; empty when an activity has none.</param>
/// <param name="Sections">The sections of the page the text shows; empty for an activity or a summary.</param>
/// <param name="Text">The activity's or summary's text; <c>null</c> for a page.</param>
public sealed record TraceResult(
    int Number,
    string Kind,
    string? Page,
    string? Title,
    string? Date,
    string? Code,
    string? Period,
    string Workstream,
    IReadOnlyList<TraceSection> Sections,
    string? Text);

/// <summary>A section of a page a result shows.</summary>
/// <param name="Recognized">Which recognized section it is, for the page to label it; <c>null</c> for any other.</param>
public sealed record TraceSection(string Name, RecognizedSection? Recognized);

/// <summary>A retrieval unit as a trace saw it. Ranks are 1-based.</summary>
/// <param name="Kind"><c>section</c>, <c>clue</c> (a page's title and aliases), <c>activity</c> or <c>summary</c>.</param>
/// <param name="Source">The page ID for a page's unit; the month file for an activity or a summary.</param>
/// <param name="Section">The section's name; <c>week</c> or <c>month</c> for a summary; <c>null</c> otherwise.</param>
/// <param name="Code">The activity's short code; <c>null</c> otherwise.</param>
/// <param name="Date">The activity's day, or the first day of the summary's period; <c>null</c> for a page's unit.</param>
/// <param name="Workstream">The unit's workstream; empty when an activity has none.</param>
/// <param name="Text">The unit's indexed text; a section's reranker passage also carries its page title.</param>
/// <param name="Bm25">The BM25 value, lower is better; <c>null</c> when full text did not return the unit.</param>
/// <param name="Distance">The cosine distance to the query; <c>null</c> when the vector search did not return the unit.</param>
/// <param name="Rrf">The reciprocal rank fusion score.</param>
/// <param name="RerankScore">The reranker's score; <c>null</c> when the unit was not scored.</param>
/// <param name="Result">The number of the result the unit is shown in; <c>null</c> unless it is shown.</param>
public sealed record TraceUnit(
    string Kind,
    string Source,
    string? Section,
    string? Code,
    string? Date,
    string Workstream,
    string Text,
    int? FullTextRank,
    double? Bm25,
    int? VectorRank,
    double? Distance,
    double Rrf,
    int FusedRank,
    double? RerankScore,
    UnitFate Fate,
    int? Result);
