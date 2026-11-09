using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TacitMemory.Core.Tests;

/// <summary>
/// The status columns that need no model or network: bindings, the last consolidation, the pending counts, the latest local
/// backup and the secrets' sources. Each is read through the facade, both with the service stopped and running.
/// </summary>
public sealed class StatusColumnsTests : IDisposable
{
    private static readonly Uri Endpoint = new("http://127.0.0.1:18888/mcp");

    private const string TwoDayCandidate = "## 候选\n\n### 回答尽量简短\n归属：偏好\n- 2026-10-30 原话「别写那么长」\n- 2026-10-31 原话「直接说结论」\n\n## 拒绝列表\n";

    private readonly TestHome _home = new();
    private readonly ModelDouble _models = new();
    private MemoryService? _service;

    public StatusColumnsTests() => _home.InitReady(models: _models);

    public void Dispose()
    {
        _service?.Dispose();
        _models.Dispose();
        _home.Dispose();
    }

    private string Memory => _home.DefaultMemory;

    private string PersonaMd => Path.Combine(Memory, "persona.md");

    private string CandidatesMd => Path.Combine(Memory, "candidates.md");

    private string AgentsMd => Path.Combine(_home.UserHome, ".omp", "agent", "AGENTS.md");

    private string SkillLink => Path.Combine(_home.UserHome, ".omp", "agent", "skills", "tacit-memory");

    private string BackupsDirectory => Path.Combine(_home.TacitHome, "backups");

    private MemoryService Service => _service ??= _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

