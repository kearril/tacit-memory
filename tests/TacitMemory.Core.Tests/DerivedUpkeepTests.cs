using System.Collections.Concurrent;
using TacitMemory.Core.Logging;
using TacitMemory.Core.Maintenance;
using TacitMemory.Core.Upkeep;

namespace TacitMemory.Core.Tests;

/// <summary>
/// How the service keeps the derived index and the server instructions in step with the memory files: the change is
/// told to the service, the test clock decides when a refresh runs, and the service is settled before each check. The
/// resident blocks are tested with the resident document.
/// </summary>
public sealed class DerivedUpkeepTests : IDisposable
{
    private readonly TestHome _home = TestHome.WithManualSources();
    private readonly ConcurrentQueue<string> _notices = new();

    public DerivedUpkeepTests() => _home.InitReady();

    public void Dispose() => _home.Dispose();

    private string Memory => _home.DefaultMemory;

    private MemoryService Open() => _home.Tacit().OpenService(_notices.Enqueue, NullLoggerFactory.Instance);

    private const string Page = "tacit-memory/db-upgrade";

    private string PageFile => Path.Combine(Memory, "pages", "tacit-memory", "db-upgrade.md");

    private static WritePageRequest Create() => new()
    {
        Op = "create",
        Type = ToolValues.PageTypes.Incident,
        Page = Page,
        Title = "数据库升级后扩展丢失",
        Aliases = ["全文检索插件失效", "升级后搜索不到中文"],
        Sections = [new("现象", "升级后中文搜索没有结果。"), new("根因", "PGroonga 扩展没有随升级重装。")],
    };

    private bool IndexSyncReported() => _notices.Any(notice => notice.Contains("同步检索索引失败", StringComparison.Ordinal));

