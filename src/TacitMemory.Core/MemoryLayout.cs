namespace TacitMemory.Core;

/// <summary>
/// Where each memory file lives in the memory repository. Relative paths use <c>/</c>, as git reports them; <see cref="Full"/>
/// turns one into a path on disk. It only names paths: reading and parsing stay with each file's own type.
/// </summary>
internal static class MemoryLayout
{
    public const string Persona = "persona.md";

    public const string Candidates = "candidates.md";

    public const string Workstreams = "workstreams.md";

    /// <summary>The folder of the activity log, one file per month.</summary>
    public const string Recent = "recent";

    /// <summary>The folder of the detail pages, one subfolder per workstream.</summary>
    public const string Pages = "pages";

    /// <summary>The folder of the derived index. It is not memory: backups and restores leave it out.</summary>
    public const string Cache = ".cache";

    public const string IndexDatabase = $"{Cache}/index.db";

    /// <summary>The activity file of the month <paramref name="day"/> falls in.</summary>
    public static string RecentMonth(DateOnly day) => In(Recent, $"{day:yyyy-MM}.md");

    /// <summary>The folder of one workstream's detail pages.</summary>
    public static string WorkstreamPages(string workstream) => $"{Pages}/{workstream}";

    /// <summary>The file of one detail page.</summary>
    public static string Page(string workstream, string slug) => In(WorkstreamPages(workstream), $"{slug}.md");

    /// <summary>The path of the file <paramref name="name"/> directly in <paramref name="folder"/>.</summary>
    public static string In(string folder, string name) => $"{folder}/{name}";

    /// <summary>Whether <paramref name="relative"/> is the folder or lies inside it.</summary>
    public static bool IsIn(string relative, string folder) =>
        relative == folder || relative.StartsWith($"{folder}/", StringComparison.Ordinal);

    /// <summary>The path on disk of <paramref name="relative"/> in the memory repository at <paramref name="memoryPath"/>.</summary>
    public static string Full(string memoryPath, string relative) => Path.Combine(memoryPath, relative);
}
