using System.Globalization;

namespace TacitMemory.Core.Localization;

/// <summary>
/// The words the resident block's generated lines use: the domain index rows and the lines of the recent weeks. The block's
/// section titles stay in English; these lines are written in the memory language.
/// </summary>
internal static class ResidentMessages
{
    private static readonly Localized EntriesOne = new("1 条", "1 entry");
    private static readonly Localized EntriesMany = new("{0} 条", "{0} entries");
    private static readonly Localized ActivitiesOne = new("1 条活动", "1 activity");
    private static readonly Localized ActivitiesMany = new("{0} 条活动", "{0} activities");

    /// <summary>A domain index row: the label, the trigger and the entry count.</summary>
    public static readonly Localized DomainRow = new("- {0}：{1}（{2}）", "- {0}: {1} ({2})");

    /// <summary>A domain index row for a domain whose block has no trigger line.</summary>
    public static readonly Localized DomainRowWithoutTrigger = new("- {0}（{1}）", "- {0} ({1})");

    /// <summary>The workstream names of a week, after the activity count.</summary>
    public static readonly Localized WeekWorkstreams = new("，涉及主线 {0}", ", workstreams {0}");

    /// <summary>The activities of a week that have no workstream label, after the workstream names.</summary>
    public static readonly Localized WeekOther = new("，其他 {0} 条", ", other {0}");

    /// <summary>Joins names in a list.</summary>
    public static readonly Localized NameSeparator = new("、", ", ");

    /// <summary>The entry count of a domain: "3 条" or "1 entry".</summary>
    public static string Entries(MemoryLanguage language, int count) =>
        (count == 1 ? EntriesOne : EntriesMany).Format(language, count.ToString(CultureInfo.InvariantCulture));

    /// <summary>The activity count of a week: "5 条活动" or "1 activity".</summary>
    public static string Activities(MemoryLanguage language, int count) =>
        (count == 1 ? ActivitiesOne : ActivitiesMany).Format(language, count.ToString(CultureInfo.InvariantCulture));
}
