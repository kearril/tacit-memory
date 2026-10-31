using System.Globalization;
using System.Text;
using TacitMemory.Core.Index;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Pages;
using TacitMemory.Core.Tools;

namespace TacitMemory.Core.Retrieval;

/// <summary>
/// The <c>recall</c> tool: reads one page by ID, or searches pages and returns the best sections, merged per page.
/// It never changes a file.
/// </summary>
internal sealed class RecallTool(TacitConfig config, PageIndex index, PageTools pages)
{
    /// <summary>The answer when nothing matches; also a plain result, not an error.</summary>
    public const string NoMemory = "No relevant memory.";

    /// <summary>How many sections a page that matched only on its title or aliases shows.</summary>
    private const int PageLevelSections = 2;

    public ToolResult Run(RecallRequest request)
    {
        var hasQuery = !string.IsNullOrWhiteSpace(request.Query);
        var hasPage = !string.IsNullOrWhiteSpace(request.Page);
        if (hasQuery == hasPage)
            return ToolText.Refuse(
                hasQuery ? "Give query or page, not both." : "Missing parameter query or page.",
                "Call recall again with query (what to look for, in the memory language) or page (a page ID to read in full).");
        if (hasPage)
            return pages.Read(request);

        var problems = new List<string>();
        var from = Date(request.From, "from", problems);
        var to = Date(request.To, "to", problems);
        if (from > to)
            problems.Add("from is later than to.");
        var kind = request.Kind?.Trim();
        if (kind is { Length: > 0 } and not ("pages" or "activities"))
            problems.Add($"Unknown kind {ToolText.Quote(kind)}; use pages or activities.");
        if (problems.Count > 0)
            return ToolText.Refuse(problems, "Fix these and call recall again.");
        if (kind == "activities")
            return ToolText.Ok(NoMemory);

        var project = string.IsNullOrWhiteSpace(request.Project) ? null : request.Project.Trim().ToLowerInvariant();
        var hits = index.Search(request.Query!, new IndexFilter(project, from, to), config.Retrieval.Candidates);
        return ToolText.Ok(Format(hits));
    }

    private string Format(IReadOnlyList<IndexHit> hits)
    {
        var results = hits.GroupBy(hit => hit.PageId).Take(config.Retrieval.TopK).ToList();
        var text = new StringBuilder();
        var shown = 0;
        foreach (var result in results)
        {
            if (!PageId.TryParse(result.Key, out var id, out _) || TextFile.Read(Path.Combine(config.MemoryPath, id.RelativePath)) is not { } read)
                continue;

            var document = PageDocument.Parse(read.Text, read.HasBom, id.Slug);
            var matched = result.Select(hit => hit.Section).OfType<string>().ToHashSet(StringComparer.Ordinal);
            var sections = matched.Count > 0
                ? document.Sections.Where(section => matched.Contains(section.Name)).ToList()
                : document.Sections.Take(PageLevelSections).ToList();
            var others = document.Sections.Except(sections).Select(section => section.Name).ToList();

            shown++;
            text.Append(CultureInfo.InvariantCulture, $"\n[{shown}] {id}: {document.Header.Title}\n");
            if (document.Header.First is { } first && document.Header.Last is { } last)
                text.Append(CultureInfo.InvariantCulture, $"Dates: {first:yyyy-MM-dd} to {last:yyyy-MM-dd}\n");
            foreach (var section in sections)
                text.Append(CultureInfo.InvariantCulture, $"### {section.Name} [{section.Code}]\n{section.Body}\n");
            if (others.Count > 0)
                text.Append(CultureInfo.InvariantCulture, $"Other sections: {string.Join(", ", others)}\n");
        }

        return shown == 0
            ? NoMemory
            : $"{shown} result(s). Short codes in brackets identify sections for write_page; read a whole page with recall page=<page ID>.\n{text}";
    }

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
