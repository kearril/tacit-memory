using System.Net;
using System.Text.Json;
using TacitMemory.Core;
using TacitMemory.Core.Tests;
using TacitMemory.Host.Api;

namespace TacitMemory.Host.Tests;

/// <summary>
/// <c>GET /api/backups</c> over real HTTP: the local backups by kind, time and size, newest first; files with other names
/// left out; whether the cloud backup is configured and its last attempt, read without touching the network; and the
/// refusal of another device without a session.
/// </summary>
public sealed class BackupListApiTests : IAsyncLifetime
{
    private readonly TestHome _home = new();
    private MemoryService? _service;
    private ServiceHost? _host;
    private readonly HttpClient _local = PeerClients.Local();
    private readonly HttpClient _remote = PeerClients.Remote();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        _local.Dispose();
        _remote.Dispose();
        if (_host is not null)
            await _host.DisposeAsync();
        _service?.Dispose();
        _home.Dispose();
    }

    /// <summary>Starts the service; <paramref name="extraConfig"/> is appended to the test configuration.</summary>
    private async Task StartAsync(string extraConfig = "")
    {
        _home.InitReady(extraConfig: extraConfig);
        _service = _home.Tacit().OpenService(_ => { }, new CapturingLoggerFactory());
        _host = await ServiceHost.TryStartAsync(_service, port: 0, NullLoggerProvider.Instance, PeerClients.OnlyLoopbackIsLocal)
            ?? throw new InvalidOperationException("Port 0 is never in use.");
    }

    private Uri Url => new($"http://127.0.0.1:{_host!.Endpoint.Port}/api/backups");

    private string Backups => Path.Combine(_home.TacitHome, "backups");

    /// <summary>Writes a file of <paramref name="bytes"/> bytes into the backup folder, last written at <paramref name="at"/>.</summary>
    private void WriteBackup(string name, int bytes, DateTime at)
    {
        Directory.CreateDirectory(Backups);
        var path = Path.Combine(Backups, name);
        File.WriteAllBytes(path, new byte[bytes]);
        File.SetLastWriteTime(path, at);
    }

    private async Task<BackupList> ReadAsync()
    {
        using var response = await _local.GetAsync(Url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonSerializer.Deserialize<BackupList>(await response.Content.ReadAsStringAsync(), ManagementApiJson.Options)!;
    }

    [Fact]
    public async Task Lists_the_three_kinds_with_their_time_and_size_newest_first_and_leaves_other_files_out()
    {
        await StartAsync();
        if (Directory.Exists(Backups))
            Directory.Delete(Backups, recursive: true);
        WriteBackup("auto-2026-11-01.zip", 10, new DateTime(2026, 11, 1, 9, 0, 0));
        WriteBackup("manual-2026-11-02-101500.zip", 20, new DateTime(2026, 11, 2, 10, 15, 0));
        WriteBackup("pre-restore-2026-10-30-080000.zip", 30, new DateTime(2026, 10, 30, 8, 0, 0));
        WriteBackup("manual-2026-11-02-101500.zip.partial", 5, new DateTime(2026, 11, 3, 0, 0, 0));
        WriteBackup("notes.zip", 5, new DateTime(2026, 11, 3, 0, 0, 0));

        var list = await ReadAsync();

        Assert.Equal(
            [
                ("manual-2026-11-02-101500.zip", LocalBackupKind.Manual, 20L),
                ("auto-2026-11-01.zip", LocalBackupKind.Auto, 10L),
                ("pre-restore-2026-10-30-080000.zip", LocalBackupKind.PreRestore, 30L),
            ],
            list.Local.Select(backup => (backup.Name, backup.Kind, backup.Bytes)));
        Assert.Equal(new DateTimeOffset(new DateTime(2026, 11, 2, 10, 15, 0)), list.Local[0].At);
    }

    [Fact]
    public async Task The_kinds_are_camel_case_on_the_wire()
    {
        await StartAsync();
        WriteBackup("pre-restore-2026-10-30-080000.zip", 1, new DateTime(2026, 10, 30, 8, 0, 0));

        var body = await _local.GetStringAsync(Url);

        Assert.Contains("\"kind\":\"preRestore\"", body);
    }

    [Fact]
    public async Task Without_a_backup_folder_the_list_is_empty_and_the_cloud_backup_not_configured()
    {
        await StartAsync();
        if (Directory.Exists(Backups))
            Directory.Delete(Backups, recursive: true);

        var list = await ReadAsync();

        Assert.Empty(list.Local);
        Assert.False(list.RemoteConfigured);
        Assert.Null(list.LastRemote);
    }

    [Fact]
    public async Task With_the_cloud_backup_configured_it_gives_the_last_attempt_without_touching_the_network()
    {
        _home.EnvironmentVariables["TACIT_WEBDAV_PASSWORD"] = "secret";
        // Nothing listens on port 9; a request to it would fail, so a listed attempt shows the network was not touched.
        await StartAsync("backup:\n  webdav: { url: 'http://127.0.0.1:9/dav/', username: me }\n");
        Directory.CreateDirectory(Backups);
        File.WriteAllText(
            Path.Combine(Backups, "remote.json"),
            """{ "at": "2026-11-01T03:00:00+00:00", "succeeded": false, "error": "401 Unauthorized" }""");

        var list = await ReadAsync();

        Assert.True(list.RemoteConfigured);
        Assert.Equal(new CloudBackupRecord(DateTimeOffset.Parse("2026-11-01T03:00:00+00:00"), false, "401 Unauthorized"), list.LastRemote);
    }

    [Fact]
    public async Task An_unreadable_cloud_record_shows_no_attempt()
    {
        _home.EnvironmentVariables["TACIT_WEBDAV_PASSWORD"] = "secret";
        await StartAsync("backup:\n  webdav: { url: 'http://127.0.0.1:9/dav/', username: me }\n");
        Directory.CreateDirectory(Backups);
        File.WriteAllText(Path.Combine(Backups, "remote.json"), "not json");

        var list = await ReadAsync();

        Assert.True(list.RemoteConfigured);
        Assert.Null(list.LastRemote);
    }

    [Fact]
    public async Task Another_device_without_a_session_is_refused_with_401()
    {
        await StartAsync();

        using var response = await _remote.GetAsync(Url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
