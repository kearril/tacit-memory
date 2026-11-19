using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace TacitMemory.Core.Tests;

/// <summary>
/// The cloud backup through the facade, against <see cref="WebDavDouble"/>: the requests it sends and how the remote
/// backup is replaced, the failures that leave the previous remote backup in place, the stage each failure names, what
/// is recorded and shown by <c>status</c>, and what is refused or kept out of the local <c>backups/</c> folder.
/// </summary>
public sealed class RemoteBackupTests : IDisposable
{
    private const string User = "tacit";
    private const string Password = "pw-for-tests-9f3";
    private const string RemoteFile = "/dav/tacit/tacit-backup.zip";
    private const string PartFile = "/dav/tacit/tacit-backup.zip.part";

    private readonly TestHome _home = new();
    private readonly WebDavDouble _dav = new();
    private readonly CapturingLoggerFactory _logs = new();
    private MemoryService? _service;

    public RemoteBackupTests()
    {
        _home.EnvironmentVariables["TACIT_WEBDAV_PASSWORD"] = Password;
    }

    public void Dispose()
    {
        _service?.Dispose();
        _dav.Dispose();
        _home.Dispose();
    }

    private string FolderUrl => new Uri(_dav.Address, "dav/tacit/").AbsoluteUri;

    private string BackupsDirectory => Path.Combine(_home.TacitHome, "backups");

    private string RecordPath => Path.Combine(BackupsDirectory, "remote.json");

    /// <summary>A config whose cloud backup is the double's folder with user <see cref="User"/>, and opens the service on it.</summary>
    private MemoryService Start() => StartWith($"  webdav: {{ url: '{FolderUrl}', username: {User} }}");

    /// <summary>Opens the service on a config whose <c>backup</c> section is <paramref name="backupSection"/>.</summary>
    private MemoryService StartWith(string backupSection)
    {
        _home.InitReady(extraConfig: $"backup:\n  keep: 7\n{backupSection}\n");
        _service = _home.Tacit().OpenService(_ => { }, _logs);
        return _service;
    }

    private static WritePageRequest Create(string page) => new()
    {
        Op = "create",
        Type = ToolValues.PageTypes.Work,
        Page = page,
        Title = "云端备份测试",
        Aliases = ["云端备份别名一", "云端备份别名二"],
        Sections = [new("现象", "用于检查云端备份。")],
    };

    private static JsonElement ManifestOf(byte[] zip)
    {
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        using var stream = archive.GetEntry("manifest.json")!.Open();
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }

