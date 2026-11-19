using System.Net;

namespace TacitMemory.Core.Localization;

/// <summary>Human-facing text of the status report, the bindings check and the hints for commands that need the service.</summary>
internal static class StatusMessages
{
    public static readonly Localized ConfigTitle = new("配置", "Config");

    public static readonly Localized ServiceTitle = new("服务", "Service");

    public static readonly Localized ServiceRunning = new("运行中", "Running");

    public static readonly Localized ServiceAddress = new("地址：{0}", "Address: {0}");

    public static readonly Localized ServiceVersion = new("版本：{0}", "Version: {0}");

    public static readonly Localized ServiceUptime = new("已运行：{0}", "Up for: {0}");

    public static readonly Localized ServiceNotRunning = new("服务未运行", "The service is not running");

    public static readonly Localized ServiceNotRunningOtherProgram = new(
        "服务未运行：端口 {0} 已被其他程序占用",
        "The service is not running: port {0} is answered by another program");

    public static readonly Localized UptimeDays = new("{0} 天", "{0} d");

    public static readonly Localized UptimeHours = new("{0} 小时", "{0} h");

    public static readonly Localized UptimeMinutes = new("{0} 分钟", "{0} min");

    public static readonly Localized UptimeSeconds = new("{0} 秒", "{0} s");

    public static readonly Localized ModelsTitle = new("辅助模型", "Auxiliary models");

    public static readonly Localized ModelAvailable = new("{0}：可用", "{0}: available");

    public static readonly Localized ModelCoolingDown = new("{0}：冷却中，剩余 {1} 秒", "{0}: cooling down, {1} s left");

    public static readonly Localized ModelUnconfigured = new("{0}：未配置", "{0}: not configured");

    public static readonly Localized ClientsTitle = new("客户端", "Clients");

    public static readonly Localized ClientsNone = new("尚无客户端连接过服务。", "No client has connected to the service yet.");

    public static readonly Localized ClientsUnreadable = new("无法读取客户端能力记录：{0}", "The client record cannot be read: {0}");

    public static readonly Localized ClientLine = new(
        "{0}：最近连接 {1}，协议 {2}，能力：{3}，扩展：{4}",
        "{0}: last connected {1}, protocol {2}, capabilities: {3}, extensions: {4}");

    public static readonly Localized ClientNothingDeclared = new("无", "none");

    public static readonly Localized LogsTitle = new("日志", "Logs");

    public static readonly Localized LogsDirectory = new("目录：{0}", "Directory: {0}");

    public static readonly Localized LogsToday = new("今天：{0} 条警告，{1} 条错误", "Today: {0} warnings, {1} errors");

    public static readonly Localized LogsTodayQuiet = new("今天没有警告与错误", "No warnings or errors today");

    public static readonly Localized LogsTodayUnreadable = new("无法读取今天的日志：{0}", "Today's log cannot be read: {0}");

    public static readonly Localized NotRunningHint = new(
        "服务未运行，先执行 tacit serve。",
        "The service is not running. Run tacit serve first.");

    public static readonly Localized NotRunningHintOtherProgram = new(
        "端口 {0} 已被其他程序占用，常驻服务未运行。请先执行 tacit serve，或在 config.yaml 中修改 server.port。",
        "Port {0} is answered by another program, so the memory service is not running. Run tacit serve first, or change server.port in config.yaml.");

    public static readonly Localized WebAddress = new("Web 界面：{0}", "Web interface: {0}");

    public static readonly Localized ExtraListenerAddress = new("额外监听：{0}", "Extra listener: {0}");

    public static readonly Localized ExtraListenerAllInterfaces = new("所有网络接口，端口 {0}", "all network interfaces, port {0}");

    public static readonly Localized ExtraListenerFailed = new("额外监听失败：{0}，{1}", "Extra listener failed: {0}, {1}");

    public static readonly Localized ExtraListenerNotRunning = new("额外监听：{0}（服务未运行）", "Extra listener: {0} (the service is not running)");

    public static readonly Localized BindingsTitle = new("绑定", "Bindings");

    public static readonly Localized PromptsTitle = new("提示词文档有误", "Prompt documents with errors");

    public static readonly Localized PromptKept = new(
        "{0} 有误，服务正在使用上一版：{1}",
        "{0} has an error, and the service runs its previous version: {1}");

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

    public static readonly Localized BindingSharedWith = new("（与 {0} 共用路径）", " (shares paths with {0})");

    public static readonly Localized ConsolidationTitle = new("上次整理", "Last consolidation");

    public static readonly Localized ConsolidationOn = new("上次整理：{0}，{1}", "Last consolidation: {0}, {1}");

    public static readonly Localized ConsolidationFromInit = new("尚未整理，从初始化提交（{0}）起算，{1}", "No consolidation yet; counting from the init commit ({0}), {1}");

    public static readonly Localized ConsolidationNever = new("从未整理（仓库没有 consolidate: 或 init: 提交）", "Never checked (the repository has no consolidate: or init: commit)");

    public static readonly Localized DaysAgo = new("距今 {0} 天", "{0} days ago");

    public static readonly Localized DayAgo = new("距今 1 天", "1 day ago");

    public static readonly Localized ConsolidationUnreadable = new("无法读取记忆仓库的历史：{0}", "The repository's history cannot be read: {0}");

    public static readonly Localized PendingTitle = new("待处理问题", "Pending issues");

    public static readonly Localized PendingCandidates = new("候选：{0}", "Candidates: {0}");

    public static readonly Localized PendingCandidatesUnknown = new(
        "候选：未统计（candidates.md 不是 UTF-8）",
        "Candidates: not counted (candidates.md is not UTF-8)");

    public static readonly Localized PendingFormatProblems = new("格式问题：{0}", "Format and rule problems: {0}");

    public static readonly Localized PendingWorkstreamEmpty = new("主线：为空", "Workstream: empty");

    public static readonly Localized PendingWorkstreamSet = new("主线：已设定", "Workstream: set");

    public static readonly Localized PendingWeeks = new("待汇总的周：{0}", "Weeks to summarize: {0}");

    public static readonly Localized PendingUnreadable = new("无法统计待处理问题：{0}", "Pending issues cannot be counted: {0}");

    public static readonly Localized SecretsTitle = new("密钥", "Secrets");

    public static readonly Localized SecretFromEnvironment = new("{0}：来自环境变量 {1}", "{0}: from the environment variable {1}");

    public static readonly Localized SecretFromEnvironmentOverridden = new(
        "{0}：来自环境变量 {1}，配置文件中的值未生效",
        "{0}: from the environment variable {1}; the value in config.yaml is ignored");

    public static readonly Localized SecretFromConfig = new("{0}：来自配置文件", "{0}: from config.yaml");

    public static readonly Localized SecretMissing = new(
        "{0}：未设置（环境变量 {1} 与配置文件都没有）",
        "{0}: not set (neither the environment variable {1} nor config.yaml holds it)");

    public static readonly Localized SecretNotConfigured = new("{0}：未配置，不需要密钥", "{0}: not configured, no secret needed");

    /// <summary>
    /// The extra listener's address as people read it: a wildcard address names every network interface and the port,
    /// and any other address is shown as its URL.
    /// </summary>
    public static string ExtraListenerName(Uri address, MemoryLanguage language) =>
        IPAddress.TryParse(address.Host.Trim('[', ']'), out var host) && (host.Equals(IPAddress.Any) || host.Equals(IPAddress.IPv6Any))
            ? ExtraListenerAllInterfaces.Format(language, address.Port)
            : address.ToString();
}
