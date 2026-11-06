namespace TacitMemory.Core.Clients;

/// <summary>
/// Whether two paths name the same place for bindings: full paths with no trailing separator, compared case-insensitively
/// on Windows. Directory junctions and symbolic links are not resolved, so two entries into one folder count as two.
/// </summary>
internal static class BindingPath
{
    public static bool Same(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
