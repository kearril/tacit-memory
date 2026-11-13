using System.Text.Json.Serialization;

namespace TacitMemory.Core;

/// <summary>Where a backup made on request goes.</summary>
public enum BackupTarget
{
    /// <summary>A manual backup zip on this machine: in <c>TACIT_HOME/backups/</c>, or in the directory given.</summary>
    Local,

    /// <summary>The cloud backup on the WebDAV service in <c>backup.webdav</c>. Its wire name is <c>remote</c>, as the CLI's <c>--remote</c>.</summary>
    [JsonStringEnumMemberName("remote")]
    Cloud,
}

/// <summary>The outcome of a backup made on request.</summary>
/// <param name="Location">The archive's full path; for a cloud backup, the remote address without its credentials.</param>
/// <param name="Bytes">The archive's size.</param>
/// <param name="Report">The line to show first: where the backup is and how big it is.</param>
/// <param name="Notice">A warning to show after the report, such as the plaintext secrets notice; <c>null</c> when there is none.</param>
public sealed record BackupResult(string Location, long Bytes, string Report, string? Notice);

/// <summary>One attempt at the cloud backup, as <c>backups/remote.json</c> keeps the last one.</summary>
/// <param name="At">When the attempt ended.</param>
/// <param name="Succeeded">Whether the cloud backup was replaced.</param>
/// <param name="Error">Why the attempt failed, in the memory language at the time; <c>null</c> when it succeeded.</param>
public sealed record CloudBackupRecord(DateTimeOffset At, bool Succeeded, string? Error);

/// <summary>What a local backup in <c>TACIT_HOME/backups/</c> is, as its file name says.</summary>
public enum LocalBackupKind
{
    /// <summary>The daily backup the service makes on its first write of a day: <c>auto-YYYY-MM-DD.zip</c>, pruned to the newest few.</summary>
    Auto,

    /// <summary>A backup made on request: <c>manual-YYYY-MM-DD-HHmmss.zip</c>, never pruned.</summary>
    Manual,

    /// <summary>The safety backup a restore writes first: <c>pre-restore-YYYY-MM-DD-HHmmss.zip</c>, never pruned.</summary>
    PreRestore,
}

/// <summary>One local backup.</summary>
/// <param name="Name">The file name.</param>
/// <param name="Kind">What the backup is, from its name.</param>
/// <param name="At">When the file was last written.</param>
/// <param name="Bytes">The file's size.</param>
public sealed record LocalBackup(string Name, LocalBackupKind Kind, DateTimeOffset At, long Bytes);

/// <summary>The backups as the dashboard shows them; reading it touches no network.</summary>
/// <param name="Local">The local backups in <c>TACIT_HOME/backups/</c>, newest first; files with other names are left out.</param>
/// <param name="RemoteConfigured">Whether <c>backup.webdav</c> is set, so a cloud backup can be made.</param>
/// <param name="LastRemote">The last attempt at the cloud backup; <c>null</c> when there was none or its record cannot be read.</param>
public sealed record BackupList(IReadOnlyList<LocalBackup> Local, bool RemoteConfigured, CloudBackupRecord? LastRemote);
