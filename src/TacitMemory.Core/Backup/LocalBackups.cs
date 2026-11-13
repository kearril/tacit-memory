using System.Text.Json;
using System.Text.RegularExpressions;
using TacitMemory.Core.Configuration;

namespace TacitMemory.Core.Backup;

/// <summary>
/// The local backups in <c>TACIT_HOME/backups/</c>: the files whose names a backup of some kind is written under. The
/// status report's latest backup and the dashboard's list both read them here.
/// </summary>
internal static partial class LocalBackups
{
    /// <summary>The local backups, newest first by when each file was last written, then by name; none when the folder is missing.</summary>
    public static IReadOnlyList<LocalBackup> Read(TacitOptions options)
    {
        var directory = Path.Combine(options.TacitHome, "backups");
        if (!Directory.Exists(directory))
            return [];
        return Directory.EnumerateFiles(directory)
            .Select(path => new FileInfo(path))
            .Select(file => KindOf(file.Name) is { } kind
                ? new LocalBackup(file.Name, kind, new DateTimeOffset(file.LastWriteTime), file.Length)
                : null)
            .OfType<LocalBackup>()
            .OrderByDescending(backup => backup.At)
            .ThenByDescending(backup => backup.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The backups for the dashboard: the local ones, whether the cloud backup is configured, and its last attempt.</summary>
    public static BackupList List(TacitOptions options, TacitConfig config)
    {
        CloudBackupRecord? last = null;
        if (config.Backup.WebDav is not null)
        {
            try
            {
                last = CloudBackup.ReadLast(options);
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                // The status report says the record cannot be read; the list shows no attempt.
            }
        }
        return new BackupList(Read(options), config.Backup.WebDav is not null, last);
    }

    /// <summary>The kind a file name belongs to, or <c>null</c> when it is not the name of a local backup.</summary>
    public static LocalBackupKind? KindOf(string fileName)
    {
        var match = Name().Match(fileName);
        if (!match.Success)
            return null;
        return match.Groups["kind"].Value switch
        {
            "auto" => LocalBackupKind.Auto,
            "manual" => LocalBackupKind.Manual,
            _ => LocalBackupKind.PreRestore,
        };
    }

    [GeneratedRegex(@"^(?:(?<kind>auto)-\d{4}-\d{2}-\d{2}|(?<kind>manual|pre-restore)-\d{4}-\d{2}-\d{2}-\d{6})\.zip$", RegexOptions.CultureInvariant)]
    private static partial Regex Name();
}
