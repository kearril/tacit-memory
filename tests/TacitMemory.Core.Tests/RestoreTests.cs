using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using TacitMemory.Core.Clients;

namespace TacitMemory.Core.Tests;

/// <summary>
/// Restore through the facade: the preview, the refusals of a bad zip, what is replaced by default and with
/// <c>--config</c>, the safety backup written first, the rollback of a failed swap, the memory path notice, and that a
/// write arriving during the restore lands in the restored repository.
/// </summary>
public sealed class RestoreTests : IDisposable
{
    /// <summary>A bound on each await: a wait that never ends fails here instead of hanging.</summary>
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);

    /// <summary>Waits until the condition holds, letting the service run the refreshes it has scheduled on the test clock.</summary>
    private static async Task Eventually(MemoryService service, Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Guard;
        while (true)
        {
            await service.SettledAsync();
            if (condition())
                return;
            Assert.True(DateTime.UtcNow < deadline, "condition not met within the guard");
            await Task.Delay(50);
        }
    }

    /// <summary>Waits until the condition holds, as the service's own debounced renders take their time.</summary>
    private static async Task Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Guard;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "condition not met within the guard");
            await Task.Delay(50);
        }
    }

    private readonly TestHome _home = new();
    private readonly CapturingLoggerFactory _logs = new();
    private readonly ModelDouble _models = new();
    private MemoryService _service;

    public RestoreTests()
    {
        _home.InitReady(models: _models);
        _service = _home.Tacit().OpenService(_ => { }, _logs);
    }

    public void Dispose()
    {
        _service.Dispose();
        _models.Dispose();
        _home.Dispose();
    }

    private string Memory => _home.DefaultMemory;

    private string BackupsDirectory => Path.Combine(_home.TacitHome, "backups");

    private string AgentsMd => Path.Combine(_home.UserHome, ".omp", "agent", "AGENTS.md");

    private string PersonaMd => Path.Combine(Memory, "persona.md");

    private string ConfigYaml => Path.Combine(_home.TacitHome, "config.yaml");

    private string BindingsJson => Path.Combine(_home.TacitHome, "bindings.json");

    private string PromptsFolder => Path.Combine(_home.TacitHome, "prompts");

    private string Head() => TestHome.Git(Memory, "rev-parse", "HEAD").Trim();

    private string PagePath(string slug) => Path.Combine(Memory, "pages", "tacit-memory", slug + ".md");

    private string[] PreRestoreBackups() =>
        Directory.Exists(BackupsDirectory) ? Directory.GetFiles(BackupsDirectory, "pre-restore-*.zip") : [];

    private static WritePageRequest Create(string page, string title, string body) => new()
    {
        Op = "create",
        Type = ToolValues.PageTypes.Work,
        Page = page,
        Title = title,
        Aliases = [$"{title}别名一", $"{title}别名二"],
        Sections = [new("现象", body)],
    };

    private static string PersonaWith(string preference) => $"""
        ## 红线
        - 不要自行发布版本；改为先请用户确认（理由：影响外部）
          <!-- 2026-10-25 原话「发布前必须问我」 -->

        ## 协作规则
        - 需求不清时 → 先连续追问

        ## 偏好
        - {preference}
          <!-- 2026-10-25 原话「{preference}」 -->

        ## Profile
        - 系统：Windows 11 x64
        """.ReplaceLineEndings("\n");

    private static Dictionary<string, byte[]> ReadEntries(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in zip.Entries.Where(entry => !entry.FullName.EndsWith('/')))
        {
            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            entries[entry.FullName] = buffer.ToArray();
        }

        return entries;
    }

    private static void WriteEntries(string zipPath, IReadOnlyDictionary<string, byte[]> entries)
    {
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var (name, bytes) in entries)
        {
            using var stream = zip.CreateEntry(name).Open();
            stream.Write(bytes);
        }
    }

    /// <summary>A copy of the backup, changed by <paramref name="change"/>, in a new zip under the test's root.</summary>
    private string Derive(string backup, Action<Dictionary<string, byte[]>> change)
    {
        var entries = ReadEntries(backup);
        change(entries);
        var path = Path.Combine(_home.Root, $"derived-{Guid.NewGuid():N}.zip");
        WriteEntries(path, entries);
        return path;
    }

    private static void EditManifest(Dictionary<string, byte[]> entries, Action<JsonObject> edit)
    {
        var manifest = JsonNode.Parse(entries["manifest.json"])!.AsObject();
        edit(manifest);
        entries["manifest.json"] = Encoding.UTF8.GetBytes(manifest.ToJsonString());
    }

    private static TacitException Refusal(Func<Task> action) =>
        Assert.ThrowsAsync<TacitException>(action).GetAwaiter().GetResult();

    [Fact]
    public async Task The_preview_shows_the_manifest_fields_and_what_would_be_replaced()
    {
        await _service.WritePageAsync(Create("tacit-memory/preview-page", "预览页面", "预览内容"));
        var backup = await _service.BackupAsync(BackupTarget.Local);
        await _service.WritePageAsync(Create("tacit-memory/later-page", "之后页面", "之后内容"));

        var preview = await _service.PreviewRestoreAsync(backup.Location, config: false);

        var manifest = JsonNode.Parse(ReadEntries(backup.Location)["manifest.json"])!.AsObject();
        Assert.Equal(backup.Location, preview.Path);
        Assert.NotNull(preview.GeneratedAt);
        Assert.Equal("manual", preview.Kind);
        Assert.Equal(manifest["memoryHead"]!.GetValue<string>(), preview.MemoryHead);
        Assert.Equal(TestHome.Git(Memory, "log", "-1", "--format=%s", manifest["memoryHead"]!.GetValue<string>()).Trim(), preview.MemoryHeadSubject);
        Assert.Equal(manifest["files"]!.AsArray().Count, preview.FileCount);
        Assert.False(preview.Config);
        Assert.Single(preview.Replaces);
        Assert.Contains(Head()[..7], preview.Report, StringComparison.Ordinal);
        Assert.Equal(preview.Replaces, preview.ReportLines.Where(line => line.Replaces).Select(line => line.Text));
    }

    [Fact]
    public async Task A_preview_with_config_lists_the_config_and_the_prompts_too()
    {
        var backup = await _service.BackupAsync(BackupTarget.Local);

        var preview = await _service.PreviewRestoreAsync(backup.Location, config: true);

        Assert.True(preview.Config);
        Assert.Equal(3, preview.Replaces.Count);
    }

    [Fact]
    public async Task A_backup_without_kind_or_subject_previews_with_its_commit_only()
    {
        var backup = await _service.BackupAsync(BackupTarget.Local);
        var derived = Derive(backup.Location, entries => EditManifest(entries, manifest =>
        {
            manifest.Remove("kind");
            manifest.Remove("memoryHeadSubject");
        }));

        var preview = await _service.PreviewRestoreAsync(derived, config: false);

        Assert.Null(preview.Kind);
        Assert.Null(preview.MemoryHeadSubject);
        Assert.Equal(Head(), preview.MemoryHead);
    }

    [Fact]
    public async Task A_zip_without_a_manifest_is_refused_and_nothing_changes()
    {
        await _service.WritePageAsync(Create("tacit-memory/keep-page", "保留页面", "保留内容"));
        var before = Head();
        var zip = Path.Combine(_home.Root, "no-manifest.zip");
        WriteEntries(zip, new Dictionary<string, byte[]> { ["memory/.git/HEAD"] = Encoding.UTF8.GetBytes("ref: refs/heads/main\n") });

        var error = Refusal(() => _service.RestoreAsync(zip, config: false));

        Assert.Equal(TacitError.RestoreInvalid, error.Error);
        Assert.Equal(before, Head());
        Assert.True(File.Exists(PagePath("keep-page")));
        Assert.Empty(PreRestoreBackups());
    }

    [Fact]
    public async Task A_manifest_that_lists_a_missing_file_is_refused()
    {
        var backup = await _service.BackupAsync(BackupTarget.Local);
        var derived = Derive(backup.Location, entries => EditManifest(entries, manifest =>
            manifest["files"]!.AsArray().Add("memory/pages/ghost.md")));

        var error = Refusal(() => _service.RestoreAsync(derived, config: false));

        Assert.Equal(TacitError.RestoreInvalid, error.Error);
        Assert.Empty(PreRestoreBackups());
    }

    [Fact]
    public async Task A_zip_entry_missing_from_the_manifest_is_refused()
    {
        var backup = await _service.BackupAsync(BackupTarget.Local);
        var derived = Derive(backup.Location, entries => entries["memory/pages/unlisted.md"] = Encoding.UTF8.GetBytes("未列入清单"));

        var error = Refusal(() => _service.RestoreAsync(derived, config: false));

        Assert.Equal(TacitError.RestoreInvalid, error.Error);
        Assert.Empty(PreRestoreBackups());
    }

    [Theory]
    [InlineData("memory/../escape.md")]
    [InlineData("../escape.md")]
    [InlineData("/escape.md")]
    [InlineData("other/escape.md")]
    [InlineData("memory/pages/../../../escape.md")]
    public async Task An_entry_outside_the_allowed_places_is_refused_and_nothing_is_written(string name)
    {
        var backup = await _service.BackupAsync(BackupTarget.Local);
        var before = Head();
        var derived = Derive(backup.Location, entries => entries[name] = Encoding.UTF8.GetBytes("越界"));

        var error = Refusal(() => _service.RestoreAsync(derived, config: false));

        Assert.Equal(TacitError.RestoreInvalid, error.Error);
        Assert.Equal(before, Head());
        Assert.False(File.Exists(Path.Combine(_home.Root, "escape.md")));
        Assert.False(File.Exists(Path.Combine(Memory, "escape.md")));
        Assert.Empty(PreRestoreBackups());
    }

    [Fact]
    public async Task Restoring_without_config_replaces_only_the_memory_repository()
    {
        await _service.WritePageAsync(Create("tacit-memory/old-page", "旧页面", "旧内容"));
        var backup = await _service.BackupAsync(BackupTarget.Local);
        await _service.WritePageAsync(Create("tacit-memory/new-page", "新页面", "新内容"));
        File.AppendAllText(ConfigYaml, "# edited after the backup\n");
        File.WriteAllText(Path.Combine(PromptsFolder, "rules.md"), "# 之后改动的规则\n");

        var result = await _service.RestoreAsync(backup.Location, config: false);

        Assert.True(File.Exists(PagePath("old-page")));
        Assert.False(File.Exists(PagePath("new-page")));
        Assert.Contains("# edited after the backup", File.ReadAllText(ConfigYaml));
        Assert.Equal("# 之后改动的规则\n", File.ReadAllText(Path.Combine(PromptsFolder, "rules.md")));
        Assert.Equal(TestHome.Git(Memory, "rev-parse", "HEAD").Trim(), result.MemoryHead);
        Assert.DoesNotContain(result.Notices, notice => notice.Contains("tacit serve", StringComparison.Ordinal));
    }

    [Fact]
    public async Task With_config_the_config_and_the_prompts_are_replaced_and_extra_prompt_files_are_deleted()
    {
        File.WriteAllText(Path.Combine(PromptsFolder, "rules.md"), "## 值得记\n- 备份时的规则\n");
        var backup = await _service.BackupAsync(BackupTarget.Local);
        var backedUpConfig = ReadEntries(backup.Location)["config.yaml"];
        var backedUpRules = ReadEntries(backup.Location)["prompts/rules.md"];
        File.AppendAllText(ConfigYaml, "# edited after the backup\n");
        File.WriteAllText(Path.Combine(PromptsFolder, "extra.md"), "# 多出的提示词\n");
        File.WriteAllText(Path.Combine(PromptsFolder, "rules.md"), "# 之后改动的规则\n");

        var result = await _service.RestoreAsync(backup.Location, config: true);

        Assert.Equal(backedUpConfig, File.ReadAllBytes(ConfigYaml));
        Assert.Equal(backedUpRules, File.ReadAllBytes(Path.Combine(PromptsFolder, "rules.md")));
        Assert.False(File.Exists(Path.Combine(PromptsFolder, "extra.md")));
        Assert.Contains(result.Notices, notice => notice.Contains("config.yaml", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_restored_config_is_reloaded_at_once_and_what_waits_for_a_restart_is_listed()
    {
        File.AppendAllText(ConfigYaml, "retrieval:\n  topK: 7\nlimits:\n  page:\n    minAliases: 4\n");
        var backup = await _service.BackupAsync(BackupTarget.Local);

        var result = await _service.RestoreAsync(backup.Location, config: true);

        Assert.Equal(7, _service.Config.Retrieval.TopK);
        Assert.Equal(2, _service.Config.Limits.Page.MinAliases);
        Assert.Equal([new RestartChange("limits.page.minAliases", "2", "4")], result.Restart);
        Assert.Contains(result.Notices, notice => notice.Contains("limits.page.minAliases", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Notices, notice => notice.Contains("重启后生效", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_restored_config_that_does_not_load_leaves_the_service_on_the_config_it_had_and_says_why()
    {
        var running = _service.Config;
        var valid = File.ReadAllText(ConfigYaml);
        File.AppendAllText(ConfigYaml, "retrieval:\n  topK: lots\n");
        var backup = await _service.BackupAsync(BackupTarget.Local);
        File.WriteAllText(ConfigYaml, valid);

        var result = await _service.RestoreAsync(backup.Location, config: true);

        Assert.Same(running, _service.Config);
        Assert.Empty(result.Restart);
        Assert.Contains("topK: lots", File.ReadAllText(ConfigYaml));
        Assert.Contains(result.Notices, notice => notice.StartsWith("恢复回来的 config.yaml 有错误", StringComparison.Ordinal)
            && notice.Contains("retrieval.topK", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_restore_without_the_config_does_not_reload()
    {
        var running = _service.Config;
        File.AppendAllText(ConfigYaml, "retrieval:\n  topK: 7\n");
        var backup = await _service.BackupAsync(BackupTarget.Local);

        var result = await _service.RestoreAsync(backup.Location, config: false);

        Assert.Same(running, _service.Config);
        Assert.Empty(result.Restart);
    }

    [Fact]
    public async Task Bindings_json_is_never_restored()
    {
        _home.Bind();
        var backup = await _service.BackupAsync(BackupTarget.Local);
        _home.Unbind();
        var current = File.ReadAllBytes(BindingsJson);

        await _service.RestoreAsync(backup.Location, config: true);

        Assert.Equal(current, File.ReadAllBytes(BindingsJson));
    }

    [Fact]
    public async Task The_safety_backup_of_the_state_before_the_restore_is_written_first_and_holds_it()
    {
        await _service.WritePageAsync(Create("tacit-memory/kept-page", "保险页面", "保险内容"));
        var backup = await _service.BackupAsync(BackupTarget.Local);
        await _service.WritePageAsync(Create("tacit-memory/unsaved-page", "未备份页面", "未备份内容"));
        var before = Head();

        var result = await _service.RestoreAsync(backup.Location, config: false);

        Assert.StartsWith(Path.Combine(BackupsDirectory, "pre-restore-"), result.SafetyBackup, StringComparison.Ordinal);
        Assert.True(File.Exists(result.SafetyBackup));
        var entries = ReadEntries(result.SafetyBackup);
        Assert.Equal("pre-restore", JsonNode.Parse(entries["manifest.json"])!["kind"]!.GetValue<string>());
        Assert.Equal(before, JsonNode.Parse(entries["manifest.json"])!["memoryHead"]!.GetValue<string>());
        Assert.True(entries.ContainsKey("memory/pages/tacit-memory/unsaved-page.md"));
        Assert.Contains(_logs.Entries, entry => entry.Message.Contains(result.SafetyBackup, StringComparison.Ordinal));
    }

    [Fact]
    public async Task After_the_restore_recall_and_the_resident_block_reflect_the_backup()
    {
        _home.Bind();
        File.WriteAllText(Path.Combine(Memory, "workstreams.md"), "## 主线\n### old-workstream\n背景：旧的主线。\n");
        await _service.WritePageAsync(Create("old-workstream/old-page", "旧版本记录", "旧版本内容"));
        var backup = await _service.BackupAsync(BackupTarget.Local);
        File.WriteAllText(Path.Combine(Memory, "workstreams.md"), "## 主线\n### old-workstream\n背景：旧的主线。\n### new-workstream\n背景：新的主线。\n");
        await _service.WritePageAsync(Create("new-workstream/new-page", "新版本记录", "新版本内容"));
        // The service renders the resident block itself, after its debounce; no second renderer may run beside it.
        await Eventually(_service, () => File.Exists(AgentsMd) && File.ReadAllText(AgentsMd).Contains("new-workstream", StringComparison.Ordinal));

        await _service.RestoreAsync(backup.Location, config: false);

        var found = await _service.RecallAsync(new RecallRequest { Query = "旧版本记录" });
        Assert.Contains("old-workstream/old-page", found.Text, StringComparison.Ordinal);
        var missing = await _service.RecallAsync(new RecallRequest { Query = "新版本记录" });
        Assert.DoesNotContain("new-workstream/new-page", missing.Text, StringComparison.Ordinal);
        await Eventually(_service, () => !File.ReadAllText(AgentsMd).Contains("new-workstream", StringComparison.Ordinal));
        var block = File.ReadAllText(AgentsMd);
        Assert.Contains("old-workstream", block, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_resident_blocks_show_the_persona_of_the_backup_after_the_restore()
    {
        File.WriteAllText(PersonaMd, PersonaWith("偏好甲"));
        _home.Bind();
        var backup = await _service.BackupAsync(BackupTarget.Local);
        File.WriteAllText(PersonaMd, PersonaWith("偏好乙"));
        await Eventually(_service, () => File.ReadAllText(AgentsMd).Contains("偏好乙", StringComparison.Ordinal));

        await _service.RestoreAsync(backup.Location, config: false);

        await Eventually(_service, () => File.ReadAllText(AgentsMd).Contains("偏好甲", StringComparison.Ordinal));
        Assert.DoesNotContain("偏好乙", File.ReadAllText(AgentsMd), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_restored_config_with_another_memory_path_is_reported_and_the_repository_goes_to_the_current_one()
    {
        await _service.WritePageAsync(Create("tacit-memory/moved-page", "迁移页面", "迁移内容"));
        var backup = await _service.BackupAsync(BackupTarget.Local);
        var backedUpHead = Head();

        var other = _home.PathFor("other-memory");
        Directory.CreateDirectory(other);
        TestHome.Git(other, "init", "--quiet");
        TestHome.Git(other, "-c", "user.name=test", "-c", "user.email=test@localhost", "commit", "--quiet", "--allow-empty", "-m", "init: other");
        File.AppendAllText(ConfigYaml, $"memory:\n  path: '{other}'\n");
        _service.Dispose();
        _service = _home.Tacit().OpenService(_ => { }, _logs);

        var result = await _service.RestoreAsync(backup.Location, config: true);

        Assert.Equal(backedUpHead, TestHome.Git(other, "rev-parse", "HEAD").Trim());
        Assert.True(File.Exists(Path.Combine(other, "pages", "tacit-memory", "moved-page.md")));
        var notice = Assert.Single(result.Notices, text => text.Contains("重启后会指向", StringComparison.Ordinal));
        Assert.Contains(Memory, notice, StringComparison.Ordinal);
        Assert.Contains(other, notice, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_swap_that_cannot_finish_puts_the_repository_back_and_names_the_safety_backup()
    {
        await _service.WritePageAsync(Create("tacit-memory/before-backup", "备份前页面", "备份前内容"));
        var backup = await _service.BackupAsync(BackupTarget.Local);
        await _service.WritePageAsync(Create("tacit-memory/after-backup", "备份后页面", "备份后内容"));
        var before = Head();
        var parent = Path.GetDirectoryName(Memory)!;
        var siblingsBefore = Directory.GetFileSystemEntries(parent).Order(StringComparer.Ordinal).ToArray();

        // A file the swap cannot move: it is open without delete sharing, so its rename fails part way through.
        using var locked = new FileStream(PersonaMd, FileMode.Open, FileAccess.Read, FileShare.Read);
        var error = Refusal(() => _service.RestoreAsync(backup.Location, config: false));

        Assert.Equal(TacitError.RestoreFailed, error.Error);
        var safety = Assert.Single(PreRestoreBackups());
        Assert.Contains(safety, error.Message, StringComparison.Ordinal);
        locked.Dispose();
        Assert.Equal(before, Head());
        Assert.True(File.Exists(PagePath("after-backup")));
        Assert.True(File.Exists(PagePath("before-backup")));
        Assert.Equal(siblingsBefore, Directory.GetFileSystemEntries(parent).Order(StringComparer.Ordinal).ToArray());

        var found = await _service.RecallAsync(new RecallRequest { Query = "备份后页面" });
        Assert.Contains("tacit-memory/after-backup", found.Text, StringComparison.Ordinal);
        var written = await _service.WritePageAsync(Create("tacit-memory/after-failure", "失败之后", "失败之后内容"));
        Assert.False(written.IsError, written.Text);
    }

    [Fact]
    public async Task A_write_that_arrives_during_the_restore_lands_in_the_restored_repository()
    {
        await _service.WritePageAsync(Create("tacit-memory/before-backup", "备份前页面", "备份前内容"));
        var backup = await _service.BackupAsync(BackupTarget.Local);

        // The restore holds at its first embedding request, which is in the rebuild after the swap.
        var reached = _models.EmbeddingCalls + 1;
        _models.HoldEmbeddings();
        var restore = _service.RestoreAsync(backup.Location, config: false);
        await _models.EmbeddingRequestsReached(reached).WaitAsync(Guard);

        var write = _service.WritePageAsync(Create("tacit-memory/during-restore", "恢复期间页面", "恢复期间内容"));
        _models.ReleaseEmbeddings();
        var result = await restore.WaitAsync(Guard);
        var written = await write.WaitAsync(Guard);

        Assert.False(written.IsError, written.Text);
        Assert.True(File.Exists(PagePath("before-backup")));
        Assert.True(File.Exists(PagePath("during-restore")));
        Assert.NotEqual(result.MemoryHead, Head());
    }

    [Fact]
    public async Task A_backup_whose_config_no_longer_loads_is_restored_and_the_error_is_reported()
    {
        await _service.WritePageAsync(Create("tacit-memory/old-page", "旧页面", "旧内容"));
        // A backup made before apiKeyEnv was removed: its config.yaml holds a key the current schema refuses.
        File.AppendAllText(ConfigYaml, "apiKeyEnv: ''\n");
        var backup = await _service.BackupAsync(BackupTarget.Local);
        await _service.WritePageAsync(Create("tacit-memory/new-page", "新页面", "新内容"));
        var running = _service.Config;

        var result = await _service.RestoreAsync(backup.Location, config: true);

        Assert.Same(running, _service.Config);
        Assert.Empty(result.Restart);
        Assert.Equal(TestHome.Git(Memory, "rev-parse", "HEAD").Trim(), result.MemoryHead);
        Assert.True(File.Exists(PagePath("old-page")));
        Assert.False(File.Exists(PagePath("new-page")));
        Assert.Contains(result.Notices, notice => notice.Contains("apiKeyEnv", StringComparison.Ordinal) && notice.Contains("tacit serve", StringComparison.Ordinal));
        Assert.Contains("apiKeyEnv", File.ReadAllText(ConfigYaml), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_preview_of_a_backup_whose_config_no_longer_loads_says_so()
    {
        File.AppendAllText(ConfigYaml, "apiKeyEnv: ''\n");
        var backup = await _service.BackupAsync(BackupTarget.Local);
        var before = Head();

        var preview = await _service.PreviewRestoreAsync(backup.Location, config: true);

        Assert.Contains("apiKeyEnv", preview.Report, StringComparison.Ordinal);
        Assert.Equal(before, Head());
    }

    [Fact]
    public async Task A_restore_leaves_no_staging_or_previous_folder_in_the_repository()
    {
        await _service.WritePageAsync(Create("tacit-memory/old-page", "旧页面", "旧内容"));
        var backup = await _service.BackupAsync(BackupTarget.Local);
        await _service.WritePageAsync(Create("tacit-memory/new-page", "新页面", "新内容"));

        await _service.RestoreAsync(backup.Location, config: false);

        Assert.Empty(Directory.GetFileSystemEntries(Memory, ".tacit-*"));
    }

    [Fact]
    public async Task A_remote_restore_is_only_previewed_and_a_preview_needs_a_source()
    {
        var backup = await _service.BackupAsync(BackupTarget.Local);

        Assert.Equal(TacitError.RequestInvalid, Refusal(() => _service.RestoreAsync(null, remote: true)).Error);
        Assert.Equal(TacitError.RequestInvalid, Refusal(() => _service.PreviewRestoreAsync(null)).Error);
        Assert.Equal(TacitError.RequestInvalid, Refusal(() => _service.PreviewRestoreAsync(backup.Location, remote: true)).Error);
        Assert.Equal(TacitError.RequestInvalid, Refusal(() => _service.RestoreAsync(backup.Location, remote: true)).Error);
    }
}
