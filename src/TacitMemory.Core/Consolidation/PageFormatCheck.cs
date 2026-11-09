using TacitMemory.Core.Pages;

namespace TacitMemory.Core.Consolidation;

/// <summary>
/// The format checks of one detail page file: its name against the page file rule, its content against the page limits
/// (<see cref="PageDocument.Problems"/>), and its related pages, which must exist.
/// </summary>
internal static class PageFormatCheck
{
    /// <param name="relative">The file's path relative to the memory repository, with forward slashes.</param>
    /// <param name="text">The file's text, valid UTF-8, without a byte order mark.</param>
    public static IReadOnlyList<FormatIssue> Check(string memoryPath, string relative, string text, PageLimits limits)
    {
        var named = TryPageIdOf(relative, out var id);
        var slug = named ? id.Slug : Path.GetFileNameWithoutExtension(relative);
        var issues = new List<FormatIssue>();
        if (!named)
            issues.Add(new FormatIssue("", "file name", "file name",
                $"A page file must be named pages/<project>/<slug>.md; the service does not read {relative} as a page.",
                FormatHandling.FixDirectly));

        var document = PageDocument.Parse(text, hasBom: false, slug);
        issues.AddRange(document.Problems(limits));
        foreach (var related in document.Header.Related)
        {
            if (!PageId.TryParse(related, out var target, out _) || !File.Exists(Path.Combine(memoryPath, target.RelativePath)))
                issues.Add(new FormatIssue("", document.LocationOf("related"), "related",
                    $"related lists \"{related}\", which is not an existing page.", FormatHandling.FixDirectly));
        }
        return issues;
    }

    /// <summary>The page ID that a path names, when the path is exactly <c>pages/&lt;project&gt;/&lt;slug&gt;.md</c>.</summary>
    private static bool TryPageIdOf(string relative, out PageId id)
    {
        var parts = relative.Split('/');
        id = null!;
        return parts.Length == 3
            && PageId.TryParse($"{parts[1]}/{Path.GetFileNameWithoutExtension(parts[2])}", out id, out _)
            && id.RelativePath == relative;
    }
}
