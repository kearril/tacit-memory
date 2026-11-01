using ConsoleAppFramework;
using TacitMemory.Core;
using TacitMemory.Mcp;
using TacitMemory.Core.Show;

namespace TacitMemory.Cli;

internal sealed class Commands
{
    private readonly Tacit _tacit = new(TacitOptions.FromEnvironment());

    /// <summary>Create the config templates and the memory repository.</summary>
    /// <param name="language">Memory language: zh or en. Defaults to the system UI language.</param>
    /// <param name="memory">Memory repository location. Defaults to memory under TACIT_HOME.</param>
    [Command("init")]
    public async Task<int> Init(MemoryLanguage? language = null, string? memory = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _tacit.InitAsync(new InitRequest { Language = language, MemoryPath = memory }, cancellationToken);
            Console.WriteLine(result.Report);
            return 0;
        }
        catch (TacitException e)
        {
            return Fail(e);
        }
    }

    /// <summary>Start the memory service: maintain the resident blocks and serve MCP on 127.0.0.1.</summary>
    [Command("serve")]
    public async Task<int> Serve(CancellationToken cancellationToken = default)
    {
        MemoryService service;
        try
        {
            service = _tacit.OpenService(Console.WriteLine);
        }
        catch (Exception e) when (IsUserFacing(e))
        {
            return Fail(e);
        }

        using (service)
        {
            foreach (var render in service.StartupRender)
                Console.WriteLine(render.Message);

            await using var host = await McpHost.TryStartAsync(service, service.Config.Port, cancellationToken);
            if (host is null)
            {
                Console.Error.WriteLine(service.PortInUseMessage());
                return 1;
            }

            Console.WriteLine(service.ListeningMessage(host.Endpoint));
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Ctrl+C: stop the host and leave.
            }

            return 0;
        }
    }

    /// <summary>Bind a client: write the resident block into its context file and register the MCP server.</summary>
    /// <param name="client">The client to bind: omp.</param>
    [Command("connect")]
    public int Connect(ClientKind client) => Run(() => _tacit.Connect(client).Report);

    /// <summary>Unbind a client: remove the resident block and the MCP registration.</summary>
    /// <param name="client">The client to unbind: omp.</param>
    [Command("disconnect")]
    public int Disconnect(ClientKind client) => Run(() => _tacit.Disconnect(client).Report);

    /// <summary>Show what the agent receives: the server instructions, the resident block, the memory skill, the tool descriptions or the remember prompt, each with its source. Needs no running service.</summary>
    /// <param name="target">instructions, resident, skill, tools or remember.</param>
    /// <param name="default">Print the built-in text alone, ready to save as the file that replaces it (instructions, resident) or the skill's system part (skill).</param>
    [Command("show")]
    public Task<int> Show([Argument] ShowWhat target, bool @default = false) => RunAsync(async () => target switch
    {
        ShowWhat.Tools => _tacit.ShowTools(ToolCatalog.Describe(), @default).Report,
        ShowWhat.Instructions => (await _tacit.ShowAsync(ShowTarget.Instructions, @default)).Report,
        ShowWhat.Resident => (await _tacit.ShowAsync(ShowTarget.Resident, @default)).Report,
        ShowWhat.Skill => (await _tacit.ShowAsync(ShowTarget.Skill, @default)).Report,
        _ => (await _tacit.ShowAsync(ShowTarget.Remember, @default)).Report,
    });

    private static int Run(Func<string> operation)
    {
        try
        {
            Console.WriteLine(operation());
            return 0;
        }
        catch (Exception e) when (IsUserFacing(e))
        {
            return Fail(e);
        }
    }

    private static async Task<int> RunAsync(Func<Task<string>> operation)
    {
        try
        {
            Console.WriteLine(await operation());
            return 0;
        }
        catch (Exception e) when (IsUserFacing(e))
        {
            return Fail(e);
        }
    }

    /// <summary>Refusals and file-system failures the user can act on; their messages are shown as they are.</summary>
    private static bool IsUserFacing(Exception e) => e is TacitException or IOException or UnauthorizedAccessException;

    private static int Fail(Exception e)
    {
        Console.Error.WriteLine(e.Message);
        return 1;
    }
}

/// <summary>The targets of <c>tacit show</c>.</summary>
internal enum ShowWhat
{
    Instructions,
    Resident,
    Skill,
    Tools,
    Remember,
}
