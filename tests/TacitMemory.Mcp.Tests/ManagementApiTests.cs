using Microsoft.Extensions.Logging;
using System.Net;
using System.Text;
using System.Text.Json;
using TacitMemory.Core;
using TacitMemory.Core.Tests;

namespace TacitMemory.Mcp.Tests;

/// <summary>
/// The management API over real HTTP on the host: the status and backup JSON, the refusals with their error bodies, the three guards, and <c>/mcp</c>
/// left unguarded.
/// </summary>
public sealed class ManagementApiTests : IAsyncLifetime
{
    private readonly TestHome _home = new();
    private readonly ModelDouble _models = new() { EmbeddingDown = true, RerankDown = true };
    private MemoryService _service = null!;
    private McpHost _host = null!;
    private HttpClient _http = null!;
    private readonly CapturingLoggerFactory _logs = new();

    public async Task InitializeAsync()
    {
        _home.InitReady(models: _models);
        _service = _home.Tacit().OpenService(_ => { }, _logs);
        _host = await McpHost.TryStartAsync(_service, port: 0, NullLoggerProvider.Instance) ?? throw new InvalidOperationException("Port 0 is never in use.");
        _http = new HttpClient();
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _host.DisposeAsync();
        _service.Dispose();
        _models.Dispose();
        _home.Dispose();
    }

    private int Port => _host.Endpoint.Port;

    private Uri Url(string path) => new($"http://127.0.0.1:{Port}{path}");