    /// <summary>The day of the init commit, which the consolidation columns count from when no consolidation has run.</summary>
    private DateOnly InitDay => DateOnly.ParseExact(
        TestHome.Git(Memory, "log", "-1", "--format=%as").Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private void Connect() => _home.Bind();

    /// <summary>Moves the test clock to 10:00 UTC on <paramref name="day"/>.</summary>
    private void ClockOn(DateOnly day)
    {
        var target = new DateTimeOffset(day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero);
        _home.Clock.Advance(target - _home.Clock.GetUtcNow());
    }

    /// <summary>An empty commit in the memory repository, dated <paramref name="day"/> by both git dates.</summary>
    private void CommitOn(DateOnly day, string message)
    {
        var date = $"{day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}T10:00:00+00:00";
        var start = new ProcessStartInfo("git") { WorkingDirectory = Memory, UseShellExecute = false };
        foreach (var arg in new[] { "-c", "user.name=tacit", "-c", "user.email=tacit@localhost", "commit", "--allow-empty", "-m", message })
            start.ArgumentList.Add(arg);
        start.Environment["GIT_AUTHOR_DATE"] = date;
        start.Environment["GIT_COMMITTER_DATE"] = date;
        using var process = Process.Start(start)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    /// <summary>The report with the service stopped, from a config loaded the way the CLI loads it.</summary>
    private async Task<StatusReport> OfflineReport()
    {
        var tacit = _home.Tacit();
        return await tacit.OfflineStatus(tacit.LoadConfig());
    }

    /// <summary>Replaces <c>config.yaml</c> with the embedding model alone: the reranker and the decision model are unconfigured.</summary>
    private void ConfigureEmbeddingOnly() => File.WriteAllText(
        Path.Combine(_home.TacitHome, "config.yaml"),
        $"language: zh\nmodels:\n  embedding: {{ url: '{_models.EmbeddingUrl}', model: double-embedding }}\n");

    private static BindingCheck OnlyBinding(StatusReport report) =>
        Assert.Single(Assert.IsType<BindingsStatus>(report.Bindings).Items);

    /// <summary>Every file under the TACIT_HOME and the user home, with its bytes, in path order.</summary>
    private List<(string Path, string Bytes)> Snapshot() =>
        new[] { _home.TacitHome, _home.UserHome }
            .SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            // The derived index is held open by a running service and cannot be read, so the cache directory is not compared.
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.cache{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(path => (path, Convert.ToBase64String(File.ReadAllBytes(path))))
            .ToList();

    /// <summary>Every request the model double has answered, of any role.</summary>
    private int Calls() => _models.EmbeddingCalls + _models.RerankCalls + _models.DecisionCalls;

    private void WriteBackup(string name, DateTime written)
    {
        Directory.CreateDirectory(BackupsDirectory);
        var path = Path.Combine(BackupsDirectory, name);
        File.WriteAllBytes(path, []);
        File.SetLastWriteTime(path, written);
    }

    /// <summary>Points the omp skill link at <paramref name="target"/>, replacing the one <c>connect</c> made.</summary>
    private void PointSkillLinkAt(string target)
    {
        Directory.Delete(SkillLink, recursive: false);
        Directory.CreateDirectory(target);
        var start = new ProcessStartInfo("cmd") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "/c", "mklink", "/J", SkillLink, target })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    private string SkillsFolder => Path.Combine(_home.UserHome, ".omp", "agent", "skills");

    [Fact]
    public async Task A_healthy_binding_is_reported_as_fine()
    {
        Connect();

        var report = await OfflineReport();

        Assert.Null(OnlyBinding(report).Problem);
        Assert.Contains("omp：正常", Assert.Single(report.Bindings!.Lines));
        Assert.Contains(SkillsFolder, Assert.Single(report.Bindings!.Lines));
        Assert.Equal(SkillsFolder, OnlyBinding(report).SkillsFolder);
    }

    [Fact]
    public async Task A_missing_context_file_is_reported_and_nothing_is_written()
    {
        Connect();
        File.Delete(AgentsMd);
        var tacit = _home.Tacit();
        var config = tacit.LoadConfig();
        var before = Snapshot();

        var report = await tacit.OfflineStatus(config);

        Assert.Equal(BindingProblem.ContextFileMissing, OnlyBinding(report).Problem);
        Assert.Contains("上下文文件不存在", Assert.Single(report.Bindings!.Lines));
        Assert.False(File.Exists(AgentsMd));
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task A_context_file_that_is_not_utf8_is_reported_and_nothing_is_written()
    {
        Connect();
        File.WriteAllBytes(AgentsMd, Encoding.Latin1.GetBytes("# régles\n"));
        var tacit = _home.Tacit();
        var config = tacit.LoadConfig();
        var before = Snapshot();

        var report = await tacit.OfflineStatus(config);

        Assert.Equal(BindingProblem.ContextFileNotUtf8, OnlyBinding(report).Problem);
        Assert.Contains("不是 UTF-8", Assert.Single(report.Bindings!.Lines));
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task A_resident_block_with_a_missing_end_marker_is_reported_and_nothing_is_written()
    {
        Connect();
        File.WriteAllText(AgentsMd, File.ReadAllText(AgentsMd).Replace("<!-- tacit:end -->", "", StringComparison.Ordinal));
        var tacit = _home.Tacit();
        var config = tacit.LoadConfig();
        var before = Snapshot();

        var report = await tacit.OfflineStatus(config);

        Assert.Equal(BindingProblem.MarkersIncomplete, OnlyBinding(report).Problem);
        Assert.Contains("起止标记不完整或重复", Assert.Single(report.Bindings!.Lines));
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task A_resident_block_whose_markers_are_repeated_is_reported_and_nothing_is_written()
    {
        Connect();
        var text = File.ReadAllText(AgentsMd);
        File.WriteAllText(AgentsMd, text + "\n" + text);
        var tacit = _home.Tacit();
        var config = tacit.LoadConfig();
        var before = Snapshot();

        var report = await tacit.OfflineStatus(config);

        Assert.Equal(BindingProblem.MarkersIncomplete, OnlyBinding(report).Problem);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task A_skill_link_that_points_elsewhere_is_reported_by_name_and_nothing_is_written()
    {
        Connect();
        PointSkillLinkAt(Path.Combine(_home.Root, "elsewhere"));
        var tacit = _home.Tacit();
        var config = tacit.LoadConfig();
        var before = Snapshot();

        var report = await tacit.OfflineStatus(config);

        var binding = OnlyBinding(report);
        Assert.Equal(BindingProblem.SkillLinkNotTacit, binding.Problem);
        Assert.Equal(SkillLink, binding.SkillLink);
        Assert.Contains(SkillLink, Assert.Single(report.Bindings!.Lines));
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task A_missing_memory_command_link_is_reported_by_name_and_nothing_is_written()
    {
        Connect();
        var remember = Path.Combine(Path.GetDirectoryName(SkillLink)!, "tacit-remember");
        Directory.Delete(remember, recursive: false);
        var tacit = _home.Tacit();
        var config = tacit.LoadConfig();
        var before = Snapshot();

        var report = await tacit.OfflineStatus(config);

        var binding = OnlyBinding(report);
        Assert.Equal(BindingProblem.SkillLinkNotTacit, binding.Problem);
        Assert.Equal(remember, binding.SkillLink);
        Assert.Contains(remember, Assert.Single(report.Bindings!.Lines));
        Assert.False(Directory.Exists(remember));
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task A_shared_context_file_is_checked_once_and_its_problem_is_named_on_the_first_binding()
    {
        Connect();
        _home.Bind("beta");
        File.Delete(AgentsMd);
        var tacit = _home.Tacit();
        var config = tacit.LoadConfig();
        var before = Snapshot();

        var report = await tacit.OfflineStatus(config);

        var items = Assert.IsType<BindingsStatus>(report.Bindings).Items;
        Assert.Equal(2, items.Count);
        Assert.Equal(BindingProblem.ContextFileMissing, items[0].Problem);
        Assert.Equal(["beta"], items[0].SharedWith);
        Assert.Null(items[1].Problem);
        Assert.Equal(["omp"], items[1].SharedWith);
        Assert.Single(report.Bindings!.Lines, line => line.Contains("上下文文件不存在", StringComparison.Ordinal));
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task A_shared_skills_folder_with_a_wrong_link_is_reported_once_on_the_first_binding()
    {
        Connect();
        _home.Bind("beta");
        PointSkillLinkAt(Path.Combine(_home.Root, "elsewhere"));
        var tacit = _home.Tacit();
        var config = tacit.LoadConfig();
        var before = Snapshot();

        var report = await tacit.OfflineStatus(config);

        var items = Assert.IsType<BindingsStatus>(report.Bindings).Items;
        Assert.Equal(BindingProblem.SkillLinkNotTacit, items[0].Problem);
        Assert.Equal(SkillLink, items[0].SkillLink);
        Assert.Null(items[1].Problem);
        Assert.Equal(["beta"], items[0].SharedWith);
        Assert.Single(report.Bindings!.Lines, line => line.Contains("没有指向 tacit", StringComparison.Ordinal));
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task With_no_binding_the_column_says_so()
    {
        var report = await OfflineReport();

        Assert.Empty(report.Bindings!.Items);
        Assert.Equal(["没有绑定的客户端。"], report.Bindings.Lines);
    }

    [Fact]
    public async Task The_offline_models_column_lists_only_the_unconfigured_models()
    {
        ConfigureEmbeddingOnly();

        var models = Assert.IsType<ModelsStatus>((await OfflineReport()).Models);

        Assert.Equal(new[] { "reranker", "decision" }, models.Items.Select(item => item.Role));
        Assert.All(models.Items, item => Assert.Equal(ModelState.Unconfigured, item.State));
        Assert.Equal("辅助模型", models.Title);
        Assert.Equal(new[] { "reranker：未配置", "decision：未配置" }, models.Lines);
    }

    [Fact]
    public async Task The_offline_models_column_is_left_out_when_every_model_is_configured()
    {
        Assert.Null((await OfflineReport()).Models);
    }

    [Fact]
    public async Task An_unconfigured_model_is_worded_the_same_online_and_offline()
    {
        ConfigureEmbeddingOnly();

        var offline = Assert.IsType<ModelsStatus>((await OfflineReport()).Models);
        var online = Assert.IsType<ModelsStatus>((await Service.StatusAsync(Endpoint)).Models);

        Assert.Equal(new[] { "reranker：未配置", "decision：未配置" }, offline.Lines);
        Assert.Equal(new[] { "embedding：可用", "reranker：未配置", "decision：未配置" }, online.Lines);
    }

    [Fact]
    public async Task A_repository_whose_only_commit_is_the_init_commit_counts_its_days_from_it()
    {
        ClockOn(InitDay.AddDays(9));

        var consolidation = (await OfflineReport()).Consolidation!;

        Assert.Equal(InitDay, consolidation.LastConsolidationDay);
        Assert.Equal(9, consolidation.DaysSince);
        Assert.Contains("尚未整理", Assert.Single(consolidation.Lines));
    }

    [Fact]
    public async Task The_latest_consolidation_commit_is_the_baseline_and_its_day_counts()
    {
        var init = InitDay;
        CommitOn(init.AddDays(3), "consolidate: first");
        CommitOn(init.AddDays(5), "consolidate: second");
        ClockOn(init.AddDays(10));

        var consolidation = (await OfflineReport()).Consolidation!;

        Assert.Equal(init.AddDays(5), consolidation.LastConsolidationDay);
        Assert.Equal(5, consolidation.DaysSince);
        Assert.Contains($"{init.AddDays(5):yyyy-MM-dd}", Assert.Single(consolidation.Lines));
    }

    [Fact]
    public async Task A_commit_with_the_old_checkup_prefix_is_not_the_baseline()
    {
        var init = InitDay;
        CommitOn(init.AddDays(3), "checkup: first");
        ClockOn(init.AddDays(10));

        var consolidation = (await OfflineReport()).Consolidation!;

        Assert.Equal(init, consolidation.LastConsolidationDay);
        Assert.Equal(10, consolidation.DaysSince);
    }

    [Fact]
    public async Task A_repository_with_no_consolidation_and_no_init_commit_has_never_been_checked()
    {
        // The init commit is reworded, so the history holds neither a consolidate: nor an init: commit.
        TestHome.Git(Memory, "-c", "user.name=tacit", "-c", "user.email=tacit@localhost", "commit", "--amend", "--allow-empty", "-m", "setup");

        var consolidation = (await OfflineReport()).Consolidation!;

        Assert.Null(consolidation.LastConsolidationDay);
        Assert.Null(consolidation.DaysSince);
        Assert.Equal(["从未整理（仓库没有 consolidate: 或 init: 提交）"], consolidation.Lines);
    }

    [Fact]
    public async Task The_pending_counts_are_the_numbers_the_consolidation_reminder_shows()
    {
        File.WriteAllText(PersonaMd, "## 红线\n- 不要删库\n\n## 偏好\n- 沟通：先要结论\n");
        File.WriteAllText(CandidatesMd, TwoDayCandidate.ReplaceLineEndings("\n"));
        File.WriteAllText(Path.Combine(_home.DefaultMemory, "recent.md"), "## 近期主线\n- 一条主线\n");
        var service = Service;

        var pending = Assert.IsType<PendingStatus>((await service.StatusAsync(Endpoint)).Pending);
        var reminder = await service.ServerInstructionsAsync();

        Assert.Equal(1, pending.Confirmable);
        Assert.Equal(StatusLevel.Attention, pending.LineLevels()[0]);
        Assert.True(pending.FormatProblems > 0);
        Assert.False(pending.MainlineEmpty);
        Assert.Contains($"- Candidates that can be confirmed: {pending.Confirmable}.", reminder);
        Assert.Contains($"- Format and rule problems: {pending.FormatProblems}.", reminder);
        Assert.Contains("- Mainline: not empty.", reminder);
    }

    [Fact]
    public async Task Reading_the_pending_counts_asks_no_model()
    {
        File.WriteAllText(CandidatesMd, TwoDayCandidate.ReplaceLineEndings("\n"));
        var service = Service;
        await service.VectorCompletion;
        await service.JudgmentCompletion;
        var calls = Calls();

        await service.StatusAsync(Endpoint);
        await OfflineReport();

        Assert.Equal(calls, Calls());
    }

    [Fact]
    public async Task A_candidates_file_that_is_not_utf8_leaves_its_count_unknown()
    {
        File.WriteAllBytes(CandidatesMd, Encoding.Latin1.GetBytes("## 候选\n- café\n"));

        var pending = (await OfflineReport()).Pending!;

        Assert.Null(pending.Confirmable);
        Assert.Contains("未统计", pending.Lines[0]);
    }

    [Fact]
    public async Task A_history_git_cannot_read_is_reported_in_its_columns_and_the_other_columns_still_come_out()
    {
        // The service loads its config while the repository is there; the history is set aside afterwards. Git objects are
        // read-only files, so the directory is moved rather than deleted.
        var service = Service;
        Directory.Move(Path.Combine(Memory, ".git"), Path.Combine(Memory, ".git-set-aside"));

        var report = await service.StatusAsync(Endpoint);

        Assert.Null(report.Consolidation!.DaysSince);
        Assert.Contains("无法读取记忆仓库的历史", Assert.Single(report.Consolidation.Lines));
        Assert.Null(report.Pending!.Confirmable);
        Assert.Null(report.Pending.MainlineEmpty);
        Assert.Contains("无法统计待处理问题", Assert.Single(report.Pending.Lines));
        Assert.NotNull(report.Bindings);
        Assert.NotNull(report.Backups);
        Assert.NotNull(report.Secrets);
        Assert.NotNull(report.Logs);
    }

    [Fact]
    public async Task The_latest_local_backup_is_the_newest_of_the_three_kinds_by_when_it_was_written()
    {
        WriteBackup("auto-2026-10-30.zip", new DateTime(2026, 10, 30, 9, 0, 0));
        WriteBackup("auto-2026-11-01.zip", new DateTime(2026, 11, 1, 8, 0, 0));
        WriteBackup("manual-2026-11-01-093000.zip", new DateTime(2026, 11, 1, 9, 30, 0));
        WriteBackup("pre-restore-2026-11-02-101500.zip", new DateTime(2026, 11, 2, 10, 15, 0));
        WriteBackup("notes.zip", new DateTime(2026, 11, 9, 0, 0, 0));
        WriteBackup("auto-2026-11-03.zip.partial", new DateTime(2026, 11, 9, 0, 0, 0));

        var backups = (await OfflineReport()).Backups!;

        Assert.Equal("pre-restore-2026-11-02-101500.zip", backups.Latest);
        Assert.Equal(new DateTime(2026, 11, 2, 10, 15, 0), backups.LatestAt!.Value.LocalDateTime);
        Assert.Contains(backups.Lines, line => line.Contains("pre-restore-2026-11-02-101500.zip", StringComparison.Ordinal));
        Assert.Contains(backups.Lines, line => line.Contains("2026-11-02 10:15", StringComparison.Ordinal));
    }

    [Fact]
    public async Task With_no_local_backup_the_column_says_so()
    {
        var backups = (await OfflineReport()).Backups!;

        Assert.Null(backups.Latest);
        Assert.Null(backups.LatestAt);
        Assert.Equal(["没有本地备份", "云端备份：未配置"], backups.Lines);
    }

    [Fact]
    public async Task A_key_from_the_config_file_is_reported_as_such()
    {
        var secrets = (await OfflineReport()).Secrets!;

        var decision = Assert.Single(secrets.Items, item => item.Key == "models.decision.apiKey");
        Assert.Equal(SecretSource.ConfigFile, decision.Source);
        Assert.False(decision.Overridden);
        Assert.Contains("来自配置文件", Assert.Single(secrets.Lines, line => line.StartsWith("models.decision.apiKey", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_key_from_the_environment_is_reported_as_overriding_the_config_file_and_the_value_is_not_shown()
    {
        _home.EnvironmentVariables["TACIT_DECISION_API_KEY"] = "env-decision-secret";

        var secrets = (await OfflineReport()).Secrets!;

        var decision = Assert.Single(secrets.Items, item => item.Key == "models.decision.apiKey");
        Assert.Equal(SecretSource.Environment, decision.Source);
        Assert.True(decision.Overridden);
        Assert.Contains("配置文件中的值未生效", Assert.Single(secrets.Lines, line => line.StartsWith("models.decision.apiKey", StringComparison.Ordinal)));
        Assert.DoesNotContain("env-decision-secret", string.Join("\n", secrets.Lines));
        Assert.DoesNotContain("test-key", string.Join("\n", secrets.Lines));
    }

    [Fact]
    public async Task A_password_with_no_source_at_all_is_reported_as_not_set()
    {
        _home.InitReady("backup:\n  webdav: { url: https://dav.example/tacit/ }\n", _models);

        var secrets = (await OfflineReport()).Secrets!;

        var password = Assert.Single(secrets.Items, item => item.Key == "backup.webdav.password");
        Assert.Null(password.Source);
        Assert.Contains("未设置", Assert.Single(secrets.Lines, line => line.StartsWith("backup.webdav.password", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Each_model_key_is_reported_from_the_environment_the_config_or_as_not_set()
    {
        File.WriteAllText(
            Path.Combine(_home.TacitHome, "config.yaml"),
            _models.ModelsYaml(embeddingKey: "emb-config-secret", decisionKey: "test-key").ReplaceLineEndings("\n"));
        _home.EnvironmentVariables["TACIT_RERANKER_API_KEY"] = "rer-env-secret";

        var secrets = (await OfflineReport()).Secrets!;

        Assert.Equal(["models.embedding.apiKey", "models.reranker.apiKey", "models.decision.apiKey"], secrets.Items.Select(item => item.Key));
        Assert.Equal(SecretSource.ConfigFile, Assert.Single(secrets.Items, item => item.Key == "models.embedding.apiKey").Source);
        Assert.Equal(SecretSource.Environment, Assert.Single(secrets.Items, item => item.Key == "models.reranker.apiKey").Source);
        Assert.Equal(SecretSource.ConfigFile, Assert.Single(secrets.Items, item => item.Key == "models.decision.apiKey").Source);
        Assert.Contains("来自配置文件", Assert.Single(secrets.Lines, line => line.StartsWith("models.embedding.apiKey", StringComparison.Ordinal)));
        Assert.Contains("来自环境变量", Assert.Single(secrets.Lines, line => line.StartsWith("models.reranker.apiKey", StringComparison.Ordinal)));
        var text = string.Join("\n", secrets.Lines);
        Assert.DoesNotContain("emb-config-secret", text);
        Assert.DoesNotContain("rer-env-secret", text);
    }

    [Fact]
    public async Task A_model_key_that_is_nowhere_is_reported_as_not_set_and_is_not_an_error()
    {
        File.WriteAllText(Path.Combine(_home.TacitHome, "config.yaml"), _models.ModelsYaml(decisionKey: string.Empty).ReplaceLineEndings("\n"));

        var report = await OfflineReport();

        var embedding = Assert.Single(report.Secrets!.Items, item => item.Key == "models.embedding.apiKey");
        Assert.Null(embedding.Source);
        Assert.Contains("未设置", Assert.Single(report.Secrets!.Lines, line => line.StartsWith("models.embedding.apiKey", StringComparison.Ordinal)));
        Assert.Null(report.Config);
        Assert.All(((IStatusColumn)report.Secrets!).LineLevels(), level => Assert.Null(level));
    }

    [Fact]
    public async Task An_unconfigured_model_needs_no_key_even_when_its_variable_is_set()
    {
        ConfigureEmbeddingOnly();
        _home.EnvironmentVariables["TACIT_DECISION_API_KEY"] = "dec-env-secret";

        var secrets = (await OfflineReport()).Secrets!;

        Assert.Equal(["models.embedding.apiKey"], secrets.Items.Select(item => item.Key));
        Assert.Equal(
            ["models.embedding.apiKey：未设置（环境变量 TACIT_EMBEDDING_API_KEY 与配置文件都没有）", "models.reranker：未配置，不需要密钥", "models.decision：未配置，不需要密钥", "backup.webdav：未配置，不需要密钥"],
            secrets.Lines);
    }

    [Fact]
    public async Task A_password_from_the_environment_alone_is_reported_as_from_the_environment()
    {
        _home.InitReady("backup:\n  webdav: { url: https://dav.example/tacit/, username: me }\n", _models);
        _home.EnvironmentVariables["TACIT_WEBDAV_PASSWORD"] = "env-webdav-secret";

        var secrets = (await OfflineReport()).Secrets!;

        var password = Assert.Single(secrets.Items, item => item.Key == "backup.webdav.password");
        Assert.Equal(SecretSource.Environment, password.Source);
        Assert.False(password.Overridden);
        Assert.DoesNotContain("env-webdav-secret", string.Join("\n", secrets.Lines));
    }

    [Fact]
    public async Task The_shared_columns_are_the_same_whether_or_not_the_service_runs()
    {
        Connect();
        var init = InitDay;
        CommitOn(init.AddDays(2), "consolidate: a");
        WriteBackup("auto-2026-11-01.zip", new DateTime(2026, 11, 1, 8, 0, 0));
        ClockOn(init.AddDays(4));
        var service = Service;
        var before = Snapshot();

        var running = await service.StatusAsync(Endpoint);
        var offline = await OfflineReport();

        Assert.Equal(offline.Bindings!.Lines, running.Bindings!.Lines);
        Assert.Equal(offline.Consolidation!.Lines, running.Consolidation!.Lines);
        Assert.Equal(offline.Consolidation.DaysSince, running.Consolidation.DaysSince);
        Assert.Equal(offline.Pending!.Lines, running.Pending!.Lines);
        Assert.Equal(offline.Backups!.Lines, running.Backups!.Lines);
        Assert.Equal(offline.Secrets!.Lines, running.Secrets!.Lines);
        Assert.Equal(offline.Logs!.Lines, running.Logs!.Lines);
        Assert.Equal(offline.Clients!.Lines, running.Clients!.Lines);
        Assert.Null(offline.Models);
        Assert.NotNull(running.Models);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task Clients_says_no_client_has_connected_when_nothing_is_recorded()
    {
        var clients = (await OfflineReport()).Clients!;

        Assert.Equal("客户端", clients.Title);
        Assert.Equal("尚未有客户端连接过服务。", Assert.Single(clients.Lines));
        Assert.Empty(clients.Items);
        Assert.All(((IStatusColumn)clients).LineLevels(), level => Assert.Null(level));
    }

    [Fact]
    public async Task Clients_lists_each_recorded_client_with_its_version_date_protocol_and_extensions_while_the_service_is_stopped()
    {
        Service.RecordClient(new ClientObservation("probe-client", "1.2.3", "2026-07-28", ["sampling"], ["io.example/tasks", "io.example/apps"]));
        _service!.Dispose();
        _service = null;

        var clients = (await OfflineReport()).Clients!;

        var line = Assert.Single(clients.Lines);
        Assert.Contains("probe-client 1.2.3", line, StringComparison.Ordinal);
        Assert.Contains(TestHome.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), line, StringComparison.Ordinal);
        Assert.Contains("2026-07-28", line, StringComparison.Ordinal);
        Assert.Contains("io.example/tasks, io.example/apps", line, StringComparison.Ordinal);
        var item = Assert.Single(clients.Items);
        Assert.Equal("probe-client", item.Name);
        Assert.Equal("1.2.3", item.Version);
        Assert.Equal("2026-07-28", item.ProtocolVersion);
        Assert.Equal(["io.example/tasks", "io.example/apps"], item.Extensions);
    }

    [Fact]
    public async Task A_client_that_declares_no_extension_is_shown_as_none()
    {
        Service.RecordClient(new ClientObservation("probe-client", "1.2.3", "2025-11-25", ["roots"], []));

        var line = Assert.Single((await OfflineReport()).Clients!.Lines);

        Assert.EndsWith("扩展：无", line, StringComparison.Ordinal);
    }
}
