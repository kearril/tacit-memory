using System.Text.RegularExpressions;

namespace TacitMemory.Core.Tests;

/// <summary>The activity log as the memory page reads it: the months that have a file, and one month by day with its summaries.</summary>
public sealed class MemoryActivityTests : IDisposable
{
    private readonly TestHome _home = new();
    private readonly MemoryService _service;

    public MemoryActivityTests()
    {
        _home.InitReady();
        _service = _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);
    }

    public void Dispose()
    {
        _service.Dispose();
        _home.Dispose();
    }

    private string Recent => Path.Combine(_home.DefaultMemory, "recent");

    private void WriteMonth(string month, string text)
    {
        Directory.CreateDirectory(Recent);
        File.WriteAllText(Path.Combine(Recent, $"{month}.md"), text.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task The_months_are_the_month_files_newest_first_with_the_current_month()
    {
        WriteMonth("2026-09", "## 2026-09-03\n- 九月的一条\n");
        WriteMonth("2026-11", "## 2026-11-01\n- 十一月的一条\n");
        File.WriteAllText(Path.Combine(Recent, "notes.md"), "不是月份文件\n");

        var months = await _service.Browse.ActivityMonthsAsync();

        Assert.Equal(["2026-11", "2026-09"], months.Months);
        Assert.Equal(TestHome.Today.ToString("yyyy-MM"), months.Current);
    }

    [Fact]
    public async Task A_month_is_grouped_by_day_with_its_labels_continuations_and_summaries()
    {
        WriteMonth("2026-10", """
            ## 2026-10-05
            - [my-app] 发版完成，因为测试都过了
              <!-- on win-main quote「发吧」 -->
            - 一条没有主线的
            ## 2026-10-30
            - [my-app] 修了回滚
            ## Summary
            - 2026-W41：一周的汇总
            - 2026-10：一个月的汇总
            """);

        var month = await _service.Browse.ActivityMonthAsync("2026-10");

        Assert.Equal("2026-10", month.Month);
        Assert.EndsWith(Path.Combine("recent", "2026-10.md"), month.File.Path);
        Assert.Empty(month.File.Problems);
        Assert.Equal(["2026-10-05", "2026-10-30"], month.Days.Select(day => day.Date));
        var first = month.Days[0].Activities;
        Assert.Equal(("my-app", "发版完成，因为测试都过了"), (first[0].Workstream, first[0].Text));
        Assert.Empty(first[0].Lines);
        Assert.Equal([new SourceView(null, "发吧")], first[0].Sources);
        Assert.Equal("win-main", first[0].Machine);
        Assert.Null(first[1].Machine);
        Assert.Matches("^[0-9a-f]{6}$", first[0].Code);
        Assert.Null(first[1].Workstream);
        Assert.Empty(first[1].Lines);
        Assert.Empty(first[1].Sources);
        Assert.Equal(["week", "month"], month.Summaries.Select(summary => summary.Kind));
        var week = month.Summaries[0];
        Assert.Equal(("2026-W41", "2026-10-05", "2026-10-11", "一周的汇总"), (week.Label, week.First, week.Last, week.Text));
    }

    [Fact]
    public async Task The_codes_are_the_ones_recall_gives()
    {
        var written = await _service.Tools.WriteRecentAsync(new WriteRecentRequest { Text = "记下一条，因为要比对短码", Workstream = "my-app" });
        Assert.False(written.IsError, written.Text);

        var month = await _service.Browse.ActivityMonthAsync(TestHome.Today.ToString("yyyy-MM"));

        Assert.Contains($"[{Assert.Single(Assert.Single(month.Days).Activities).Code}]", written.Text);
    }

    [Fact]
    public async Task The_current_month_without_a_file_is_empty_and_another_month_is_not_found()
    {
        var current = await _service.Browse.ActivityMonthAsync(TestHome.Today.ToString("yyyy-MM"));

        Assert.Empty(current.Days);
        Assert.Empty(current.Summaries);
        var error = await Assert.ThrowsAsync<TacitException>(() => _service.Browse.ActivityMonthAsync("2025-01"));
        Assert.Equal(TacitError.MemoryNotFound, error.Error);
    }

    [Theory]
    [InlineData("2026-13")]
    [InlineData("2026-1")]
    [InlineData("latest")]
    public async Task A_month_not_written_yyyy_MM_is_refused(string month)
    {
        var error = await Assert.ThrowsAsync<TacitException>(() => _service.Browse.ActivityMonthAsync(month));

        Assert.Equal(TacitError.RequestInvalid, error.Error);
    }

    [Fact]
    public async Task A_file_in_the_wrong_form_lists_its_problems_and_returns_what_it_can()
    {
        WriteMonth("2026-10", "- 在日期标题之前\n## 2026/10/05\n- 日期写错了\n## 2026-10-06\n- 照常读出\n");

        var month = await _service.Browse.ActivityMonthAsync("2026-10");

        Assert.Equal([1, 2, 3], month.File.Problems.Select(problem => problem.Line));
        Assert.Equal("照常读出", Assert.Single(Assert.Single(month.Days).Activities).Text);
    }

    [Fact]
    public async Task Reading_counts_nothing()
    {
        var written = await _service.Tools.WriteRecentAsync(new WriteRecentRequest { Text = "核对恢复演练的备份清单" });
        var code = Regex.Match(written.Text, @"\[([0-9a-f]{6}(?:-\d+)?)\]").Groups[1].Value;
        await _service.Tools.RecallAsync(new RecallRequest { Query = "恢复演练的备份清单" });
        var before = await _service.ActivityCountsAsync(TestHome.Today);

        await _service.Browse.ActivityMonthsAsync();
        await _service.Browse.ActivityMonthAsync(TestHome.Today.ToString("yyyy-MM"));

        Assert.Equal(1, before[code]);
        Assert.Equal(before, await _service.ActivityCountsAsync(TestHome.Today));
    }

    [Fact]
    public async Task Each_activity_shows_how_often_recall_returned_it_and_a_trace_adds_nothing()
    {
        var written = await _service.Tools.WriteRecentAsync(new WriteRecentRequest { Text = "核对恢复演练的备份清单" });
        var other = await _service.Tools.WriteRecentAsync(new WriteRecentRequest { Text = "调整日志轮转天数" });
        var code = Regex.Match(written.Text, @"\[([0-9a-f]{6}(?:-\d+)?)\]").Groups[1].Value;
        var otherCode = Regex.Match(other.Text, @"\[([0-9a-f]{6}(?:-\d+)?)\]").Groups[1].Value;
        Assert.Equal(0, (await _service.ActivityCountsAsync(TestHome.Today))[code]);

        await _service.Tools.RecallAsync(new RecallRequest { Query = "恢复演练的备份清单" });
        await _service.Browse.TraceRecallAsync(new RecallTraceRequest("恢复演练的备份清单"));

        var counts = await _service.ActivityCountsAsync(TestHome.Today);
        Assert.Equal(1, counts[code]);
        Assert.Equal(0, counts[otherCode]);
    }
}