    [Fact]
    public async Task Status_is_served_as_camel_case_json_that_reads_back_as_a_report()
    {
        using var response = await _http.GetAsync(Url("/api/status"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"uptimeSeconds\":", text);
        Assert.Contains("\"state\":\"available\"", text);

        var report = JsonSerializer.Deserialize<StatusReport>(text, ManagementApiJson.Options)!;
        Assert.True(report.Service!.Running);
        Assert.Equal(_host.Endpoint, report.Service.Endpoint);
        Assert.Equal(["embedding", "reranker", "decision"], report.Models!.Items.Select(item => item.Role));
        Assert.NotNull(report.Logs);
        Assert.Null(report.Config);
    }

    [Fact]
    public async Task Status_json_reads_back_an_unconfigured_model_as_unconfigured()
    {
        // A service of its own: the shared one has all three models configured.
        using var home = new TestHome();
        using var models = new ModelDouble();
        home.InitReady(models: models);
        File.WriteAllText(
            Path.Combine(home.TacitHome, "config.yaml"),
            $"language: zh\nmodels:\n  embedding: {{ url: '{models.EmbeddingUrl}', model: double-embedding }}\n");
        using var service = home.Tacit().OpenService(_ => { }, _logs);
        await using var host = await McpHost.TryStartAsync(service, port: 0, NullLoggerProvider.Instance)
            ?? throw new InvalidOperationException("Port 0 is never in use.");

        using var response = await _http.GetAsync(new Uri($"http://127.0.0.1:{host.Endpoint.Port}/api/status"));
        var text = await response.Content.ReadAsStringAsync();

        Assert.Contains("\"unconfigured\"", text, StringComparison.Ordinal);
        var status = JsonSerializer.Deserialize<StatusReport>(text, ManagementApiJson.Options)!.Models!;
        Assert.Equal(ModelState.Available, Assert.Single(status.Items, item => item.Role == "embedding").State);
        Assert.Equal(ModelState.Unconfigured, Assert.Single(status.Items, item => item.Role == "reranker").State);
        Assert.Equal(ModelState.Unconfigured, Assert.Single(status.Items, item => item.Role == "decision").State);
    }

    [Fact]
    public async Task Status_json_reads_back_a_secret_entry_for_each_model_and_an_unset_key_is_not_an_error()
    {
        using var response = await _http.GetAsync(Url("/api/status"));
        var text = await response.Content.ReadAsStringAsync();

        var report = JsonSerializer.Deserialize<StatusReport>(text, ManagementApiJson.Options)!;
        var secrets = report.Secrets!;
        Assert.Equal(["models.embedding.apiKey", "models.reranker.apiKey", "models.decision.apiKey"], secrets.Items.Select(item => item.Key));
        Assert.Null(secrets.Items.Single(item => item.Key == "models.embedding.apiKey").Source);
        Assert.Equal(SecretSource.ConfigFile, secrets.Items.Single(item => item.Key == "models.decision.apiKey").Source);
        Assert.Contains(secrets.Lines, line => line.StartsWith("models.embedding.apiKey", StringComparison.Ordinal) && line.Contains("未设置", StringComparison.Ordinal));
        Assert.Contains("models.reranker.apiKey", text, StringComparison.Ordinal);
        Assert.DoesNotContain("test-key", text, StringComparison.Ordinal);
        Assert.DoesNotContain(((IStatusColumn)secrets).LineLevels(), level => level is not null);
    }

    [Fact]
    public async Task One_call_to_the_api_writes_one_event_with_its_route_and_status()
    {
        var response = await Send(new HttpRequestMessage(HttpMethod.Get, Url("/api/status")));

        var line = Assert.Single(_logs.Entries, entry => entry.Message.Contains("/api/status", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Information, line.Level);
        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Contains("200", line.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_request_that_carries_an_origin_header_is_refused_with_an_error_body()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Url("/api/status"));
        request.Headers.TryAddWithoutValidation("Origin", "https://evil.example");

        var (status, text) = await Send(request);

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal(["error", "message"], JsonDocument.Parse(text).RootElement.EnumerateObject().Select(property => property.Name));
        var error = JsonSerializer.Deserialize<ApiError>(text, ManagementApiJson.Options)!;
        Assert.Equal(nameof(TacitError.ApiOriginRefused), error.Error);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Theory]
    [InlineData("evil.example")]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.1:1")]
    [InlineData("localhost:1")]
    public async Task A_host_other_than_this_services_loopback_address_and_port_is_refused(string host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Url("/api/status"));
        request.Headers.Host = host;

        var (status, text) = await Send(request);

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal(nameof(TacitError.ApiHostRefused), JsonSerializer.Deserialize<ApiError>(text, ManagementApiJson.Options)!.Error);
    }

    [Fact]
    public async Task The_loopback_address_and_localhost_with_this_port_are_accepted()
    {
        foreach (var host in new[] { $"127.0.0.1:{Port}", $"localhost:{Port}" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Url("/api/status"));
            request.Headers.Host = host;

            var (status, _) = await Send(request);

            Assert.Equal(HttpStatusCode.OK, status);
        }
    }

    [Fact]
    public async Task A_post_that_is_not_application_json_is_refused_with_415()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url("/api/status"))
        {
            Content = new StringContent("{}", Encoding.UTF8, "text/plain"),
        };

