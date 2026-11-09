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

    private PageUsage Usage(string page) => Service.PageUsages().Single(usage => usage.Page == page);

    [Fact]
    public async Task Reading_a_whole_page_counts_each_section_once_and_not_the_title()
    {
        Hand("backup", "备份", "## 要点\n每日备份到 zip\n\n## 恢复\n从 zip 恢复\n");

        await Service.RecallAsync(new RecallRequest { Page = "tacit-memory/backup" });

        var usage = Usage("tacit-memory/backup");
        Assert.Equal(2, usage.Count);
        Assert.Equal(TestHome.Today, usage.LastReturned);
    }

    [Fact]
    public async Task A_search_counts_the_section_it_shows_once_per_recall()
    {
        // The title shares no word with the query, so only the section is returned.
        Hand("backup", "脚本", "## 要点\n每日备份到 zip\n\n## 恢复\n从 zip 恢复\n");

        await Service.RecallAsync(new RecallRequest { Query = "每日备份到" });
        await Service.RecallAsync(new RecallRequest { Query = "每日备份到" });

        Assert.Equal(2, Usage("tacit-memory/backup").Count);
    }

    [Fact]
    public async Task A_match_on_the_title_or_aliases_counts_the_clue_unit_only()
    {
        Hand("backup", "备份", "## 要点\n每日备份到 zip\n\n## 恢复\n从 zip 恢复\n");

        var result = await Service.RecallAsync(new RecallRequest { Query = "别名一" });

        Assert.Contains("### 要点 [", result.Text);
        Assert.Equal(1, Usage("tacit-memory/backup").Count);
    }

    [Fact]
    public async Task The_last_day_moves_forward_with_the_clock()
    {
        // The title shares no word with the query, so only the section is returned.
        Hand("backup", "脚本", "## 要点\n每日备份到 zip\n");
        await Service.RecallAsync(new RecallRequest { Page = "tacit-memory/backup" });
        _home.Clock.Advance(TimeSpan.FromDays(2));

        await Service.RecallAsync(new RecallRequest { Query = "每日备份到" });

        var usage = Usage("tacit-memory/backup");
        Assert.Equal(2, usage.Count);
        Assert.Equal(TestHome.Today.AddDays(2), usage.LastReturned);
    }

    [Fact]
    public async Task Editing_a_section_keeps_the_counts_of_every_section_of_the_page()
    {
        Hand("backup", "备份", "## 要点\n每日备份到 zip\n\n## 恢复\n从 zip 恢复\n");
        var read = await Service.RecallAsync(new RecallRequest { Page = "tacit-memory/backup" });
        Assert.Equal(2, Usage("tacit-memory/backup").Count);
        var code = Regex.Match(read.Text, @"## 恢复 \[([0-9a-f]{6}(?:-\d+)?)\]").Groups[1].Value;

        var edit = await Service.WritePageAsync(new WritePageRequest
        {
            Op = "upsert_section", Page = "tacit-memory/backup", Section = "恢复", Body = "从 zip 恢复，先停服务。", Hash = code,
        }).WaitAsync(Guard);
        Assert.False(edit.IsError, edit.Text);

        var usage = Usage("tacit-memory/backup");
        Assert.Equal(2, usage.Count);
        Assert.Equal(TestHome.Today, usage.LastReturned);
    }

    [Fact]
    public void A_page_never_returned_has_no_count_and_no_day()
    {
        Hand("backup", "备份", "## 要点\n每日备份到 zip\n");

        var usage = Usage("tacit-memory/backup");

        Assert.Equal(0, usage.Count);
        Assert.Null(usage.LastReturned);
    }

    [Fact]
    public async Task Reindex_resets_every_count()
    {
        Hand("backup", "备份", "## 要点\n每日备份到 zip\n");
        await Service.RecallAsync(new RecallRequest { Page = "tacit-memory/backup" });
        Assert.Equal(1, Usage("tacit-memory/backup").Count);

        await Service.ReindexAsync().WaitAsync(Guard);

        var usage = Usage("tacit-memory/backup");
        Assert.Equal(0, usage.Count);
        Assert.Null(usage.LastReturned);
    }
}
