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
using TacitMemory.Host.Localization;
using TacitMemory.Host.Mcp;
using TacitMemory.Host.Web;

namespace TacitMemory.Host;

/// <summary>
/// The memory service's HTTP endpoints: MCP at <c>/mcp</c>, the management API at <c>/api</c> and the web interface at
/// <c>/ui</c> (with <c>/</c> redirected there), on 127.0.0.1 and, when <c>server.host</c> names one, on that address too.
/// The peer address decides who is local: a connection from a loopback address is local, any other is remote; a remote
/// connection reaches the API only with a session. Stateless.
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
        TryStartAsync(service, port, logs, IsLoopback, webRoot: null, cancellationToken: cancellationToken);

    /// <summary>
    /// <see cref="TryStartAsync(MemoryService, int, ILoggerProvider, CancellationToken)"/> with the rule that says which peers
    /// are local, and the directory the web interface is read from. Tests narrow the rule, so a remote connection can be made
    /// from a loopback address, and they point the directory at a temporary one; neither is a setting. Without a directory the
    /// web interface is read from <c>web</c> beside the executable. <paramref name="probe"/> binds an address the way the
    /// server will; a test replaces it to reach a failure that comes after the probe.
    /// </summary>
    internal static async Task<ServiceHost?> TryStartAsync(
        MemoryService service, int port, ILoggerProvider logs, Func<IPAddress?, bool> isLocal, string? webRoot = null,
        Func<IPAddress, int, Exception?>? probe = null, CancellationToken cancellationToken = default)
    {
        service.CheckExtraListenerAllowed();
        probe ??= Probe;
        var directory = webRoot ?? Path.Combine(AppContext.BaseDirectory, "web");
        var extra = service.Config.ExtraAddress;
        // Both listeners share one port, so a port 0 is picked here, before either is bound.
        if (extra is not null && port == 0)
            port = FreePort();
        // Loopback is always listened on, so its port must be free before anything is bound. A wildcard address does not show
        // that: on Windows it binds even while another program holds 127.0.0.1 on the same port.
        if (port != 0 && probe(IPAddress.Loopback, port) is SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse })
            return null;

        Exception? extraFailure = extra is null ? null : probe(extra, port);
        var listenOnExtra = extra is not null && extraFailure is null;
        // Elsewhere 0.0.0.0 covers loopback and cannot share its port with 127.0.0.1, so it stands alone. Windows allows both.
        var listenOnLoopback = !(listenOnExtra && extra!.Equals(IPAddress.Any) && !OperatingSystem.IsWindows());
        ServiceHost host;
        try
        {
            host = await StartOn(service, logs, isLocal, directory, port, listenOnLoopback, listenOnExtra ? extra : null, cancellationToken);
        }
        catch (IOException e) when (IsAddressInUse(e))
        {
            if (!listenOnExtra)
                return null;
            // The extra address was taken after it was probed. The service runs on loopback alone, and the status report says why.
            extraFailure = e.InnerException?.InnerException ?? e;
            try
            {
                host = await StartOn(service, logs, isLocal, directory, port, loopback: true, extra: null, cancellationToken);
            }
            catch (IOException again) when (IsAddressInUse(again))
            {
                return null;
            }
        }

        if (extra is not null)
            service.RecordExtraListener(
                extra, port, extraFailure is null ? null : HostMessages.ExtraListenerFailure(extraFailure, extra, port, service.Language));
        return host;
    }

    /// <summary>
    /// Builds the host and starts it on loopback, when <paramref name="loopback"/> is set, and on <paramref name="extra"/> when
    /// it is not null. A taken address makes the start throw an <see cref="IOException"/> that <see cref="IsAddressInUse"/> names.
    /// </summary>
    private static async Task<ServiceHost> StartOn(
        MemoryService service, ILoggerProvider logs, Func<IPAddress?, bool> isLocal, string webRoot, int port,
        bool loopback, IPAddress? extra, CancellationToken cancellationToken)
    {
        var app = Build(service, logs, isLocal, webRoot, port, loopback, extra);
        try
        {
            await app.StartAsync(cancellationToken);
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
        var bound = new Uri(app.Urls.First()).Port;
        return new ServiceHost(app, new Uri($"http://127.0.0.1:{bound}/mcp"));
    }

    /// <summary>Whether a start failed because an address was already in use.</summary>
    private static bool IsAddressInUse(IOException e) => e.InnerException is AddressInUseException;

    /// <summary>Builds the application with its endpoints, listening as <see cref="StartOn"/> says.</summary>
    private static WebApplication Build(
        MemoryService service, ILoggerProvider logs, Func<IPAddress?, bool> isLocal, string webRoot, int port,
        bool loopback, IPAddress? extra)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Logging.AddProvider(logs);
        // A taken port is reported to the user in their language; the host's own error log would only repeat it.
        builder.Logging.AddFilter("Microsoft.Extensions.Hosting", LogLevel.None);
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            if (loopback)
                kestrel.Listen(IPAddress.Loopback, port);
            if (extra is not null)
                kestrel.Listen(extra, port);
        });
        builder.Services.AddSingleton(service);
        ManagementApi.AddOpenApi(builder.Services);
        builder.Services.AddMcpServer(options =>
            {
                options.ServerInfo = new() { Name = "tacit", Version = service.Version };
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
        // A connection from another device may reach only the root, the web interface and the management API, and MCP not at
        // all. Every other path is refused here, before any endpoint can answer it. The paths are matched the way the routes
        // match them, without regard to letter case.
        app.Use(async (context, next) =>
        {
            if (!isLocal(context.Connection.RemoteIpAddress) && !RemoteMayReach(context.Request.Path))
            {
                var error = context.Request.Path.StartsWithSegments(McpPath, StringComparison.OrdinalIgnoreCase)
                    ? TacitError.McpRemoteRefused
                    : TacitError.RemotePathRefused;
                await ManagementApi.Json(new ApiError(error, HostMessages.Refusal(error, service.Language)), StatusCodes.Status403Forbidden)
                    .ExecuteAsync(context);
                return;
            }
            await next(context);
        });
        ManagementApi.Map(app, service, isLocal);
        WebInterface.Map(app, service, webRoot);
        app.MapMcp(McpPath);
        return app;
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
            // Off Windows a port left in TIME_WAIT by an earlier run is bound again, not reported as taken. On Windows the option
            // would let the probe bind a port that another socket holds, so it is not set there.
            if (!OperatingSystem.IsWindows())
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
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

    /// <summary>The path of the MCP endpoint.</summary>
    private const string McpPath = "/mcp";

    /// <summary>Whether a connection from another device may use the path: the root, and the paths under the web interface and the management API.</summary>
    private static bool RemoteMayReach(PathString path) =>
        path == "/" || path.StartsWithSegments("/ui", StringComparison.OrdinalIgnoreCase) || path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);

    /// <summary>The peers the default rule counts as local: the whole IPv4 loopback range, IPv6 loopback and their IPv4-mapped forms.</summary>
    internal static bool IsLoopback(IPAddress? peer) =>
        peer is not null && IPAddress.IsLoopback(peer.IsIPv4MappedToIPv6 ? peer.MapToIPv4() : peer);

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
                ? JsonSerializer.Deserialize(parameters.ToJsonString(), McpJsonUtilities.DefaultOptions.For<InitializeRequestParams>())
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
}
