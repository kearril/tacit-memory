using System.Text.RegularExpressions;

namespace TacitMemory.Core.Tests;

/// <summary>
/// The usage statistics through the facade: a search counts the units it shows, a whole-page read counts each section once,
/// the last day moves forward with the clock, and <c>reindex</c> resets every count.
/// </summary>
public sealed class UsageStatsTests : IDisposable
{
    /// <summary>A bound on each await: a wait that never ends fails here instead of hanging.</summary>
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);

    private readonly TestHome _home = new();
    private MemoryService? _service;

    public UsageStatsTests() => _home.InitReady();

    public void Dispose()
    {
        _service?.Dispose();
        _home.Dispose();
    }

    private MemoryService Service => _service ??= _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

    /// <summary>Writes a page file by hand, so it is not committed and is dated today.</summary>
    private void Hand(string slug, string title, string body, string aliases = "[别名一, 别名二]")
    {
        var path = Path.Combine(_home.DefaultMemory, "pages", "tacit-memory", slug + ".md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $"---\ntitle: {title}\naliases: {aliases}\n---\n\n{body.ReplaceLineEndings("\n")}\n");
    }

    private Task<BriefPage> Usage(string page) => Service.PageUsageAsync(page);

    [Fact]
    public async Task Reading_a_whole_page_counts_each_section_once_and_not_the_title()
    {
        Hand("backup", "备份", "## 要点\n每日备份到 zip\n\n## 恢复\n从 zip 恢复\n");

        await Service.Tools.RecallAsync(new RecallRequest { Page = "tacit-memory/backup" });

        var usage = await Usage("tacit-memory/backup");
        Assert.Equal(2, usage.Uses);
        Assert.Equal(TestHome.Today.Written(), usage.LastUse);
    }

    [Fact]
    public async Task A_search_counts_the_section_it_shows_once_per_recall()
    {
        // The title shares no word with the query, so only the section is returned.
        Hand("backup", "脚本", "## 要点\n每日备份到 zip\n\n## 恢复\n从 zip 恢复\n");

        await Service.Tools.RecallAsync(new RecallRequest { Query = "每日备份到" });
        await Service.Tools.RecallAsync(new RecallRequest { Query = "每日备份到" });

        Assert.Equal(2, (await Usage("tacit-memory/backup")).Uses);
    }

    [Fact]
    public async Task A_match_on_the_title_or_aliases_counts_the_clue_unit_only()
    {
        Hand("backup", "备份", "## 要点\n每日备份到 zip\n\n## 恢复\n从 zip 恢复\n");

        var result = await Service.Tools.RecallAsync(new RecallRequest { Query = "别名一" });

        Assert.Contains("### 要点 [", result.Text);
        Assert.Equal(1, (await Usage("tacit-memory/backup")).Uses);
    }

    [Fact]
    public async Task The_last_day_moves_forward_with_the_clock()
    {
        // The title shares no word with the query, so only the section is returned.
        Hand("backup", "脚本", "## 要点\n每日备份到 zip\n");
        await Service.Tools.RecallAsync(new RecallRequest { Page = "tacit-memory/backup" });
        _home.Clock.Advance(TimeSpan.FromDays(2));

        await Service.Tools.RecallAsync(new RecallRequest { Query = "每日备份到" });

        var usage = await Usage("tacit-memory/backup");
        Assert.Equal(2, usage.Uses);
        Assert.Equal(TestHome.Today.AddDays(2).Written(), usage.LastUse);
    }

    [Fact]
    public async Task Editing_a_section_keeps_the_counts_of_every_section_of_the_page()
    {
        Hand("backup", "备份", "## 要点\n每日备份到 zip\n\n## 恢复\n从 zip 恢复\n");
        var read = await Service.Tools.RecallAsync(new RecallRequest { Page = "tacit-memory/backup" });
        Assert.Equal(2, (await Usage("tacit-memory/backup")).Uses);
        var code = Regex.Match(read.Text, @"## 恢复 \[([0-9a-f]{6}(?:-\d+)?)\]").Groups[1].Value;

        var edit = await Service.Tools.WritePageAsync(new WritePageRequest
        {
            Op = "upsert_section", Page = "tacit-memory/backup", Section = "恢复", Body = "从 zip 恢复，先停服务。", Hash = code,
        }).WaitAsync(Guard);
        Assert.False(edit.IsError, edit.Text);

        var usage = await Usage("tacit-memory/backup");
        Assert.Equal(2, usage.Uses);
        Assert.Equal(TestHome.Today.Written(), usage.LastUse);
    }

    [Fact]
    public async Task A_page_never_returned_has_no_count_and_no_day()
    {
        Hand("backup", "备份", "## 要点\n每日备份到 zip\n");

        var usage = await Usage("tacit-memory/backup");

        Assert.Equal(0, usage.Uses);
        Assert.Null(usage.LastUse);
    }

    [Fact]
    public async Task Adding_an_activity_keeps_the_counts_of_the_other_activities_of_the_same_month()
    {
        var first = await Service.Tools.WriteRecentAsync(new WriteRecentRequest { Text = "核对恢复演练的备份清单" });
        Assert.False(first.IsError, first.Text);
        var code = Regex.Match(first.Text, @"\[([0-9a-f]{6}(?:-\d+)?)\]").Groups[1].Value;
        await Service.Tools.RecallAsync(new RecallRequest { Query = "恢复演练的备份清单" });
        Assert.Equal(1, (await Service.ActivityCountsAsync(TestHome.Today))[code]);

        var second = await Service.Tools.WriteRecentAsync(new WriteRecentRequest { Text = "改写备份脚本的超时设置" });
        Assert.False(second.IsError, second.Text);

        Assert.Equal(1, (await Service.ActivityCountsAsync(TestHome.Today))[code]);
    }

    [Fact]
    public async Task Reindex_resets_every_count()
    {
        Hand("backup", "备份", "## 要点\n每日备份到 zip\n");
        await Service.Tools.RecallAsync(new RecallRequest { Page = "tacit-memory/backup" });
        Assert.Equal(1, (await Usage("tacit-memory/backup")).Uses);

        await Service.Maintenance.ReindexAsync().WaitAsync(Guard);

        var usage = await Usage("tacit-memory/backup");
        Assert.Equal(0, usage.Uses);
        Assert.Null(usage.LastUse);
    }
}
