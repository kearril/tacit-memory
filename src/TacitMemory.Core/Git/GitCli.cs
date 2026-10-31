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
}
