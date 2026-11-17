namespace TacitMemory.Core.Localization;

/// <summary>Human-facing text of the refusals Core gives a request it cannot answer: a changed file, an unknown name, a value out of place.</summary>
internal static class RequestMessages
{
    public static readonly Localized ConfigChanged = new(
        "config.yaml 在你打开设置页之后被修改过，保存会覆盖这些修改。请重新载入后再改。",
        "config.yaml was changed after you opened the settings page, and saving would overwrite that change. Load it again, then make your changes.");

    public static readonly Localized SecretUnknown = new("{0} 不是密钥。", "{0} is not a secret.");

    public static readonly Localized PromptUnknown = new(
        "{0} 不是提示词文档。提示词文档有 instructions、rules、resident、skill。",
        "{0} is not a prompt document. The prompt documents are instructions, rules, resident and skill.");

    public static readonly Localized PromptChanged = new(
        "prompts/{0} 在你打开提示词页之后被修改过，保存会覆盖这些修改。请重新载入后再改。",
        "prompts/{0} was changed after you opened the prompts page, and saving would overwrite that change. Load it again, then make your changes.");

    public static readonly Localized LogDateInvalid = new("{0} 不是日期，请写成 2026-11-14 这样的形式。", "{0} is not a date; write it like 2026-11-14.");

    public static readonly Localized LogAfterInvalid = new("after 应为已读到的条数（0 或正整数），而不是 {0}。", "after is the number of events already read (0 or more), not {0}.");

    public static readonly Localized WorkstreamNotFound = new("没有主线 {0}：它既未成立，也没有细节页或活动。", "There is no workstream {0}: it is not established and has no pages or activities.");

    public static readonly Localized ActivityMonthInvalid = new("{0} 不是 yyyy-MM 格式的月份。", "{0} is not a month written yyyy-MM.");

    public static readonly Localized ActivityMonthNotFound = new("{0} 没有活动文件。", "There is no activity file for {0}.");

    public static readonly Localized PageNotFound = new("没有细节页 {0}。", "There is no page {0}.");

    public static readonly Localized PageIdInvalid = new("{0} 不是有效的页面标识：{1}", "{0} is not a valid page ID: {1}");

    public static readonly Localized LogDateNotFound = new("{0} 没有日志。", "There is no log for {0}.");

    public static readonly Localized SecretNotSecret = new("不是密钥，不能按密钥修改", "not a secret, so it cannot be changed as one");

    public static readonly Localized ConfigReadOnly = new(
        "设置页不能修改这一项：请在 config.yaml 中修改，重启 tacit serve 后生效。",
        "The settings page cannot change this: edit it in config.yaml, and it takes effect when tacit serve restarts.");

    public static readonly Localized ConfigExtraNeedsPassword = new(
        "额外监听地址让其他设备能连上服务，必须先在「访问」一节设置访问密码。",
        "An extra listener lets other devices reach the service; set an access password in the Access section first.");

    public static readonly Localized TraceQueryMissing = new("请填写查询。", "Enter a query.");

    public static readonly Localized TraceDateInvalid = new("{0} 不是 YYYY-MM-DD 格式的日期。", "{0} is not a date written YYYY-MM-DD.");

    public static readonly Localized TraceDatesReversed = new("起始日期晚于结束日期。", "The start date is later than the end date.");

    public static readonly Localized TraceKindInvalid = new("范围只能是 pages 或 activities，不能是 {0}。", "The kind must be pages or activities, not {0}.");

    public static readonly Localized TraceValueOutOfRange = new("{0} 应在 {1} 到 {2} 之间。", "{0} must be between {1} and {2}.");
}
