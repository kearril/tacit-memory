using System.Net;
using System.Text.Json;
using TacitMemory.Core;
using TacitMemory.Core.Tests;
using TacitMemory.Host.Api;

namespace TacitMemory.Host.Tests;

/// <summary>
/// <c>GET /api/consolidation</c> over real HTTP: whether a consolidation is due, and the refusal when the memory
/// repository's history cannot be read.
/// </summary>
public sealed class ConsolidationApiTests : IAsyncLifetime
{
    private readonly TestHome _home = new();
    private MemoryService _service = null!;
    private ServiceHost _host = null!;
    private HttpClient _local = null!;

    public async Task InitializeAsync()
    {
        _home.InitReady();
        _service = _home.Tacit().OpenService(_ => { }, new CapturingLoggerFactory());
        _host = await ServiceHost.TryStartAsync(_service, port: 0, NullLoggerProvider.Instance, PeerClients.OnlyLoopbackIsLocal)
            ?? throw new InvalidOperationException("Port 0 is never in use.");
        _local = PeerClients.Local();
    }

    public async Task DisposeAsync()
    {
        _local.Dispose();
        await _host.DisposeAsync();
        _service.Dispose();
        _home.Dispose();
    }

    private Uri Url => new($"http://127.0.0.1:{_host.Endpoint.Port}/api/consolidation");

    private void EstablishWorkstream() =>
        File.WriteAllText(Path.Combine(_home.DefaultMemory, "workstreams.md"), "## Workstream\n### alpha\nBackground: 有背景的主线。\n");

    [Fact]
    public async Task A_fresh_memory_with_a_workstream_is_not_due()
    {
        EstablishWorkstream();

        using var response = await _local.GetAsync(Url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("""{"due":false}""", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_empty_workstream_is_due()
    {
        using var response = await _local.GetAsync(Url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("""{"due":true}""", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_history_that_cannot_be_read_is_answered_with_an_error_rather_than_not_due()
    {
        EstablishWorkstream();
        Directory.Move(Path.Combine(_home.DefaultMemory, ".git"), Path.Combine(_home.DefaultMemory, ".git-moved"));

        using var response = await _local.GetAsync(Url);

        Assert.False(response.IsSuccessStatusCode);
        var error = JsonSerializer.Deserialize<ApiError>(await response.Content.ReadAsStringAsync(), ManagementApiJson.Options)!;
        Assert.Equal(TacitError.GitFailed, error.Error);
    }
}