    [Fact]
    public async Task An_index_sync_that_meets_a_held_file_is_retried_five_times_then_reported_once()
    {
        // Written by a service that is gone, so the one under test sees no watcher event for it.
        using (var first = Open())
            Assert.False((await first.WritePageAsync(Create())).IsError);
        using var service = Open();

        using (new FileStream(PageFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            _home.Sources!.MemoryChanged($"pages/{Page}.md");
            await service.SettledAsync();
            for (var retry = 1; retry <= DerivedUpkeep.RetryLimit; retry++)
            {
                Assert.False(IndexSyncReported());
                _home.Clock.Elapse(DerivedUpkeep.Debounce);
                await service.SettledAsync();
            }
        }

        Assert.Single(_notices, notice => notice.Contains("同步检索索引失败", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_change_during_the_retries_starts_the_count_again()
    {
        using (var first = Open())
            Assert.False((await first.WritePageAsync(Create())).IsError);
        using var service = Open();

        using (new FileStream(PageFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            _home.Sources!.MemoryChanged($"pages/{Page}.md");
            await service.SettledAsync();
            for (var retry = 1; retry < DerivedUpkeep.RetryLimit; retry++)
            {
                _home.Clock.Elapse(DerivedUpkeep.Debounce);
                await service.SettledAsync();
            }

            _home.Sources!.MemoryChanged($"pages/{Page}.md");
            await service.SettledAsync();
            for (var retry = 1; retry <= DerivedUpkeep.RetryLimit; retry++)
            {
                Assert.False(IndexSyncReported());
                _home.Clock.Elapse(DerivedUpkeep.Debounce);
                await service.SettledAsync();
            }
        }

        Assert.Single(_notices, notice => notice.Contains("同步检索索引失败", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_index_sync_that_meets_a_held_file_catches_up_once_the_file_is_let_go()
    {
        using (var first = Open())
            Assert.False((await first.WritePageAsync(Create())).IsError);
        using var service = Open();
        var text = File.ReadAllText(PageFile).Replace("升级后中文搜索没有结果。", "升级后中文搜索没有结果，Zhparser 也失效。", StringComparison.Ordinal);

        using (var held = new FileStream(PageFile, FileMode.Truncate, FileAccess.Write, FileShare.None))
        {
            using (var writer = new StreamWriter(held, leaveOpen: true))
                writer.Write(text);
            _home.Sources!.MemoryChanged($"pages/{Page}.md");
            await service.SettledAsync();
        }

        _home.Clock.Elapse(DerivedUpkeep.Debounce);
        await service.SettledAsync();

        Assert.False(IndexSyncReported());
        var found = await service.RecallAsync(new RecallRequest { Query = "Zhparser" });
        Assert.Contains(Page, found.Text);
    }

    [Fact]
    public async Task After_an_index_sync_gives_up_the_next_search_finds_the_file_as_it_is_without_another_change()
    {
        using (var first = Open())
            Assert.False((await first.WritePageAsync(Create())).IsError);
        using var service = Open();
        var text = File.ReadAllText(PageFile).Replace("升级后中文搜索没有结果。", "升级后中文搜索没有结果，Zhparser 也失效。", StringComparison.Ordinal);

        using (var held = new FileStream(PageFile, FileMode.Truncate, FileAccess.Write, FileShare.None))
        {
            using (var writer = new StreamWriter(held, leaveOpen: true))
                writer.Write(text);
            _home.Sources!.MemoryChanged($"pages/{Page}.md");
            await service.SettledAsync();
            for (var retry = 1; retry <= DerivedUpkeep.RetryLimit; retry++)
            {
                _home.Clock.Elapse(DerivedUpkeep.Debounce);
                await service.SettledAsync();
            }
        }
        Assert.True(IndexSyncReported());

        var found = await service.RecallAsync(new RecallRequest { Query = "Zhparser" });

        Assert.Contains(Page, found.Text);
    }

    [Fact]
    public async Task A_full_sync_that_meets_the_held_file_after_the_give_up_keeps_the_next_search_catching_up()
    {
        using (var first = Open())
            Assert.False((await first.WritePageAsync(Create())).IsError);
        using var service = Open();
        var text = File.ReadAllText(PageFile).Replace("升级后中文搜索没有结果。", "升级后中文搜索没有结果，Zhparser 也失效。", StringComparison.Ordinal);

        using (var held = new FileStream(PageFile, FileMode.Truncate, FileAccess.Write, FileShare.None))
        {
            using (var writer = new StreamWriter(held, leaveOpen: true))
                writer.Write(text);
            _home.Sources!.MemoryChanged($"pages/{Page}.md");
            await service.SettledAsync();
            for (var retry = 1; retry <= DerivedUpkeep.RetryLimit; retry++)
            {
                _home.Clock.Elapse(DerivedUpkeep.Debounce);
                await service.SettledAsync();
            }
            // The consolidation syncs every file too, and meets the held one.
            await Record.ExceptionAsync(() => service.ConsolidationReportAsync());
        }

        var found = await service.RecallAsync(new RecallRequest { Query = "Zhparser" });

        Assert.Contains(Page, found.Text);
    }

    [Fact]
    public async Task An_activity_is_written_while_the_persona_file_is_held()
    {
        using var service = Open();
        Assert.False((await service.WriteRecentAsync(new WriteRecentRequest { Text = "核对恢复演练的备份清单" })).IsError);

        using (new FileStream(Path.Combine(Memory, "persona.md"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var written = await service.WriteRecentAsync(new WriteRecentRequest { Text = "调整日志轮转天数" });

            Assert.False(written.IsError, written.Text);
        }
    }

    [Fact]
    public async Task A_page_changed_without_a_notice_reads_as_it_is_in_its_workstream_brief()
    {
        using var service = Open();
        Assert.False((await service.WritePageAsync(Create())).IsError);
        File.WriteAllText(PageFile, File.ReadAllText(PageFile).Replace("title: 数据库升级后扩展丢失", "title: 数据库升级后两个扩展都丢失", StringComparison.Ordinal));

        var brief = await service.RecallAsync(new RecallRequest { Workstream = "tacit-memory" });

        Assert.Contains("数据库升级后两个扩展都丢失", brief.Text);
    }

    [Fact]
    public async Task A_prompt_reload_that_meets_a_held_file_is_retried_five_times_then_reported_once()
    {
        var rules = Path.Combine(_home.TacitHome, "prompts", "rules.md");
        File.WriteAllText(rules, "- 用户纠正或打断 Agent\n");
        using var service = Open();

        using (new FileStream(rules, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            _home.Sources!.PromptsChanged();
            await service.SettledAsync();
            for (var retry = 1; retry <= DerivedUpkeep.RetryLimit; retry++)
            {
                Assert.DoesNotContain(_notices, notice => notice.Contains("提示词文档的修改未生效", StringComparison.Ordinal));
                _home.Clock.Elapse(DerivedUpkeep.Debounce);
                await service.SettledAsync();
            }
        }

        Assert.Single(_notices, notice => notice.Contains("提示词文档的修改未生效", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_render_scheduled_before_a_maintenance_operation_runs_before_it()
    {
        File.WriteAllText(Path.Combine(Memory, "persona.md"), "## 档案\n- 系统：Windows 11\n");
        _home.Bind();
        using var service = Open();
        var agents = Path.Combine(_home.UserHome, ".omp", "agent", "AGENTS.md");
        File.WriteAllText(Path.Combine(Memory, "persona.md"), "## 档案\n- 系统：Windows 12\n");
        _home.Sources!.MemoryChanged("persona.md");

        // The debounce has not passed on the test clock: only the maintenance operation can have run the render.
        await service.ReindexAsync();

        Assert.Contains("- 系统：Windows 12\n", File.ReadAllText(agents));
    }

    [Fact]
    public async Task Lost_events_refresh_the_resident_blocks_the_index_and_the_instructions()
    {
        _home.Bind();
        using var service = Open();
        var agents = Path.Combine(_home.UserHome, ".omp", "agent", "AGENTS.md");
        File.WriteAllText(Path.Combine(Memory, "persona.md"), "## 档案\n- 系统：Windows 11\n");
        File.WriteAllText(
            Path.Combine(Memory, "recent", $"{TestHome.Today:yyyy-MM}.md"),
            $"## {TestHome.Today:yyyy-MM-dd}\n- 导出脚本改用按月分文件导出，因为单个文件太大。\n");

        _home.Sources!.EventsLost();
        await service.SettledAsync();

        Assert.Contains("- 系统：Windows 11\n", File.ReadAllText(agents));
        Assert.Contains("改用按月分文件导出", await service.ServerInstructionsAsync());
        Assert.Contains("按月分文件导出", (await service.RecallAsync(new RecallRequest { Query = "按月分文件", Kind = "activities" })).Text);
    }

    [Fact]
    public async Task A_change_the_upkeep_fails_to_take_is_reported_and_does_not_reach_the_watcher()
    {
        var notices = new ConcurrentQueue<string>();
        var options = _home.Options() with { Time = new RefusingTime() };
        var log = new ServiceLog(NullLogger.Instance, MemoryLanguage.Zh, notices.Enqueue);
        using var gate = new MaintenanceGate();
        using var upkeep = new DerivedUpkeep(options, log, gate, () => throw new InvalidOperationException("no runtime"));
        using var watch = new FileSourceWatch(Memory, _home.TacitHome, upkeep, log);

        File.WriteAllText(Path.Combine(Memory, "persona.md"), "## 档案\n- 系统：Windows 11\n");

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!notices.Any(notice => notice.Contains("timer refused", StringComparison.Ordinal)))
        {
            Assert.True(DateTime.UtcNow < deadline, "the watcher did not report the failure within 10 s");
            await Task.Delay(50);
        }
        Assert.StartsWith("后台任务失败：", notices.First(notice => notice.Contains("timer refused", StringComparison.Ordinal)));
    }

    /// <summary>A clock whose timers refuse to be set: a change the upkeep hears fails when it schedules the refresh.</summary>
    private sealed class RefusingTime : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new Refusing();

        private sealed class Refusing : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => throw new InvalidOperationException("timer refused");

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task A_changed_activity_file_shows_in_the_next_server_instructions()
    {
        using var service = Open();
        Assert.DoesNotContain("改用按月分文件导出", await service.ServerInstructionsAsync());

        File.WriteAllText(
            Path.Combine(Memory, "recent", $"{TestHome.Today:yyyy-MM}.md"),
            $"## {TestHome.Today:yyyy-MM-dd}\n- 导出脚本改用按月分文件导出，因为单个文件太大。\n");
        _home.Sources!.MemoryChanged($"recent/{TestHome.Today:yyyy-MM}.md");

        Assert.Contains("改用按月分文件导出", await service.ServerInstructionsAsync());
    }
}