    /// <summary>A port nothing listens on: it was free a moment ago.</summary>
    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Fact]
    public async Task The_upload_puts_a_part_file_then_moves_it_over_the_remote_backup_with_basic_auth()
    {
        _dav.RequireBasic(User, Password);
        _dav.CreateCollection("/dav/tacit/");
        var service = Start();

        var result = await service.Maintenance.BackupAsync(BackupTarget.Cloud);

        var sent = _dav.Requests.ToList();
        Assert.Equal(["PUT", "MOVE"], sent.Select(request => request.Method));
        Assert.Equal(PartFile, sent[0].Path);
        Assert.Equal(PartFile, sent[1].Path);
        Assert.All(sent, request => Assert.Equal(WebDavDouble.BasicAuthorization(User, Password), request.Authorization));
        Assert.Equal(new Uri(_dav.Address, "dav/tacit/tacit-backup.zip").AbsoluteUri, sent[1].Destination);
        Assert.Equal("T", sent[1].Overwrite);

        Assert.Null(_dav.FileAt(PartFile));
        var remote = _dav.FileAt(RemoteFile)!;
        Assert.Equal(remote.Length, result.Bytes);
        Assert.Equal("remote", ManifestOf(remote).GetProperty("kind").GetString());
        Assert.Equal(new Uri(_dav.Address, "dav/tacit/tacit-backup.zip").AbsoluteUri, result.Location);
    }

    [Fact]
    public async Task Nothing_is_kept_under_backups_when_the_cloud_backup_is_made()
    {
        _dav.CreateCollection("/dav/tacit/");
        var service = Start();

        await service.Maintenance.BackupAsync(BackupTarget.Cloud);

        var zips = Directory.Exists(BackupsDirectory) ? Directory.GetFiles(BackupsDirectory, "*.zip") : [];
        Assert.Empty(zips);
    }

    [Fact]
    public async Task A_second_upload_replaces_the_remote_backup_with_the_newer_memory()
    {
        _dav.CreateCollection("/dav/tacit/");
        var service = Start();
        await service.Maintenance.BackupAsync(BackupTarget.Cloud);
        var first = _dav.FileAt(RemoteFile)!;
        await service.Tools.WritePageAsync(Create("tacit-memory/written-after-first-upload"));

        await service.Maintenance.BackupAsync(BackupTarget.Cloud);

        var second = _dav.FileAt(RemoteFile)!;
        Assert.NotEqual(first, second);
        var files = ManifestOf(second).GetProperty("files").EnumerateArray().Select(entry => entry.GetString());
        Assert.Contains(files, name => name!.Contains("written-after-first-upload", StringComparison.Ordinal));
        Assert.Equal(["PUT", "MOVE", "PUT", "MOVE"], _dav.Requests.Select(request => request.Method));
    }

    [Fact]
    public async Task A_missing_folder_is_made_once_and_the_upload_is_sent_again()
    {
        var service = Start();

        await service.Maintenance.BackupAsync(BackupTarget.Cloud);

        var sent = _dav.Requests.ToList();
        Assert.Equal(["PUT", "MKCOL", "PUT", "MOVE"], sent.Select(request => request.Method));
        Assert.Equal("/dav/tacit/", sent[1].Path);
        Assert.NotNull(_dav.FileAt(RemoteFile));
    }

    [Fact]
    public async Task A_service_without_atomic_replacement_is_refused_and_the_old_remote_backup_stays()
    {
        _dav.CreateCollection("/dav/tacit/");
        var service = Start();
        await service.Maintenance.BackupAsync(BackupTarget.Cloud);
        var before = _dav.FileAt(RemoteFile)!;
        await service.Tools.WritePageAsync(Create("tacit-memory/unsupported-replace"));
        _dav.MoveSupported = false;

        var error = await Assert.ThrowsAsync<TacitException>(() => service.Maintenance.BackupAsync(BackupTarget.Cloud));

        Assert.Equal(TacitError.CloudBackupFailed, error.Error);
        Assert.Contains("替换", error.Message, StringComparison.Ordinal);
        Assert.Contains("不支持原子替换", error.Message, StringComparison.Ordinal);
        Assert.Contains("405", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, _dav.FileAt(RemoteFile));
        Assert.Null(_dav.FileAt(PartFile));
        Assert.Contains(_dav.Requests, request => request.Method == "DELETE" && request.Path == PartFile);
    }

    [Fact]
    public async Task An_upload_cut_short_leaves_the_old_remote_backup_in_place()
    {
        _dav.CreateCollection("/dav/tacit/");
        var service = Start();
        await service.Maintenance.BackupAsync(BackupTarget.Cloud);
        var before = _dav.FileAt(RemoteFile)!;
        await service.Tools.WritePageAsync(Create("tacit-memory/cut-short"));
        _dav.DropNextPutAfter(64);

        var error = await Assert.ThrowsAsync<TacitException>(() => service.Maintenance.BackupAsync(BackupTarget.Cloud));

        Assert.Equal(TacitError.CloudBackupFailed, error.Error);
        Assert.Contains("上传", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, _dav.FileAt(RemoteFile));
        Assert.Single(_dav.Requests, request => request.Method == "MOVE");
    }

    [Fact]
    public async Task A_wrong_password_fails_at_the_authentication_stage_and_the_password_appears_nowhere()
    {
        _dav.RequireBasic(User, Password);
        _dav.CreateCollection("/dav/tacit/");
        const string wrong = "wrong-password-7c1";
        _home.EnvironmentVariables["TACIT_WEBDAV_PASSWORD"] = wrong;
        var service = Start();

        var error = await Assert.ThrowsAsync<TacitException>(() => service.Maintenance.BackupAsync(BackupTarget.Cloud));

        Assert.Contains("认证", error.Message, StringComparison.Ordinal);
        Assert.Contains("401", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(wrong, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(wrong, _logs.AllText, StringComparison.Ordinal);
        Assert.DoesNotContain(wrong, File.ReadAllText(RecordPath), StringComparison.Ordinal);
        Assert.Null(_dav.FileAt(RemoteFile));
    }

    [Fact]
    public async Task A_service_that_cannot_be_reached_fails_at_the_connection_stage()
    {
        var service = StartWith($"  webdav: {{ url: 'http://127.0.0.1:{FreePort()}/dav/tacit/', username: {User} }}");

        var error = await Assert.ThrowsAsync<TacitException>(() => service.Maintenance.BackupAsync(BackupTarget.Cloud));

        Assert.Equal(TacitError.CloudBackupFailed, error.Error);
        Assert.Contains("连接", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("上传", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_last_attempt_is_recorded_and_status_shows_it_without_a_network_call()
    {
        _dav.CreateCollection("/dav/tacit/");
        var service = Start();
        await service.Maintenance.BackupAsync(BackupTarget.Cloud);
        var config = _home.Tacit().LoadConfig();
        var requestsBefore = _dav.Requests.Count;

        var status = await _home.Tacit().OfflineStatus(config);

        Assert.Equal(requestsBefore, _dav.Requests.Count);
        Assert.True(File.Exists(RecordPath));
        Assert.True(status.Backups!.LastRemote!.Succeeded);
        Assert.Single(status.Backups.Lines.Where(line => line.StartsWith("云端备份", StringComparison.Ordinal)), line => line.Contains("成功", StringComparison.Ordinal));

        _dav.Answer("MOVE", 500);
        await Assert.ThrowsAsync<TacitException>(() => service.Maintenance.BackupAsync(BackupTarget.Cloud));

        status = await _home.Tacit().OfflineStatus(config);
        var failed = status.Backups!.Lines.Single(line => line.StartsWith("云端备份", StringComparison.Ordinal));
        Assert.Contains("失败", failed, StringComparison.Ordinal);
        Assert.Contains("HTTP 500", failed, StringComparison.Ordinal);
        Assert.False(status.Backups.LastRemote!.Succeeded);
        Assert.Contains("HTTP 500", status.Backups.LastRemote.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_backup_webdav_the_cloud_backup_says_it_is_not_configured_and_makes_no_attempt()
    {
        var service = StartWith("");

        var error = await Assert.ThrowsAsync<TacitException>(() => service.Maintenance.BackupAsync(BackupTarget.Cloud));

        Assert.Equal(TacitError.CloudBackupNotConfigured, error.Error);
        Assert.Contains("未配置云端备份", error.Message, StringComparison.Ordinal);
        Assert.Empty(_dav.Requests);
        Assert.False(File.Exists(RecordPath));
    }

    [Fact]
    public async Task A_directory_is_refused_for_the_cloud_backup_and_nothing_is_sent()
    {
        var service = Start();

        var refused = await Assert.ThrowsAsync<TacitException>(() => service.Maintenance.BackupAsync(BackupTarget.Cloud, Path.Combine(_home.Root, "usb")));

        Assert.Equal(TacitError.RequestInvalid, refused.Error);
        Assert.Empty(_dav.Requests);
    }

    [Fact]
    public async Task Without_a_username_no_authorization_is_sent()
    {
        _dav.CreateCollection("/dav/tacit/");
        var service = StartWith($"  webdav: {{ url: '{FolderUrl}' }}");

        await service.Maintenance.BackupAsync(BackupTarget.Cloud);

        Assert.NotEmpty(_dav.Requests);
        Assert.All(_dav.Requests, request => Assert.Null(request.Authorization));
    }

    [Fact]
    public async Task A_password_written_in_the_config_is_named_in_the_result_and_the_log_but_never_written_to_it()
    {
        _dav.CreateCollection("/dav/tacit/");
        _home.EnvironmentVariables.Remove("TACIT_WEBDAV_PASSWORD");
        var service = StartWith($"  webdav: {{ url: '{FolderUrl}', username: {User}, password: '{Password}' }}");

        var result = await service.Maintenance.BackupAsync(BackupTarget.Cloud);

        Assert.NotNull(result.Notice);
        Assert.Contains("明文密钥", result.Notice, StringComparison.Ordinal);
        Assert.Contains(_logs.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("明文密钥", StringComparison.Ordinal));
        Assert.DoesNotContain(Password, _logs.AllText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_successful_upload_is_logged_without_the_password()
    {
        _dav.RequireBasic(User, Password);
        _dav.CreateCollection("/dav/tacit/");
        var service = Start();

        await service.Maintenance.BackupAsync(BackupTarget.Cloud);

        Assert.Contains(_logs.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("云端备份已上传", StringComparison.Ordinal));
        Assert.DoesNotContain(Password, _logs.AllText, StringComparison.Ordinal);
    }
}
