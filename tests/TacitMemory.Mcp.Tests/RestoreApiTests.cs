using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TacitMemory.Core;
using TacitMemory.Core.Tests;

namespace TacitMemory.Mcp.Tests;

/// <summary>
/// The restore route of the management API over real HTTP: the preview and the restore as JSON that reads back, a body
/// that cannot be restored refused with its error body, and the guards that apply to a POST.
/// </summary>
public sealed class RestoreApiTests : IAsyncLifetime
{
    private readonly TestHome _home = new();
    private readonly ModelDouble _models = new();
    private MemoryService _service = null!;
    private McpHost _host = null!;
    private HttpClient _http = null!;

    public async Task InitializeAsync()
    {
        _home.InitReady(models: _models);
        _service = _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);
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

    private Uri Url(string path) => new($"http://127.0.0.1:{_host.Endpoint.Port}{path}");

    private string Head => TestHome.Git(_home.DefaultMemory, "rev-parse", "HEAD").Trim();

    private string BackupsDirectory => Path.Combine(_home.TacitHome, "backups");

    private Task<HttpResponseMessage> PostRestore(object body) =>
        _http.PostAsync(Url("/api/restore"), new StringContent(JsonSerializer.Serialize(body, ManagementApiJson.Options), Encoding.UTF8, "application/json"));

    [Fact]
    public async Task The_preview_answers_with_the_manifest_as_json_and_changes_nothing()
    {
        var backup = await _service.BackupAsync(BackupTarget.Local);
        var before = Head;

        using var response = await PostRestore(new { path = backup.Location, config = false, preview = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"memoryHead\":", text, StringComparison.Ordinal);
        var preview = JsonSerializer.Deserialize<RestorePreview>(text, ManagementApiJson.Options)!;
        Assert.Equal("manual", preview.Kind);
        Assert.Equal(before, preview.MemoryHead);
        Assert.Single(preview.Replaces);
        Assert.Equal(before, Head);
        Assert.False(Directory.Exists(BackupsDirectory) && Directory.GetFiles(BackupsDirectory, "pre-restore-*.zip").Length > 0);
    }

    [Fact]
    public async Task The_restore_answers_with_the_safety_backup_the_head_and_the_rebuild_as_json()
    {
        var backup = await _service.BackupAsync(BackupTarget.Local);

        using var response = await PostRestore(new { path = backup.Location, config = false, preview = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"safetyBackup\":", text, StringComparison.Ordinal);
        var result = JsonSerializer.Deserialize<RestoreResult>(text, ManagementApiJson.Options)!;
        Assert.True(File.Exists(result.SafetyBackup));
        Assert.StartsWith(Path.Combine(BackupsDirectory, "pre-restore-"), result.SafetyBackup, StringComparison.Ordinal);
        Assert.Equal(Head, result.MemoryHead);
        Assert.True(result.Reindex.Files > 0);
    }

    [Fact]
    public async Task A_zip_that_cannot_be_restored_is_answered_with_its_error_body()
    {
        var zip = Path.Combine(_home.Root, "no-manifest.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        using (var stream = archive.CreateEntry("memory/.git/HEAD").Open())
            stream.Write("ref: refs/heads/main\n"u8);

        using var response = await PostRestore(new { path = zip, config = false, preview = true });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("RestoreInvalid", error.GetProperty("error").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task A_request_without_a_path_or_with_a_relative_one_is_refused()
    {
        using var noPath = await PostRestore(new { config = false, preview = true });
        using var relative = await PostRestore(new { path = "backups/manual.zip", config = false, preview = true });

        Assert.Equal(HttpStatusCode.BadRequest, noPath.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, relative.StatusCode);
    }

    [Fact]
    public async Task A_request_without_the_preview_flag_is_refused_rather_than_restoring()
    {
        var backup = await _service.BackupAsync(BackupTarget.Local);
        var before = Head;

        using var response = await PostRestore(new { path = backup.Location, config = false });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, Head);
    }

    [Fact]
    public async Task A_restore_posted_without_json_is_refused_by_the_content_type_guard()
    {
        using var response = await _http.PostAsync(Url("/api/restore"), new StringContent("{}", Encoding.UTF8, "text/plain"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }
}
