namespace TacitMemory.Core.Localization;

/// <summary>Human-facing text of the status report, the management API's refusals and the hints for commands that need the service.</summary>
internal static class ManagementApiMessages
{
    public static readonly Localized ConfigTitle = new("配置", "Config");

    public static readonly Localized ServiceTitle = new("服务", "Service");
    public static readonly Localized ServiceRunning = new("运行中", "Running");
    public static readonly Localized ServiceAddress = new("地址：{0}", "Address: {0}");
    public static readonly Localized ServiceVersion = new("版本：{0}", "Version: {0}");
    public static readonly Localized ServiceUptime = new("已运行：{0}", "Up for: {0}");
    public static readonly Localized ServiceNotRunning = new("服务未运行", "The service is not running");
    public static readonly Localized ServiceNotRunningOtherProgram = new(
        "服务未运行：端口 {0} 有其他程序在回答",
        "The service is not running: port {0} is answered by another program");
    public static readonly Localized UptimeDays = new("{0} 天", "{0} d");
    public static readonly Localized UptimeHours = new("{0} 小时", "{0} h");
    public static readonly Localized UptimeMinutes = new("{0} 分钟", "{0} min");
    public static readonly Localized UptimeSeconds = new("{0} 秒", "{0} s");

    public static readonly Localized ModelsTitle = new("辅助模型", "Auxiliary models");
    public static readonly Localized ModelAvailable = new("{0}：可用", "{0}: available");
    public static readonly Localized ModelCoolingDown = new("{0}：冷却中，剩余 {1} 秒", "{0}: cooling down, {1} s left");

    public static readonly Localized LogsTitle = new("日志", "Logs");
    public static readonly Localized LogsDirectory = new("目录：{0}", "Directory: {0}");

    public static readonly Localized NotRunningHint = new(
        "服务未运行，先执行 tacit serve。",
        "The service is not running. Run tacit serve first.");
    public static readonly Localized NotRunningHintOtherProgram = new(
        "端口 {0} 有其他程序在回答，常驻服务未运行。请先执行 tacit serve，或在 config.yaml 中修改 server.port。",
        "Port {0} is answered by another program, so the memory service is not running. Run tacit serve first, or change server.port in config.yaml.");

    public static readonly Localized ApiOriginRefused = new(
        "管理 API 不接受带 Origin 头的请求。",
        "The management API does not accept requests that carry an Origin header.");
    public static readonly Localized ApiHostRefused = new(
        "管理 API 只接受 Host 为 127.0.0.1:{0} 或 localhost:{0} 的请求。",
        "The management API only accepts requests whose Host is 127.0.0.1:{0} or localhost:{0}.");
    public static readonly Localized ApiContentTypeRefused = new(
        "管理 API 的 POST 请求必须是 Content-Type: application/json。",
        "POST requests to the management API must have Content-Type: application/json.");
    public static readonly Localized ApiBodyInvalid = new(
        "请求体无效：应为 JSON 对象，格式见管理 API 说明。",
        "The request body is invalid: it must be a JSON object, as the management API describes.");
    public static readonly Localized ApiBodyInvalidField = new(
        "请求体无效：字段 {0} 的类型或取值不对。",
        "The request body is invalid: the value of {0} has the wrong type or value.");

