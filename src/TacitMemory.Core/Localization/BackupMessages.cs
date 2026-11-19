namespace TacitMemory.Core.Localization;

/// <summary>Human-facing text of the backups: the automatic, manual, safety and cloud backups, and the backup status lines.</summary>
internal static class BackupMessages
{
    public static readonly Localized BackupFailed = new(
        "自动备份失败：{0}。写入结果不受影响，下一次写入时会再次尝试。",
        "The automatic backup failed: {0}. The write itself is unaffected; the next write tries again.");

    public static readonly Localized BackupPlaintextKey = new(
        "备份中含明文密钥：{0} 中包含 apiKey 或 webdav.password，备份文件中同样包含这些密钥，请妥善保管备份。",
        "The backup holds plaintext secrets: {0} has an apiKey or webdav.password written in it, and the backup holds them too; keep the backup safe.");

    public static readonly Localized ManualBackupWritten = new(
        "手动备份已写入：{0}（{1:N0} 字节）",
        "Manual backup written: {0} ({1:N0} bytes)");

    public static readonly Localized BackupExists = new(
        "备份文件 {0} 已存在。备份从不覆盖已有文件，请稍后再试。",
        "The backup file {0} already exists. A backup never overwrites a file; try again a moment later.");

    public static readonly Localized BackupDirectoryNotAbsolute = new(
        "备份目录必须是绝对路径：{0}",
        "The backup directory must be an absolute path: {0}");

    public static readonly Localized BackupCloudWithDirectory = new(
        "--to 只用于本地备份，不能与云端备份同时指定。",
        "--to applies to local backups only; it cannot be combined with the cloud backup.");

    public static readonly Localized CloudBackupNotConfigured = new(
        "未配置云端备份：请在 config.yaml 的 backup.webdav 中填写 url（username 与 password 可选）。",
        "Cloud backup is not configured: set backup.webdav.url in config.yaml (username and password are optional).");

    public static readonly Localized CloudBackupFailed = new(
        "WebDAV 请求失败（{0}阶段）：{1}",
        "The WebDAV request failed at the {0} stage: {1}");

    public static readonly Localized CloudStageConnect = new("连接", "connection");

    public static readonly Localized CloudStageAuthentication = new("认证", "authentication");

    public static readonly Localized CloudStageUpload = new("上传", "upload");

    public static readonly Localized CloudStageReplace = new("替换", "replacement");

    public static readonly Localized CloudStageDownload = new("下载", "download");

    public static readonly Localized CloudReasonUnreachable = new(
        "无法连接 WebDAV 服务",
        "the WebDAV service cannot be reached");

    public static readonly Localized CloudReasonInterrupted = new(
        "传输中断",
        "the transfer was cut short");

    public static readonly Localized CloudReasonTimeout = new(
        "超过 10 分钟仍未完成",
        "it did not finish within 10 minutes");

    public static readonly Localized CloudReasonMissing = new(
        "远端没有云端备份",
        "there is no cloud backup on the remote service");

    public static readonly Localized CloudReasonStatus = new("HTTP {0}", "HTTP {0}");

    public static readonly Localized CloudReasonUnsupported = new(
        "该 WebDAV 服务不支持原子替换（HTTP {0}），远端文件未改动",
        "the WebDAV service does not support atomic replacement (HTTP {0}), so the remote file was left unchanged");

    public static readonly Localized CloudUploaded = new(
        "云端备份已上传：{0}（{1:N0} 字节）",
        "Cloud backup uploaded: {0} ({1:N0} bytes)");

    public static readonly Localized BackupsTitle = new("备份", "Backups");

    public static readonly Localized BackupLatest = new("最近本地备份：{0}，{1:yyyy-MM-dd HH:mm}", "Latest local backup: {0}, {1:yyyy-MM-dd HH:mm}");

    public static readonly Localized BackupNone = new("没有本地备份", "No local backup yet");

    public static readonly Localized BackupCloudNotConfigured = new("云端备份：未配置", "Cloud backup: not configured");

    public static readonly Localized BackupCloudNever = new("云端备份：从未执行", "Cloud backup: never made");

    public static readonly Localized BackupCloudSucceeded = new("云端备份：{0:yyyy-MM-dd HH:mm}，成功", "Cloud backup: {0:yyyy-MM-dd HH:mm}, succeeded");

    public static readonly Localized BackupCloudFailed = new("云端备份：{0:yyyy-MM-dd HH:mm}，失败：{1}", "Cloud backup: {0:yyyy-MM-dd HH:mm}, failed: {1}");

    public static readonly Localized BackupCloudUnreadable = new("云端备份：记录无法读取（{0}）", "Cloud backup: the record cannot be read ({0})");
}
