using System.Text.RegularExpressions;

namespace TacitMemory.Core.Tests;

public sealed partial class RecentSummaryInstructionTests : IDisposable
{
    private readonly TestHome _home = TestHome.WithManualSources();
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

    private string RecentPath => Path.Combine(Memory, "workstreams.md");

    private MemoryService Open()
    {
        var service = _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);
        _services.Add(service);
        return service;
    }

    [GeneratedRegex(@"\[[0-9a-f]{6}")]
    private static partial Regex ShortCode();

    private void WriteMonth(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MonthFile)!);
        File.WriteAllText(MonthFile, text);
    }

    [Fact]
    public async Task Recent_activities_appear_in_a_recent_activity_block_without_short_codes_and_the_workstream_stays_out()
    {
        Directory.CreateDirectory(Memory);
        File.WriteAllText(RecentPath, "## Workstream\n### alpha\nBackground: 一号主线。\n### beta\nBackground: 二号主线。\n");
        WriteMonth("## 2026-11-01\n- 昨天的事\n## 2026-11-02\n- [tacit-memory] 今天的事\n");

        var instructions = await Open().Tools.ServerInstructionsAsync();

        Assert.Contains("<recent_activity days=\"7\">\n- 2026-11-02 [tacit-memory] 今天的事\n- 2026-11-01 昨天的事\n</recent_activity>", instructions);
        Assert.DoesNotContain("alpha", instructions);
        Assert.DoesNotContain("\n## Recent\n", instructions);
        Assert.DoesNotMatch(ShortCode(), instructions);
    }

    [Fact]
    public async Task Every_activity_of_the_last_seven_days_is_listed_newest_first()
    {
        var lines = Enumerable.Range(1, 9).Select(i => $"## 2026-11-02\n- 第{i}件事\n");
        WriteMonth("## 2026-10-20\n- 太早的事\n" + string.Concat(lines));

        var instructions = await Open().Tools.ServerInstructionsAsync();

        Assert.Equal(9, instructions.Split('\n').Count(line => line.StartsWith("- 2026-11-02 第", StringComparison.Ordinal)));
        Assert.Contains("- 2026-11-02 第9件事", instructions);
        Assert.Contains("- 2026-11-02 第2件事", instructions);
        Assert.DoesNotContain("太早的事", instructions);
    }

    [Fact]
    public async Task The_window_moves_forward_at_midnight_and_drops_the_oldest_day()
    {
        WriteMonth("## 2026-10-27\n- 七天前的事\n## 2026-10-28\n- 最早保留的事\n");
        var service = Open();

        var before = await service.Tools.ServerInstructionsAsync();
        Assert.Contains("- 2026-10-27 七天前的事", before);
        Assert.Contains("- 2026-10-28 最早保留的事", before);

        _home.Clock.Advance(TimeSpan.FromHours(24));

        var after = await service.Tools.ServerInstructionsAsync();
        Assert.DoesNotContain("七天前的事", after);
        Assert.Contains("- 2026-10-28 最早保留的事", after);
    }

    [Fact]
    public async Task The_recent_block_is_omitted_when_there_is_no_workstream_and_no_activity()
    {
        var instructions = await Open().Tools.ServerInstructionsAsync();

        Assert.DoesNotContain("\n## Recent\n", instructions);
        Assert.DoesNotContain("Activities from the last 7 days", instructions);
    }

    [Fact]
    public async Task The_workstream_list_is_not_in_the_instructions_even_when_workstreams_exist()
    {
        Directory.CreateDirectory(Path.Combine(Memory, "pages", "zeta"));
        WriteMonth("## 2026-11-02\n- [zeta] 今天的事\n");

        var instructions = await Open().Tools.ServerInstructionsAsync();

        Assert.Contains("- 2026-11-02 [zeta] 今天的事", instructions);
        Assert.DoesNotContain("## Workstreams", instructions);
    }

    [Fact]
    public async Task Truncation_drops_the_oldest_activities_first_and_keeps_the_fixed_parts_whole()
    {
        var padding = new string('长', 400);
        WriteMonth(string.Concat(Enumerable.Range(1, 7).Select(i => $"## 2026-11-02\n- {i}{padding}\n")));
        // A non-empty workstream keeps the consolidation reminder out, so the activities are the last part.
        File.WriteAllText(RecentPath, "## Workstream\n### alpha\nBackground: 一号主线。\n");

        var instructions = await Open().Tools.ServerInstructionsAsync();

        Assert.True(instructions.Length <= 4000, $"length {instructions.Length}");
        Assert.Contains("## Using memory", instructions);
        Assert.Contains("## Write\n", instructions);
        Assert.EndsWith("</recent_activity>", instructions);
        Assert.Contains("- 2026-11-02 7" + padding, instructions);
        Assert.DoesNotContain("- 2026-11-02 1" + padding, instructions);
    }

    [Fact]
    public async Task Editing_the_activity_log_by_hand_changes_the_next_instructions()
    {
        var service = Open();
        Assert.DoesNotContain("手改的活动", await service.Tools.ServerInstructionsAsync());

        Directory.CreateDirectory(Memory);
        WriteMonth("## 2026-11-02\n- 手改的活动\n");
        _home.Sources!.MemoryChanged("recent/2026-11.md");
        await service.SettledAsync();
        Assert.Contains("手改的活动", (await service.Tools.ServerInstructionsAsync()));
    }

    [Fact]
    public async Task Writing_an_activity_through_the_service_updates_the_instructions_at_once()
    {
        var service = Open();
        Assert.DoesNotContain("刚写的活动", await service.Tools.ServerInstructionsAsync());

        var result = await service.Tools.WriteRecentAsync(new WriteRecentRequest { Text = "刚写的活动" });

        Assert.False(result.IsError, result.Text);
        Assert.Contains("刚写的活动", await service.Tools.ServerInstructionsAsync());
    }
}
