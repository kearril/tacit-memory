namespace TacitMemory.Core;

/// <summary>The four page types, as a page header writes them.</summary>
public enum PageType
{
    /// <summary><c>incident</c>: a fault, its cause and its fix.</summary>
    Incident,

    /// <summary><c>decision</c>: one decision and the reasons for it.</summary>
    Decision,

    /// <summary><c>entity</c>: a standing system or tool.</summary>
    Entity,

    /// <summary><c>work</c>: one unit of work and its record.</summary>
    Work,
}

/// <summary>
/// The page sections the service finds by name, in every memory language by their English names; any other section is
/// content.
/// </summary>
public enum RecognizedSection
{
    /// <summary><c>Lesson</c>: the rule an incident teaches, which the consolidation report quotes.</summary>
    Lesson,

    /// <summary><c>Overturned</c>: on a decision page, the page that replaced the decision.</summary>
    Overturned,

    /// <summary><c>Leftovers</c>: on a work page, the open items the brief lists.</summary>
    Leftovers,
}

/// <summary>Every detail page, as the memory page lists them. Reading counts nothing and changes no file.</summary>
public sealed record PageList(IReadOnlyList<PageListItem> Pages);

/// <summary>A detail page in the list.</summary>
/// <param name="Id">The page ID, <c>&lt;workstream&gt;/&lt;slug&gt;</c>.</param>
/// <param name="Workstream">The workstream, or <c>_topics</c> for a topic page.</param>
/// <param name="Title">The title; the slug when the header cannot be read.</param>
/// <param name="Type">The page type; <c>null</c> when the page has none, or one that is not a page type.</param>
/// <param name="WrittenType">The type as written when it is not a page type, for the page to show as it is; otherwise <c>null</c>.</param>
/// <param name="First">The first date, written yyyy-MM-dd; <c>null</c> when the page has none.</param>
/// <param name="Last">The last date, written yyyy-MM-dd; <c>null</c> when the page has none.</param>
/// <param name="Changed">The latest day a section changed, or the last date when no section has one; <c>null</c> when neither is known.</param>
/// <param name="Uses">How often <c>recall</c> has returned the page's units.</param>
/// <param name="Overturned">Whether the page has a non-empty <c>## Overturned</c> section or is superseded by another page.</param>
public sealed record PageListItem(
    string Id,
    string Workstream,
    string Title,
    IReadOnlyList<string> Aliases,
    PageType? Type,
    string? WrittenType,
    string? First,
    string? Last,
    string? Changed,
    long Uses,
    bool Overturned = false);

/// <summary>
/// A detail page as the memory page shows it. A page that is not valid UTF-8, or whose header cannot be read, is returned
/// with what can be read and its problems. Reading counts nothing and changes no file.
/// </summary>
/// <param name="Type">The page type; <c>null</c> when the page has none, or one that is not a page type.</param>
/// <param name="WrittenType">The type as written when it is not a page type, for the page to show as it is; otherwise <c>null</c>.</param>
/// <param name="File">The page file: its path and the problems consolidation reports for it.</param>
/// <param name="Uses">How often <c>recall</c> has returned the page's units.</param>
/// <param name="Sections">The <c>##</c> sections in file order.</param>
/// <param name="Supersedes">Pages listed in this page's <c>supersedes</c> header field, with whether each exists.</param>
/// <param name="SupersededBy">Existing pages whose header lists this page in <c>supersedes</c>.</param>
/// <param name="Overturned">Whether the page has a non-empty <c>## Overturned</c> section or is superseded by another page.</param>
public sealed record PageView(
    string Id,
    string Workstream,
    string Title,
    IReadOnlyList<string> Aliases,
    PageType? Type,
    string? WrittenType,
    string? First,
    string? Last,
    IReadOnlyList<RelatedPage> Related,
    MemoryFileInfo File,
    long Uses,
    IReadOnlyList<PageSectionView> Sections,
    IReadOnlyList<RelatedPage> Supersedes,
    IReadOnlyList<RelatedPage> SupersededBy,
    bool Overturned);

/// <summary>A page the header's <c>related</c> lists, and whether it exists.</summary>
public sealed record RelatedPage(string Id, bool Exists);

/// <summary>A section of a page.</summary>
/// <param name="Code">The short code <c>write_page</c> takes.</param>
/// <param name="Changed">The day it last changed, written yyyy-MM-dd; <c>null</c> when unknown.</param>
/// <param name="Uses">How often <c>recall</c> has returned it.</param>
/// <param name="Body">Its Markdown as written, without the heading.</param>
/// <param name="Sources">The source comments in its Markdown, each once, for the page to show where they are.</param>
/// <param name="Recognized">Which recognized section it is, for the page to label it; <c>null</c> for any other.</param>
public sealed record PageSectionView(
    string Name, string Code, string? Changed, long Uses, string Body, IReadOnlyList<SourceCommentView> Sources, RecognizedSection? Recognized);
