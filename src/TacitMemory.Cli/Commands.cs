using ConsoleAppFramework;
using TacitMemory.Core;

namespace TacitMemory.Cli;

internal sealed class Commands
{
    /// <summary>Create the config templates and the memory repository.</summary>
    /// <param name="language">Memory language: zh or en. Defaults to the system UI language.</param>
    /// <param name="memory">Memory repository location. Defaults to memory under TACIT_HOME.</param>
    [Command("init")]
    public async Task<int> Init(MemoryLanguage? language = null, string? memory = null, CancellationToken cancellationToken = default)
    {
        var tacit = new Tacit(TacitOptions.FromEnvironment());
        try
        {
            var result = await tacit.InitAsync(new InitRequest { Language = language, MemoryPath = memory }, cancellationToken);
            Console.WriteLine(result.Report);
            return 0;
        }
        catch (TacitException e)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }
    }
}
