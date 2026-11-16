using System.Globalization;
using System.Text;
using SimpleExec;
using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Git;

/// <summary>Runs the <c>git</c> command line in one memory repository; failures are reported in <paramref name="language"/>.</summary>
internal sealed class GitCli(string repository, MemoryLanguage language)
{
    /// <summary>
    /// Settings passed on every call: unescaped non-ASCII paths, no line-ending conversion, no signing prompts, and
    /// the fixed author and committer.
    /// </summary>
    private static readonly string[] FixedSettings =
    [
        "-c", "core.quotepath=off",
        "-c", "core.autocrlf=false",
        "-c", "commit.gpgsign=false",
        "-c", "user.name=tacit",
        "-c", "user.email=tacit@localhost",
    ];

    /// <summary>Whether the directory is itself the top of a repository (has its own <c>.git</c>).</summary>
    public bool IsRepositoryRoot()
    {
        var dotGit = Path.Combine(repository, ".git");
        return Directory.Exists(dotGit) || File.Exists(dotGit);
    }

    public async Task<string> RunAsync(CancellationToken cancellationToken, params string[] args)
    {
        try
        {
            var (output, _) = await Command.ReadAsync(
                "git", [.. FixedSettings, .. args], workingDirectory: repository, encoding: Encoding.UTF8,
                ct: cancellationToken);
            return output;
        }
        catch (Exception e) when (e is ExitCodeReadException or System.ComponentModel.Win32Exception)
        {
            var detail = e is ExitCodeReadException read ? read.StandardError.Trim() : e.Message;
            throw new TacitException(TacitError.GitFailed, Messages.GitFailed.Format(language, args[0], detail), e);
        }
    }

    /// <summary>
    /// Commits exactly <paramref name="files"/> (paths relative to the repository) with <paramref name="message"/>,
    /// whatever else is staged.
    /// </summary>
    public async Task CommitAsync(string message, IReadOnlyList<string> files, CancellationToken cancellationToken)
    {
        await RunAsync(cancellationToken, ["add", "--", .. files]);
        await RunAsync(cancellationToken, ["commit", "--quiet", "-m", message, "--", .. files]);
    }

    /// <summary>
    /// Commits exactly <paramref name="path"/> (relative to the repository) with <paramref name="message"/>, whatever else is
    /// staged. A tracked file is committed in one call; a file git does not track yet is added first.
    /// </summary>
    public async Task CommitFileAsync(string message, string path, bool tracked, CancellationToken cancellationToken)
    {
        if (!tracked)
            await RunAsync(cancellationToken, "add", "--", path);
        await RunAsync(cancellationToken, "commit", "--quiet", "-m", message, "--", path);
    }

    /// <summary>How git sees <paramref name="path"/> (relative to the repository), which must exist.</summary>
    public async Task<FileState> FileStateAsync(string path, CancellationToken cancellationToken)
    {
        var status = await RunAsync(cancellationToken, "status", "--porcelain", "--", path);
        if (status.Trim().Length == 0)
            return FileState.Committed;
        return status.StartsWith("??", StringComparison.Ordinal) ? FileState.Untracked : FileState.Changed;
    }

    /// <summary>Whether <paramref name="path"/> (relative to the repository) has uncommitted changes, or is untracked.</summary>
    public async Task<bool> IsDirtyAsync(string path, CancellationToken cancellationToken) =>
        (await RunAsync(cancellationToken, "status", "--porcelain", "--", path)).Trim().Length > 0;

    /// <summary>The text of <paramref name="path"/> (relative to the repository) at <paramref name="commit"/>; <c>null</c> when the file is not there.</summary>
    public async Task<string?> ShowAsync(string commit, string path, CancellationToken cancellationToken)
    {
        var listed = await RunAsync(cancellationToken, "ls-tree", "--name-only", commit, "--", path);
        return listed.Trim().Length == 0 ? null : await RunAsync(cancellationToken, "show", $"{commit}:{path}");
    }

    /// <summary>
    /// The author date (local, as the commit records it) of the last commit that touched each file under
    /// <paramref name="folder"/>, keyed by path relative to the repository. Files never committed are absent. A repository
    /// with no commits, or no repository at all, gives none. Blocks on the async read because the index it serves is
    /// synchronous; the callers run without a synchronization context, so this cannot deadlock.
    /// </summary>
    public IReadOnlyDictionary<string, DateOnly> LastCommitDays(string folder)
    {
        var days = new Dictionary<string, DateOnly>(StringComparer.Ordinal);
        string output;
        try
        {
            (output, _) = Command.ReadAsync(
                "git", [.. FixedSettings, "log", "--format=%x1f%as", "--name-only", "--relative", "--", folder],
                workingDirectory: repository, encoding: Encoding.UTF8).GetAwaiter().GetResult();
        }
        catch (ExitCodeReadException)
        {
            return days;
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            throw new TacitException(TacitError.GitFailed, Messages.GitFailed.Format(language, "log", e.Message), e);
        }

        // Each commit starts with its date marked by the separator; the file names after it belong to that commit. The
        // newest commit comes first, so the first date seen for a file is its last commit.
        DateOnly? day = null;
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith('\x1f'))
                day = DateOnly.ParseExact(line[1..], "yyyy-MM-dd", CultureInfo.InvariantCulture);
            else if (line.Length > 0 && day is { } date)
                days.TryAdd(line, date);
        }

        return days;
    }
}

/// <summary>How git sees an existing file in the repository.</summary>
internal enum FileState
{
    /// <summary>Tracked, and the file matches its last commit.</summary>
    Committed,

    /// <summary>Tracked, with changes not yet committed.</summary>
    Changed,

    /// <summary>Not tracked by git yet.</summary>
    Untracked,
}
