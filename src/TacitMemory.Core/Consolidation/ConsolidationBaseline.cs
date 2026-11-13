using System.Globalization;
using TacitMemory.Core.Git;

namespace TacitMemory.Core.Consolidation;

/// <summary>Where the last consolidation stands: the latest <c>consolidate:</c> commit, else the latest <c>init:</c> commit, else none.</summary>
internal sealed record ConsolidationBaseline(BaselineKind Kind, ConsolidationCommit? Commit)
{
    /// <summary>Reads the history of the memory repository. Commits are matched by their subject prefix.</summary>
    public static async Task<ConsolidationBaseline> ReadAsync(GitCli git, CancellationToken cancellationToken)
    {
        var commits = await HistoryAsync(git, cancellationToken);

        // git log lists the newest commit first, so the first match is the latest one.
        if (commits.FirstOrDefault(IsConsolidation) is { } consolidation)
            return new ConsolidationBaseline(BaselineKind.Consolidation, consolidation);
        if (commits.FirstOrDefault(commit => commit.Subject.StartsWith("init:", StringComparison.Ordinal)) is { } init)
            return new ConsolidationBaseline(BaselineKind.Init, init);
        return new ConsolidationBaseline(BaselineKind.None, null);
    }

    /// <summary>Every commit of the memory repository, newest first.</summary>
    /// <exception cref="TacitException">The repository's history cannot be read.</exception>
    public static async Task<IReadOnlyList<ConsolidationCommit>> HistoryAsync(GitCli git, CancellationToken cancellationToken)
    {
        var log = await git.RunAsync(cancellationToken, "log", "--format=%h%x09%as%x09%s");
        return log.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t', 3))
            .Where(parts => parts.Length == 3)
            .Select(parts => new ConsolidationCommit(parts[0], DateOnly.ParseExact(parts[1], "yyyy-MM-dd", CultureInfo.InvariantCulture), parts[2]))
            .ToList();
    }

    /// <summary>Whether a commit is a consolidation: its subject starts with <c>consolidate:</c>.</summary>
    public static bool IsConsolidation(ConsolidationCommit commit) =>
        commit.Subject.StartsWith("consolidate:", StringComparison.Ordinal);
}

/// <summary>A commit of the memory repository: its abbreviated hash, its local date and its subject.</summary>
internal sealed record ConsolidationCommit(string Hash, DateOnly Day, string Subject);

/// <summary>Which commit the baseline is: a consolidation, the init commit standing in for a consolidation, or none at all.</summary>
internal enum BaselineKind
{
    Consolidation,
    Init,
    None,
}
