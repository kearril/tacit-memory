using System.Collections.Concurrent;
using TacitMemory.Core.Upkeep;

namespace TacitMemory.Core.Tests;

/// <summary>
/// How the service keeps the derived index and the server instructions in step with the memory files: the change is
/// told to the service, the test clock decides when a refresh runs, and the service is settled before each check. The
/// resident blocks are tested with the resident document.
/// </summary>
public sealed class DerivedUpkeepTests : IDisposable
{
    private readonly TestHome _home = new();
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
            service.MemoryChanged($"pages/{Page}.md");
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
            service.MemoryChanged($"pages/{Page}.md");
            await service.SettledAsync();
        }

        _home.Clock.Elapse(DerivedUpkeep.Debounce);
        await service.SettledAsync();

        Assert.False(IndexSyncReported());
        var found = await service.RecallAsync(new RecallRequest { Query = "Zhparser" });
        Assert.Contains(Page, found.Text);
    }

    [Fact]
    public async Task A_changed_activity_file_shows_in_the_next_server_instructions()
    {
        using var service = Open();
        Assert.DoesNotContain("改用按月分文件导出", await service.ServerInstructionsAsync());

        File.WriteAllText(
            Path.Combine(Memory, "recent", $"{TestHome.Today:yyyy-MM}.md"),
            $"## {TestHome.Today:yyyy-MM-dd}\n- 导出脚本改用按月分文件导出，因为单个文件太大。\n");
        service.MemoryChanged($"recent/{TestHome.Today:yyyy-MM}.md");

        Assert.Contains("改用按月分文件导出", await service.ServerInstructionsAsync());
    }
}
