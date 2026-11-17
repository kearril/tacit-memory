namespace TacitMemory.Core.Tests;

/// <summary>
/// Week and month summaries in the <c>## Summary</c> section of a month file: retrieval, the same-topic check, writing a new
/// activity next to them, and the same-topic check for activities written next to them.
/// </summary>
public sealed class SummaryTests : IDisposable
{
    private readonly TestHome _home = new();
    private readonly ModelDouble _models = new();
    private MemoryService? _service;

    public SummaryTests()
    {
        _home.InitReady(models: _models);
    }

    public void Dispose()
    {
        _service?.Dispose();
        _models.Dispose();
        _home.Dispose();
    }

    private MemoryService Service => _service ??= _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

    private string Memory => _home.DefaultMemory;

    private string MonthFile(string month = "2026-11") => Path.Combine(Memory, "recent", month + ".md");

    /// <summary>Writes a month file by hand, as the user would, with LF line endings.</summary>
    private void Month(string text, string month = "2026-11")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MonthFile(month))!);
        File.WriteAllText(MonthFile(month), text.ReplaceLineEndings("\n"));
    }

    private Task<ToolResult> Recall(string query, string? kind = null, string? workstream = null, string? from = null, string? to = null) =>
        Service.RecallAsync(new RecallRequest { Query = query, Kind = kind, Workstream = workstream, From = from, To = to });

    private Task<ToolResult> Write(string text, string? workstream = null) =>
        Service.WriteRecentAsync(new WriteRecentRequest { Text = text, Workstream = workstream });

    [Fact]
    public async Task A_week_summary_is_recalled_as_an_activity_and_labelled_as_a_summary_of_its_week()
    {
        Month("## 2026-11-02\n- 想清楚了画像的分工\n\n## Summary\n- 2026-W45：本周整理了画像与记忆结构\n");

        var result = await Recall("画像", kind: "activities");

        Assert.False(result.IsError, result.Text);
        Assert.Contains("summary, week starting 2026-11-02", result.Text);
        Assert.Contains("2026-W45：本周整理了画像与记忆结构", result.Text);
        Assert.Contains("想清楚了画像的分工", result.Text);
    }

    [Fact]
    public async Task The_summary_heading_is_found_whatever_its_case()
    {
        Month("## 2026-11-02\n- 想清楚了画像的分工\n\n## summary\n- 2026-W45：本周整理了画像与记忆结构\n");

        var result = await Recall("画像", kind: "activities");

        Assert.Contains("summary, week starting 2026-11-02", result.Text);
    }

    [Fact]
    public async Task A_month_summary_is_recalled_and_labelled_with_its_month()
    {
        Month("## Summary\n- 2026-11：十一月主要在做记忆重设计\n");

        var result = await Recall("重设计", kind: "activities");

        Assert.Contains("summary, month starting 2026-11-01", result.Text);
        Assert.Contains("2026-11：十一月主要在做记忆重设计", result.Text);
    }

    [Fact]
    public async Task Summaries_are_left_out_of_pages_workstreams_and_dates_outside_their_period()
    {
        Month("## Summary\n- 2026-W45：本周整理了画像\n");

        var pages = await Recall("画像", kind: "pages");
        var labelled = await Recall("画像", kind: "activities", workstream: "tacit-memory");
        var outside = await Recall("画像", kind: "activities", from: "2026-10-01", to: "2026-10-31");
        var inside = await Recall("画像", kind: "activities", from: "2026-11-08", to: "2026-11-08");

        Assert.DoesNotContain("summary,", pages.Text);
        Assert.DoesNotContain("summary,", labelled.Text);
        Assert.DoesNotContain("summary,", outside.Text);
        Assert.Contains("summary, week starting 2026-11-02", inside.Text);
    }

    [Fact]
    public async Task A_week_across_the_year_end_is_found_by_its_iso_week_dates()
    {
        // 2026-W53 runs from Monday 2026-12-28 to Sunday 2027-01-03, and its Thursday is 2026-12-31, so it lives in 2026-12.md.
        Month("## Summary\n- 2026-W53：跨年的一周\n", month: "2026-12");

        var onNewYearsDay = await Recall("跨年", kind: "activities", from: "2027-01-01", to: "2027-01-01");
        var afterTheWeek = await Recall("跨年", kind: "activities", from: "2027-01-04", to: "2027-01-10");

        Assert.Contains("2026-W53：跨年的一周", onNewYearsDay.Text);
        Assert.Contains("summary, week starting 2026-12-28", onNewYearsDay.Text);
        Assert.DoesNotContain("跨年的一周", afterTheWeek.Text);
    }

    [Fact]
    public async Task A_summary_is_not_offered_to_the_same_topic_check()
    {
        Month("## 2026-11-02\n- 今天先做的一件事\n\n## Summary\n- 2026-W45：汇总里的一句话不该被比对\n");
        IReadOnlyDictionary<string, string>? offered = null;
        _models.Choose = (_, options) =>
        {
            offered = options;
            return options.Keys.ToDictionary(key => key, _ => 0.0);
        };

        await Write("今天另一件事");

        Assert.NotNull(offered);
        Assert.Contains(offered!.Values, text => text.Contains("今天先做的一件事", StringComparison.Ordinal));
        Assert.DoesNotContain(offered.Values, text => text.Contains("汇总里的一句话", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Adding_an_activity_leaves_the_summary_section_and_every_other_byte_alone()
    {
        Month("## 2026-11-02\n- 早上的活动\n\n## Summary\n- 2026-W45：本周\n- 2026-11：本月\n");

        await Write("下午的活动");

        Assert.Equal(
            "## 2026-11-02\n- 早上的活动\n- 下午的活动\n\n## Summary\n- 2026-W45：本周\n- 2026-11：本月\n",
            File.ReadAllText(MonthFile()));
    }

    [Fact]
    public async Task An_activity_on_a_day_with_no_heading_gets_a_heading_above_the_summary_section()
    {
        Month("## Summary\n- 2026-W45：本周\n- 2026-11：本月\n");

        await Write("第一件事");

        Assert.Equal(
            "## 2026-11-02\n- 第一件事\n\n## Summary\n- 2026-W45：本周\n- 2026-11：本月\n",
            File.ReadAllText(MonthFile()));
    }

    [Fact]
    public async Task A_summary_edited_by_hand_after_the_index_ran_is_found_once_the_next_write_syncs_it()
    {
        Month("## 2026-11-02\n- 最早的活动\n");
        await Recall("记忆结构", kind: "activities");

        // Hand edits reach the index on the next write (or a restart, or reindex), as for activities.
        Month("## 2026-11-02\n- 最早的活动\n\n## Summary\n- 2026-W45：记忆结构在这一周定下来\n");
        await Write("之后的一件事");
        var result = await Recall("记忆结构", kind: "activities");

        Assert.Contains("2026-W45：记忆结构在这一周定下来", result.Text);
    }
}
