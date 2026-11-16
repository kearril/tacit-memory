using System.IO.Compression;
using Microsoft.Extensions.Logging.Abstractions;

namespace TacitMemory.Core.Tests;

/// <summary>
/// The restore from the cloud backup through the facade, against <see cref="WebDavDouble"/>: the preview reads the remote
/// file and keeps the download out of the local backups, restoring the previewed download replaces the repository with
/// the remote content, writes a safety backup first and deletes the download afterwards, and a missing, unauthorized or
/// broken remote backup is refused with its own message and changes nothing.
/// </summary>
public sealed class RemoteRestoreTests : IDisposable
{
    private const string User = "tacit";
    private const string Password = "pw-for-restore-7c1";
    private const string RemoteFile = "/dav/tacit/tacit-backup.zip";

    private readonly TestHome _home = new();
    private readonly WebDavDouble _dav = new();
    private readonly ModelDouble _models = new();
    private MemoryService? _service;

    public RemoteRestoreTests()
    {
        _home.EnvironmentVariables["TACIT_WEBDAV_PASSWORD"] = Password;
    }

    public void Dispose()
    {
        _service?.Dispose();
        _models.Dispose();
        _dav.Dispose();
        _home.Dispose();
    }

    private string FolderUrl => new Uri(_dav.Address, "dav/tacit/").AbsoluteUri;

    private string Memory => _home.DefaultMemory;

    private string BackupsDirectory => Path.Combine(_home.TacitHome, "backups");

    private string DownloadFile => Path.Combine(BackupsDirectory, ".download", "tacit-backup.zip");

    private string Head() => TestHome.Git(Memory, "rev-parse", "HEAD").Trim();

    private string PagePath(string slug) => Path.Combine(Memory, "pages", "tacit-memory", slug + ".md");

    /// <summary>Opens the service on a config whose cloud backup is the double's folder, or on <paramref name="backupSection"/>.</summary>
    private MemoryService Start(string? backupSection = null)
    {
        var section = backupSection ?? $"  webdav: {{ url: '{FolderUrl}', username: {User} }}";
        _home.InitReady(extraConfig: $"backup:\n  keep: 7\n{section}\n", models: _models);
        _service = _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);
        return _service;
    }

    private static WritePageRequest Create(string page) => new()
    {
        Op = "create",
        Type = ToolValues.PageTypes.Work,
        Page = page,
        Title = "云端恢复测试",
        Aliases = ["云端恢复别名一", "云端恢复别名二"],
        Sections = [new("现象", "用于检查云端恢复。")],
    };

    /// <summary>Writes page <c>remote-a</c>, uploads the cloud backup, then writes page <c>remote-b</c>: the remote backup lacks it.</summary>
    private async Task<string> UploadBackupThenWriteAsync(MemoryService service)
    {
        _dav.CreateCollection("/dav/tacit/");
        await service.WritePageAsync(Create("tacit-memory/remote-a"));
        await service.BackupAsync(BackupTarget.Cloud);
        var remoteHead = Head();
        await service.WritePageAsync(Create("tacit-memory/remote-b"));
        return remoteHead;
    }

    [Fact]
    public async Task The_preview_reads_the_remote_backup_and_keeps_the_download_out_of_the_local_backups()
    {
        var service = Start();
        var remoteHead = await UploadBackupThenWriteAsync(service);
        var localHead = Head();

        var preview = await service.PreviewRemoteRestoreAsync(config: false);

        Assert.Equal(Path.GetFullPath(DownloadFile), Path.GetFullPath(preview.Path), StringComparer.OrdinalIgnoreCase);
        Assert.Equal("remote", preview.Kind);
        Assert.Equal(remoteHead, preview.MemoryHead);
        Assert.Equal(_dav.FileAt(RemoteFile), File.ReadAllBytes(DownloadFile));
        Assert.Equal(localHead, Head());

        await service.SettledAsync();
        var status = await _home.Tacit().OfflineStatus(_home.Tacit().LoadConfig());
        Assert.StartsWith("auto-", status.Backups!.Latest, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Restoring_the_previewed_download_makes_the_repository_the_remote_content_and_deletes_the_download()
    {
        var service = Start();
        var remoteHead = await UploadBackupThenWriteAsync(service);
        var preview = await service.PreviewRemoteRestoreAsync(config: false);

        var result = await service.RestoreAsync(preview.Path, config: false);

        Assert.Equal(remoteHead, Head());
        Assert.Equal(remoteHead, result.MemoryHead);
        Assert.True(File.Exists(PagePath("remote-a")));
        Assert.False(File.Exists(PagePath("remote-b")));
        Assert.True(File.Exists(result.SafetyBackup));
        Assert.StartsWith(Path.Combine(BackupsDirectory, "pre-restore-"), result.SafetyBackup, StringComparison.Ordinal);
        Assert.False(File.Exists(DownloadFile));
    }

    [Fact]
    public async Task A_remote_with_no_cloud_backup_says_so_and_downloads_nothing()
    {
        var service = Start();
        _dav.CreateCollection("/dav/tacit/");
        var before = Head();

        var error = await Assert.ThrowsAsync<TacitException>(() => service.PreviewRemoteRestoreAsync(config: false));

        Assert.Equal(TacitError.CloudBackupFailed, error.Error);
        Assert.Contains("远端没有云端备份", error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(DownloadFile));
        Assert.Equal(before, Head());
    }

    [Fact]
    public async Task A_wrong_password_is_an_authentication_failure_that_does_not_show_the_password()
    {
        _dav.RequireBasic(User, Password);
        _dav.CreateCollection("/dav/tacit/");
        _dav.PutFile(RemoteFile, "placeholder"u8.ToArray());
        _home.EnvironmentVariables["TACIT_WEBDAV_PASSWORD"] = "wrong-pw-7c1";
        var service = Start();

        var error = await Assert.ThrowsAsync<TacitException>(() => service.PreviewRemoteRestoreAsync(config: false));

        Assert.Equal(TacitError.CloudBackupFailed, error.Error);
        Assert.Contains("认证阶段", error.Message, StringComparison.Ordinal);
        Assert.Contains("HTTP 401", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("wrong-pw-7c1", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_remote_file_that_is_not_a_backup_is_refused_and_the_download_is_kept_for_inspection()
    {
        _dav.CreateCollection("/dav/tacit/");
        _dav.PutFile(RemoteFile, "not a zip"u8.ToArray());
        var service = Start();
        var before = Head();

        var error = await Assert.ThrowsAsync<TacitException>(() => service.PreviewRemoteRestoreAsync(config: false));

        Assert.Equal(TacitError.RestoreInvalid, error.Error);
        Assert.True(File.Exists(DownloadFile));
        Assert.Equal(before, Head());
    }

    [Fact]
    public async Task A_remote_restore_without_a_configured_cloud_backup_is_refused()
    {
        var service = Start(backupSection: "");

        var error = await Assert.ThrowsAsync<TacitException>(() => service.PreviewRemoteRestoreAsync(config: false));

        Assert.Equal(TacitError.CloudBackupNotConfigured, error.Error);
        Assert.False(File.Exists(DownloadFile));
    }
}
