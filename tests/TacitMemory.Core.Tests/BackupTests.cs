using System.IO.Compression;
using System.Text.Json;

namespace TacitMemory.Core.Tests;

public sealed class BackupTests : IDisposable
{
    private static readonly DateOnly Today = TestHome.Today;

    private readonly TestHome _home = new();
    private readonly List<string> _notices = [];
    private readonly MemoryService _service;

    public BackupTests()
    {
        _home.InitReady();
        _service = _home.Tacit().OpenService(_notices.Add, NullLoggerFactory.Instance);
    }

    public void Dispose()
    {
        _service.Dispose();
        _home.Dispose();
    }

    private string Memory => _home.DefaultMemory;

    private string BackupsDirectory => Path.Combine(_home.TacitHome, "backups");

    private static string BackupFor(string home, DateOnly day) => Path.Combine(home, "backups", $"auto-{day:yyyy-MM-dd}.zip");

    private static WritePageRequest Create(string page) => new()
    {
        Op = "create",
        Page = page,
        Title = "数据库升级后扩展丢失",
        Aliases = ["全文检索插件失效", "升级后搜索不到中文"],
        Sections = [new("现象", "升级后中文搜索没有结果。"), new("根因", "PGroonga 扩展没有随升级重装。")],
    };

    private static string[] AutomaticBackups(string directory) =>
        Directory.Exists(directory)
            ? Directory.GetFiles(directory, "auto-*.zip").Select(path => Path.GetFileName(path) ?? "").Order(StringComparer.Ordinal).ToArray()
            : [];

