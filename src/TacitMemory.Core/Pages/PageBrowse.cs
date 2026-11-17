using System.Globalization;
using TacitMemory.Core.Formats;
using TacitMemory.Core.Index;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Retrieval;

namespace TacitMemory.Core.Pages;

/// <summary>
/// The detail pages for the memory page, read from the files as they are now. The index follows each file first, so the
/// section dates and counts are current; nothing is counted.
/// </summary>
internal static class PageBrowse
{
    /// <summary>Every page file under the pages folder whose name is a page ID, ordered by ID.</summary>
    public static PageList List(TacitConfig config, MemoryIndex index)
    {
        var uses = index.PageUsages().ToDictionary(row => row.Page, row => row.Count, StringComparer.Ordinal);
        var pages = new List<PageListItem>();
        foreach (var id in PageIds(config.MemoryPath))
        {
            var document = PageDocument.TryLoad(config.MemoryPath, id);
            if (document is not null)
                index.SyncPage(id);
            var header = document?.Header ?? new PageHeader(id.Slug, [], null, null, [], null);
            var changes = document is null ? null : index.SectionChanges(id);
            var changed = changes is { Count: > 0 } ? changes.Values.Max() : header.Last;
            var (type, written) = PageDocument.BrowseTypeOf(header.Type);
            pages.Add(new PageListItem(
                id.ToString(), id.Workstream, header.Title, header.Aliases, type, written, Day(header.First), Day(header.Last), Day(changed),
                uses.GetValueOrDefault(id.ToString())));
        }

        return new PageList(pages);
    }

    /// <summary>The page <paramref name="workstream"/>/<paramref name="slug"/>.</summary>
    /// <exception cref="TacitException">
    /// <see cref="TacitError.RequestInvalid"/> when the two do not make a page ID; <see cref="TacitError.MemoryNotFound"/>
    /// when there is no such page.
    /// </exception>
    public static PageView Read(TacitConfig config, MemoryIndex index, MemoryReader reader, string workstream, string slug)
    {
        var raw = $"{workstream}/{slug}";
        if (!PageId.TryParse(raw, out var id, out var problem))
            throw new TacitException(TacitError.RequestInvalid, RequestMessages.PageIdInvalid.Format(config.Language, raw, problem));
        if (!File.Exists(MemoryLayout.Full(config.MemoryPath, id.RelativePath)))
            throw new TacitException(TacitError.MemoryNotFound, RequestMessages.PageNotFound.Format(config.Language, raw));

        var document = reader.Page(id, Usage.NotCounted).Document;
        var header = document?.Header ?? new PageHeader(id.Slug, [], null, null, [], null);
        var changes = index.SectionChanges(id);
        var returns = index.SectionReturns(id);
        var uses = index.PageUsages().FirstOrDefault(row => row.Page == id.ToString())?.Count ?? 0;
        var related = header.Related
            .Select(entry => new RelatedPage(entry, PageId.TryParse(entry, out var target, out _) && File.Exists(MemoryLayout.Full(config.MemoryPath, target.RelativePath))))
            .ToList();
        var sections = (document?.Sections ?? [])
            .Select(section => new PageSectionView(
                section.Name, section.Code, Day(changes.TryGetValue(section.Name, out var day) ? day : null), returns.GetValueOrDefault(section.Name), section.Body,
                SourceComment.In(section.Body), PageDocument.RecognizedOf(section.Name)))
            .ToList();
        var (type, written) = PageDocument.BrowseTypeOf(header.Type);
        return new PageView(
            id.ToString(), id.Workstream, header.Title, header.Aliases, type, written, Day(header.First), Day(header.Last), related,
            MemoryFileReport.Of(config, id.RelativePath), uses, sections);
    }

    private static IEnumerable<PageId> PageIds(string memoryPath)
    {
        var folder = MemoryLayout.Full(memoryPath, MemoryLayout.Pages);
        if (!Directory.Exists(folder))
            return [];
        return Directory.EnumerateFiles(folder, "*.md", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(folder, file).Replace('\\', '/'))
            .Select(relative => PageId.TryParse(relative[..^".md".Length], out var id, out _) ? id : (PageId?)null)
            .OfType<PageId>()
            .OrderBy(id => id.ToString(), StringComparer.Ordinal);
    }

    private static string? Day(DateOnly? day) => day?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
