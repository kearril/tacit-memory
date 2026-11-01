using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using TacitMemory.Core;

namespace TacitMemory.Mcp;

/// <summary>The memory service's HTTP MCP endpoint at <c>/mcp</c> on 127.0.0.1, stateless.</summary>
public sealed class McpHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private McpHost(WebApplication app, Uri endpoint)
    {
        _app = app;
        Endpoint = endpoint;
    }

    public Uri Endpoint { get; }

    /// <summary>Starts listening; <c>null</c> when <paramref name="port"/> is already in use. Port 0 picks a free one.</summary>
    public static async Task<McpHost?> TryStartAsync(MemoryService service, int port, CancellationToken cancellationToken = default)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        // A taken port is reported to the user in their language; the host's own error log would only repeat it.
        builder.Logging.AddFilter("Microsoft.Extensions.Hosting", LogLevel.None);
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, port));
        builder.Services.AddSingleton(service);
        builder.Services.AddMcpServer(options => options.ServerInfo = new() { Name = "tacit", Version = Version })
            .WithHttpTransport(options =>
            {
                options.SessionMode = HttpServerSessionMode.Stateless;
                // Stateless mode calls this for every request; clients only take the instructions from initialize,
                // and the service caches them, so handing them out every time costs nothing.
                options.ConfigureSessionOptions = (_, session, _) =>
                {
                    session.ServerInstructions = service.ServerInstructions;
                    return Task.CompletedTask;
                };
            })
            .WithTools<MemoryTools>()
            // Registers both the list and the get handlers. omp asks for the list regardless of the advertised
            // capabilities and drops the server when that request fails, so it must always answer.
            .WithPrompts<MemoryPrompts>();

        var app = builder.Build();
        app.MapMcp("/mcp");
        try
        {
            await app.StartAsync(cancellationToken);
        }
        catch (IOException e) when (e.InnerException is AddressInUseException)
        {
            await app.DisposeAsync();
            return null;
        }

        var bound = new Uri(app.Urls.Single()).Port;
        return new McpHost(app, new Uri($"http://127.0.0.1:{bound}/mcp"));
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private static string Version =>
        typeof(McpHost).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
