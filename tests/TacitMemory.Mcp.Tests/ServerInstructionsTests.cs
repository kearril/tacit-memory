using ModelContextProtocol.Client;
using TacitMemory.Core;
using TacitMemory.Core.Tests;

namespace TacitMemory.Mcp.Tests;

public sealed class ServerInstructionsTests : IAsyncLifetime
{
    private readonly TestHome _home = new();
    private MemoryService _service = null!;
    private McpHost _host = null!;

    public async Task InitializeAsync()
    {
        _home.InitReady();
        Directory.CreateDirectory(Path.Combine(_home.DefaultMemory, "pages", "tacit-memory"));
        _service = _home.Tacit().OpenService(_ => { });
        _host = await McpHost.TryStartAsync(_service, port: 0) ?? throw new InvalidOperationException("Port 0 is never in use.");
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        _service.Dispose();
        _home.Dispose();
    }

    private Task<McpClient> ConnectAsync() =>
        McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = _host.Endpoint,
            TransportMode = HttpTransportMode.StreamableHttp,
        }));

    [Fact]
    public async Task A_client_receives_the_server_instructions_when_it_connects()
    {
        await using var client = await ConnectAsync();

        Assert.Equal("tacit", client.ServerInfo?.Name);
        var instructions = Assert.IsType<string>(client.ServerInstructions);
        Assert.StartsWith("TacitMemory is the user's personal memory across sessions.", instructions);
        Assert.Contains("Memory is written in Chinese.", instructions);
        Assert.EndsWith("## Projects\ntacit-memory", instructions);
    }

    [Fact]
    public async Task No_tools_or_prompts_are_offered_yet()
    {
        await using var client = await ConnectAsync();

        Assert.Null(client.ServerCapabilities.Tools);
        Assert.Null(client.ServerCapabilities.Prompts);
    }

    [Fact]
    public async Task A_second_service_on_a_taken_port_is_refused()
    {
        Assert.Equal("127.0.0.1", _host.Endpoint.Host);
        Assert.Null(await McpHost.TryStartAsync(_service, _host.Endpoint.Port));
    }
}
