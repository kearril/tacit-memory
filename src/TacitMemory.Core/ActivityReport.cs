namespace TacitMemory.Core;

/// <summary>
/// How many activities the activity log holds per day over the last 53 weeks: the week of today (from Monday, by the
/// service's local date) and the 52 weeks before it, up to today. Only the days with activities are listed.
/// </summary>
/// <param name="From">The window's first day: a Monday.</param>
/// <param name="To">The window's last day: today.</param>
/// <param name="Days">The days with at least one activity, oldest first.</param>
/// <param name="ConsolidationDays">The days in the window with a consolidation commit, oldest first; empty when the history cannot be read.</param>
/// <param name="SkippedFiles">The month files in the window that could not be read, as paths relative to the memory repository; their days are left out.</param>
/// <param name="HistoryUnreadable">Whether the memory repository's history could not be read, so the consolidation days are unknown.</param>
public sealed record ActivityReport(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<ActivityDay> Days,
    IReadOnlyList<DateOnly> ConsolidationDays,
    IReadOnlyList<string> SkippedFiles,
    bool HistoryUnreadable);

/// <summary>The activities of one day: the count for each workstream label as written, and the count without a label.</summary>
/// <param name="Day">The day of the heading the activities sit under.</param>
/// <param name="Workstreams">The count per workstream, most first, then by name.</param>
/// <param name="Unlabeled">The activities without a workstream label.</param>
public sealed record ActivityDay(DateOnly Day, IReadOnlyList<WorkstreamCount> Workstreams, int Unlabeled);

/// <summary>How many activities of a day carry one workstream label.</summary>
public sealed record WorkstreamCount(string Workstream, int Count);
