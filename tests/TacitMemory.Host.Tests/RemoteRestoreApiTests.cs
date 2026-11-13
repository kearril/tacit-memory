using System.Net;
using System.Text;
using System.Text.Json;
using TacitMemory.Core;
using TacitMemory.Core.Tests;
using TacitMemory.Host.Api;

namespace TacitMemory.Host.Tests;

/// <summary>
/// The cloud restore over the management API: <c>POST /api/restore</c> with <c>remote</c> previews the downloaded
/// backup and answers the preview as JSON, with the local path the restore will take; the request shapes the API
/// refuses (a path together with <c>remote</c>, an execution with <c>remote</c>) answer 400, and a missing cloud backup
/// answers its error body.
/// </summary>
public sealed class RemoteRestoreApiTests : IAsyncLifetime
{
    private const string User = "tacit";
    private const string Password = "api-pw-restore-2";

    private readonly TestHome _home = new();
    private readonly WebDavDouble _dav = new();
    private readonly ModelDouble _models = new();
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
        _service = _home.Tacit().OpenService(_ => { }, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
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

    private string Head => TestHome.Git(_home.DefaultMemory, "rev-parse", "HEAD").Trim();

    private string DownloadFile => Path.Combine(_home.TacitHome, "backups", ".download", "tacit-backup.zip");

    private Task<HttpResponseMessage> PostRestore(object body) =>
        _http.PostAsync(Url("/api/restore"), new StringContent(JsonSerializer.Serialize(body, ManagementApiJson.Options), Encoding.UTF8, "application/json"));

    [Fact]
    public async Task The_remote_preview_answers_with_the_downloaded_path_as_json()
    {
        await _service.BackupAsync(BackupTarget.Cloud);

        using var response = await PostRestore(new { remote = true, config = false, preview = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = JsonSerializer.Deserialize<RestorePreview>(await response.Content.ReadAsStringAsync(), ManagementApiJson.Options)!;
        Assert.Equal(DownloadFile, preview.Path);
        Assert.Equal("remote", preview.Kind);
        Assert.True(File.Exists(DownloadFile));
    }

    [Fact]
    public async Task A_remote_request_with_a_path_is_a_request_error()
    {
        await _service.BackupAsync(BackupTarget.Cloud);
        var zip = Path.Combine(_home.Root, "other.zip");

        using var response = await PostRestore(new { remote = true, path = zip, config = false, preview = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("RequestInvalid", JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetString());
        Assert.False(File.Exists(DownloadFile));
    }

    [Fact]
    public async Task A_remote_restore_that_would_execute_is_refused_before_anything_is_downloaded()
    {
        await _service.BackupAsync(BackupTarget.Cloud);
        var before = Head;

        using var response = await PostRestore(new { remote = true, config = false, preview = false });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("RequestInvalid", JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetString());
        Assert.False(File.Exists(DownloadFile));
        Assert.Equal(before, Head);
    }

    [Fact]
    public async Task A_remote_preview_without_a_cloud_backup_answers_its_error_body()
    {
        using var response = await PostRestore(new { remote = true, config = false, preview = true });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var error = JsonSerializer.Deserialize<ApiError>(await response.Content.ReadAsStringAsync(), ManagementApiJson.Options)!;
        Assert.Equal("CloudBackupFailed", error.Error);
        Assert.Contains("远端没有云端备份", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, error.Message, StringComparison.Ordinal);
    }
}
