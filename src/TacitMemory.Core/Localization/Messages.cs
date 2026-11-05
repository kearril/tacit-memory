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
        "下一步：在 {0} 中填写 models 一节，然后执行 tacit connect --client omp 与 tacit serve。",
        "Next: fill in the models section of {0}, then run tacit connect --client omp and tacit serve.");

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
    public static readonly Localized ConfigExpectedLanguage = new("应为 zh 或 en，实际为 {0}", "must be zh or en, but is {0}");
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
    public static readonly Localized ClientConfigInvalid = new(
        "{0} 不是有效的 JSON，未改动：{1}",
        "{0} is not valid JSON and was left alone: {1}");
    public static readonly Localized BlockWritten = new("已写入常驻区块：{0}", "Wrote the resident block: {0}");
    public static readonly Localized BlockUnchanged = new("常驻区块已是最新：{0}", "The resident block is up to date: {0}");
    public static readonly Localized BlockRemoved = new("已移除常驻区块：{0}", "Removed the resident block: {0}");
    public static readonly Localized McpRegistered = new("已注册 MCP 服务 tacit：{0}", "Registered the MCP server tacit: {0}");
    public static readonly Localized McpUnchanged = new("MCP 服务 tacit 已注册：{0}", "The MCP server tacit is already registered: {0}");
    public static readonly Localized McpRemoved = new("已注销 MCP 服务 tacit：{0}", "Unregistered the MCP server tacit: {0}");
    public static readonly Localized Connected = new(
        "已绑定 {0}，新会话开始生效。MCP 服务需要 tacit serve 在运行。",
        "Bound {0}; it takes effect in new sessions. The MCP server needs tacit serve running.");
    public static readonly Localized Disconnected = new("已解绑 {0}。", "Unbound {0}.");
    public static readonly Localized SkillWritten = new("已写入记忆 skill：{0}", "Wrote the memory skill: {0}");
    public static readonly Localized SkillUnchanged = new("记忆 skill 已是最新：{0}", "The memory skill is up to date: {0}");
    public static readonly Localized SkillLinked = new("已建立 skill 链接：{0}", "Created the skill link: {0}");
    public static readonly Localized SkillLinkUnchanged = new("skill 链接已指向记忆 skill：{0}", "The skill link already points to the memory skill: {0}");
    public static readonly Localized SkillLinkRemoved = new("已删除 skill 链接：{0}", "Removed the skill link: {0}");
    public static readonly Localized SkillLinkForeign = new(
        "{0} 不是指向记忆 skill 的链接，未改动。",
        "{0} is not a link to the memory skill and was left alone.");
    public static readonly Localized SkillLinkOccupied = new(
        "{0} 已存在且不指向记忆 skill，未改动。请先移走或改名后重试。",
        "{0} already exists and does not point to the memory skill; nothing was changed. Move or rename it, then try again.");
    public static readonly Localized SkillLinkFailed = new(
        "无法建立 skill 链接 {0}（指向 {1}）：{2}。未改动其他文件。",
        "Could not create the skill link {0} (pointing to {1}): {2}. Other files were left alone.");
    public static readonly Localized FileNotUtf8 = new(
        "{0} 不是 UTF-8 编码，未改动该文件。",
        "{0} is not UTF-8 encoded and was left alone.");
    public static readonly Localized BindingsInvalid = new(
        "无法读取 {0}：{1}。可修正该文件，或删除后重新执行 tacit connect。",
        "{0} cannot be read: {1}. Fix the file, or delete it and run tacit connect again.");
    public static readonly Localized ClientConfigHasComments = new(
        "{0} 含有注释，自动改写会丢失它们，因此未改动。请手工在 mcpServers 中加入：\"tacit\": {1}",
        "{0} has comments that rewriting would lose, so it was left alone. Add this under mcpServers by hand: \"tacit\": {1}");
    public static readonly Localized RenderFailed = new("渲染常驻区块失败：{0}", "Rendering the resident blocks failed: {0}");
    public static readonly Localized IndexSyncFailed = new("同步检索索引失败：{0}", "Syncing the search index failed: {0}");
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
