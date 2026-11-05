using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace TacitMemory.Core.Tests;

/// <summary>
/// Manual backups through the facade: where they go and how they are named, what they hold, that pruning never touches
/// them, that the write queue orders them, and that a plaintext secret in the config is named in the result.
/// </summary>
public sealed class ManualBackupTests : IDisposable
{
    private static readonly DateOnly Today = TestHome.Today;

    /// <summary>The test clock's local time: 10:00 in UTC, the zone the test clock reports.</summary>
    private const string FirstStamp = "2026-11-02-100000";

    private readonly TestHome _home = new();
    private readonly CapturingLoggerFactory _logs = new();
    private readonly MemoryService _service;

    public ManualBackupTests()
    {
        _home.InitReady();
        _service = _home.Tacit().OpenService(_ => { }, _logs);
    }

    public void Dispose()
    {
        _service.Dispose();
        _home.Dispose();
    }

    private string Memory => _home.DefaultMemory;

    private string BackupsDirectory => Path.Combine(_home.TacitHome, "backups");

    private static WritePageRequest Create(string page) => new()
    {
        Op = "create",
        Page = page,
        Title = "手动备份测试",
        Aliases = ["手动备份别名一", "手动备份别名二"],
        Sections = [new("现象", "用于检查手动备份。")],
    };

    private static string[] AutomaticBackups(string directory) =>
        Directory.Exists(directory)
            ? Directory.GetFiles(directory, "auto-*.zip").Select(path => Path.GetFileName(path) ?? "").Order(StringComparer.Ordinal).ToArray()
            : [];

    [Fact]
    public async Task A_manual_backup_is_written_under_backups_with_the_manual_timestamp_and_the_same_content_as_an_automatic_one()
    {
        Directory.CreateDirectory(Path.Combine(Memory, ".cache"));
        File.WriteAllText(Path.Combine(Memory, ".cache", "scratch.bin"), "cache");
        await _service.WritePageAsync(Create("tacit-memory/manual-page"));

        var result = await _service.BackupAsync(BackupTarget.Local);

        var expected = Path.Combine(BackupsDirectory, $"manual-{FirstStamp}.zip");
        Assert.Equal(expected, result.Location);
        Assert.Equal(new FileInfo(expected).Length, result.Bytes);
        Assert.Contains(expected, result.Report, StringComparison.Ordinal);

        using var zip = ZipFile.OpenRead(expected);
        var names = zip.Entries.Select(entry => entry.FullName).ToArray();
        Assert.Contains("memory/.git/HEAD", names);
        Assert.Contains("memory/pages/tacit-memory/manual-page.md", names);
        Assert.Contains("config.yaml", names);
        Assert.Contains("prompts/rules.md", names);
        Assert.Contains("manifest.json", names);
        Assert.DoesNotContain(names, name => name.StartsWith("memory/.cache/", StringComparison.Ordinal));

        using var manifestStream = zip.GetEntry("manifest.json")!.Open();
        using var manifest = JsonDocument.Parse(manifestStream);
        var root = manifest.RootElement;
        Assert.Equal("manual", root.GetProperty("kind").GetString());
        Assert.Equal(TestHome.Git(Memory, "rev-parse", "HEAD").Trim(), root.GetProperty("memoryHead").GetString());
        Assert.Equal(TestHome.Git(Memory, "log", "-1", "--format=%s", "HEAD").Trim(), root.GetProperty("memoryHeadSubject").GetString());
    }

    [Fact]
    public async Task An_automatic_backup_records_its_kind_and_the_subject_of_the_commit_it_holds()
    {
        await _service.WritePageAsync(Create("tacit-memory/auto-kind"));
        await _service.BackupCompletion;

        using var zip = ZipFile.OpenRead(Path.Combine(BackupsDirectory, $"auto-{Today:yyyy-MM-dd}.zip"));
        using var manifestStream = zip.GetEntry("manifest.json")!.Open();
        using var manifest = JsonDocument.Parse(manifestStream);
        Assert.Equal("auto", manifest.RootElement.GetProperty("kind").GetString());
        Assert.Equal(TestHome.Git(Memory, "log", "-1", "--format=%s", "HEAD").Trim(), manifest.RootElement.GetProperty("memoryHeadSubject").GetString());
    }

