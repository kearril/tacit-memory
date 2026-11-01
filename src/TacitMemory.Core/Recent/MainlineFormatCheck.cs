using TacitMemory.Core.Checkup;
using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Recent;

/// <summary>The format problems of <c>recent.md</c>'s mainline: more items than <c>limits.mainline</c> allows.</summary>
internal static class MainlineFormatCheck
{
    /// <summary>Finds the problems in <paramref name="text"/>; the file must already be valid UTF-8.</summary>
    public static IReadOnlyList<FormatIssue> Check(string text, TacitConfig config)
    {
        var count = MainlineSection.Items(text).Count;
        var limit = config.Limits.Mainline;
        if (count <= limit)
            return [];

        return
        [
            new FormatIssue("", $"section \"{StructureWords.Mainline.In(config.Language)}\"", "Mainline over limit",
                $"The mainline has {count} items; the limit is {limit}. The user decides which to keep, and the mainline is rewritten from there.",
                FormatHandling.AskUser),
        ];
    }
}
