using System.Net;
using System.Net.Sockets;
using System.Text;
using TacitMemory.Core;
using TacitMemory.Core.Tests;

namespace TacitMemory.Host.Tests;

/// <summary>
/// Which addresses the service answers on: loopback always, plus the one <c>server.host</c> names when it binds. A
/// <c>server.host</c> that cannot bind leaves the service on loopback, with a warning and the reason in the status report.
/// </summary>
public sealed class ListenerTests : IAsyncDisposable, IDisposable
{
    private const string Password = "correct horse";

    private readonly TestHome _home = new();
    private readonly ModelDouble _models = new() { EmbeddingDown = true, RerankDown = true };
    private readonly CapturingLoggerFactory _logs = new();
    private readonly List<IDisposable> _disposables = [];
    private readonly List<IAsyncDisposable> _asyncDisposables = [];

    public void Dispose()
    {
        foreach (var disposable in _disposables)
            disposable.Dispose();
        _models.Dispose();
        _home.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var disposable in _asyncDisposables)
            await disposable.DisposeAsync();
        Dispose();
    }

    /// <summary>
    /// Starts a service on a free port with <paramref name="extraConfig"/> in its config. A config that names a
    /// <c>server.host</c> needs an access password to start, so the password is set first.
    /// </summary>
    private async Task<(ServiceHost Host, MemoryService Service)> StartAsync(string extraConfig = "", int port = 0)
    {
        _home.InitReady(extraConfig, models: _models);
        var service = _home.Tacit().OpenService(_ => { }, _logs);
        _disposables.Add(service);
        if (extraConfig.Length > 0)
            service.Access.SetPassword(Password, current: null, local: true, IPAddress.Loopback);
        var host = await ServiceHost.TryStartAsync(service, port, NullLoggerProvider.Instance, PeerClients.OnlyLoopbackIsLocal)
            ?? throw new InvalidOperationException($"Port {port} is in use.");
        _asyncDisposables.Add(host);
        return (host, service);
    }

    /// <summary>The status code of <c>/api/status</c> at <paramref name="address"/>, asked from <paramref name="client"/>.</summary>
    private static async Task<HttpStatusCode> StatusOf(int port, IPAddress address, HttpClient? client = null)
    {
        using var owned = client is null ? new HttpClient() : null;
        using var response = await (client ?? owned!).GetAsync(new Uri($"http://{address}:{port}/api/status"));
        return response.StatusCode;
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Fact]
    public async Task Without_server_host_the_service_answers_on_loopback_only()
    {
        var (host, _) = await StartAsync();

        Assert.Equal(HttpStatusCode.OK, await StatusOf(host.Endpoint.Port, IPAddress.Loopback));
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => StatusOf(host.Endpoint.Port, PeerClients.RemoteAddress));
    }

    [Fact]
    public async Task With_server_host_the_service_answers_on_loopback_and_on_that_address_on_the_same_port()
    {
        var (host, service) = await StartAsync("server:\n  host: 127.0.0.2\n");
        var port = host.Endpoint.Port;
        using var remote = PeerClients.Remote();

        // Answered on both addresses: the local client gets the API, the remote one gets the 401 that says the API is not open to it.
        Assert.Equal(HttpStatusCode.OK, await StatusOf(port, IPAddress.Loopback));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusOf(port, PeerClients.RemoteAddress, remote));
        var report = await service.StatusAsync(host.Endpoint);
        Assert.Equal($"额外监听：http://127.0.0.2:{port}/", report.Service!.Lines[^1]);
    }

    [Fact]
    public async Task A_wildcard_server_host_answers_on_loopback_and_on_the_same_port()
    {
        var (host, service) = await StartAsync("server:\n  host: 0.0.0.0\n");
        var port = host.Endpoint.Port;

        // The wildcard cannot be a request target, so the check is that both listeners start together: loopback answers,
        // and the status reports the wildcard as bound.
        Assert.Equal(HttpStatusCode.OK, await StatusOf(port, IPAddress.Loopback));
        var column = Assert.IsType<ServiceStatus>((await service.StatusAsync(host.Endpoint)).Service);
        Assert.Equal($"额外监听：http://0.0.0.0:{port}/", column.Lines[^1]);
        Assert.Null(column.ExtraListener!.FailureReason);
    }

    [Fact]
    public async Task A_server_host_that_cannot_bind_leaves_the_service_on_loopback_with_a_warning_and_the_reason()
    {
        // 192.0.2.1 is a documentation address (RFC 5737) that this machine does not have, so binding to it fails.
        var (host, service) = await StartAsync("server:\n  host: 192.0.2.1\n");

        Assert.Equal(HttpStatusCode.OK, await StatusOf(host.Endpoint.Port, IPAddress.Loopback));
        var report = await service.StatusAsync(host.Endpoint);
        var column = Assert.IsType<ServiceStatus>(report.Service);
        Assert.True(column.Running);
        Assert.StartsWith($"额外监听失败：http://192.0.2.1:{host.Endpoint.Port}/，", column.Lines[^1], StringComparison.Ordinal);
        Assert.Equal(StatusLevel.Problem, column.LineLevels()[^1]);
        var warning = Assert.Single(_logs.Entries, entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
        Assert.Contains("192.0.2.1", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_server_host_whose_port_is_taken_on_that_address_leaves_the_service_on_loopback()
    {
        var port = FreePort();
        using var taken = new TcpListener(PeerClients.RemoteAddress, port);
        taken.Start();

        var (host, service) = await StartAsync("server:\n  host: 127.0.0.2\n", port);

        Assert.Equal(HttpStatusCode.OK, await StatusOf(port, IPAddress.Loopback));
        var column = Assert.IsType<ServiceStatus>((await service.StatusAsync(host.Endpoint)).Service);
        Assert.Equal($"额外监听失败：http://127.0.0.2:{port}/，端口 {port} 已被占用", column.Lines[^1]);
    }

    [Fact]
    public async Task A_taken_loopback_port_refuses_to_start_beside_a_wildcard_server_host()
    {
        var port = FreePort();
        using var taken = new TcpListener(IPAddress.Loopback, port);
        taken.Start();
        _home.InitReady("server:\n  host: 0.0.0.0\n", models: _models);
        using var service = _home.Tacit().OpenService(_ => { }, _logs);
        service.Access.SetPassword(Password, current: null, local: true, IPAddress.Loopback);

        var host = await ServiceHost.TryStartAsync(service, port, NullLoggerProvider.Instance, PeerClients.OnlyLoopbackIsLocal);

        Assert.Null(host);
    }

    [Fact]
    public async Task An_extra_address_taken_after_it_was_probed_leaves_the_service_on_loopback_and_says_why()
    {
        var port = FreePort();
        using var taken = new TcpListener(PeerClients.RemoteAddress, port);
        taken.Start();
        _home.InitReady("server:\n  host: 127.0.0.2\n", models: _models);
        var service = _home.Tacit().OpenService(_ => { }, _logs);
        _disposables.Add(service);
        service.Access.SetPassword(Password, current: null, local: true, IPAddress.Loopback);

        // The probe finds the address free, as it would when the other program takes the port just after the probe.
        var host = await ServiceHost.TryStartAsync(
            service, port, NullLoggerProvider.Instance, PeerClients.OnlyLoopbackIsLocal, probe: (_, _) => null)
            ?? throw new InvalidOperationException($"Port {port} is in use on loopback too.");
        _asyncDisposables.Add(host);

        Assert.Equal(HttpStatusCode.OK, await StatusOf(port, IPAddress.Loopback));
        var column = Assert.IsType<ServiceStatus>((await service.StatusAsync(host.Endpoint)).Service);
        Assert.Equal($"额外监听失败：http://127.0.0.2:{port}/，端口 {port} 已被占用", column.Lines[^1]);
    }

    [Fact]
    public async Task A_loopback_server_host_is_the_same_as_none()
    {
        var (host, service) = await StartAsync("server:\n  host: 127.0.0.1\n");

        var column = Assert.IsType<ServiceStatus>((await service.StatusAsync(host.Endpoint)).Service);
        Assert.Null(column.ExtraListener);
        Assert.DoesNotContain(column.Lines, line => line.StartsWith("额外监听", StringComparison.Ordinal));
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => StatusOf(host.Endpoint.Port, PeerClients.RemoteAddress));
    }

    [Fact]
    public async Task A_taken_loopback_port_still_refuses_to_start()
    {
        var port = FreePort();
        using var taken = new TcpListener(IPAddress.Loopback, port);
        taken.Start();
        _home.InitReady("server:\n  host: 127.0.0.2\n", models: _models);
        using var service = _home.Tacit().OpenService(_ => { }, _logs);
        service.Access.SetPassword(Password, current: null, local: true, IPAddress.Loopback);

        var host = await ServiceHost.TryStartAsync(service, port, NullLoggerProvider.Instance, PeerClients.OnlyLoopbackIsLocal);

        Assert.Null(host);
    }

    [Fact]
    public async Task A_server_host_without_an_access_password_refuses_to_start_and_names_the_key()
    {
        _home.InitReady("server:\n  host: 127.0.0.2\n", models: _models);
        using var service = _home.Tacit().OpenService(_ => { }, _logs);

        var refusal = await Assert.ThrowsAsync<TacitException>(
            () => ServiceHost.TryStartAsync(service, 0, NullLoggerProvider.Instance, PeerClients.OnlyLoopbackIsLocal));

        Assert.Equal(TacitError.ConfigInvalid, refusal.Error);
        Assert.Contains("server.host", refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, _logs.AllText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_wildcard_server_host_without_an_access_password_refuses_to_start()
    {
        _home.InitReady("server:\n  host: 0.0.0.0\n", models: _models);
        using var service = _home.Tacit().OpenService(_ => { }, _logs);

        await Assert.ThrowsAsync<TacitException>(
            () => ServiceHost.TryStartAsync(service, 0, NullLoggerProvider.Instance, PeerClients.OnlyLoopbackIsLocal));
    }

    [Fact]
    public async Task Without_server_host_a_service_starts_with_no_access_password()
    {
        var (host, _) = await StartAsync();

        Assert.Equal(HttpStatusCode.OK, await StatusOf(host.Endpoint.Port, IPAddress.Loopback));
    }
    [Fact]
    public async Task Clearing_the_password_from_this_machine_is_409_while_server_host_names_an_address()
    {
        var (host, _) = await StartAsync("server:\n  host: 127.0.0.2\n");
        using var local = new HttpClient();

        using var response = await local.DeleteAsync(new Uri($"http://127.0.0.1:{host.Endpoint.Port}/api/access-password"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(nameof(TacitError.PasswordClearRefused), await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clearing_the_password_from_another_device_is_403_even_with_a_session_and_whatever_server_host_says()
    {
        var (host, _) = await StartAsync("server:\n  host: 127.0.0.2\n");
        var port = host.Endpoint.Port;
        using var remote = PeerClients.Remote();
        using var login = await remote.PostAsync(
            new Uri($"http://127.0.0.2:{port}/api/session"),
            new StringContent($"{{\"password\":\"{Password}\"}}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var cookie = login.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri($"http://127.0.0.2:{port}/api/access-password"));
        request.Headers.TryAddWithoutValidation("Cookie", cookie);

        using var response = await remote.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(nameof(TacitError.PasswordClearRemoteRefused), await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
