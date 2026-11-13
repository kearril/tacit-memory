using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TacitMemory.Core;
using TacitMemory.Core.Tests;
using TacitMemory.Host.Api;

namespace TacitMemory.Host.Tests;

/// <summary>
/// The error bodies of the management API over real HTTP: a file-system failure, a request the service cannot read,
/// and a restore source that does not exist are all answered with the JSON error body the CLI shows as it is.
/// </summary>
public sealed class ManagementErrorTests : IAsyncLifetime
{
    private readonly TestHome _home = new();
    private readonly ModelDouble _models = new();
    private MemoryService _service = null!;
    private ServiceHost _host = null!;
    private HttpClient _http = null!;

    public async Task InitializeAsync()
    {
        _home.InitReady(models: _models);
        _service = _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);
        _host = await ServiceHost.TryStartAsync(_service, port: 0, NullLoggerProvider.Instance) ?? throw new InvalidOperationException("Port 0 is never in use.");
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

    private Uri Url(string path) => new($"http://127.0.0.1:{_host.Endpoint.Port}{path}");

    private Task<HttpResponseMessage> Post(string route, string json) =>
        _http.PostAsync(Url(route), new StringContent(json, Encoding.UTF8, "application/json"));

    [Fact]
    public async Task A_backup_into_a_directory_that_cannot_be_created_is_answered_with_a_json_error()
    {
        // A regular file where the directory should be: creating the directory fails with an IOException.
        var blocker = Path.Combine(_home.Root, "not-a-directory");
        File.WriteAllText(blocker, "occupied");

        using var response = await Post("/api/backup", JsonSerializer.Serialize(new { target = "local", to = blocker }, ManagementApiJson.Options));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        var error = JsonSerializer.Deserialize<ApiError>(await response.Content.ReadAsStringAsync(), ManagementApiJson.Options)!;
        Assert.Equal("FileSystemFailed", error.Error);
        Assert.Contains(blocker, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("IOException", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_restore_from_a_file_that_does_not_exist_is_answered_with_a_json_error()
    {
        var missing = Path.Combine(_home.Root, "missing.zip");

        using var response = await Post("/api/restore", JsonSerializer.Serialize(new { path = missing, config = false, preview = true }, ManagementApiJson.Options));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = JsonSerializer.Deserialize<ApiError>(await response.Content.ReadAsStringAsync(), ManagementApiJson.Options)!;
        Assert.Equal("RequestInvalid", error.Error);
        Assert.Contains(missing, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_body_that_does_not_read_names_the_field_without_the_parser_text()
    {
        using var response = await Post("/api/backup", "{\"target\": 5}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = JsonSerializer.Deserialize<ApiError>(await response.Content.ReadAsStringAsync(), ManagementApiJson.Options)!;
        Assert.Equal("RequestInvalid", error.Error);
        Assert.Contains("target", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonException", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("System.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_cloud_target_is_named_remote_on_the_wire_and_refused_when_not_configured()
    {
        // The CLI's --remote is the wire name "remote"; with no backup.webdav in the test config, the refusal is a 409.
        using var response = await Post("/api/backup", "{\"target\": \"remote\"}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = JsonSerializer.Deserialize<ApiError>(await response.Content.ReadAsStringAsync(), ManagementApiJson.Options)!;
        Assert.Equal("CloudBackupNotConfigured", error.Error);
    }
}
