namespace TacitMemory.Core.Localization;

/// <summary>
/// The fixed words that give memory files and <c>rules.md</c> their structure. They are written in the memory
/// language; parsers accept every language's form.
/// </summary>
internal static class StructureWords
{
    public static readonly Localized RedLines = new("红线", "Red lines");
    public static readonly Localized WorkingRules = new("协作规则", "Working rules");
    public static readonly Localized Preferences = new("偏好", "Preferences");
    public static readonly Localized Profile = new("档案", "Profile");
    public static readonly Localized Candidates = new("候选", "Candidates");
    public static readonly Localized RejectionList = new("拒绝列表", "Rejection list");
    public static readonly Localized Mainline = new("近期主线", "Mainline");
    public static readonly Localized Summary = new("汇总", "Summary");
    public static readonly Localized BelongsTo = new("归属：", "Belongs to: ");
    public static readonly Localized Quote = new("原话", "quote");
    public static readonly Localized Instead = new("改为", "instead");
    public static readonly Localized Rejected = new("拒绝", "rejected");
    public static readonly Localized WorthRecording = new("值得记", "Worth recording");
    public static readonly Localized NotRecorded = new("不记", "Not recorded");
    public static readonly Localized Domain = new("领域", "Domain");
    public static readonly Localized Project = new("项目", "Project");
    public static readonly Localized Trigger = new("触发", "Trigger");
}
