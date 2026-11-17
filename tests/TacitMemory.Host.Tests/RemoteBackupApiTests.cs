using System.Net;
using System.Text;
using System.Text.Json;
using TacitMemory.Core;
using TacitMemory.Core.Tests;
using TacitMemory.Host.Api;

namespace TacitMemory.Host.Tests;

/// <summary>
/// The cloud backup over the management API: <c>POST /api/backup</c> with the remote target uploads and answers the
/// result as JSON, and the refusals answer their error bodies with the status code each one maps to.
/// </summary>
public sealed class RemoteBackupApiTests : IAsyncLifetime
{
    private const string User = "tacit";
    private const string Password = "api-pw-1";
    private const string RemoteFile = "/dav/tacit/tacit-backup.zip";

    private readonly TestHome _home = new();
    private readonly WebDavDouble _dav = new();
    private readonly ModelDouble _models = new() { EmbeddingDown = true, RerankDown = true };
    private MemoryService _service = null!;
    private ServiceHost _host = null!;
    private HttpClient _http = null!;

    public async Task InitializeAsync()
    {
        _dav.RequireBasic(User, Password);
        _dav.CreateCollection("/dav/tacit/");
        _home.EnvironmentVariables["TACIT_WEBDAV_PASSWORD"] = Password;
        var folder = new Uri(_dav.Address, "dav/tacit/").AbsoluteUri;
        _home.InitReady(extraConfig: $"backup:\n  keep: 7\n  webdav: {{ url: '{folder}', username: {User} }}\n", models: _models);
        _service = _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);
        _host = await ServiceHost.TryStartAsync(_service, port: 0, Microsoft.Extensions.Logging.Abstractions.NullLoggerProvider.Instance)
            ?? throw new InvalidOperationException("Port 0 is never in use.");
        _http = new HttpClient();
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _host.DisposeAsync();
        _service.Dispose();
        _models.Dispose();
        _dav.Dispose();
        _home.Dispose();
    }

    private Uri Url(string path) => new($"http://127.0.0.1:{_host.Endpoint.Port}{path}");

    private async Task<(HttpStatusCode Status, string Text)> PostBackup(BackupBody body)
    {
        var json = JsonSerializer.Serialize(body, RequestJson.Options);
        using var response = await _http.PostAsync(Url("/api/backup"), new StringContent(json, Encoding.UTF8, "application/json"));
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_remote_backup_request_uploads_and_answers_the_result_as_json()
    {
        var (status, text) = await PostBackup(new BackupBody { Target = BackupTarget.Cloud });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("\"location\":", text);
        var result = JsonSerializer.Deserialize<BackupResult>(text, ManagementApiJson.Options)!;
        Assert.Equal(new Uri(_dav.Address, "dav/tacit/tacit-backup.zip").AbsoluteUri, result.Location);
        Assert.Equal(_dav.FileAt(RemoteFile)!.Length, result.Bytes);
        Assert.DoesNotContain(Password, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_directory_with_the_remote_target_is_a_request_error_with_its_body()
    {
        var (status, text) = await PostBackup(new BackupBody { Target = BackupTarget.Cloud, To = Path.Combine(_home.Root, "usb") });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        var error = JsonSerializer.Deserialize<ApiError>(text, ManagementApiJson.Options)!;
        Assert.Equal(TacitError.RequestInvalid, error.Error);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Fact]
    public async Task A_failed_upload_is_a_server_error_that_names_the_failure()
    {
        _dav.Answer("PUT", 500);

        var (status, text) = await PostBackup(new BackupBody { Target = BackupTarget.Cloud });

        Assert.Equal(HttpStatusCode.InternalServerError, status);
        var error = JsonSerializer.Deserialize<ApiError>(text, ManagementApiJson.Options)!;
        Assert.Equal(TacitError.CloudBackupFailed, error.Error);
        Assert.Contains("HTTP 500", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, text, StringComparison.Ordinal);
    }
}
