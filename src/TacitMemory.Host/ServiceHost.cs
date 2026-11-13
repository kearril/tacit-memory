using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TacitMemory.Core;
using TacitMemory.Host.Api;
using TacitMemory.Host.Mcp;

namespace TacitMemory.Host;

/// <summary>
/// The memory service's HTTP endpoints: MCP at <c>/mcp</c> and the management API at <c>/api</c>, on 127.0.0.1 and, when
/// <c>server.host</c> names one, on that address too. The peer address decides who is local: a connection from a loopback
/// address is local, any other is remote. Stateless.
/// </summary>
public sealed class ServiceHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private ServiceHost(WebApplication app, Uri endpoint)
    {
        _app = app;
        Endpoint = endpoint;
    }

    public Uri Endpoint { get; }

    /// <summary>
    /// Starts listening on loopback, and on the address <c>server.host</c> names when there is one; <c>null</c> when
    /// <paramref name="port"/> is already in use on loopback. Port 0 picks a free one. An extra address that cannot bind does
    /// not stop the start: the service runs on loopback, and the status report says why. ASP.NET Core and the MCP SDK write
    /// nothing below warning.
    /// </summary>
    public static Task<ServiceHost?> TryStartAsync(MemoryService service, int port, ILoggerProvider logs, CancellationToken cancellationToken = default) =>
        TryStartAsync(service, port, logs, IsLoopback, cancellationToken);

    /// <summary>
    /// <see cref="TryStartAsync(MemoryService, int, ILoggerProvider, CancellationToken)"/> with the rule that says which peers
    /// are local. Tests narrow the rule, so a remote connection can be made from a loopback address; the rule is not a setting.
    /// </summary>
    internal static async Task<ServiceHost?> TryStartAsync(
        MemoryService service, int port, ILoggerProvider logs, Func<IPAddress?, bool> isLocal, CancellationToken cancellationToken = default)
    {
        var extra = service.Config.ExtraAddress;
        Exception? extraFailure = null;
        if (extra is not null)
        {
            // Both listeners share one port, so a port 0 is picked here, before either is bound.
            if (port == 0)
                port = FreePort();
            extraFailure = Probe(extra, port);
        }
        var listenOnExtra = extra is not null && extraFailure is null;
        // 0.0.0.0 already covers loopback, and on most systems it cannot share a port with 127.0.0.1, so it stands alone.
        var listenOnLoopback = !(listenOnExtra && extra!.Equals(IPAddress.Any));

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Logging.AddProvider(logs);
        // A taken port is reported to the user in their language; the host's own error log would only repeat it.
        builder.Logging.AddFilter("Microsoft.Extensions.Hosting", LogLevel.None);
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            if (listenOnLoopback)
                kestrel.Listen(IPAddress.Loopback, port);
            if (listenOnExtra)
                kestrel.Listen(extra!, port);
        });
        builder.Services.AddSingleton(service);
        ManagementApi.AddOpenApi(builder.Services);
        builder.Services.AddMcpServer(options =>
            {
                options.ServerInfo = new() { Name = "tacit", Version = Version };
                options.Filters.Message.IncomingFilters.Add(RecordClients(service));
            })
            .WithHttpTransport(options =>
            {
                options.SessionMode = HttpServerSessionMode.Stateless;
                // Stateless mode calls this for every request; clients only take the instructions from initialize,
                // and the service caches them, so handing them out every time costs nothing.
                options.ConfigureSessionOptions = async (_, session, cancellationToken) =>
                {
                    session.ServerInstructions = await service.ServerInstructionsAsync(cancellationToken);
                };
            })
            // The tools are built with the limits this service loaded at start, so the descriptions name the values it enforces.
            .WithTools(ToolCatalog.Tools(new MemoryTools(service), service.Config.Limits))
            .WithResources<MemoryResources>()
            // TacitMemory offers no prompts, but some clients ask for the list regardless of the advertised capabilities and
            // drop the server when that request fails, so the list must always answer, with nothing in it.
            .WithListPromptsHandler((_, _) => ValueTask.FromResult(new ListPromptsResult()))
            .WithGetPromptHandler((request, _) =>
                throw new McpProtocolException($"Unknown prompt '{request.Params?.Name}'.", McpErrorCode.InvalidParams));

        var app = builder.Build();
        ManagementApi.Map(app, service, isLocal);
        // MCP is for this machine only: a connection from anywhere else is refused before the endpoint sees it.
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/mcp", StringComparison.Ordinal) && !isLocal(context.Connection.RemoteIpAddress))
            {
                var refusal = new ApiError(nameof(TacitError.McpRemoteRefused), service.RefusalMessage(TacitError.McpRemoteRefused));
                await Results.Text(JsonSerializer.Serialize(refusal, ManagementApiJson.Options), "application/json; charset=utf-8", statusCode: StatusCodes.Status403Forbidden)
                    .ExecuteAsync(context);
                return;
            }
            await next(context);
        });
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

        if (extra is not null)
            service.RecordExtraListener(extra, port, extraFailure);
        var bound = new Uri(app.Urls.First()).Port;
        return new ServiceHost(app, new Uri($"http://127.0.0.1:{bound}/mcp"));
    }

    /// <summary>
    /// Binds the address the way the server will, before the server starts, so a failure is known in advance and costs the extra
    /// listener alone. Returns the failure, or <c>null</c> when the address can be bound.
    /// </summary>
    private static Exception? Probe(IPAddress address, int port)
    {
        try
        {
            using var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(new IPEndPoint(address, port));
            return null;
        }
        catch (SocketException e)
        {
            return e;
        }
    }

    /// <summary>A port that was free a moment ago; used only when a port 0 must name one port for two listeners.</summary>
    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>The peers the default rule counts as local: the whole IPv4 loopback range, IPv6 loopback and their IPv4-mapped forms.</summary>
    private static bool IsLoopback(IPAddress? peer) => peer is not null && IPAddress.IsLoopback(peer);

    /// <summary>
    /// The incoming filter that hands each client's declaration to the service. On the 2026-07-28 revision every request carries
    /// it in its metadata. On the older revisions it comes only with the initialize request, whose parameters are read here, so
    /// the other requests of those sessions pass on unrecorded. The filter only observes: it never changes the request or its answer.
    /// </summary>
    private static McpMessageFilter RecordClients(MemoryService service) => next => async (context, cancellationToken) =>
    {
        if (context.JsonRpcMessage is JsonRpcRequest request)
            RecordRequest(service, request);
        await next(context, cancellationToken);
    };

    private static void RecordRequest(MemoryService service, JsonRpcRequest request)
    {
        if (request.Method == RequestMethods.Initialize)
        {
            var initialize = request.Params is { } parameters
                ? JsonSerializer.Deserialize<InitializeRequestParams>(parameters.ToJsonString(), McpJsonUtilities.DefaultOptions)
                : null;
            if (initialize?.ClientInfo is { } info)
                service.RecordClient(Observation(info, initialize.ProtocolVersion, initialize.Capabilities));
            return;
        }

        var envelope = request.Context;
        if (envelope?.ClientInfo is { } client)
            service.RecordClient(Observation(client, envelope.ProtocolVersion, envelope.ClientCapabilities));
    }

    /// <summary>The plain record of what a client declared: the name and version, the protocol revision, and the capabilities and extensions it named.</summary>
    private static ClientObservation Observation(Implementation client, string? protocolVersion, ClientCapabilities? declared)
    {
        var capabilities = new List<string>();
        if (declared?.Elicitation is not null)
            capabilities.Add("elicitation");
        // Sampling and roots are deprecated for the 2026-07-28 revision, but clients still declare them, and the record
        // reports what a client declares on the wire. Reading the field here does not use the feature.
#pragma warning disable MCP9005
        if (declared?.Sampling is not null)
            capabilities.Add("sampling");
        if (declared?.Roots is not null)
            capabilities.Add("roots");
#pragma warning restore MCP9005
        capabilities.AddRange(declared?.Experimental?.Keys ?? []);
        var extensions = declared?.Extensions?.Keys ?? [];
        return new ClientObservation(client.Name, client.Version, protocolVersion ?? "", capabilities, extensions.ToList());
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private static string Version =>
        typeof(ServiceHost).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
