namespace TacitMemory.Core.Clients;

/// <summary>
/// Whether two paths name the same place for bindings: full paths with no trailing separator, compared case-insensitively
/// where the platform ignores case (<see cref="PathCase"/>). Directory junctions and symbolic links are not resolved, so
/// two entries into one folder count as two.
/// </summary>
internal static class BindingPath
{
    /// <summary>Equality and hashing of paths as bindings compare them, for grouping and de-duplicating.</summary>
    public static readonly IEqualityComparer<string> Comparer = new PathComparer();

    public static bool Same(string a, string b) => Comparer.Equals(a, b);

    private sealed class PathComparer : IEqualityComparer<string>
    {
        private static readonly StringComparer Text = PathCase.Comparer;

        public bool Equals(string? x, string? y) => Text.Equals(Normalize(x!), Normalize(y!));

        public int GetHashCode(string obj) => Text.GetHashCode(Normalize(obj));

        private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
}
