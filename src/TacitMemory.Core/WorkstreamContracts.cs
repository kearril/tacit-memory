namespace TacitMemory.Core;

/// <summary>The workstreams, as the memory page lists them. Reading counts nothing and changes no file.</summary>
/// <param name="Established">The workstreams <c>workstreams.md</c> establishes, in file order.</param>
/// <param name="Unestablished">The names a page folder or an activity label uses that are not established, by name.</param>
/// <param name="File"><c>workstreams.md</c>: its path and the problems consolidation reports for it.</param>
public sealed record WorkstreamList(
    IReadOnlyList<WorkstreamListItem> Established,
    IReadOnlyList<WorkstreamListItem> Unestablished,
    MemoryFileInfo File);

/// <summary>A workstream in the list.</summary>
/// <param name="Background">What the workstream is; <c>null</c> for a name that is not established.</param>
/// <param name="Recent">The summary of its recent phase; <c>null</c> when there is none.</param>
/// <param name="Pages">How many detail pages its folder holds.</param>
/// <param name="LastActivity">The day of its latest activity, written yyyy-MM-dd; <c>null</c> when it has none.</param>
public sealed record WorkstreamListItem(string Name, string? Background, string? Recent, int Pages, string? LastActivity);

/// <summary>
/// A workstream's brief, whole: what <c>recall</c> lists for the workstream, gathered and ordered the same way, without
/// the length limit and without counting a return.
/// </summary>
/// <param name="Established">Whether <c>workstreams.md</c> establishes it.</param>
/// <param name="Conventions">The persona entries of its workstream scope, by section, as the persona view gives them.</param>
/// <param name="OpenItems">The leftovers of its work pages.</param>
/// <param name="Groups">Its pages by type, in the brief's order, each newest first; overturned decisions are left out.</param>
/// <param name="Overturned">The decisions another page overturned.</param>
/// <param name="Activities">Its latest activities, newest first.</param>
/// <param name="Summary">The latest week summary of a week it had activity in; <c>null</c> when there is none.</param>
public sealed record WorkstreamBriefView(
    string Name,
    bool Established,
    string? Background,
    string? Recent,
    IReadOnlyList<PersonaSectionView> Conventions,
    IReadOnlyList<OpenItemView> OpenItems,
    IReadOnlyList<BriefPageGroup> Groups,
    IReadOnlyList<string> Overturned,
    IReadOnlyList<BriefActivity> Activities,
    BriefSummary? Summary);

/// <summary>A work page's leftovers: the page and the non-blank lines of its leftovers section.</summary>
public sealed record OpenItemView(string Page, IReadOnlyList<string> Lines);

/// <summary>The pages of one type.</summary>
/// <param name="Type"><c>decision</c>, <c>incident</c>, <c>work</c> or <c>entity</c>; <c>null</c> for pages without a valid type.</param>
public sealed record BriefPageGroup(string? Type, IReadOnlyList<BriefPage> Pages);

/// <summary>A page in a brief.</summary>
/// <param name="Changed">The latest day a section changed, or the page's last date; <c>null</c> when neither is known.</param>
/// <param name="Uses">How often <c>recall</c> has returned the page's units.</param>
/// <param name="LastUse">The last day one was returned, written yyyy-MM-dd; <c>null</c> when never.</param>
public sealed record BriefPage(string Id, string Title, IReadOnlyList<string> Aliases, string? Changed, long Uses, string? LastUse);

/// <summary>An activity in a brief: its day, its short code and its text.</summary>
public sealed record BriefActivity(string Date, string Code, string Text);

/// <summary>A summary in a brief.</summary>
/// <param name="Label">The period: <c>2026-W45</c> for a week.</param>
/// <param name="First">The period's first day, written yyyy-MM-dd.</param>
/// <param name="Last">The period's last day, written yyyy-MM-dd.</param>
public sealed record BriefSummary(string Label, string First, string Last, string Text);
