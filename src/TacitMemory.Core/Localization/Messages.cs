namespace TacitMemory.Core.Localization;

/// <summary>Short human-facing messages: CLI output and refusals.</summary>
internal static class Messages
{
    public static readonly Localized InitRepositoryCreated = new("已创建记忆仓库：{0}", "Created the memory repository: {0}");
    public static readonly Localized InitRepositoryExists = new("记忆仓库已存在：{0}", "The memory repository already exists: {0}");
    public static readonly Localized InitCreated = new("已创建：{0}", "Created: {0}");
    public static readonly Localized InitExisting = new("已存在，未改动：{0}", "Already exists, left unchanged: {0}");
    public static readonly Localized InitSkeletonNotCommitted = new(
        "补上的骨架文件未提交，请在记忆仓库中自行提交。",
        "The added skeleton files are not committed; commit them in the memory repository yourself.");
    public static readonly Localized InitNextSteps = new(
        "下一步：按需在 {0} 中填写 models 一节（可省略，见注释），然后用 tacit connect --client <名称> --context <文件> --skills <目录> 绑定客户端，最后执行 tacit serve。",
        "Next: fill in the models section of {0} if you want models (optional, see its comments), then bind a client with tacit connect --client <name> --context <file> --skills <folder>, and run tacit serve.");

    public static readonly Localized MemoryDirectoryNotEmpty = new(
        "目录 {0} 不为空且不是 Git 仓库，不能用作记忆仓库。请指定空目录或已有的记忆仓库（--memory）。",
        "The directory {0} is not empty and is not a Git repository, so it cannot be the memory repository. Choose an empty directory or an existing memory repository (--memory).");
    public static readonly Localized ConflictsWithConfig = new(
        "{0} 已将 {1} 设为 {2}，与传入的 {3} 不同。请先修改配置，或不传该选项。",
        "{0} already sets {1} to {2}, which differs from the given {3}. Edit the config first, or leave the option out.");
    public static readonly Localized GitFailed = new("git {0} 执行失败：{1}", "git {0} failed: {1}");
    public static readonly Localized NoBuiltInText = new(
        "{0} 没有内置默认文本，请去掉 --default。",
        "{0} has no built-in text; leave out --default.");

    public static readonly Localized NotInitializedConfig = new(
        "找不到 {0}。请先执行 tacit init。",
        "{0} does not exist. Run tacit init first.");
    public static readonly Localized NotInitializedMemory = new(
        "记忆仓库 {0} 不存在或不是 Git 仓库。请先执行 tacit init，或修改 memory.path。",
        "The memory repository {0} does not exist or is not a Git repository. Run tacit init first, or change memory.path.");
    public static readonly Localized ConfigUnparsable = new(
        "{0} 第 {1} 行第 {2} 列无法解析：{3}",
        "{0} cannot be parsed at line {1}, column {2}: {3}");
    public static readonly Localized ConfigErrors = new("{0} 有误：", "{0} has errors:");
    public static readonly Localized ConfigErrorLine = new("- {0}：{1}", "- {0}: {1}");
    public static readonly Localized ConfigUnknownKey = new("不认识的配置项", "unknown setting");
    public static readonly Localized ConfigRequired = new("必填，不能为空", "required and must not be empty");
    public static readonly Localized ConfigExpectedMapping = new("应为一组键值", "must be a mapping of keys to values");
    public static readonly Localized ConfigExpectedText = new("应为文本", "must be text");
    public static readonly Localized ConfigExpectedInteger = new("应为 {0} 到 {1} 之间的整数，实际为 {2}", "must be a whole number from {0} to {1}, but is {2}");
    public static readonly Localized ConfigExpectedProbability = new("应为 0 到 1 之间的数，实际为 {0}", "must be a number from 0 to 1, but is {0}");
    public static readonly Localized ConfigExpectedUrl = new("应为 http 或 https 地址，实际为 {0}", "must be an http or https URL, but is {0}");
    public static readonly Localized ConfigExpectedIpAddress = new(
        "应为一个 IP 地址，如 192.168.1.10 或 0.0.0.0（不接受主机名），实际为 {0}",
        "must be one IP address, such as 192.168.1.10 or 0.0.0.0 (host names are not accepted), but is {0}");
    public static readonly Localized ConfigExpectedLanguage = new("应为 zh 或 en，实际为 {0}", "must be zh or en, but is {0}");
    public static readonly Localized ConfigExpectedProvider = new(
        "可用取值为 cloudflare（省略即为通用协议），实际为 {0}",
        "the values are cloudflare (omit it for the generic protocol), but is {0}");
    public static readonly Localized ConfigSecretMissing = new(
        "未设置，请写入此键或设置环境变量 {0}",
        "not set; write it here or set the environment variable {0}");
    public static readonly Localized ConfigThresholdOrder = new(
        "unlikely（{0}）应小于 certain（{1}）",
        "unlikely ({0}) must be lower than certain ({1})");
    public static readonly Localized RulesTooMany = new(
        "{0} 中「{1}」一节有 {2} 条，上限为 {3} 条，多出 {4} 条。",
        "The \"{1}\" section of {0} has {2} items, {4} more than the limit of {3}.");
    public static readonly Localized InstructionsTooLong = new(
        "服务端说明的固定部分共 {1} 字符，上限为 {2} 字符，超出 {3} 字符。请精简以下文件中的内容：{0}。",
        "The fixed part of the server instructions is {1} characters, {3} over the limit of {2}. Shorten the content of: {0}.");
    public static readonly Localized PromptsNotApplied = new(
        "提示词文档的修改未生效，新会话仍用之前的内容：{0}",
        "The change to the prompt documents was not applied; new sessions keep the previous ones: {0}");
    public static readonly Localized SkillHasFrontmatter = new(
        "{0} 不能以 frontmatter（开头的 --- 部分）起始：skill 的 name 与 description 由 TacitMemory 提供，无法修改。请删去该部分。",
        "{0} must not start with frontmatter: TacitMemory provides the skill's name and description. Remove the leading --- block.");
    public static readonly Localized SkillTooLong = new(
        "{0} 共 {1} 字符，上限为 {2} 字符，超出 {3} 字符。请精简其中的内容。",
        "{0} is {1} characters, {3} over the limit of {2}. Shorten its content.");
    public static readonly Localized ResidentTooLong = new(
        "{0} 共 {1} 字符，上限为 {2} 字符，超出 {3} 字符。请精简该文件。",
        "{0} is {1} characters, {3} over the limit of {2}. Shorten it.");
    public static readonly Localized ResidentUnknownSection = new(
        "{0} 第 {1} 行的 \"## {2}\" 不是小节名；可用的小节为 {3}。",
        "{0} line {1}: \"## {2}\" is not a section name; the sections are {3}.");
    public static readonly Localized ResidentRepeatedSection = new(
        "{0} 第 {1} 行重复了小节 \"## {2}\"。",
        "{0} line {1}: the section \"## {2}\" appears more than once.");