        var (status, text) = await Send(request);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, status);
        Assert.Equal(nameof(TacitError.ApiContentTypeRefused), JsonSerializer.Deserialize<ApiError>(text, ManagementApiJson.Options)!.Error);
    }

    [Fact]
    public async Task A_post_with_application_json_passes_the_guards()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url("/api/status"))
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };

        var (status, _) = await Send(request);

        // /api/status takes GET only, so the request passes the guards and routing answers it with 405.
        Assert.Equal(HttpStatusCode.MethodNotAllowed, status);
    }

    [Fact]
    public async Task Reindex_is_posted_as_json_and_answers_with_the_rebuilt_index_as_json()
    {
        var written = await _service.WritePageAsync(new WritePageRequest
        {
            Op = "create",
            Type = ToolValues.PageTypes.Work,
            Page = "tacit-memory/backup",
            Title = "备份",
            Aliases = ["备份的坑", "备份问题"],
            Sections = [new("要点", "每日自动备份到 zip")],
        });
        Assert.False(written.IsError, written.Text);
        using var request = new HttpRequestMessage(HttpMethod.Post, Url("/api/reindex"))
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };

        var (status, text) = await Send(request);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("\"skippedFiles\":[]", text);
        Assert.Contains("\"elapsedMilliseconds\":", text);
        var result = JsonSerializer.Deserialize<ReindexResult>(text, ManagementApiJson.Options)!;
        Assert.True(result.Units > 0);
        Assert.True(result.VectorsPending);
        Assert.Contains("向量将在模型恢复后补齐", result.Report);
    }

    [Fact]
    public async Task The_mcp_endpoint_is_not_guarded()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Url("/mcp"));
        request.Headers.TryAddWithoutValidation("Origin", "https://evil.example");
        request.Headers.Host = "evil.example";

        var (status, _) = await Send(request);

        Assert.NotEqual(HttpStatusCode.Forbidden, status);
        Assert.NotEqual(HttpStatusCode.UnsupportedMediaType, status);
    }

    [Fact]
    public async Task A_local_backup_posted_as_json_is_written_and_answered_with_its_result()
    {
        using var request = Post("/api/backup", """{ "target": "local" }""");

        var (status, text) = await Send(request);

        Assert.Equal(HttpStatusCode.OK, status);
        var result = JsonSerializer.Deserialize<BackupResult>(text, ManagementApiJson.Options)!;
        Assert.Equal(Path.Combine(_home.TacitHome, "backups", "manual-2026-11-02-100000.zip"), result.Location);
        Assert.True(File.Exists(result.Location));
        Assert.Equal(new FileInfo(result.Location).Length, result.Bytes);
    }

    [Fact]
    public async Task A_backup_with_a_directory_is_written_into_that_directory()
    {
        var directory = Path.Combine(_home.Root, "usb");
        var json = JsonSerializer.Serialize(new BackupBody { Target = BackupTarget.Local, To = directory }, ManagementApiJson.Options);
        using var request = Post("/api/backup", json);

        var (status, text) = await Send(request);

        Assert.Equal(HttpStatusCode.OK, status);
        var result = JsonSerializer.Deserialize<BackupResult>(text, ManagementApiJson.Options)!;
        Assert.Equal(Path.Combine(directory, "manual-2026-11-02-100000.zip"), result.Location);
        Assert.True(File.Exists(result.Location));
    }

    [Theory]
    [InlineData("""{ "target": "local", "to": "backups" }""")]
    [InlineData("""{ "target": "elsewhere" }""")]
    [InlineData("""{ }""")]
    public async Task A_backup_request_the_API_does_not_accept_is_refused_with_400_and_an_error_body(string json)
    {
        using var request = Post("/api/backup", json);

        var (status, text) = await Send(request);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        var error = JsonSerializer.Deserialize<ApiError>(text, ManagementApiJson.Options)!;
        Assert.Equal(nameof(TacitError.RequestInvalid), error.Error);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Fact]
    public async Task A_cloud_backup_without_backup_webdav_is_a_conflict_with_its_error_body()
    {
        using var request = Post("/api/backup", """{ "target": "remote" }""");

        var (status, text) = await Send(request);

        Assert.Equal(HttpStatusCode.Conflict, status);
        var error = JsonSerializer.Deserialize<ApiError>(text, ManagementApiJson.Options)!;
        Assert.Equal(nameof(TacitError.CloudBackupNotConfigured), error.Error);
        Assert.Contains("未配置云端备份", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_backup_that_would_overwrite_a_file_is_refused_with_409_and_an_error_body()
    {
        using (var first = Post("/api/backup", """{ "target": "local" }"""))
            Assert.Equal(HttpStatusCode.OK, (await Send(first)).Status);
        using var again = Post("/api/backup", """{ "target": "local" }""");

        var (status, text) = await Send(again);

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal(nameof(TacitError.BackupExists), JsonSerializer.Deserialize<ApiError>(text, ManagementApiJson.Options)!.Error);
    }

    private HttpRequestMessage Post(string path, string json) =>
        new(HttpMethod.Post, Url(path)) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private async Task<(HttpStatusCode Status, string Text)> Send(HttpRequestMessage request)
    {
        using var response = await _http.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }
}
