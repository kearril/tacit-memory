using System.Net;
using System.Text;
using System.Text.Json;
using TacitMemory.Core;
using TacitMemory.Core.Tests;
using TacitMemory.Host.Api;

namespace TacitMemory.Host.Tests;

/// <summary>
/// <c>GET /api/logs</c> and <c>GET /api/logs/&lt;date&gt;</c> over real HTTP: the days with a log file and today, one day's
/// events as JSON, the refusals of a date that is not one and of another day without a file, and the refusal of another
/// device without a session. The test clock shows 2026-11-02.
/// </summary>
public sealed class LogApiTests : IAsyncLifetime
{
    private readonly TestHome _home = new();
    private MemoryService _service = null!;
    private ServiceHost _host = null!;
    private HttpClient _local = null!;
    private HttpClient _remote = null!;

    public async Task InitializeAsync()
    {
        _home.InitReady();
        _service = _home.Tacit().OpenService(_ => { }, new CapturingLoggerFactory());
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
        _home.Dispose();
    }

    private Uri Url(string path) => new($"http://127.0.0.1:{_host.Endpoint.Port}{path}");

    private void Write(string day, string text)
    {
        var logs = Path.Combine(_home.TacitHome, "logs");
        Directory.CreateDirectory(logs);
        File.WriteAllText(Path.Combine(logs, $"tacit-{day}.log"), text.ReplaceLineEndings("\n"), new UTF8Encoding(false));
    }

    [Fact]
    public async Task The_days_are_listed_newest_first_with_today()
    {
        Write("20261030", "");
        Write("20261101", "");

        using var response = await _local.GetAsync(Url("/api/logs"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("2026-11-02", json.RootElement.GetProperty("today").GetString());
        Assert.Equal(["2026-11-01", "2026-10-30"], json.RootElement.GetProperty("dates").EnumerateArray().Select(date => date.GetString()));
    }

    [Fact]
    public async Task A_days_events_are_served_with_their_time_level_and_message()
    {
        Write("20261101", """
            2026-11-01 09:15:02.031 +08:00 INF 常驻服务启动
            2026-11-01 09:16:00.000 +08:00 WRN 额外监听绑定失败
            - 原因一

            """);

        using var response = await _local.GetAsync(Url("/api/logs/2026-11-01"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal("2026-11-01", root.GetProperty("date").GetString());
        Assert.Equal("2026-11-02", root.GetProperty("today").GetString());
        Assert.Equal(2, root.GetProperty("next").GetInt32());
        var second = root.GetProperty("entries")[1];
        Assert.Equal("2026-11-01T09:16:00+08:00", second.GetProperty("time").GetString());
        Assert.Equal("warning", second.GetProperty("level").GetString());
        Assert.Equal("额外监听绑定失败\n- 原因一", second.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Today_without_a_file_has_no_events()
    {
        using var response = await _local.GetAsync(Url("/api/logs/2026-11-02"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var day = JsonSerializer.Deserialize<ServiceLogDay>(await response.Content.ReadAsStringAsync(), ManagementApiJson.Options)!;
        Assert.Empty(day.Entries);
        Assert.Equal(0, day.Next);
    }

    [Fact]
    public async Task Another_day_without_a_file_is_404()
    {
        using var response = await _local.GetAsync(Url("/api/logs/2026-10-01"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = JsonSerializer.Deserialize<ApiError>(await response.Content.ReadAsStringAsync(), ManagementApiJson.Options)!;
        Assert.Equal(nameof(TacitError.LogDateNotFound), error.Error);
    }

    [Fact]
    public async Task A_date_that_is_not_one_is_400()
    {
        using var response = await _local.GetAsync(Url("/api/logs/20261101"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = JsonSerializer.Deserialize<ApiError>(await response.Content.ReadAsStringAsync(), ManagementApiJson.Options)!;
        Assert.Equal(nameof(TacitError.RequestInvalid), error.Error);
    }

    [Theory]
    [InlineData("/api/logs")]
    [InlineData("/api/logs/2026-11-02")]
    public async Task Another_device_without_a_session_is_refused_with_401(string path)
    {
        using var response = await _remote.GetAsync(Url(path));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
