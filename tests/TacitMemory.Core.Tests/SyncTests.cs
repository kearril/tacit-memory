namespace TacitMemory.Core.Tests;

/// <summary>
/// The sync of the memory repository between two machines through a git remote: two homes stand for the two machines, and
/// a bare repository in a temporary folder is the remote, reached by its local path. Both homes start from the same
/// initialized repository, as a machine that joined by cloning does.
/// </summary>
public sealed class SyncTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tacit-tests", Guid.NewGuid().ToString("N"));
    private readonly TestHome _a = new();
    private readonly TestHome _b = new();
    private readonly List<MemoryService> _services = [];

    public SyncTests()
    {
        Directory.CreateDirectory(_root);
        TestHome.Git(_root, "init", "--bare", "--quiet", "remote.git");
    }

    public void Dispose()
    {
        foreach (var service in _services)
            service.Dispose();
        _a.Dispose();
        _b.Dispose();
        try
        {
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Left for the system to clean up.
        }
    }

    private string Remote => Path.Combine(_root, "remote.git");

    private static string SyncConfig(string remote, int? pullMinutes = null) =>
        $"sync:\n  remote: '{remote}'\n" + (pullMinutes is { } minutes ? $"  pullMinutes: {minutes}\n" : "");

    /// <summary>Initializes <paramref name="home"/> with the sync pointed at <paramref name="remote"/> and opens its service.</summary>
    private MemoryService Open(TestHome home, string? remote = null, int? pullMinutes = null)
    {
        home.InitReady(SyncConfig(remote ?? Remote, pullMinutes));
        return Start(home);
    }

    private MemoryService Start(TestHome home)
    {
        var service = home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);
        _services.Add(service);
        return service;
    }

    private static string Activities(TestHome home) => File.ReadAllText(Path.Combine(home.DefaultMemory, "recent", "2026-11.md"));

    private static WriteRecentRequest Activity(string text) => new() { Text = text };

    private static string Head(string repository, string reference = "HEAD") => TestHome.Git(repository, "rev-parse", reference).Trim();

    private static Task<StatusReport> Status(MemoryService service) => service.Reports.StatusAsync(new Uri("http://127.0.0.1:18888/mcp"));

    [Fact]
    public async Task What_one_machine_writes_reaches_the_other_with_a_sync()
    {
        var a = Open(_a);
        var b = Open(_b);

        await a.Tools.WriteRecentAsync(Activity("在 A 上把代理分流改成按域名匹配"));
        await a.SettledAsync();
        var result = await b.Maintenance.SyncAsync();

        Assert.Equal(SyncOutcome.Synced, result.Record!.Outcome);
        Assert.Contains("在 A 上把代理分流改成按域名匹配", Activities(_b));
        Assert.Equal(Head(_a.DefaultMemory), Head(_b.DefaultMemory));
    }

    [Fact]
    public async Task The_first_sync_pushes_the_whole_history_to_an_empty_remote()
    {
        var a = Open(_a);

        var result = await a.Maintenance.SyncAsync();

        Assert.Equal(SyncOutcome.Synced, result.Record!.Outcome);
        var branch = TestHome.Git(_a.DefaultMemory, "symbolic-ref", "--short", "HEAD").Trim();
        Assert.Equal(Head(_a.DefaultMemory), Head(Remote, branch));
        var status = await Status(a);
        Assert.Equal(0, status.Sync!.Unpushed);
        Assert.Equal(SyncOutcome.Synced, status.Sync.Last!.Outcome);
    }

    [Fact]
    public async Task A_remote_with_unrelated_history_is_neither_pulled_nor_pushed_and_pauses_the_automatic_syncs()
    {
        var a = Open(_a);
        await a.Maintenance.SyncAsync();
        var remoteHead = Head(Remote, TestHome.Git(_a.DefaultMemory, "symbolic-ref", "--short", "HEAD").Trim());

        // The other machine made its own repository instead of cloning: its history starts from another commit.
        _b.InitReady(SyncConfig(Remote));
        TestHome.Git(_b.DefaultMemory, "commit", "--amend", "--quiet", "-m", "init: create memory repo elsewhere");
        var b = Start(_b);

        var result = await b.Maintenance.SyncAsync();

        Assert.Equal(SyncOutcome.Unrelated, result.Record!.Outcome);
        Assert.Contains("tacit sync --use-local", result.Report);
        Assert.Equal(remoteHead, Head(Remote, TestHome.Git(_a.DefaultMemory, "symbolic-ref", "--short", "HEAD").Trim()));
        var head = Head(_b.DefaultMemory);
        await b.Tools.WriteRecentAsync(Activity("历史不相关时写入照常在本机提交"));
        await b.SettledAsync();
        Assert.NotEqual(head, Head(_b.DefaultMemory));
        Assert.Equal(remoteHead, Head(Remote, TestHome.Git(_a.DefaultMemory, "symbolic-ref", "--short", "HEAD").Trim()));
        Assert.True((await Status(b)).Sync!.Last!.Paused);
    }

    [Fact]
    public async Task An_unreachable_remote_fails_the_sync_without_stopping_writes_and_the_commits_wait_to_be_pushed()
    {
        var a = Open(_a, remote: Path.Combine(_root, "missing.git"));

        await a.Tools.WriteRecentAsync(Activity("远端不可达时也能写入"));
        await a.SettledAsync();
        var result = await a.Maintenance.SyncAsync();

        Assert.Equal(SyncOutcome.Failed, result.Record!.Outcome);
        Assert.Contains("远端不可达时也能写入", Activities(_a));
        var status = await Status(a);
        Assert.Equal(SyncOutcome.Failed, status.Sync!.Last!.Outcome);
        Assert.Equal(
            int.Parse(TestHome.Git(_a.DefaultMemory, "rev-list", "--count", "HEAD").Trim(), System.Globalization.CultureInfo.InvariantCulture),
            status.Sync.Unpushed);
    }

    [Fact]
    public async Task Without_a_remote_the_sync_is_off_and_nothing_is_recorded()
    {
        _a.InitReady();
        var a = Start(_a);

        var result = await a.Maintenance.SyncAsync();

        Assert.False(result.Enabled);
        Assert.Contains("sync.remote", result.Report);
        Assert.Null((await Status(a)).Sync!.Remote);
        Assert.False(File.Exists(Path.Combine(_a.TacitHome, "sync.json")));
    }

    private static string Branch(TestHome home) => TestHome.Git(home.DefaultMemory, "symbolic-ref", "--short", "HEAD").Trim();

    /// <summary>Takes the remote away, as a network that is down does, until <see cref="Reconnect"/>.</summary>
    private void Disconnect() => Directory.Move(Remote, Remote + ".away");

    private void Reconnect() => Directory.Move(Remote + ".away", Remote);

    /// <summary>Commits <paramref name="text"/> as <paramref name="file"/> by hand, as the user or an agent outside the service does.</summary>
    private static void CommitByHand(TestHome home, string file, string text)
    {
        File.WriteAllText(Path.Combine(home.DefaultMemory, file), text.ReplaceLineEndings("\n"));
        TestHome.Git(home.DefaultMemory, "add", file);
        TestHome.Git(home.DefaultMemory, "commit", "--quiet", "-m", $"edit {file} by hand");
    }

    /// <summary>Runs git in <paramref name="directory"/> and returns its exit code, for the commands expected to fail.</summary>
    private static int GitExitCode(string directory, params string[] args)
    {
        var start = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in (string[])["-c", "user.name=tacit", "-c", "user.email=tacit@localhost", .. args])
            start.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(start)!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode;
    }

    private static WritePersonaRequest Preference(string text) => new() { Op = "add", Section = "preferences", Text = text, Source = text };

    private const string Persona = "## Red lines\n\n## Working rules\n\n## Preferences\n- 回答先给结论\n\n## Profile\n";

    /// <summary>Both machines start from the same history on the remote.</summary>
    private async Task<(MemoryService A, MemoryService B)> SharedAsync(int? pullMinutes = null)
    {
        var a = Open(_a, pullMinutes: pullMinutes);
        var b = Open(_b, pullMinutes: pullMinutes);
        await a.Maintenance.SyncAsync();
        await b.Maintenance.SyncAsync();
        return (a, b);
    }

    [Fact]
    public async Task A_write_pulls_first_so_it_lands_after_what_the_other_machine_pushed()
    {
        var (a, b) = await SharedAsync();

        await a.Tools.WriteRecentAsync(Activity("A 先写的一条"));
        await a.SettledAsync();
        await b.Tools.WriteRecentAsync(Activity("B 后写的一条"));
        await b.SettledAsync();

        var activities = Activities(_b);
        Assert.True(activities.IndexOf("A 先写的一条", StringComparison.Ordinal) < activities.IndexOf("B 后写的一条", StringComparison.Ordinal));
        Assert.Equal(Head(_b.DefaultMemory), Head(Remote, Branch(_b)));
        Assert.Equal("0", TestHome.Git(_b.DefaultMemory, "rev-list", "--count", "--merges", "HEAD").Trim());
    }

    [Fact]
    public async Task The_timed_pull_brings_the_other_machines_persona_into_the_resident_block()
    {
        var (a, b) = await SharedAsync(pullMinutes: 30);
        _b.Bind();

        await a.Tools.WritePersonaAsync(Preference("代码注释用英文写"));
        await a.SettledAsync();
        _b.Clock.Elapse(TimeSpan.FromMinutes(30));
        await b.SettledAsync();

        Assert.Contains("代码注释用英文写", File.ReadAllText(Path.Combine(_b.UserHome, ".omp", "agent", "AGENTS.md")));
    }

    [Fact]
    public async Task Commits_made_while_the_remote_is_down_are_pushed_by_the_next_sync()
    {
        var (a, _) = await SharedAsync();
        Disconnect();

        var written = await a.Tools.WriteRecentAsync(Activity("断网时写下的一条"));
        await a.SettledAsync();
        Assert.False(written.IsError, written.Text);
        Assert.Equal(SyncOutcome.Failed, (await Status(a)).Sync!.Last!.Outcome);
        Assert.Equal(1, (await Status(a)).Sync!.Unpushed);

        Reconnect();
        await a.Maintenance.SyncAsync();

        Assert.Equal(Head(_a.DefaultMemory), Head(Remote, Branch(_a)));
        Assert.Equal(0, (await Status(a)).Sync!.Unpushed);
    }

    [Fact]
    public async Task Activities_both_machines_add_on_the_same_day_are_merged_and_both_kept()
    {
        var (a, b) = await SharedAsync();
        Disconnect();
        await a.Tools.WriteRecentAsync(Activity("A 离线时写的活动"));
        await b.Tools.WriteRecentAsync(Activity("B 离线时写的活动"));
        await a.SettledAsync();
        await b.SettledAsync();
        Reconnect();

        Assert.Equal(SyncOutcome.Synced, (await a.Maintenance.SyncAsync()).Record!.Outcome);
        Assert.Equal(SyncOutcome.Synced, (await b.Maintenance.SyncAsync()).Record!.Outcome);
        await a.Maintenance.SyncAsync();

        foreach (var home in new[] { _a, _b })
        {
            var activities = Activities(home);
            Assert.Contains("A 离线时写的活动", activities);
            Assert.Contains("B 离线时写的活动", activities);
            Assert.Single(activities.Split('\n'), line => line == "## 2026-11-02");
        }
        Assert.Equal(Head(_a.DefaultMemory), Head(_b.DefaultMemory));
    }

    [Fact]
    public async Task Occurrences_both_machines_add_under_one_candidate_are_merged_and_both_kept()
    {
        var (a, b) = await SharedAsync();
        const string candidate = "## Candidates\n### 回答先给结论\nbelongs to: Preferences\n- 2026-11-01 quote「先说结论」\n";
        CommitByHand(_a, "candidates.md", candidate + "\n## Rejection list\n");
        await a.Maintenance.SyncAsync();
        await b.Maintenance.SyncAsync();

        CommitByHand(_a, "candidates.md", candidate + "- 2026-11-02 quote「A：结论放前面」\n\n## Rejection list\n");
        CommitByHand(_b, "candidates.md", candidate + "- 2026-11-02 quote「B：别绕弯子」\n\n## Rejection list\n");
        await a.Maintenance.SyncAsync();
        var result = await b.Maintenance.SyncAsync();

        Assert.Equal(SyncOutcome.Synced, result.Record!.Outcome);
        var merged = File.ReadAllText(Path.Combine(_b.DefaultMemory, "candidates.md"));
        Assert.Contains("A：结论放前面", merged);
        Assert.Contains("B：别绕弯子", merged);
    }

    [Fact]
    public async Task Both_machines_changing_one_line_pause_the_sync_until_the_conflict_is_resolved()
    {
        var (a, b) = await SharedAsync();
        CommitByHand(_a, "persona.md", Persona);
        await a.Maintenance.SyncAsync();
        await b.Maintenance.SyncAsync();
        CommitByHand(_a, "persona.md", Persona.Replace("回答先给结论", "回答先给结论，再给理由"));
        CommitByHand(_b, "persona.md", Persona.Replace("回答先给结论", "回答控制在三段以内"));
        await a.Maintenance.SyncAsync();
        var localHead = Head(_b.DefaultMemory);

        var conflict = await b.Maintenance.SyncAsync();

        Assert.Equal(SyncOutcome.Conflict, conflict.Record!.Outcome);
        Assert.Contains("persona.md", conflict.Report);
        Assert.Equal(localHead, Head(_b.DefaultMemory));
        Assert.Contains("回答控制在三段以内", File.ReadAllText(Path.Combine(_b.DefaultMemory, "persona.md")));
        var report = await b.Tools.ConsolidationReportAsync();
        Assert.Contains("### Sync conflicts\n- This machine and the remote both changed persona.md", report);
        Assert.Contains($"pull --rebase tacit {Branch(_b)}", report);

        // Writes go on locally while the sync waits, and a restart keeps it waiting.
        var remoteHead = Head(Remote, Branch(_b));
        await b.Tools.WriteRecentAsync(Activity("冲突期间照常写入"));
        await b.SettledAsync();
        Assert.Equal(remoteHead, Head(Remote, Branch(_b)));
        b.Dispose();
        b = Start(_b);
        await b.Tools.WriteRecentAsync(Activity("重启后仍然暂停"));
        await b.SettledAsync();
        Assert.Equal(remoteHead, Head(Remote, Branch(_b)));
        Assert.True((await Status(b)).Sync!.Last!.Paused);

        // The agent resolves it in the repository as the report says, then resumes the sync.
        Assert.NotEqual(0, GitExitCode(_b.DefaultMemory, "pull", "--rebase", "tacit", Branch(_b)));
        File.WriteAllText(Path.Combine(_b.DefaultMemory, "persona.md"), Persona.Replace("回答先给结论", "回答先给结论，再给理由\n- 回答控制在三段以内"));
        TestHome.Git(_b.DefaultMemory, "add", "persona.md");
        TestHome.Git(_b.DefaultMemory, "-c", "core.editor=true", "rebase", "--continue");
        var resumed = await b.Maintenance.SyncAsync();

        Assert.Equal(SyncOutcome.Synced, resumed.Record!.Outcome);
        Assert.Equal(Head(_b.DefaultMemory), Head(Remote, Branch(_b)));
        Assert.Contains("### Sync conflicts\nNone.", await b.Tools.ConsolidationReportAsync());
    }

    [Fact]
    public async Task Uncommitted_edits_in_the_way_hold_the_sync_back_and_are_left_untouched()
    {
        var (a, b) = await SharedAsync();
        CommitByHand(_a, "persona.md", Persona);
        await a.Maintenance.SyncAsync();
        var edited = Persona.Replace("回答先给结论", "手改还没提交");
        File.WriteAllText(Path.Combine(_b.DefaultMemory, "persona.md"), edited);
        var head = Head(_b.DefaultMemory);

        var result = await b.Maintenance.SyncAsync();

        Assert.Equal(SyncOutcome.HandEdits, result.Record!.Outcome);
        Assert.Contains("persona.md", result.Report);
        Assert.Equal(edited, File.ReadAllText(Path.Combine(_b.DefaultMemory, "persona.md")));
        Assert.Equal(head, Head(_b.DefaultMemory));
        Assert.False((await Status(b)).Sync!.Last!.Paused);
    }

    [Fact]
    public async Task A_pull_by_hand_on_a_machine_that_converts_line_endings_keeps_the_files_in_lf()
    {
        var (a, b) = await SharedAsync();
        TestHome.Git(_b.DefaultMemory, "config", "core.autocrlf", "true");
        await a.Tools.WriteRecentAsync(Activity("换行不应被改成 CRLF"));
        await a.SettledAsync();

        TestHome.Git(_b.DefaultMemory, "pull", "--quiet", "--ff-only", "tacit", Branch(_b));

        Assert.DoesNotContain("\r\n", Activities(_b));
        Assert.Equal("", TestHome.Git(_b.DefaultMemory, "status", "--porcelain"));
    }

    [Fact]
    public async Task A_restore_pauses_the_sync_until_a_side_is_chosen()
    {
        var (a, b) = await SharedAsync();
        var backup = await a.Maintenance.BackupAsync(BackupTarget.Local);
        await b.Tools.WriteRecentAsync(Activity("B 在恢复前写的"));
        await b.SettledAsync();

        var restored = await a.Maintenance.RestoreAsync(backup.Location);

        Assert.Contains("tacit sync --use-local", restored.Report);
        var head = Head(_a.DefaultMemory);
        var waiting = await a.Maintenance.SyncAsync();
        Assert.Equal(SyncOutcome.RestorePaused, waiting.Record!.Outcome);
        await a.Tools.WriteRecentAsync(Activity("A 恢复后写的"));
        await a.SettledAsync();
        Assert.DoesNotContain("B 在恢复前写的", Activities(_a));
        Assert.NotEqual(head, Head(_a.DefaultMemory));
        Assert.Equal(Head(_b.DefaultMemory), Head(Remote, Branch(_b)));
        Assert.Contains("tacit sync --use-remote", string.Join('\n', (await Status(a)).Sync!.Lines));
    }

    [Fact]
    public async Task Choosing_this_machine_replaces_the_remote_branch_and_resumes_the_sync()
    {
        var (a, b) = await SharedAsync();
        await b.Tools.WriteRecentAsync(Activity("只在远端的一条"));
        await b.SettledAsync();
        var backup = await a.Maintenance.BackupAsync(BackupTarget.Local);
        await a.Maintenance.RestoreAsync(backup.Location);

        var preview = await a.Maintenance.PreviewSyncSideAsync(SyncSide.Local);
        var chosen = await a.Maintenance.ChooseSyncSideAsync(new SyncSideChoice(SyncSide.Local, preview.RemoteHead));

        Assert.Equal(1, preview.RemoteOnly);
        Assert.Equal(SyncOutcome.Synced, chosen.Record!.Outcome);
        Assert.Equal(Head(_a.DefaultMemory), Head(Remote, Branch(_a)));
        Assert.False((await Status(a)).Sync!.Last!.Paused);
        await a.Tools.WriteRecentAsync(Activity("选边后自动同步恢复"));
        await a.SettledAsync();
        Assert.Equal(Head(_a.DefaultMemory), Head(Remote, Branch(_a)));
    }

    [Fact]
    public async Task Choosing_the_remote_resets_this_machine_after_a_backup_and_rebuilds_the_index()
    {
        var (a, b) = await ConflictAsync();

        var preview = await b.Maintenance.PreviewSyncSideAsync(SyncSide.Remote);
        var chosen = await b.Maintenance.ChooseSyncSideAsync(new SyncSideChoice(SyncSide.Remote, preview.RemoteHead));

        Assert.Equal(1, preview.LocalOnly);
        Assert.Equal(SyncOutcome.Synced, chosen.Record!.Outcome);
        Assert.Equal(Head(Remote, Branch(_b)), Head(_b.DefaultMemory));
        Assert.Single(Directory.GetFiles(Path.Combine(_b.TacitHome, "backups"), "manual-*.zip"));
        Assert.Contains("回答先给结论，再给理由", File.ReadAllText(Path.Combine(_b.DefaultMemory, "persona.md")));
        var found = await b.Tools.RecallAsync(new RecallRequest { Query = "再给理由", Kind = ToolValues.RecallKinds.Persona });
        Assert.Contains("回答先给结论，再给理由", found.Text);
    }

    [Fact]
    public async Task A_side_can_be_chosen_in_the_middle_of_an_unfinished_rebase()
    {
        var (a, b) = await ConflictAsync();
        var localHead = Head(_b.DefaultMemory);
        // The agent began resolving the conflict by hand and stopped in the middle of the rebase.
        Assert.NotEqual(0, GitExitCode(_b.DefaultMemory, "pull", "--rebase", "tacit", Branch(_b)));

        var preview = await b.Maintenance.PreviewSyncSideAsync(SyncSide.Local);
        var chosen = await b.Maintenance.ChooseSyncSideAsync(new SyncSideChoice(SyncSide.Local, preview.RemoteHead));

        Assert.Equal(SyncOutcome.Synced, chosen.Record!.Outcome);
        Assert.Equal(localHead, Head(_b.DefaultMemory));
        Assert.Equal(localHead, Head(Remote, Branch(_b)));
        Assert.Equal("", TestHome.Git(_b.DefaultMemory, "status", "--porcelain"));
    }

    [Fact]
    public async Task A_side_chosen_on_a_preview_the_remote_has_moved_past_is_refused_and_changes_nothing()
    {
        var (a, b) = await ConflictAsync();
        var preview = await b.Maintenance.PreviewSyncSideAsync(SyncSide.Local);
        await a.Tools.WriteRecentAsync(Activity("预览之后 A 又推了一条"));
        await a.SettledAsync();
        var remoteHead = Head(Remote, Branch(_b));

        var refused = await Assert.ThrowsAsync<TacitException>(
            () => b.Maintenance.ChooseSyncSideAsync(new SyncSideChoice(SyncSide.Local, preview.RemoteHead)));

        Assert.Equal(TacitError.RemoteMoved, refused.Error);
        Assert.Equal(remoteHead, Head(Remote, Branch(_b)));
        Assert.Equal(SyncOutcome.Conflict, (await Status(b)).Sync!.Last!.Outcome);
    }

    [Fact]
    public async Task Reading_the_consolidation_report_pulls_first()
    {
        var (a, b) = await SharedAsync();
        await a.Tools.WriteRecentAsync(Activity("A 写下、B 整理前应已拉到"));
        await a.SettledAsync();

        await b.Tools.ConsolidationReportAsync();

        Assert.Contains("A 写下、B 整理前应已拉到", Activities(_b));
        Assert.Equal(Head(_a.DefaultMemory), Head(_b.DefaultMemory));
    }

    /// <summary>Two shared machines that changed the same persona line, the second one paused on the conflict.</summary>
    private async Task<(MemoryService A, MemoryService B)> ConflictAsync()
    {
        var (a, b) = await SharedAsync();
        CommitByHand(_a, "persona.md", Persona);
        await a.Maintenance.SyncAsync();
        await b.Maintenance.SyncAsync();
        CommitByHand(_a, "persona.md", Persona.Replace("回答先给结论", "回答先给结论，再给理由"));
        CommitByHand(_b, "persona.md", Persona.Replace("回答先给结论", "回答控制在三段以内"));
        await a.Maintenance.SyncAsync();
        Assert.Equal(SyncOutcome.Conflict, (await b.Maintenance.SyncAsync()).Record!.Outcome);
        return (a, b);
    }
}