    [Fact]
    public async Task The_first_write_of_the_day_leaves_a_backup_that_holds_the_repository_and_config()
    {
        Directory.CreateDirectory(Path.Combine(Memory, ".cache"));
        File.WriteAllText(Path.Combine(Memory, ".cache", "scratch.bin"), "cache");

        await _service.WritePageAsync(Create("tacit-memory/db-upgrade"));
        await _service.BackupCompletion;

        using var zip = ZipFile.OpenRead(BackupFor(_home.TacitHome, Today));
        var names = zip.Entries.Select(entry => entry.FullName).ToArray();
        Assert.Contains("memory/.git/HEAD", names);
        Assert.Contains("memory/pages/tacit-memory/db-upgrade.md", names);
        Assert.Contains("config.yaml", names);
        Assert.Contains("prompts/rules.md", names);
        Assert.Contains("manifest.json", names);
        Assert.DoesNotContain(names, name => name.StartsWith("memory/.cache/", StringComparison.Ordinal));

        using var manifestStream = zip.GetEntry("manifest.json")!.Open();
        using var manifest = JsonDocument.Parse(manifestStream);
        var root = manifest.RootElement;
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("generatedAt").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("tacitVersion").GetString()));
        Assert.Equal(TestHome.Git(Memory, "rev-parse", "HEAD").Trim(), root.GetProperty("memoryHead").GetString());
        Assert.Contains("memory/pages/tacit-memory/db-upgrade.md",
            root.GetProperty("files").EnumerateArray().Select(file => file.GetString()));
    }

    [Fact]
    public async Task A_second_write_on_the_same_day_does_not_back_up_again()
    {
        await _service.WritePageAsync(Create("tacit-memory/first"));
        await _service.BackupCompletion;

        _home.Clock.Advance(TimeSpan.FromHours(8));
        await _service.WritePageAsync(Create("tacit-memory/second"));
        await _service.BackupCompletion;

        using var zip = ZipFile.OpenRead(BackupFor(_home.TacitHome, Today));
        using var manifestStream = zip.GetEntry("manifest.json")!.Open();
        using var manifest = JsonDocument.Parse(manifestStream);
        Assert.StartsWith("2026-11-02T10:00:00", manifest.RootElement.GetProperty("generatedAt").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_the_newest_keep_automatic_backups_are_kept()
    {
        using var home = new TestHome();
        home.InitReady("backup:\n  keep: 2\n");
        using var service = home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

        for (var day = 0; day < 3; day++)
        {
            await service.WritePageAsync(Create($"tacit-memory/day-{day}"));
            await service.BackupCompletion;
            home.Clock.Advance(TimeSpan.FromDays(1));
        }

        Assert.Equal(["auto-2026-11-03.zip", "auto-2026-11-04.zip"],
            AutomaticBackups(Path.Combine(home.TacitHome, "backups")));
    }

    [Fact]
    public async Task Keep_zero_turns_automatic_backups_off()
    {
        var home = new TestHome();
        try
        {
            home.InitReady("backup:\n  keep: 0\n");
            using var service = home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

            await service.WritePageAsync(Create("tacit-memory/no-backup"));
            await service.BackupCompletion;

            Assert.Empty(AutomaticBackups(Path.Combine(home.TacitHome, "backups")));
        }
        finally
        {
            home.Dispose();
        }
    }

    [Fact]
    public async Task A_plaintext_key_in_config_is_reported_after_the_backup()
    {
        using var home = new TestHome();
        home.InitReady();

        var notices = await NoticesAfterWrite(home);

        Assert.Contains(notices, notice => notice.Contains("明文密钥", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_plaintext_key_in_config_is_reported_even_when_the_environment_overrides_it()
    {
        using var home = new TestHome();
        home.InitReady();
        home.EnvironmentVariables["TACIT_DECISION_API_KEY"] = "env-key";

        var notices = await NoticesAfterWrite(home);

        Assert.Contains(notices, notice => notice.Contains("明文密钥", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_key_only_in_the_environment_is_not_reported()
    {
        using var home = new TestHome();
        home.InitReady();
        RemovePlaintextDecisionKey(home);
        home.EnvironmentVariables["TACIT_DECISION_API_KEY"] = "env-key";

        var notices = await NoticesAfterWrite(home);

        Assert.DoesNotContain(notices, notice => notice.Contains("明文密钥", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_plaintext_webdav_password_in_config_is_reported()
    {
        using var home = new TestHome();
        home.InitReady();
        RemovePlaintextDecisionKey(home);
        home.EnvironmentVariables["TACIT_DECISION_API_KEY"] = "env-key";
        File.AppendAllText(
            Path.Combine(home.TacitHome, "config.yaml"),
            "backup: { webdav: { url: 'https://dav.example/tacit/', username: me, password: pw } }\n");

        var notices = await NoticesAfterWrite(home);

        Assert.Contains(notices, notice => notice.Contains("明文密钥", StringComparison.Ordinal));
    }

    /// <summary>Opens the service of <paramref name="home"/>, makes one write, and returns what the service told the user.</summary>
    private static async Task<List<string>> NoticesAfterWrite(TestHome home)
    {
        var notices = new List<string>();
        using var service = home.Tacit().OpenService(notices.Add, NullLoggerFactory.Instance);
        await service.WritePageAsync(Create("tacit-memory/plain-key"));
        await service.BackupCompletion;
        return notices;
    }

    private static void RemovePlaintextDecisionKey(TestHome home)
    {
        var path = Path.Combine(home.TacitHome, "config.yaml");
        File.WriteAllText(path, File.ReadAllText(path).Replace(", apiKey: test-key", ""));
    }

    [Fact]
    public async Task A_failed_backup_only_notifies_and_the_write_stands()
    {
        // A regular file where the backups directory belongs makes the backup fail.
        File.WriteAllText(BackupsDirectory, "not a directory");

        var result = await _service.WritePageAsync(Create("tacit-memory/backup-fails"));
        await _service.BackupCompletion;

        Assert.False(result.IsError);
        Assert.Contains(_notices, notice => notice.Contains("自动备份失败", StringComparison.Ordinal));
        var recall = await _service.RecallAsync(new RecallRequest { Page = "tacit-memory/backup-fails" });
        Assert.False(recall.IsError);
    }
}