    public static readonly Localized MarkersBroken = new(
        "{0} 中的常驻区块标记不完整或重复（第 {1} 行），未改动该文件。请手工修正标记后重试。",
        "The resident block markers in {0} are incomplete or repeated (line {1}); the file was left alone. Fix the markers by hand and try again.");
    public static readonly Localized PersonaUnreadable = new(
        "{0} 不是 UTF-8 编码，已跳过；常驻区块保持上一次的内容。",
        "{0} is not UTF-8 encoded and was skipped; the resident blocks keep their last content.");
    public static readonly Localized ContextFileMissing = new(
        "绑定的文件 {0} 已不存在，已跳过。要重新绑定请执行 tacit connect。",
        "The bound file {0} no longer exists and was skipped. Run tacit connect to bind it again.");
    public static readonly Localized BlockWritten = new("已写入常驻区块：{0}", "Wrote the resident block: {0}");
    public static readonly Localized BlockUnchanged = new("常驻区块已是最新：{0}", "The resident block is up to date: {0}");
    public static readonly Localized BlockRemoved = new("已移除常驻区块：{0}", "Removed the resident block: {0}");
    public static readonly Localized Connected = new(
        "已绑定 {0}，新会话开始生效。MCP 服务需要 tacit serve 在运行。",
        "Bound {0}; it takes effect in new sessions. The MCP server needs tacit serve running.");
    public static readonly Localized Disconnected = new("已解绑 {0}。", "Unbound {0}.");
    public static readonly Localized BindingConflict = new(
        "客户端 {0} 已绑定到其他路径（上下文文件 {1}，skill 目录 {2}），未改动。请先执行 tacit disconnect --client {0}，再重新绑定。",
        "The client {0} is already bound to other paths (context file {1}, skills folder {2}); nothing was changed. Run tacit disconnect --client {0} first, then connect again.");
    public static readonly Localized BindingNotFound = new(
        "没有名为 {0} 的绑定。",
        "No binding is named {0}.");
    public static readonly Localized ContextFolderMissing = new(
        "上下文文件所在的目录 {0} 不存在，未做任何改动。请先创建该目录，或改用正确的路径。",
        "The folder {0} that the context file goes into does not exist; nothing was changed. Create it, or give the right path.");
    public static readonly Localized SkillsFolderMissing = new(
        "skill 目录 {0} 不存在，未做任何改动。tacit 不替客户端创建它，请确认路径，或先让客户端生成该目录。",
        "The skills folder {0} does not exist; nothing was changed. tacit does not create it for the client: check the path, or let the client create the folder first.");
    public static readonly Localized ContextFileNoun = new("上下文文件", "context file");
    public static readonly Localized SkillsFolderNoun = new("skill 目录", "skills folder");
    public static readonly Localized PathEmpty = new(
        "{0} 不能为空。",
        "The {0} must not be empty.");
    public static readonly Localized ContextFileAbsent = new(
        "上下文文件 {0} 已不存在，未改动。",
        "The context file {0} no longer exists; left alone.");
    public static readonly Localized ResidentBlockAbsent = new(
        "{0} 中没有常驻区块，未改动。",
        "{0} has no resident block; left alone.");
    public static readonly Localized SkillLinkAbsent = new(
        "skill 链接已不存在：{0}",
        "The skill link is already gone: {0}");
    public static readonly Localized McpEndpoint = new(
        "MCP 端点：{0}",
        "MCP endpoint: {0}");
    public static readonly Localized McpTransport = new(
        "传输方式：Streamable HTTP",
        "Transport: Streamable HTTP");
    public static readonly Localized McpServerName = new(
        "建议的服务名：tacit",
        "Suggested server name: tacit");
    public static readonly Localized McpPortReminder = new(
        "改了 server.port 后，请自行更新客户端中的 MCP 配置；tacit 不再替你修改它。",
        "After changing server.port, update the MCP configuration in your client yourself; tacit no longer edits it.");
    public static readonly Localized SkillWritten = new("已写入 skill：{0}", "Wrote the skill: {0}");
    public static readonly Localized SkillUnchanged = new("skill 已是最新：{0}", "The skill is up to date: {0}");
    public static readonly Localized SkillLinked = new("已建立 skill 链接：{0}", "Created the skill link: {0}");
    public static readonly Localized SkillLinkUnchanged = new("skill 链接已指向 tacit：{0}", "The skill link already points to tacit: {0}");
    public static readonly Localized SkillLinkRemoved = new("已删除 skill 链接：{0}", "Removed the skill link: {0}");
    public static readonly Localized SkillLinkForeign = new(
        "{0} 不是指向 tacit 的 skill 链接，未改动。",
        "{0} is not tacit's skill link and was left alone.");
    public static readonly Localized ContextFileShared = new(
        "上下文文件 {0} 已由 {1} 使用，复用其常驻区块；文件中只保留一个常驻区块。",
        "The context file {0} is already used by {1}; it reuses their resident block, so the file keeps only one.");
    public static readonly Localized SkillsFolderShared = new(
        "skill 目录 {0} 已由 {1} 使用，复用其已有的 skill 链接。",
        "The skills folder {0} is already used by {1}; it reuses their skill links.");
    public static readonly Localized ContextFileStillUsed = new(
        "上下文文件 {0} 仍被 {1} 使用，保留，未改动。",
        "The context file {0} is still used by {1}; kept, left alone.");
    public static readonly Localized SkillLinkStillUsed = new(
        "skill 链接 {0} 仍被 {1} 使用，保留。",
        "The skill link {0} is still used by {1}; kept.");
    public static readonly Localized SkillLinkOccupied = new(
        "{0} 已存在且不是指向 tacit 的 skill 链接，未建立任何链接、未改动其他文件。请先移走或改名后重试。",
        "{0} already exists and is not tacit's skill link; no link was made and no other file was changed. Move or rename it, then try again.");
    public static readonly Localized SkillLinkFailed = new(
        "无法建立 skill 链接 {0}（指向 {1}）：{2}。本次建立的链接已撤回，未改动其他文件。",
        "Could not create the skill link {0} (pointing to {1}): {2}. The links made by this call were removed, and other files were left alone.");
    public static readonly Localized FileNotUtf8 = new(
        "{0} 不是 UTF-8 编码，未改动该文件。",
        "{0} is not UTF-8 encoded and was left alone.");
    public static readonly Localized BindingsInvalid = new(
        "无法读取 {0}：{1}。可修正该文件，或删除后重新执行 tacit connect。",
        "{0} cannot be read: {1}. Fix the file, or delete it and run tacit connect again.");
    public static readonly Localized RenderFailed = new("渲染常驻区块失败：{0}", "Rendering the resident blocks failed: {0}");
    public static readonly Localized IndexSyncFailed = new("同步检索索引失败：{0}", "Syncing the search index failed: {0}");
    public static readonly Localized BackgroundFailed = new("后台任务失败：{0}", "A background task failed: {0}");
    public static readonly Localized VectorExtensionInvalid = new(
        "向量检索扩展 {0} 缺失或不是 sqlite-vec {1} 的官方发布文件，为安全起见不加载，常驻服务无法启动。请重新安装 TacitMemory。",
        "The vector search extension {0} is missing or is not the official sqlite-vec {1} release, so it is not loaded and the memory service cannot start. Reinstall TacitMemory.");

    public static readonly Localized ServiceListening = new(
        "常驻服务已启动：{0}（Ctrl+C 停止）",
        "The memory service is running at {0} (Ctrl+C to stop)");
    public static readonly Localized PortInUse = new(
        "端口 {0} 已被占用，常驻服务无法启动。请在 {1} 中修改 server.port。",
        "Port {0} is already in use, so the memory service cannot start. Change server.port in {1}.");

}
