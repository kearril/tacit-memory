using TacitMemory.Core.Localization;
using TacitMemory.Core.Pages;

namespace TacitMemory.Core.Consolidation;

/// <summary>
/// The two categories that read the pages changed since the baseline, without a model. "Changed pages" gives each page's path,
/// type, size, section dates, related pages and use count, never its body. "Incident lessons" quotes the lesson section of each
/// changed incident page. Both are listed oldest first.
/// </summary>
internal static class ChangedPageFindings
{
    /// <summary>The type label of a page without a type; the report is for the agent, so the label is English as in the brief.</summary>
    private const string Unclassified = "untyped";

    public static (ConsolidationSection Lessons, ConsolidationSection Changed) Produce(
        ConsolidationContext context, ConsolidationServices services, IReadOnlyList<PageId> pages)
    {
        var memory = context.Config.MemoryPath;
        var uses = services.Index.PageUsages().ToDictionary(row => row.Page, row => row.Count, StringComparer.Ordinal);
        var changed = new List<(DateOnly Latest, string Id, string Text)>();
        var lessons = new List<(DateOnly Day, string Id, string Text)>();

        foreach (var id in pages)
        {
            if (PageDocument.TryLoad(memory, id) is not { } document)
                continue;
            services.Index.SyncPage(id);
            var dates = services.Index.SectionChanges(id);
            var latest = dates.Count > 0 ? dates.Values.Max() : document.Header.Last ?? DateOnly.MinValue;

            var size = new FileInfo(Path.Combine(memory, id.RelativePath)).Length;
            var sections = document.Sections.Count == 0
                ? "none"
                : string.Join(", ", document.Sections.Select(section => dates.TryGetValue(section.Name, out var day) ? $"{section.Name} ({day:yyyy-MM-dd})" : section.Name));
            var related = document.Header.Related.Count == 0 ? "none" : "[" + string.Join(", ", document.Header.Related) + "]";
            var type = document.Header.Type ?? Unclassified;
            var count = uses.GetValueOrDefault(id.ToString());
            changed.Add((latest, id.ToString(), $"- {id} ({id.RelativePath}): type: {type}; {size} bytes; sections: {sections}; related: {related}; uses: {count}."));

            if (document.Header.Type == ToolValues.PageTypes.Incident
                && document.Sections.FirstOrDefault(section => StructureWords.Lesson.Matches(section.Name)) is { Body.Length: > 0 } lesson)
            {
                var day = dates.TryGetValue(lesson.Name, out var lessonDay) ? lessonDay : latest;
                var body = string.Join('\n', lesson.Body.Split('\n').Select(line => "  " + line));
                lessons.Add((day, id.ToString(), $"- {id} ({day:yyyy-MM-dd}):\n{body}"));
            }
        }

        return (
            new ConsolidationSection(ConsolidationTitles.IncidentLessons, lessons.OrderBy(item => item.Day).ThenBy(item => item.Id, StringComparer.Ordinal).Select(item => item.Text).ToList()),
            new ConsolidationSection(ConsolidationTitles.ChangedPages, changed.OrderBy(item => item.Latest).ThenBy(item => item.Id, StringComparer.Ordinal).Select(item => item.Text).ToList()));
    }
}
