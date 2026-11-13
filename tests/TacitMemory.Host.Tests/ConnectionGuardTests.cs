using System.Net;
using System.Text;
using System.Text.Json;
using TacitMemory.Core;
using TacitMemory.Core.Tests;
using TacitMemory.Host.Api;

namespace TacitMemory.Host.Tests;

/// <summary>
/// The connection rules over real HTTP, with the host's locality narrowed to <c>127.0.0.1</c>: a connection from anywhere
/// else is remote. Remote connections get <c>/mcp</c> 403 always. Without a session, a remote connection gets 401 on every
/// <c>/api</c> path except the session endpoints, which answer; the guards of <c>/api</c> run first, so a bad Origin or
/// Content-Type is refused before the 401. A logged-in remote connection is covered in <see cref="AccessApiTests"/>.
/// </summary>
public sealed class ConnectionGuardTests : IAsyncLifetime
{
    private readonly TestHome _home = new();
    private readonly ModelDouble _models = new() { EmbeddingDown = true, RerankDown = true };
    private readonly CapturingLoggerFactory _logs = new();
    private MemoryService _service = null!;
    private ServiceHost _host = null!;
    private HttpClient _local = null!;
    private HttpClient _remote = null!;

    public async Task InitializeAsync()
    {
        _home.InitReady(models: _models);
        _service = _home.Tacit().OpenService(_ => { }, _logs);
        _host = await ServiceHost.TryStartAsync(_service, port: 0, NullLoggerProvider.Instance, PeerClients.OnlyLoopbackIsLocal)
            ?? throw new InvalidOperationException("Port 0 is never in use.");
        _local = PeerClients.Local();
        _remote = PeerClients.Remote();
    }

    public async Task DisposeAsync()
    {
        _local.Dispose();
        _remote.Dispose();
        await _host.DisposeAsync();
        _service.Dispose();
        _models.Dispose();
        _home.Dispose();
    }

    private int Port => _host.Endpoint.Port;

    private Uri Url(string path) => new($"http://127.0.0.1:{Port}{path}");

    [Fact]
    public async Task A_remote_connection_without_a_session_gets_401_with_an_error_body_on_every_api_path_but_the_session_ones()
    {
        foreach (var (method, path) in new[]
        {
            (HttpMethod.Get, "/api/status"),
            (HttpMethod.Get, "/api/openapi.json"),
            (HttpMethod.Post, "/api/reindex"),
            (HttpMethod.Post, "/api/backup"),
            (HttpMethod.Post, "/api/restore"),
            (HttpMethod.Get, "/api/no-such-endpoint"),
        })
        {
            using var request = new HttpRequestMessage(method, Url(path));
            if (method == HttpMethod.Post)
                request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

            var (status, text) = await Send(_remote, request);

            Assert.True(status == HttpStatusCode.Unauthorized, $"{method} {path} answered {status}");
            Assert.Equal(["error", "message"], JsonDocument.Parse(text).RootElement.EnumerateObject().Select(property => property.Name));
            var error = JsonSerializer.Deserialize<ApiError>(text, ManagementApiJson.Options)!;
            Assert.Equal(nameof(TacitError.ApiUnauthorized), error.Error);
            Assert.False(string.IsNullOrWhiteSpace(error.Message));
        }
    }

    [Fact]
    public async Task A_local_connection_is_not_asked_to_log_in()
    {
        var (status, _) = await Send(_local, new HttpRequestMessage(HttpMethod.Get, Url("/api/status")));

        Assert.Equal(HttpStatusCode.OK, status);
    }

    [Fact]
    public async Task A_remote_connection_is_refused_at_mcp_with_403_and_an_error_body()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url("/mcp")) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };

        var (status, text) = await Send(_remote, request);

        Assert.Equal(HttpStatusCode.Forbidden, status);
        var error = JsonSerializer.Deserialize<ApiError>(text, ManagementApiJson.Options)!;
        Assert.Equal(nameof(TacitError.McpRemoteRefused), error.Error);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Fact]
    public async Task A_remote_connection_is_refused_at_mcp_whatever_the_request()
    {
        var (status, _) = await Send(_remote, new HttpRequestMessage(HttpMethod.Get, Url("/mcp")));

        Assert.Equal(HttpStatusCode.Forbidden, status);
    }

    [Fact]
    public async Task A_local_connection_reaches_mcp_as_before()
    {
        var (status, _) = await Send(_local, new HttpRequestMessage(HttpMethod.Get, Url("/mcp")));

        Assert.NotEqual(HttpStatusCode.Forbidden, status);
        Assert.NotEqual(HttpStatusCode.Unauthorized, status);
    }

    [Fact]
    public async Task A_remote_connection_is_not_refused_for_its_host_header()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Url("/api/status"));
        request.Headers.Host = "evil.example";

        var (status, text) = await Send(_remote, request);

        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal(nameof(TacitError.ApiUnauthorized), JsonSerializer.Deserialize<ApiError>(text, ManagementApiJson.Options)!.Error);
    }

    [Fact]
    public async Task A_remote_connection_with_a_foreign_origin_is_refused_by_the_guard_before_the_401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Url("/api/status"));
        request.Headers.TryAddWithoutValidation("Origin", "https://evil.example");

        var (status, text) = await Send(_remote, request);

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal(nameof(TacitError.ApiOriginRefused), JsonSerializer.Deserialize<ApiError>(text, ManagementApiJson.Options)!.Error);
    }

    [Fact]
    public async Task A_remote_connection_with_its_own_origin_passes_the_guards_and_reaches_the_401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Url("/api/status"));
        request.Headers.TryAddWithoutValidation("Origin", $"http://127.0.0.1:{Port}");

        var (status, _) = await Send(_remote, request);

        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    [Fact]
    public async Task A_remote_post_that_is_not_json_is_refused_with_415_before_the_401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url("/api/reindex"))
        {
            Content = new StringContent("{}", Encoding.UTF8, "text/plain"),
        };

        var (status, text) = await Send(_remote, request);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, status);
        Assert.Equal(nameof(TacitError.ApiContentTypeRefused), JsonSerializer.Deserialize<ApiError>(text, ManagementApiJson.Options)!.Error);
    }

    [Fact]
    public async Task A_remote_put_that_is_not_json_is_refused_with_415()
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, Url("/api/status"))
        {
            Content = new StringContent("{}", Encoding.UTF8, "text/plain"),
        };

        var (status, _) = await Send(_remote, request);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, status);
    }

    [Fact]
    public async Task A_remote_refusal_of_api_is_logged_once_with_its_status()
    {
        await Send(_remote, new HttpRequestMessage(HttpMethod.Get, Url("/api/status")));

        var line = Assert.Single(_logs.Entries, entry => entry.Message.Contains("/api/status", StringComparison.Ordinal));
        Assert.Contains("401", line.Message, StringComparison.Ordinal);
    }

    private static async Task<(HttpStatusCode Status, string Text)> Send(HttpClient client, HttpRequestMessage request)
    {
        using var response = await client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }
}