    public static readonly Localized BindingsTitle = new("绑定", "Bindings");
    public static readonly Localized BindingsNone = new("没有绑定的客户端。", "No client is bound.");
    public static readonly Localized BindingsUnreadable = new("bindings.json 无法读取：{0}", "bindings.json cannot be read: {0}");
    public static readonly Localized BindingOk = new("{0}：正常（上下文文件 {1}；skill 目录 {2}）", "{0}: fine (context file {1}; skills folder {2})");
    public static readonly Localized BindingContextFileMissing = new(
        "{0}：上下文文件不存在（{1}；skill 目录 {2}）",
        "{0}: the context file is missing ({1}; skills folder {2})");
    public static readonly Localized BindingContextFileNotUtf8 = new(
        "{0}：上下文文件不是 UTF-8（{1}；skill 目录 {2}）",
        "{0}: the context file is not UTF-8 ({1}; skills folder {2})");
    public static readonly Localized BindingContextFileUnreadable = new(
        "{0}：上下文文件无法读取（{1}；skill 目录 {2}）",
        "{0}: the context file cannot be read ({1}; skills folder {2})");
    public static readonly Localized BindingMarkersIncomplete = new(
        "{0}：常驻区块的起止标记不完整或重复（{1}；skill 目录 {2}）",
        "{0}: the resident block's markers are incomplete or repeated ({1}; skills folder {2})");
    public static readonly Localized BindingSkillLinkNotTacit = new(
        "{0}：skill 链接 {3} 缺失或没有指向 tacit（上下文文件 {1}；skill 目录 {2}）",
        "{0}: the skill link {3} is missing or does not point at tacit (context file {1}; skills folder {2})");

    public static readonly Localized CheckupTitle = new("上次体检", "Last checkup");
    public static readonly Localized CheckupOn = new("上次体检：{0}，{1}", "Last checkup: {0}, {1}");
    public static readonly Localized CheckupFromInit = new("尚未体检，从初始化提交（{0}）起算，{1}", "No checkup yet; counting from the init commit ({0}), {1}");
    public static readonly Localized CheckupNever = new("从未体检（仓库没有 checkup: 或 init: 提交）", "Never checked (the repository has no checkup: or init: commit)");
    public static readonly Localized DaysAgo = new("距今 {0} 天", "{0} days ago");
    public static readonly Localized DayAgo = new("距今 1 天", "1 day ago");
    public static readonly Localized CheckupUnreadable = new("无法读取记忆仓库的历史：{0}", "The repository's history cannot be read: {0}");

    public static readonly Localized PendingTitle = new("待处理问题", "Pending issues");
    public static readonly Localized PendingConfirmable = new("可确认的候选：{0}", "Candidates that can be confirmed: {0}");
    public static readonly Localized PendingConfirmableUnknown = new(
        "可确认的候选：未统计（candidates.md 不是 UTF-8）",
        "Candidates that can be confirmed: not counted (candidates.md is not UTF-8)");
    public static readonly Localized PendingFormatProblems = new("格式问题：{0}", "Format and rule problems: {0}");
    public static readonly Localized PendingMainlineStale = new("主线：已过期", "Mainline: stale");
    public static readonly Localized PendingMainlineFresh = new("主线：未过期", "Mainline: not stale");
    public static readonly Localized PendingUnreadable = new("无法统计待处理问题：{0}", "Pending issues cannot be counted: {0}");


    public static readonly Localized SecretsTitle = new("密钥", "Secrets");
    public static readonly Localized SecretFromEnvironment = new("{0}：来自环境变量 {1}", "{0}: from the environment variable {1}");
    public static readonly Localized SecretFromEnvironmentOverridden = new(
        "{0}：来自环境变量 {1}，配置文件中的值未生效",
        "{0}: from the environment variable {1}; the value in config.yaml is ignored");
    public static readonly Localized SecretFromConfig = new("{0}：来自配置文件", "{0}: from config.yaml");
    public static readonly Localized SecretMissing = new(
        "{0}：缺失（环境变量 {1} 与配置文件都没有）",
        "{0}: missing (neither the environment variable {1} nor config.yaml holds it)");
    public static readonly Localized SecretWebDavNotConfigured = new("backup.webdav：未配置，不需要密钥", "backup.webdav: not configured, no secret needed");

    public static readonly Localized ApiFileFailed = new(
        "无法读写文件或文件夹：{0}",
        "A file or folder cannot be read or written: {0}");
    public static readonly Localized ApiInternalFailure = new(
        "服务内部出错，详情见 logs 目录中的日志。",
        "The service failed unexpectedly; the details are in the logs folder.");
}
