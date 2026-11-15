using System.Globalization;
using TacitMemory.Core.Formats;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Pages;
using TacitMemory.Core.Persona;
using TacitMemory.Core.Recent;
using TacitMemory.Core.Retrieval;

namespace TacitMemory.Core.Workstreams;

/// <summary>The workstreams for the memory page: the list, and one workstream's brief whole and structured. Counts nothing.</summary>
internal static class WorkstreamBrowse
{
    /// <summary>The order the brief lists the page types in; pages without a valid type come last.</summary>
    private static readonly string?[] GroupOrder =
        [ToolValues.PageTypes.Decision, ToolValues.PageTypes.Incident, ToolValues.PageTypes.Work, ToolValues.PageTypes.Entity, null];

    public static WorkstreamList List(TacitConfig config)
    {
        var established = WorkstreamFile.Established(config.MemoryPath);
        var names = established.Select(entry => entry.Name).ToHashSet(StringComparer.Ordinal);
        var pages = PageCounts(config.MemoryPath);
        var latest = RecentFile.AllActivities(config.MemoryPath)
            .Where(activity => activity.Workstream is not null)
            .GroupBy(activity => activity.Workstream!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Max(activity => activity.Day), StringComparer.Ordinal);

        WorkstreamListItem Item(string name, string? background, string? recent) =>
            new(name, background, recent, pages.GetValueOrDefault(name), latest.TryGetValue(name, out var day) ? Day(day) : null);

        return new WorkstreamList(
            [.. established.Select(entry => Item(entry.Name, entry.Background, entry.Recent))],
            [.. pages.Keys.Union(latest.Keys, StringComparer.Ordinal)
                .Where(name => !names.Contains(name) && PageId.IsWorkstreamName(name))
                .Order(StringComparer.Ordinal)
                .Select(name => Item(name, null, null))],
            MemoryFileReport.Of(config, WorkstreamFile.RelativePath));
    }

    /// <summary>The brief of <paramref name="workstream"/>.</summary>
    /// <exception cref="TacitException">
    /// <see cref="TacitError.MemoryNotFound"/> when it is neither established nor used by a page or an activity.
    /// </exception>
    public static WorkstreamBriefView Brief(TacitConfig config, WorkstreamBrief brief, string workstream)
    {
        if (!PageId.IsWorkstreamName(workstream) || brief.Gather(workstream, null, null) is not { } parts)
            throw new TacitException(TacitError.MemoryNotFound, ManagementApiMessages.WorkstreamNotFound.Format(config.Language, workstream));

        var established = WorkstreamFile.Established(config.MemoryPath).Any(entry => entry.Name == workstream);
        var conventions = parts.Conventions is { } block
            ? PersonaBrowse.BlockSections(block, (PersonaDocument.Read(Path.Combine(config.MemoryPath, PersonaFiles.Persona)) ?? new PersonaDocument([], [])).CodesByLine())
            : [];
        var groups = GroupOrder
            .Select(type => new BriefPageGroup(type, [.. parts.Entries.Where(entry => TypeOf(entry) == type).Select(Page)]))
            .Where(group => group.Pages.Count > 0)
            .ToList();
        return new WorkstreamBriefView(
            workstream,
            established,
            parts.Background,
            parts.Recent,
            conventions,
            [.. parts.OpenItems.Select(item => new OpenItemView(item.Id.ToString(), item.Lines))],
            groups,
            [.. parts.Overturned.Select(id => id.ToString())],
            [.. parts.Activities.Select(activity => new BriefActivity(Day(activity.Day), activity.Code, activity.Body))],
            parts.Summary is { } summary ? new BriefSummary(summary.Label, Day(summary.First), Day(summary.Last), summary.Body) : null);
    }

    private static string? TypeOf(WorkstreamBrief.Entry entry) =>
        PageDocument.IsPageType(entry.Document.Header.Type) ? entry.Document.Header.Type : null;

    private static BriefPage Page(WorkstreamBrief.Entry entry) =>
        new(entry.Id.ToString(), entry.Document.Header.Title, entry.Document.Header.Aliases, entry.Changed is { } changed ? Day(changed) : null, entry.Uses, entry.LastUse);

    /// <summary>The number of page files in each workstream folder; topics and folders that are not a workstream name are left out.</summary>
    private static Dictionary<string, int> PageCounts(string memoryPath)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var folder = Path.Combine(memoryPath, PageId.Folder);
        if (!Directory.Exists(folder))
            return counts;
        foreach (var directory in Directory.EnumerateDirectories(folder))
        {
            var name = Path.GetFileName(directory);
            if (name == PageId.Topics || !PageId.IsWorkstreamName(name))
                continue;
            var count = Directory.EnumerateFiles(directory, "*.md", SearchOption.TopDirectoryOnly).Count();
            if (count > 0)
                counts[name] = count;
        }

        return counts;
    }

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
