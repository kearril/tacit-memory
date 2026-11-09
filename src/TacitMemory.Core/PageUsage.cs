namespace TacitMemory.Core;

/// <summary>
/// How often <c>recall</c> has returned a page's units. A unit counts when a search result shows it, and reading a whole page
/// counts each of its sections once. The statistics live in the derived index only: <c>reindex</c> resets them, and they never
/// affect the order of results.
/// </summary>
/// <param name="Page">The page ID, such as <c>my-app/db-upgrade</c>.</param>
/// <param name="Count">The number of returns of the page's units.</param>
/// <param name="LastReturned">The last day any of the page's units was returned; <c>null</c> when none was.</param>
internal sealed record PageUsage(string Page, int Count, DateOnly? LastReturned);
