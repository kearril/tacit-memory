namespace TacitMemory.Core;

/// <summary>
/// Whether paths on this platform that differ only in case name one place: yes on Windows and on macOS, whose file
/// systems ignore case by default, no on Linux. Decided by the platform, not by probing a folder, so a case-sensitive
/// volume made by hand on a Mac is treated like any other.
/// </summary>
internal static class PathCase
{
    public static readonly bool Ignored = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    public static StringComparer Comparer => Ignored ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static StringComparison Comparison => Ignored ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
