namespace TacitMemory.Core.Localization;

/// <summary>Human-facing text of a restore: the refusals of a backup that cannot be restored, the preview and the result.</summary>
internal static class RestoreMessages
{
    public static readonly Localized PathNotAbsolute = new(
        "恢复的备份文件路径必须是绝对路径：{0}",
        "The backup file to restore must be an absolute path: {0}");
    public static readonly Localized FileMissing = new(
        "找不到备份文件 {0}。",
        "The backup file {0} does not exist.");
    public static readonly Localized NotZip = new(
        "{0} 不是有效的 zip 文件，不能恢复。",
        "{0} is not a valid zip file, so it cannot be restored.");
    public static readonly Localized NoManifest = new(
        "{0} 中没有 manifest.json（清单），不能恢复。",
        "{0} has no manifest.json, so it cannot be restored.");
    public static readonly Localized ManifestUnreadable = new(
        "{0} 中的清单无法解析，不能恢复：{1}",
        "The manifest in {0} cannot be read, so it cannot be restored: {1}");
    public static readonly Localized EntryPathRefused = new(
        "{0} 中的条目 {1} 位置不允许：条目只能位于 memory/、prompts/ 或根下的 config.yaml、bindings.json，且不能含 .. 或绝对路径。",
        "The entry {1} in {0} is not allowed: entries must lie in memory/, prompts/, or be config.yaml or bindings.json at the root, without .. or an absolute path.");
    public static readonly Localized DuplicateEntry = new(
        "{0} 中的条目 {1} 出现了两次，不能恢复。",
        "The entry {1} appears twice in {0}, so it cannot be restored.");
    public static readonly Localized MissingEntry = new(
        "清单列出了 {1}，但 {0} 中没有它，不能恢复。",
        "The manifest lists {1}, but {0} does not hold it, so it cannot be restored.");
    public static readonly Localized UnlistedEntry = new(
        "{0} 中的 {1} 不在清单里，清单与内容不符，不能恢复。",
        "{1} is in {0} but not in its manifest; the manifest and the content disagree, so it cannot be restored.");
    public static readonly Localized NoRepository = new(
        "{0} 中没有 memory/.git/，不是记忆仓库的备份，不能恢复。",
        "{0} has no memory/.git/, so it is not a backup of a memory repository and cannot be restored.");
    public static readonly Localized NoConfig = new(
        "{0} 中没有 config.yaml，不能用于带 --config 的恢复。",
        "{0} has no config.yaml, so it cannot be used for a restore with --config.");

    public static readonly Localized PreviewBackup = new("备份：{0}", "Backup: {0}");
    public static readonly Localized PreviewGenerated = new("生成时间：{0}", "Made at: {0}");
    public static readonly Localized PreviewVersion = new("tacit 版本：{0}", "tacit version: {0}");
    public static readonly Localized PreviewKind = new("种类：{0}", "Kind: {0}");
    public static readonly Localized PreviewUnknown = new("未知（旧版备份未记录）", "unknown (an older backup does not record it)");
    public static readonly Localized PreviewHead = new("记忆仓库 HEAD：{0}", "Memory repository HEAD: {0}");
    public static readonly Localized PreviewHeadSubject = new("提交说明：{0}", "Commit message: {0}");
    public static readonly Localized PreviewFiles = new("文件数：{0}", "Files: {0}");
    public static readonly Localized PreviewReplacesTitle = new("将被替换：", "Will be replaced:");
    public static readonly Localized ReplacesMemory = new(
        "  记忆仓库 {0}（当前 HEAD {1}：{2}）",
        "  the memory repository {0} (current HEAD {1}: {2})");
    public static readonly Localized ReplacesConfig = new(
        "  config.yaml（当前配置）",
        "  config.yaml (the current config)");
    public static readonly Localized ReplacesPrompts = new(
        "  prompts/（当前提示词；备份中没有的文件会删除）",
        "  prompts/ (the current prompt documents; files the backup does not hold are deleted)");
    public static readonly Localized BindingsKept = new(
        "bindings.json 不恢复，绑定保持不变。",
        "bindings.json is never restored; the bindings stay as they are.");
    public static readonly Localized Prompt = new(
        "确认恢复？输入 y 继续，输入其他内容取消：",
        "Restore? Type y to continue, anything else to cancel:");
    public static readonly Localized Cancelled = new(
        "已取消，未做任何修改。",
        "Cancelled; nothing was changed.");

