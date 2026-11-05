using System.Text.RegularExpressions;

namespace TacitMemory.Core.Tests;

public sealed partial class RecentSummaryInstructionTests : IDisposable
{
    private readonly TestHome _home = new();
    private readonly List<MemoryService> _services = new();

    public RecentSummaryInstructionTests() => _home.InitReady();

    public void Dispose()
    {
        foreach (var service in _services)
            service.Dispose();
        _home.Dispose();
    }

    private string Memory => _home.DefaultMemory;

    private string MonthFile => Path.Combine(Memory, "recent", "2026-11.md");

    private string RecentPath => Path.Combine(Memory, "recent.md");

    private MemoryService Open()
    {
        var service = _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);
        _services.Add(service);
        return service;
    }

    [GeneratedRegex(@"\[[0-9a-f]{6}")]
    private static partial Regex ShortCode();

    private static async Task Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "condition not met within 10 s");
            await Task.Delay(50);
        }
    }

    private static async Task Eventually(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "condition not met within 10 s");
            await Task.Delay(50);
        }
    }

    private void WriteMonth(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MonthFile)!);
        File.WriteAllText(MonthFile, text);
    }

    [Fact]
    public async Task Recent_activities_and_mainline_appear_in_the_instructions_without_short_codes()
    {
        Directory.CreateDirectory(Memory);
        File.WriteAllText(RecentPath, "## 近期主线\n- 主线一\n- 主线二\n");
        WriteMonth("## 2026-11-01\n- 昨天的事\n## 2026-11-02\n- [tacit-memory] 今天的事\n");

        var instructions = await Open().ServerInstructionsAsync();

        Assert.Contains("## Recent", instructions);
        Assert.Contains("- 主线一", instructions);
        Assert.Contains("- 2026-11-02 [tacit-memory] 今天的事", instructions);
        Assert.Contains("- 2026-11-01 昨天的事", instructions);
        Assert.DoesNotMatch(ShortCode(), instructions);
    }

    [Fact]
    public async Task Only_the_seven_newest_activities_of_the_last_seven_days_are_listed()
    {
        var lines = Enumerable.Range(1, 9).Select(i => $"## 2026-11-02\n- 第{i}件事\n");
        WriteMonth("## 2026-10-20\n- 太早的事\n" + string.Concat(lines));

        var instructions = await Open().ServerInstructionsAsync();

        Assert.Contains("- 2026-11-02 第9件事", instructions);
        Assert.Contains("- 2026-11-02 第3件事", instructions);
        Assert.DoesNotContain("第2件事", instructions);
        Assert.DoesNotContain("太早的事", instructions);
    }

    [Fact]
    public async Task The_window_moves_forward_at_midnight_and_drops_the_oldest_day()
    {
        WriteMonth("## 2026-10-27\n- 七天前的事\n## 2026-10-28\n- 最早保留的事\n");
        var service = Open();

        var before = await service.ServerInstructionsAsync();
        Assert.Contains("- 2026-10-27 七天前的事", before);
        Assert.Contains("- 2026-10-28 最早保留的事", before);

        _home.Clock.Advance(TimeSpan.FromHours(24));

        var after = await service.ServerInstructionsAsync();
        Assert.DoesNotContain("七天前的事", after);
        Assert.Contains("- 2026-10-28 最早保留的事", after);
    }

    [Fact]
    public async Task The_recent_block_is_omitted_when_there_is_no_mainline_and_no_activity()
    {
        var instructions = await Open().ServerInstructionsAsync();

        Assert.DoesNotContain("\n## Recent\n", instructions);
        Assert.DoesNotContain("Activities from the last 7 days", instructions);
    }

    [Fact]
    public async Task Projects_list_the_page_directories_and_every_activity_project_once_sorted()
    {
        Directory.CreateDirectory(Path.Combine(Memory, "pages", "zeta"));
        Directory.CreateDirectory(Path.Combine(Memory, "pages", "_topics"));
        WriteMonth("## 2026-10-01\n- [alpha] 很久以前的事\n## 2026-11-02\n- [zeta] 今天的事\n");

        var instructions = await Open().ServerInstructionsAsync();

        Assert.EndsWith("## Projects\nalpha, zeta", instructions);
        Assert.DoesNotContain("_topics", instructions);
    }

    [Fact]
    public async Task Truncation_drops_the_oldest_activities_first_and_keeps_the_fixed_parts_whole()
    {
        var padding = new string('长', 400);
        WriteMonth(string.Concat(Enumerable.Range(1, 7).Select(i => $"## 2026-11-02\n- {i}{padding}\n")));

        var instructions = await Open().ServerInstructionsAsync();

        Assert.True(instructions.Length <= 4000, $"length {instructions.Length}");
        Assert.Contains("## Using memory", instructions);
        Assert.Contains("## Writing activities and mainline", instructions);
        Assert.Contains("- 2026-11-02 7" + padding, instructions);
        Assert.DoesNotContain("- 2026-11-02 1" + padding, instructions);
    }

    [Fact]
    public async Task Editing_recent_md_or_the_activity_log_by_hand_changes_the_next_instructions()
    {
        var service = Open();
        Assert.DoesNotContain("手改的主线", await service.ServerInstructionsAsync());

        Directory.CreateDirectory(Memory);
        File.WriteAllText(RecentPath, "## 近期主线\n- 手改的主线\n");
        await Eventually(async () => (await service.ServerInstructionsAsync()).Contains("- 手改的主线", StringComparison.Ordinal));

        WriteMonth("## 2026-11-02\n- 手改的活动\n");
        await Eventually(async () => (await service.ServerInstructionsAsync()).Contains("手改的活动", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Writing_an_activity_through_the_service_updates_the_instructions_at_once()
    {
        var service = Open();
        Assert.DoesNotContain("刚写的活动", await service.ServerInstructionsAsync());

        var result = await service.WriteRecentAsync(new WriteRecentRequest { Op = "add_activity", Text = "刚写的活动" });

        Assert.False(result.IsError, result.Text);
        Assert.Contains("刚写的活动", await service.ServerInstructionsAsync());
    }
}
