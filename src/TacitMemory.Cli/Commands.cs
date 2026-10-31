using ConsoleAppFramework;
using TacitMemory.Core;
using TacitMemory.Mcp;

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
            service = _tacit.OpenService(render => Console.WriteLine(render.Message));
        }
        catch (TacitException e)
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

    private static int Run(Func<string> operation)
    {
        try
        {
            Console.WriteLine(operation());
            return 0;
        }
        catch (TacitException e)
        {
            return Fail(e);
        }
    }

    private static int Fail(TacitException e)
    {
        Console.Error.WriteLine(e.Message);
        return 1;
    }
}
