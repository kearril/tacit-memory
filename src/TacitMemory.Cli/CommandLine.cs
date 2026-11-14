using ConsoleAppFramework;
using Microsoft.Extensions.DependencyInjection;
using TacitMemory.Core;

namespace TacitMemory.Cli;

/// <summary>
/// The console the CLI reads and writes. The process fills it from <see cref="Console"/> and the environment; tests fill it with buffers.
/// The three flags say whether each stream is a terminal, so a question is asked only where someone can answer it.
/// NoColor says that the environment set NO_COLOR. A stream is styled only when it is a terminal and NoColor is not set.
/// </summary>
public sealed record CliConsole(
    TextWriter Out,
    TextWriter Error,
    TextReader In,
    bool OutIsTerminal,
    bool ErrorIsTerminal,
    bool InIsTerminal,
    bool NoColor)
{
    /// <summary>Whether standard output is styled: it is a terminal and <c>NO_COLOR</c> is not set.</summary>
    public bool StylesOut => OutIsTerminal && !NoColor;

    /// <summary>Whether standard error is styled: it is a terminal and <c>NO_COLOR</c> is not set.</summary>
    public bool StylesError => ErrorIsTerminal && !NoColor;
}

/// <summary>
/// The one entry into the CLI. Every command runs in process through here with the options and console it is given,
/// so nothing outside it reads the environment or the process streams.
/// </summary>
public static class CommandLine
{
    /// <summary>
    /// Runs one command line and returns its exit code: 0 success, 1 failed, 2 usage error, 3 config refused, 4 service
    /// not running. Results go to <see cref="CliConsole.Out"/>; errors go to <see cref="CliConsole.Error"/>. No arguments
    /// means the overview help.
    /// </summary>
    public static async Task<int> RunAsync(string[] args, TacitOptions options, CliConsole console, CancellationToken cancellationToken = default)
    {
        if (args.Length == 0)
            args = ["-h"];
        args = WithCanonicalWords(args);

        // The framework reports through the exit code property; keep the caller's value and restore it afterwards.
        var previous = Environment.ExitCode;
        Environment.ExitCode = 0;
        try
        {
            // The help is printed before routing, so these two are the only output the framework writes itself.
            ConsoleApp.Log = line => console.Out.WriteLine(line);
            ConsoleApp.LogError = line => console.Error.WriteLine(line);

            var app = ConsoleApp.Create()
                .ConfigureServices(services =>
                {
                    services.AddSingleton(options);
                    services.AddSingleton(console);
                });
            app.UseFilter<UsageFilter>();
            app.Add<Commands>();
            await app.RunAsync(args, cancellationToken);
            return Environment.ExitCode;
        }
        finally
        {
            Environment.ExitCode = previous;
        }
    }

    /// <summary>The command names. Their match ignores case, so <c>tacit CONNECT</c> is <c>tacit connect</c>.</summary>
    private static readonly string[] CommandNames = ["init", "serve", "connect", "disconnect", "show", "probe", "status", "reindex", "backup", "restore"];

    /// <summary>The <c>show</c> subcommands. Their match ignores case, like the command names.</summary>
    private static readonly string[] ShowNames = ["instructions", "rules", "resident", "skill", "tools", "remember", "consolidate"];

    /// <summary>The <c>probe</c> targets. Their match ignores case, like the <c>show</c> subcommands.</summary>
    private static readonly string[] ProbeNames = [.. ModelRoles.All.Select(ModelRoles.Name)];

    /// <summary>
    /// Rewrites the command word, and the subcommand word after <c>show</c> or <c>probe</c>, to the spelling the framework routes by. Every
    /// other word, option values included, is passed on as it was given.
    /// </summary>
    private static string[] WithCanonicalWords(string[] args)
    {
        var words = (string[])args.Clone();
        if (words.Length == 0)
            return words;

        words[0] = Canonical(words[0], CommandNames);
        if (words[0] == "show" && words.Length > 1)
            words[1] = Canonical(words[1], ShowNames);
        if (words[0] == "probe" && words.Length > 1)
            words[1] = Canonical(words[1], ProbeNames);
        return words;
    }

    private static string Canonical(string word, string[] names) =>
        names.FirstOrDefault(name => string.Equals(name, word, StringComparison.OrdinalIgnoreCase)) ?? word;
}

/// <summary>A command received arguments it cannot use: exit code 2, and the message goes to stderr.</summary>
internal sealed class UsageException(string message) : Exception(message);

/// <summary>Turns an argument the framework cannot parse into a usage error: exit code 2, the framework's message on stderr.</summary>
internal sealed class UsageFilter(CliConsole console, ConsoleAppFilter next) : ConsoleAppFilter(next)
{
    public override async Task InvokeAsync(ConsoleAppContext context, CancellationToken cancellationToken)
    {
        try
        {
            await Next.InvokeAsync(context, cancellationToken);
        }
        catch (ArgumentParseFailedException e)
        {
            Environment.ExitCode = 2;
            console.Error.WriteLine(e.Message);
        }
    }
}
