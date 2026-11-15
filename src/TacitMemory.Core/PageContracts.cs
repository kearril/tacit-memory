namespace TacitMemory.Core;

/// <summary>Every detail page, as the memory page lists them. Reading counts nothing and changes no file.</summary>
public sealed record PageList(IReadOnlyList<PageListItem> Pages);

/// <summary>A detail page in the list.</summary>
/// <param name="Id">The page ID, <c>&lt;workstream&gt;/&lt;slug&gt;</c>.</param>
/// <param name="Workstream">The workstream, or <c>_topics</c> for a topic page.</param>
/// <param name="Title">The title; the slug when the header cannot be read.</param>
/// <param name="Type">The page type as written; <c>null</c> when the page has none.</param>
/// <param name="First">The first date, written yyyy-MM-dd; <c>null</c> when the page has none.</param>
/// <param name="Last">The last date, written yyyy-MM-dd; <c>null</c> when the page has none.</param>
/// <param name="Changed">The latest day a section changed, or the last date when no section has one; <c>null</c> when neither is known.</param>
/// <param name="Uses">How often <c>recall</c> has returned the page's units.</param>
public sealed record PageListItem(
    string Id,
    string Workstream,
    string Title,
    IReadOnlyList<string> Aliases,
    string? Type,
    string? First,
    string? Last,
    string? Changed,
    long Uses);

/// <summary>
/// A detail page as the memory page shows it. A page that is not valid UTF-8, or whose header cannot be read, is returned
/// with what can be read and its problems. Reading counts nothing and changes no file.
/// </summary>
/// <param name="File">The page file: its path and the problems consolidation reports for it.</param>
/// <param name="Uses">How often <c>recall</c> has returned the page's units.</param>
/// <param name="Sections">The <c>##</c> sections in file order.</param>
public sealed record PageView(
    string Id,
    string Workstream,
    string Title,
    IReadOnlyList<string> Aliases,
    string? Type,
    string? First,
    string? Last,
    IReadOnlyList<RelatedPage> Related,
    MemoryFileInfo File,
    long Uses,
    IReadOnlyList<PageSectionView> Sections);

/// <summary>A page the header's <c>related</c> lists, and whether it exists.</summary>
public sealed record RelatedPage(string Id, bool Exists);

/// <summary>A section of a page.</summary>
/// <param name="Code">The short code <c>write_page</c> takes.</param>
/// <param name="Changed">The day it last changed, written yyyy-MM-dd; <c>null</c> when unknown.</param>
/// <param name="Uses">How often <c>recall</c> has returned it.</param>
/// <param name="Body">Its Markdown as written, without the heading.</param>
public sealed record PageSectionView(string Name, string Code, string? Changed, long Uses, string Body);
