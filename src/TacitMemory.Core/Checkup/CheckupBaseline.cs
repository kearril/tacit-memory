using System.Globalization;
using TacitMemory.Core.Git;

namespace TacitMemory.Core.Checkup;

/// <summary>Where the last checkup stands: the latest <c>checkup:</c> commit, else the latest <c>init:</c> commit, else none.</summary>
internal sealed record CheckupBaseline(BaselineKind Kind, CheckupCommit? Commit)
{
    /// <summary>Reads the history of the memory repository. Commits are matched by their subject prefix.</summary>
    public static async Task<CheckupBaseline> ReadAsync(GitCli git, CancellationToken cancellationToken)
    {
        var log = await git.RunAsync(cancellationToken, "log", "--format=%h%x09%as%x09%s");
        var commits = log.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t', 3))
            .Where(parts => parts.Length == 3)
            .Select(parts => new CheckupCommit(parts[0], DateOnly.ParseExact(parts[1], "yyyy-MM-dd", CultureInfo.InvariantCulture), parts[2]))
            .ToList();

        // git log lists the newest commit first, so the first match is the latest one.
        if (commits.FirstOrDefault(commit => commit.Subject.StartsWith("checkup:", StringComparison.Ordinal)) is { } checkup)
            return new CheckupBaseline(BaselineKind.Checkup, checkup);
        if (commits.FirstOrDefault(commit => commit.Subject.StartsWith("init:", StringComparison.Ordinal)) is { } init)
            return new CheckupBaseline(BaselineKind.Init, init);
        return new CheckupBaseline(BaselineKind.None, null);
    }
}

/// <summary>A commit of the memory repository: its abbreviated hash, its local date and its subject.</summary>
internal sealed record CheckupCommit(string Hash, DateOnly Day, string Subject);

/// <summary>Which commit the baseline is: a checkup, the init commit standing in for a checkup, or none at all.</summary>
internal enum BaselineKind
{
    Checkup,
    Init,
    None,
}
