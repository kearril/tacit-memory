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
