using TacitMemory.Core.Git;
using TacitMemory.Core.Pages;

namespace TacitMemory.Core.Consolidation;

/// <summary>
/// The detail pages changed since the baseline: those whose content in the working tree differs from the baseline
/// commit, uncommitted edits and new pages included, and deleted pages left out. Without a baseline every page counts.
/// </summary>
internal static class ChangedPages
{
    /// <summary>The folder of the detail pages, relative to the repository.</summary>
    private const string Folder = "pages";

    /// <summary>The changed pages, in ordinal order of their paths.</summary>
    public static async Task<IReadOnlyList<PageId>> ReadAsync(ConsolidationContext context, CancellationToken cancellationToken)
    {
        var memory = context.Config.MemoryPath;
        var paths = context.Baseline.Commit is { } commit
            ? await ChangedSinceAsync(context.Git, commit.Hash, cancellationToken)
            : AllPageFiles(memory);

        var pages = new List<PageId>();
        foreach (var path in paths.Distinct().Order(StringComparer.Ordinal))
            if (TryPageOf(path, out var id) && File.Exists(Path.Combine(memory, path)))
                pages.Add(id);
        return pages;
    }

    /// <summary>
    /// The tracked pages whose content differs from <paramref name="commit"/>, staged or not, and the untracked ones.
    /// Deleted files are listed too; the caller drops them by their absence from disk.
    /// </summary>
    private static async Task<IEnumerable<string>> ChangedSinceAsync(GitCli git, string commit, CancellationToken cancellationToken)
    {
        var tracked = await git.RunAsync(cancellationToken, "diff", "--name-only", "--no-renames", "--relative", commit, "--", Folder);
        var untracked = await git.RunAsync(cancellationToken, "ls-files", "--others", "--exclude-standard", "--", Folder);
        return Lines(tracked).Concat(Lines(untracked));
    }

    private static IEnumerable<string> AllPageFiles(string memory)
    {
        var folder = Path.Combine(memory, Folder);
        return Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*.md", SearchOption.AllDirectories).Select(file => Path.GetRelativePath(memory, file).Replace('\\', '/'))
            : [];
    }

    private static IEnumerable<string> Lines(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(path => path.Replace('\\', '/'));

    /// <summary>The page a path names, when the path is exactly <c>pages/&lt;project&gt;/&lt;slug&gt;.md</c>.</summary>
    private static bool TryPageOf(string relative, out PageId id)
    {
        var parts = relative.Split('/');
        id = null!;
        return parts.Length == 3
            && parts[0] == Folder
            && PageId.TryParse($"{parts[1]}/{Path.GetFileNameWithoutExtension(parts[2])}", out id, out _)
            && id.RelativePath == relative;
    }
}
