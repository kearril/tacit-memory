namespace TacitMemory.Core.Configuration;

internal static class HomePaths
{
    /// <summary>A full path, with a leading <c>~</c> taken as the user's home directory.</summary>
    public static string Expand(string path, string userHome) =>
        Path.GetFullPath(path == "~" ? userHome
            : path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal) ? Path.Combine(userHome, path[2..])
            : path);
}