    public static readonly Localized ScopeMemory = new("仅记忆仓库", "memory repository only");
    public static readonly Localized ScopeWithConfig = new("记忆仓库、config.yaml 与 prompts/", "memory repository, config.yaml and prompts/");

    public static readonly Localized Restored = new("已恢复记忆仓库，HEAD：{0}", "Restored the memory repository; HEAD: {0}");
    public static readonly Localized SafetyBackup = new("保险备份：{0}", "Safety backup: {0}");
    public static readonly Localized ConfigApplied = new(
        "config.yaml 已恢复，服务已按它重新加载。",
        "config.yaml is restored, and the service has reloaded with it.");
    public static readonly Localized ConfigNotApplied = new(
        "config.yaml 已恢复，但服务未能按其重新加载，继续使用原配置：{0}",
        "config.yaml is restored, but the service could not reload with it and keeps the config it had: {0}");
    public static readonly Localized RestartPending = new(
        "以下设置要重启 tacit serve 后才生效：",
        "These settings take effect when tacit serve restarts:");
    public static readonly Localized RestartPendingItem = new(
        "  {0}：运行中为 {1}，恢复后为 {2}",
        "  {0}: {1} while running, {2} as restored");
    public static readonly Localized PromptsApplied = new(
        "prompts/ 已恢复并立即生效。",
        "prompts/ is restored and takes effect now.");
    public static readonly Localized MemoryPathNotice = new(
        "重启后会指向 {0}，记忆仓库已恢复到 {1}。",
        "After a restart it points to {0}; the memory repository was restored to {1}.");

    public static readonly Localized SafetyFailed = new(
        "保险备份失败，未改动任何内容：{0}",
        "The safety backup failed, so nothing was changed: {0}");
    public static readonly Localized Failed = new(
        "恢复失败：{0}。记忆仓库已回到恢复前的内容；保险备份：{1}",
        "The restore failed: {0}. The memory repository is back to its state before the restore; safety backup: {1}");
    public static readonly Localized FailedRollbackFailed = new(
        "恢复失败：{0}。回退也未能完成：{1}。恢复前的内容保留在 {2}，保险备份：{3}，请据此手工恢复。",
        "The restore failed: {0}. The rollback did not finish: {1}. The content from before the restore is kept in {2}; safety backup: {3}. Recover it by hand from these.");

    public static readonly Localized SourceCount = new(
        "恢复需要指定一个备份文件（绝对路径），或 remote（云端备份），二者只能选其一。",
        "A restore needs one source: a backup file's absolute path, or remote for the cloud backup; not both and not neither.");
    public static readonly Localized RemotePreviewFirst = new(
        "云端备份只能先预览：预览会把备份下载到本地，之后以预览返回的路径恢复。",
        "A cloud backup can only be previewed: the preview downloads it, and the restore then takes the downloaded file.");
    public static readonly Localized ConfigProblem = new(
        "恢复的 config.yaml 存在错误，服务继续使用原配置；修正后在设置页保存一次或重启 tacit serve 才会生效：\n{0}",
        "The restored config.yaml has errors, so the service keeps the config it had; fix them, then save on the settings page or restart tacit serve:\n{0}");
    public static readonly Localized PreviewConfigProblem = new(
        "注意：备份中的 config.yaml 有错误，恢复后需修正才能重启 tacit serve：\n{0}",
        "Note: the config.yaml in the backup has errors; they must be fixed before tacit serve can restart with it:\n{0}");
}
