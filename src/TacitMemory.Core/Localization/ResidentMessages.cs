using System.Globalization;

namespace TacitMemory.Core.Localization;

/// <summary>
/// The words the resident block's generated lines use: the domain index rows and the workstream rows. The block's section
/// titles stay in English; these lines are written in the memory language.
/// </summary>
internal static class ResidentMessages
{
    private static readonly Localized EntriesOne = new("1 条", "1 entry");
    private static readonly Localized EntriesMany = new("{0} 条", "{0} entries");

    /// <summary>A domain index row: the label, the trigger and the entry count.</summary>
    public static readonly Localized DomainRow = new("- {0}：{1}（{2}）", "- {0}: {1} ({2})");

    /// <summary>A domain index row for a domain whose block has no trigger line.</summary>
    public static readonly Localized DomainRowWithoutTrigger = new("- {0}（{1}）", "- {0} ({1})");

    /// <summary>A workstream row for a quiet workstream, or an active one with no recent line: the name and the background.</summary>
    public static readonly Localized WorkstreamRow = new("- {0}：{1}", "- {0}: {1}");

    /// <summary>A workstream row for an active workstream that has a recent line: the name, the background and the recent line.</summary>
    public static readonly Localized WorkstreamRowWithRecent = new("- {0}：{1} 近况：{2}", "- {0}: {1} Recent: {2}");

    /// <summary>The entry count of a domain: "3 条" or "1 entry".</summary>
    public static string Entries(MemoryLanguage language, int count) =>
        (count == 1 ? EntriesOne : EntriesMany).Format(language, count.ToString(CultureInfo.InvariantCulture));
}
