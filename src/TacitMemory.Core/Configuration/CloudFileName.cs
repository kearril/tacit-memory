namespace TacitMemory.Core.Configuration;

/// <summary>
/// The name of the cloud backup's file in the WebDAV folder, <c>backup.webdav.file</c>: a file name ending in <c>.zip</c>,
/// so machines that share one folder can each keep their own backup.
/// </summary>
internal static class CloudFileName
{
    public const string Default = "tacit-backup.zip";

    /// <summary>
    /// Whether <paramref name="name"/> is a plain file name ending in <c>.zip</c> in any case: no surrounding spaces, and none
    /// of <c>/ \ : ? # %</c>, which would change the address it resolves to in the folder (a colon reads as a URI scheme or,
    /// on Windows, a drive) or the path of its download.
    /// </summary>
    public static bool IsValid(string name) =>
        name.Trim() == name
        && name.IndexOfAny(['/', '\\', ':', '?', '#', '%']) < 0
        && name.Length > ".zip".Length
        && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
}
