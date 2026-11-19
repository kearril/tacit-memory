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

    /// <summary>
    /// UTF-8 without a byte order mark. The encoding is also git's standard input, and a process start writes the mark
    /// there at once; off Windows, a git that has already exited turns that write into a broken pipe.
    /// </summary>
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>How long a command that reaches a remote may take before it is stopped.</summary>
    internal static readonly TimeSpan RemoteTimeout = TimeSpan.FromSeconds(60);

    /// <summary>How long a clone may take: it brings the whole history, where a sync brings a few commits.</summary>
    internal static readonly TimeSpan CloneTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The SSH command a remote command runs with when the user has not chosen one: it fails at once instead of asking for a
    /// password, a passphrase or a host key, and gives up on a host that does not answer.
    /// </summary>
    private const string BatchSsh = "ssh -o BatchMode=yes -o ConnectTimeout=15";

    /// <summary>The directory git runs in.</summary>
    public string WorkingDirectory => repository;

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
                "git", [.. FixedSettings, .. args], workingDirectory: repository, encoding: Utf8,
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
    /// Runs a command whose exit code is an answer, such as <c>merge-base</c> or <c>rev-parse --verify</c>: any exit code is
    /// returned with the standard output and error; only a git that cannot be started is a failure.
    /// </summary>
    public async Task<(int ExitCode, string Output, string Error)> TryRunAsync(CancellationToken cancellationToken, params string[] args)
    {
        var exitCode = 0;
        try
        {
            var (output, error) = await Command.ReadAsync(
                "git", [.. FixedSettings, .. args], workingDirectory: repository, encoding: Utf8,
                handleExitCode: code =>
                {
                    exitCode = code;
                    return true;
                },
                ct: cancellationToken);
            return (exitCode, output, error);
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            throw new TacitException(TacitError.GitFailed, Messages.GitFailed.Format(language, args[0], e.Message), e);
        }
    }

    /// <summary>
    /// Runs a command that reaches a remote (<c>fetch</c>, <c>push</c>, <c>clone</c>, <c>ls-remote</c>) so that it never waits
    /// for an answer from a person: git's own prompts and the credential manager's windows are off, and SSH runs in batch mode
    /// unless the user has chosen an SSH command (<c>core.sshCommand</c>, <c>GIT_SSH_COMMAND</c> or <c>GIT_SSH</c>), which
    /// must then not ask either. The command is stopped after <see cref="RemoteTimeout"/>.
    /// </summary>
    public Task<string> RunRemoteAsync(CancellationToken cancellationToken, params string[] args) =>
        RunRemoteAsync(RemoteTimeout, cancellationToken, args);

    /// <summary>Runs a command that reaches a remote as <see cref="RunRemoteAsync(CancellationToken, string[])"/> does, stopped after <paramref name="limit"/>.</summary>
    public async Task<string> RunRemoteAsync(TimeSpan limit, CancellationToken cancellationToken, params string[] args)
    {
        var ownSsh = Environment.GetEnvironmentVariable("GIT_SSH_COMMAND") is { Length: > 0 }
            || Environment.GetEnvironmentVariable("GIT_SSH") is { Length: > 0 }
            || (await TryRunAsync(cancellationToken, "config", "--get", "core.sshCommand")).Output.Trim().Length > 0;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(limit);
        try
        {
            var (output, _) = await Command.ReadAsync(
                "git", [.. FixedSettings, .. args], workingDirectory: repository, encoding: Utf8,
                configureEnvironment: environment =>
                {
                    environment["GIT_TERMINAL_PROMPT"] = "0";
                    environment["GCM_INTERACTIVE"] = "never";
                    if (!ownSsh)
                        environment["GIT_SSH_COMMAND"] = BatchSsh;
                },
                ct: timeout.Token);
            return output;
        }
        catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TacitException(
                TacitError.GitFailed, Messages.GitTimedOut.Format(language, args[0], (int)limit.TotalSeconds), e);
        }
        catch (Exception e) when (e is ExitCodeReadException or System.ComponentModel.Win32Exception)
        {
            var detail = e is ExitCodeReadException read ? read.StandardError.Trim() : e.Message;
            throw new TacitException(TacitError.GitFailed, Messages.GitFailed.Format(language, args[0], detail), e);
        }
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
                workingDirectory: repository, encoding: Utf8).GetAwaiter().GetResult();
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
