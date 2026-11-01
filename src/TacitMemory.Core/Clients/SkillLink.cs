using System.ComponentModel;
using System.Diagnostics;
using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Clients;

/// <summary>
/// The client's link to the memory skill folder, placed where the client scans for skills. On Windows it is a
/// directory junction, which needs no administrator rights; elsewhere a symbolic link. Only the link entry is ever
/// created or removed: the target is never followed, so removing the link leaves the skill in place.
/// </summary>
internal static class SkillLink
{
    private enum State
    {
        /// <summary>Nothing is at the link path.</summary>
        Absent,

        /// <summary>A link that resolves to the skill folder.</summary>
        Pointing,

        /// <summary>Something else is at the link path: a file, a directory, or a link elsewhere.</summary>
        Occupied,
    }

    /// <summary>The outcome of <see cref="Remove"/>.</summary>
    public enum RemoveOutcome
    {
        Removed,
        NotOurs,
        Absent,
    }

    /// <summary>
    /// Makes <paramref name="linkPath"/> a link to <paramref name="targetPath"/>. A link that already points there is
    /// left alone. Returns <c>true</c> when this call created the link.
    /// </summary>
    /// <exception cref="TacitException">
    /// The name is taken by something that is not such a link, or the link cannot be made (for example on a network
    /// drive). Nothing else is changed in either case.
    /// </exception>
    public static bool Put(string linkPath, string targetPath, MemoryLanguage language)
    {
        switch (Inspect(linkPath, targetPath))
        {
            case State.Pointing:
                return false;
            case State.Occupied:
                throw new TacitException(TacitError.SkillLinkRefused, Messages.SkillLinkOccupied.Format(language, linkPath));
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
            Create(linkPath, targetPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or Win32Exception)
        {
            throw Refused(linkPath, targetPath, language, e);
        }

        // Creation can report success for a link that does not reach the skill, so the result is checked as well.
        if (Inspect(linkPath, targetPath) == State.Pointing)
            return true;
        DeleteIfLink(linkPath);
        throw Refused(linkPath, targetPath, language, null);
    }

    /// <summary>Removes the link when it points at <paramref name="targetPath"/>; any other entry is left alone.</summary>
    public static RemoveOutcome Remove(string linkPath, string targetPath)
    {
        switch (Inspect(linkPath, targetPath))
        {
            case State.Pointing:
                DeleteIfLink(linkPath);
                return RemoveOutcome.Removed;
            case State.Occupied:
                return RemoveOutcome.NotOurs;
            default:
                return RemoveOutcome.Absent;
        }
    }

    private static State Inspect(string linkPath, string targetPath)
    {
        var entry = new DirectoryInfo(linkPath);
        var linkTarget = entry.LinkTarget;
        if (linkTarget is null)
            return entry.Exists || File.Exists(linkPath) ? State.Occupied : State.Absent;

        var resolved = Path.GetFullPath(linkTarget, Path.GetDirectoryName(linkPath)!);
        return SamePath(resolved, targetPath) ? State.Pointing : State.Occupied;
    }

    /// <summary>
    /// Removes the entry itself. A junction is removed by <see cref="Directory.Delete(string, bool)"/> without
    /// recursion, which never enters it. On Unix <see cref="File.Delete"/> unlinks a symbolic link, even one to a
    /// directory. Callers check that the entry is a link first, so a real directory is never deleted.
    /// </summary>
    private static void DeleteIfLink(string linkPath)
    {
        if (new DirectoryInfo(linkPath).LinkTarget is null)
            return;
        if (OperatingSystem.IsWindows())
            Directory.Delete(linkPath, recursive: false);
        else
            File.Delete(linkPath);
    }

    private static void Create(string linkPath, string targetPath)
    {
        if (OperatingSystem.IsWindows())
            CreateJunction(linkPath, targetPath);
        else
            Directory.CreateSymbolicLink(linkPath, targetPath);
    }

    /// <summary><c>mklink /J</c> is a <c>cmd</c> built-in; it makes a junction without the symbolic-link privilege.</summary>
    private static void CreateJunction(string linkPath, string targetPath)
    {
        var startInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            // The paths are quoted by hand: cmd treats characters such as & as operators outside quotes.
            Arguments = $"/d /c mklink /J \"{linkPath}\" \"{targetPath}\" 2>&1",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        using var process = Process.Start(startInfo) ?? throw new IOException("cmd.exe could not be started.");
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new IOException(output.Trim());
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static TacitException Refused(string linkPath, string targetPath, MemoryLanguage language, Exception? cause) =>
        new(
            TacitError.SkillLinkRefused,
            Messages.SkillLinkFailed.Format(language, linkPath, targetPath, cause?.Message ?? "the link does not point to the memory skill"),
            cause);
}