    [Fact]
    public async Task The_manual_backup_holds_the_writes_that_came_before_it()
    {
        var write = _service.WritePageAsync(Create("tacit-memory/queued-write"));
        var backup = _service.BackupAsync(BackupTarget.Local);

        await write;
        var result = await backup;

        using var zip = ZipFile.OpenRead(result.Location);
        Assert.Contains("memory/pages/tacit-memory/queued-write.md", zip.Entries.Select(entry => entry.FullName));
    }

    [Fact]
    public async Task Pruning_the_automatic_backups_never_removes_a_manual_one()
    {
        using var home = new TestHome();
        home.InitReady("backup:\n  keep: 1\n");
        using var service = home.Tacit().OpenService(_ => { }, _logs);
        await service.WritePageAsync(Create("tacit-memory/first-day"));
        await service.BackupCompletion;
        var manual = await service.BackupAsync(BackupTarget.Local);

        home.Clock.Advance(TimeSpan.FromDays(1));
        await service.WritePageAsync(Create("tacit-memory/second-day"));
        await service.BackupCompletion;

        Assert.True(File.Exists(manual.Location));
        Assert.Equal(["auto-2026-11-03.zip"], AutomaticBackups(Path.Combine(home.TacitHome, "backups")));
    }

    [Fact]
    public async Task A_target_directory_is_created_when_it_is_missing()
    {
        var directory = Path.Combine(_home.Root, "usb", "nested");

        var result = await _service.BackupAsync(BackupTarget.Local, directory);

        Assert.Equal(Path.Combine(directory, $"manual-{FirstStamp}.zip"), result.Location);
        Assert.True(File.Exists(result.Location));
    }

    [Fact]
    public async Task A_second_manual_backup_in_the_same_second_is_refused_and_the_first_is_left_alone()
    {
        var first = await _service.BackupAsync(BackupTarget.Local);
        var before = File.ReadAllBytes(first.Location);

        var refused = await Assert.ThrowsAsync<TacitException>(() => _service.BackupAsync(BackupTarget.Local));

        Assert.Equal(TacitError.BackupExists, refused.Error);
        Assert.Equal(before, File.ReadAllBytes(first.Location));
        Assert.Single(Directory.GetFiles(BackupsDirectory, "manual-*"));

        _home.Clock.Advance(TimeSpan.FromSeconds(1));
        var later = await _service.BackupAsync(BackupTarget.Local);
        Assert.Equal(Path.Combine(BackupsDirectory, "manual-2026-11-02-100001.zip"), later.Location);
    }

    [Fact]
    public async Task Keep_zero_turns_automatic_backups_off_and_manual_ones_still_work()
    {
        using var home = new TestHome();
        home.InitReady("backup:\n  keep: 0\n");
        using var service = home.Tacit().OpenService(_ => { }, _logs);
        await service.WritePageAsync(Create("tacit-memory/no-auto"));
        await service.BackupCompletion;

        var manual = await service.BackupAsync(BackupTarget.Local);

        Assert.Empty(AutomaticBackups(Path.Combine(home.TacitHome, "backups")));
        Assert.True(File.Exists(manual.Location));
    }

    [Fact]
    public async Task The_result_names_a_plaintext_secret_in_the_config()
    {
        var result = await _service.BackupAsync(BackupTarget.Local);

        Assert.NotNull(result.Notice);
        Assert.Contains("明文密钥", result.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_result_has_no_notice_when_the_only_key_is_in_the_environment()
    {
        using var home = new TestHome();
        home.InitReady();
        var path = Path.Combine(home.TacitHome, "config.yaml");
        File.WriteAllText(path, File.ReadAllText(path).Replace(", apiKey: test-key", ""));
        home.EnvironmentVariables["TACIT_DECISION_API_KEY"] = "env-key";
        using var service = home.Tacit().OpenService(_ => { }, _logs);

        var result = await service.BackupAsync(BackupTarget.Local);

        Assert.Null(result.Notice);
    }

    [Fact]
    public async Task A_manual_backup_is_logged_with_its_path_and_size()
    {
        var result = await _service.BackupAsync(BackupTarget.Local);

        var line = Assert.Single(_logs.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("手动备份已写入", StringComparison.Ordinal));
        Assert.Contains(result.Location, line.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_relative_target_directory_is_refused_and_nothing_is_written()
    {
        var refused = await Assert.ThrowsAsync<TacitException>(() => _service.BackupAsync(BackupTarget.Local, "backups"));

        Assert.Equal(TacitError.RequestInvalid, refused.Error);
    }
}
