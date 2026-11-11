using System.Globalization;
using TacitMemory.Core.Pages;
using TacitMemory.Core.Recent;
using TacitMemory.Core.Workstreams;

namespace TacitMemory.Core.Consolidation;

/// <summary>
/// The "Workstream proposal material" category of part B, computed without a model. It lists three things the agent drafts
/// its workstream proposals from: the names that are not established yet (page folders and activity labels that
/// <c>workstreams.md</c> does not write), the topic pages whose slugs share a prefix, and the activities since the baseline that
/// carry no workstream label. Nothing here decides anything; the user confirms each proposal.
/// </summary>
internal static class WorkstreamProposals
{
    /// <summary>How many topic pages must share a first slug segment before that group is listed.</summary>
    private const int MinimumGroup = 3;

    public static async Task<ConsolidationSection> ProduceAsync(ConsolidationContext context, CancellationToken cancellationToken)
    {
        var memory = context.Config.MemoryPath;
        var written = WorkstreamFile.Read(memory).Names.ToHashSet(StringComparer.Ordinal);
        var fresh = (await context.NewActivitiesAsync(cancellationToken)).Select(item => item.Activity).ToList();
        var pages = PageFolderCounts(memory);
        var activities = fresh
            .Where(activity => activity.Workstream is not null)
            .GroupBy(activity => activity.Workstream!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        var findings = new List<string>();
        foreach (var name in pages.Keys.Union(activities.Keys, StringComparer.Ordinal).Where(name => !written.Contains(name)).Order(StringComparer.Ordinal))
            findings.Add($"- {name} (unestablished): pages {pages.GetValueOrDefault(name)}, activities {activities.GetValueOrDefault(name)}.");

        findings.AddRange(PrefixGroups(memory));

        foreach (var activity in fresh.Where(activity => activity.Workstream is null).OrderBy(activity => activity.Day).ThenBy(activity => activity.Line))
            findings.Add($"- {Day(activity.Day)} activity [{activity.Code}] without a workstream: {activity.Body}");

        return new ConsolidationSection(ConsolidationTitles.WorkstreamProposals, findings);
    }

    /// <summary>
    /// The number of pages in each workstream folder under <c>pages/</c>. The topics folder, invalid names and folders that hold
    /// no page files are left out.
    /// </summary>
    private static Dictionary<string, int> PageFolderCounts(string memory)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var folder = Path.Combine(memory, "pages");
        if (!Directory.Exists(folder))
            return counts;

        foreach (var directory in Directory.EnumerateDirectories(folder))
        {
            var name = Path.GetFileName(directory);
            if (name == PageId.Topics || !PageId.IsWorkstreamName(name))
                continue;
            var pageCount = Directory.EnumerateFiles(directory, "*.md", SearchOption.TopDirectoryOnly).Count();
            if (pageCount > 0)
                counts[name] = pageCount;
        }

        return counts;
    }

    /// <summary>
    /// The topic pages grouped by the first segment of their slug, for each group of at least three pages: the prefix the whole
    /// group still shares, segment by segment, and the page IDs of the group.
    /// </summary>
    private static List<string> PrefixGroups(string memory)
    {
        var lines = new List<string>();
        var folder = Path.Combine(memory, "pages", PageId.Topics);
        if (!Directory.Exists(folder))
            return lines;

        var slugs = Directory.EnumerateFiles(folder, "*.md", SearchOption.TopDirectoryOnly)
            .Select(file => Path.GetFileNameWithoutExtension(file)!)
            .ToList();
        foreach (var group in slugs.GroupBy(slug => slug.Split('-')[0], StringComparer.Ordinal).Where(group => group.Count() >= MinimumGroup).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var segments = group.Select(slug => slug.Split('-')).ToList();
            var shared = new List<string>();
            for (var index = 0; segments.All(parts => parts.Length > index) && segments.All(parts => parts[index] == segments[0][index]); index++)
                shared.Add(segments[0][index]);

            var pages = string.Join(", ", group.Order(StringComparer.Ordinal).Select(slug => $"{PageId.Topics}/{slug}"));
            lines.Add($"- prefix {string.Join('-', shared)}: {pages}.");
        }

        return lines;
    }

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
