namespace TacitMemory.Core;

/// <summary>The months of the activity log, for the memory page. Reading counts nothing and changes no file.</summary>
/// <param name="Months">The months that have a file, written yyyy-MM, newest first.</param>
/// <param name="Current">The month of the service's today, written yyyy-MM; it may have no file yet.</param>
public sealed record ActivityMonths(IReadOnlyList<string> Months, string Current);

/// <summary>One month of the activity log: its activities by day and its summaries, with the file's path and problems.</summary>
/// <param name="Month">The month, written yyyy-MM.</param>
/// <param name="Days">The days that have activities, oldest first.</param>
/// <param name="Summaries">The month file's summaries, in file order.</param>
public sealed record ActivityMonthView(
    string Month,
    MemoryFileInfo File,
    IReadOnlyList<ActivityDayView> Days,
    IReadOnlyList<ActivitySummaryView> Summaries);

/// <summary>A day of a month: its date, written yyyy-MM-dd, and its activities in file order.</summary>
public sealed record ActivityDayView(string Date, IReadOnlyList<ActivityItemView> Activities);

/// <summary>An activity.</summary>
/// <param name="Code">The short code <c>write_recent</c> takes to replace it.</param>
/// <param name="Workstream">Its workstream label; <c>null</c> when it has none.</param>
/// <param name="Text">Its text without the label.</param>
/// <param name="Continuation">The indented lines under it, as written; a source comment keeps the user's words.</param>
/// <param name="Uses">How often <c>recall</c> has returned it; 0 when never, or when the index has not reached it yet.</param>
public sealed record ActivityItemView(string Code, string? Workstream, string Text, IReadOnlyList<string> Continuation, long Uses);

/// <summary>A summary of the month file.</summary>
/// <param name="Kind"><c>week</c> or <c>month</c>.</param>
/// <param name="Label">The period: <c>2026-W45</c> for a week, <c>2026-11</c> for a month.</param>
/// <param name="First">The period's first day, written yyyy-MM-dd.</param>
/// <param name="Last">The period's last day, written yyyy-MM-dd.</param>
public sealed record ActivitySummaryView(string Kind, string Label, string First, string Last, string Text);
