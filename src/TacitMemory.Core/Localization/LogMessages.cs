namespace TacitMemory.Core.Localization;

/// <summary>
/// The events written to the log, in the memory language. Their arguments are identifiers, counts, times and system error
/// text; no event holds memory text, queries, prompts, secrets or WebDAV credentials.
/// </summary>
internal static class LogMessages
{
    public static readonly Localized Started = new(
        "常驻服务启动：版本 {0}，端口 {1}，记忆仓库 {2}",
        "Memory service starting: version {0}, port {1}, memory repository {2}");
    public static readonly Localized PromptsReloaded = new("提示词文档已重新载入", "The prompt documents were reloaded");
    public static readonly Localized BackupWritten = new("自动备份已写入：{0}", "Automatic backup written: {0}");
    public static readonly Localized ManualBackupWritten = new("手动备份已写入：{0}，{1} 字节", "Manual backup written: {0}, {1} bytes");
    public static readonly Localized ManualBackupFailed = new("手动备份失败：{0}", "The manual backup failed: {0}");
    public static readonly Localized CloudBackupWritten = new("云端备份已上传：{0}，{1} 字节", "Cloud backup uploaded: {0}, {1} bytes");
    public static readonly Localized CloudBackupFailed = new("云端备份失败：{0}", "The cloud backup failed: {0}");
    public static readonly Localized CloudBackupDownloaded = new("云端备份已下载：{0}，{1} 字节", "Cloud backup downloaded: {0}, {1} bytes");
    public static readonly Localized CloudRecordFailed = new("无法写入云端备份记录：{0}", "The cloud backup record cannot be written: {0}");
    public static readonly Localized ModelFailed = new(
        "辅助模型 {0} 请求最终失败，进入 {1} 秒冷却",
        "The auxiliary model {0} failed for good and cools down for {1} s");
    public static readonly Localized ApiCall = new(
        "管理 API {0} {1} 返回 {2}，耗时 {3} 毫秒",
        "Management API {0} {1} answered {2} in {3} ms");
    public static readonly Localized ToolCall = new(
        "工具 {0}（op {1}，目标 {2}）：{3}，耗时 {4} 毫秒",
        "Tool {0} (op {1}, target {2}): {3}, {4} ms");
    public static readonly Localized ToolWritten = new("已写入", "written");
    public static readonly Localized ToolDone = new("完成", "done");
    public static readonly Localized ToolRefused = new("已拒绝", "refused");
    public static readonly Localized ToolAborted = new("已中断", "aborted");
    public static readonly Localized MaintenanceStarted = new("维护操作开始：{0}", "Maintenance started: {0}");
    public static readonly Localized MaintenanceFinished = new("维护操作结束：{0}，耗时 {1} 毫秒", "Maintenance finished: {0} in {1} ms");
    public static readonly Localized MaintenanceFailed = new("维护操作失败：{0}，耗时 {1} 毫秒：{2}", "Maintenance failed: {0} after {1} ms: {2}");
    public static readonly Localized IndexRebuilt = new(
        "索引已重建：文件 {0}，单元 {1}，向量 {2}，跳过 {3} 个非 UTF-8 文件，耗时 {4} 毫秒",
        "Index rebuilt: {0} files, {1} units, {2} vectors, {3} files skipped as not UTF-8, in {4} ms");
    public static readonly Localized VectorsWaiting = new(
        "嵌入模型不可用，向量待模型恢复后补齐",
        "The embedding model is unavailable; the vectors wait for it to return");
    public static readonly Localized RestoreStarted = new("恢复开始：备份 {0}，范围 {1}", "Restore started: backup {0}, scope {1}");
    public static readonly Localized PreRestoreWritten = new("保险备份已写入：{0}，{1} 字节", "Safety backup written: {0}, {1} bytes");
    public static readonly Localized RestoreApplied = new(
        "恢复完成：HEAD {0}，保险备份 {1}，索引 {2} 个文件",
        "Restore finished: HEAD {0}, safety backup {1}, index of {2} files");
    public static readonly Localized RestoreRolledBack = new(
        "恢复失败，记忆仓库已回到恢复前的内容；保险备份 {0}",
        "The restore failed and the memory repository is back to its state before it; safety backup {0}");
    public static readonly Localized RestoreRollbackFailed = new(
        "恢复失败且回退未完成：{0}；恢复前的内容保留在 {1}",
        "The restore failed and the rollback did not finish: {0}; the content from before the restore is kept in {1}");
    public static readonly Localized TemporaryFolderKept = new(
        "恢复的临时目录未能删除，可手工删除：{0}",
        "A temporary folder of the restore could not be deleted; delete it by hand: {0}");
    public static readonly Localized UnexpectedFailure = new(
        "管理 API 出现未预期的异常 {0}：{1}",
        "An unexpected exception reached the management API: {0}: {1}");
}
