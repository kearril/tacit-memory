using System.Globalization;

namespace TacitMemory.Core.Tests;

/// <summary>The usage statistics as the memory page shows them; reading them counts nothing.</summary>
internal static class UsageViews
{
    /// <summary>How often <c>recall</c> has returned each page, by page ID, from the page list.</summary>
    public static async Task<IReadOnlyDictionary<string, long>> PageCountsAsync(this MemoryService service) =>
        (await service.Browse.PagesAsync()).Pages.ToDictionary(page => page.Id, page => page.Uses, StringComparer.Ordinal);

    /// <summary>One page's count and the last day it was returned, from its workstream's brief.</summary>
    public static async Task<BriefPage> PageUsageAsync(this MemoryService service, string page)
    {
        var brief = await service.Browse.WorkstreamBriefAsync(page[..page.IndexOf('/', StringComparison.Ordinal)]);
        return brief.Groups.SelectMany(group => group.Pages).Single(entry => entry.Id == page);
    }

    /// <summary>How often <c>recall</c> has returned each activity of <paramref name="day"/>'s month, by short code.</summary>
    public static async Task<IReadOnlyDictionary<string, long>> ActivityCountsAsync(this MemoryService service, DateOnly day) =>
        (await service.Browse.ActivityMonthAsync(day.ToString("yyyy-MM", CultureInfo.InvariantCulture))).Days
            .SelectMany(entry => entry.Activities)
            .ToDictionary(activity => activity.Code, activity => activity.Uses, StringComparer.Ordinal);

    /// <summary>A day as the views write it, yyyy-MM-dd.</summary>
    public static string Written(this DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
