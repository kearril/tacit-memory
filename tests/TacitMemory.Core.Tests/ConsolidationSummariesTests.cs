namespace TacitMemory.Core.Tests;

/// <summary>
/// The consolidation's material for the summaries, through the facade: the completed ISO weeks and calendar months that lack a
/// summary line, and the workstream material (the established workstreams with activity since the last consolidation). Dates are
/// the test clock's; each test moves it to the day it needs.
/// </summary>
public sealed class ConsolidationSummariesTests : IDisposable
{
    private readonly TestHome _home = new();

    public void Dispose() => _home.Dispose();

    private string Memory => _home.DefaultMemory;

    private MemoryService Open()
    {
        _home.InitReady();
        return _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);
    }

    /// <summary>Writes a month file of the activity log by hand, with LF line endings.</summary>
    private void Month(string month, string text)
    {
        Directory.CreateDirectory(Path.Combine(Memory, "recent"));
        File.WriteAllText(Path.Combine(Memory, "recent", month + ".md"), text.ReplaceLineEndings("\n"));
    }

    private void Workstreams(string text) => File.WriteAllText(Path.Combine(Memory, "workstreams.md"), text.ReplaceLineEndings("\n"));

    /// <summary>Stages every change of the memory repository and commits it, so the files written so far are in the baseline.</summary>
    private void CommitAll(string message)
    {
        TestHome.Git(Memory, "add", "-A");
        TestHome.Git(Memory, "-c", "user.name=tacit", "-c", "user.email=tacit@localhost", "commit", "--allow-empty", "-m", message);
    }

    /// <summary>Moves the test clock to 10:00 UTC on <paramref name="day"/>.</summary>
    private void ClockOn(DateOnly day)
    {
        var target = new DateTimeOffset(day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero);
        _home.Clock.Advance(target - _home.Clock.GetUtcNow());
    }

    /// <summary>The text of one category: from its heading to the next heading of the second or third level, or to the end.</summary>
    private static string Section(string text, string title)
    {
        var start = text.IndexOf($"### {title}\n", StringComparison.Ordinal);
        Assert.True(start >= 0, $"No category \"{title}\" in the report.");
        var body = text[(start + title.Length + 5)..];
        var ends = new[] { "\n### ", "\n## " }
            .Select(marker => body.IndexOf(marker, StringComparison.Ordinal))
            .Where(index => index >= 0)
            .ToList();
        return ends.Count == 0 ? body : body[..ends.Min()];
    }

    private async Task<string> Report(MemoryService service) => await service.Tools.ConsolidationReportAsync();

    [Fact]
    public async Task A_completed_week_without_a_summary_lists_all_its_activities_grouped_by_workstream_with_the_month_to_write_in()
    {
        using var service = Open();
        // Week 2026-W44 runs from Monday 10-26 to Sunday 11-01; its Thursday is 10-29, so its summary goes into the October file.
        Month("2026-10", "## 2026-10-27\n- [tacit-memory] 整理了报告骨架\n");
        Month("2026-11", "## 2026-11-01\n- 临时记录的一件事\n## 2026-11-04\n- [my-app] 发布准备\n");
        ClockOn(new DateOnly(2026, 11, 9));

        var section = Section(await Report(service), "Weeks to summarize");

        Assert.Contains("2026-W44", section);
        Assert.Contains("recent/2026-10.md", section);
        Assert.Contains("tacit-memory", section);
        Assert.Contains("整理了报告骨架", section);
        Assert.Contains("  - Other:", section);
        Assert.Contains("临时记录的一件事", section);
        Assert.Equal(1, CountOf(section, "2026-W44"));
        Assert.True(section.IndexOf("2026-W44", StringComparison.Ordinal) < section.IndexOf("2026-W45", StringComparison.Ordinal),
            "The oldest week comes first.");
    }

    [Fact]
    public async Task A_week_with_activities_on_only_some_days_lists_just_those_activities()
    {
        using var service = Open();
        Month("2026-11", "## 2026-11-03\n- 周二的活动\n## 2026-11-05\n- 周四的活动\n");
        ClockOn(new DateOnly(2026, 11, 9));

        var section = Section(await Report(service), "Weeks to summarize");

        Assert.Contains("2026-W45", section);
        Assert.Contains("周二的活动", section);
        Assert.Contains("周四的活动", section);
        Assert.Contains("2026-11-03", section);
        Assert.Contains("2026-11-05", section);
        Assert.DoesNotContain("2026-11-04", section);
    }

    [Fact]
    public async Task Activities_without_a_workstream_are_grouped_under_other_and_not_under_any_workstream()
    {
        using var service = Open();
        Month("2026-11", "## 2026-11-03\n- 没有项目的活动\n");
        ClockOn(new DateOnly(2026, 11, 9));

        var section = Section(await Report(service), "Weeks to summarize");

        Assert.Contains("  - Other:", section);
        Assert.Contains("没有项目的活动", section);
        Assert.DoesNotContain("[", section);
    }

    [Fact]
    public async Task A_week_that_already_has_a_summary_line_and_the_week_in_progress_are_not_listed()
    {
        using var service = Open();
        Month("2026-10", "## 2026-10-27\n- 已经汇总的活动\n\n## Summary\n- 2026-W44：这一周已经汇总\n");
        Month("2026-11", "## 2026-11-09\n- 本周一的活动\n");
        ClockOn(new DateOnly(2026, 11, 9));

        var section = Section(await Report(service), "Weeks to summarize");

        Assert.DoesNotContain("2026-W44", section);
        Assert.DoesNotContain("2026-W46", section);
        Assert.DoesNotContain("本周一的活动", section);
    }

    [Fact]
    public async Task A_completed_week_without_activities_is_not_listed()
    {
        using var service = Open();
        Month("2026-11", "## 2026-11-03\n- 本周的活动\n");
        ClockOn(new DateOnly(2026, 11, 9));

        var section = Section(await Report(service), "Weeks to summarize");

        Assert.DoesNotContain("2026-W42", section);
        Assert.Contains("2026-W45", section);
    }

    [Fact]
    public async Task The_iso_week_across_the_year_boundary_takes_the_days_of_its_own_week_and_no_others()
    {
        using var service = Open();
        // 2026-W53 runs from 12-28 to 2027-01-03, and 2027-W01 from 2027-01-04 to 01-10.
        Month("2026-12", "## 2026-12-31\n- 跨年的活动\n");
        Month("2027-01", "## 2027-01-01\n- 元旦的活动\n## 2027-01-04\n- 新一周的活动\n");
        ClockOn(new DateOnly(2027, 1, 11));

        var section = Section(await Report(service), "Weeks to summarize");

        var second = section.IndexOf("2027-W01", StringComparison.Ordinal);
        Assert.True(second > 0, section);
        var firstWeek = section[..second];
        Assert.Contains("2026-W53", firstWeek);
        Assert.Contains("跨年的活动", firstWeek);
        Assert.Contains("元旦的活动", firstWeek);
        Assert.Contains("recent/2026-12.md", firstWeek);
        Assert.DoesNotContain("新一周的活动", firstWeek);
        Assert.Contains("新一周的活动", section[second..]);
    }

    [Fact]
    public async Task Each_completed_month_without_a_summary_is_listed_with_the_week_summaries_written_in_its_file()
    {
        using var service = Open();
        Month("2026-10", "## 2026-10-06\n- 十月初的活动\n## 2026-10-27\n- 十月底的活动\n\n## Summary\n- 2026-W41：第二周的总结\n");
        Month("2026-11", "## 2026-11-03\n- 本月的活动\n");
        ClockOn(new DateOnly(2026, 11, 9));

        var section = Section(await Report(service), "Months to summarize");

        Assert.Contains("2026-10", section);
        Assert.Contains("2026-W41：第二周的总结", section);
        Assert.DoesNotContain("2026-11", section);
    }

    [Fact]
    public async Task A_month_that_already_has_a_summary_line_is_not_listed()
    {
        using var service = Open();
        Month("2026-10", "## 2026-10-06\n- 十月的活动\n\n## Summary\n- 2026-10：十月已经汇总\n");
        ClockOn(new DateOnly(2026, 11, 9));

        var section = Section(await Report(service), "Months to summarize");

        Assert.DoesNotContain("十月已经汇总", section);
        Assert.Contains("None.", section);
    }

    [Fact]
    public async Task The_workstream_material_lists_only_workstreams_with_activity_since_the_last_consolidation_with_their_entries_and_activities()
    {
        _home.InitReady();
        Workstreams("## Workstream\n### alpha\nBackground: 一号主线。\nRecent: 正在收敛。\n### beta\nBackground: 二号主线。\n### gamma\nBackground: 三号主线，没有新活动。\n");
        Month("2026-10", "## 2026-10-20\n- [alpha] 整理之前的活动\n");
        CommitAll("consolidate: earlier pass");
        Month("2026-11", "## 2026-11-09\n- [alpha] 本周的新活动\n- [beta] 贝塔的新活动\n");
        using var service = _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);
        ClockOn(new DateOnly(2026, 11, 9));

        var section = Section(await Report(service), "Workstream material");

        Assert.Contains("alpha", section);
        Assert.Contains("一号主线。", section);
        Assert.Contains("正在收敛。", section);
        Assert.Contains("本周的新活动", section);
        Assert.Contains("beta", section);
        Assert.Contains("二号主线。", section);
        Assert.Contains("贝塔的新活动", section);
        Assert.DoesNotContain("gamma", section);
        Assert.DoesNotContain("三号主线", section);
        Assert.DoesNotContain("整理之前的活动", section);
        Assert.DoesNotContain("Latest weekly", section);
    }

    [Theory]
    [InlineData("consolidation")]
    [InlineData("init")]
    [InlineData("none")]
    public async Task The_workstream_material_takes_the_activities_since_the_baseline_whichever_kind_of_baseline_it_is(string baseline)
    {
        _home.InitReady();
        Workstreams("## Workstream\n### alpha\nBackground: 一号主线。\n");
        if (baseline == "consolidation")
        {
            Month("2026-11", "## 2026-11-02\n- [alpha] 整理之前的活动\n");
            CommitAll("consolidate: earlier pass");
        }
        else if (baseline == "none")
        {
            TestHome.Git(Memory, "-c", "user.name=tacit", "-c", "user.email=tacit@localhost", "commit", "--amend", "--allow-empty", "-m", "setup memory");
        }

        Month("2026-11", "## 2026-11-02\n- [alpha] 整理之前的活动\n\n## 2026-11-09\n- [alpha] 之后的活动\n");
        using var service = _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);
        ClockOn(new DateOnly(2026, 11, 9));

        var section = Section(await Report(service), "Workstream material");

        Assert.Contains("之后的活动", section);
        if (baseline == "consolidation")
            Assert.DoesNotContain("整理之前的活动", section);
        else
            Assert.Contains("整理之前的活动", section);
    }

    [Fact]
    public async Task Without_an_established_workstream_the_workstream_material_says_so_in_one_line()
    {
        using var service = Open();
        Workstreams("## Workstream\n");
        Month("2026-10", "## Summary\n- 2026-W44：第五周\n");
        ClockOn(new DateOnly(2026, 11, 9));

        var section = Section(await Report(service), "Workstream material");

        Assert.Contains("No workstream is established", section);
        Assert.DoesNotContain("2026-W44", section);
    }

    [Fact]
    public async Task An_established_workstream_without_activity_since_the_baseline_is_not_listed()
    {
        _home.InitReady();
        Workstreams("## Workstream\n### alpha\nBackground: 没有新活动的主线。\n");
        using var service = _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

        var section = Section(await Report(service), "Workstream material");

        Assert.DoesNotContain("alpha", section);
        Assert.Contains("None.", section);
    }

    private static int CountOf(string text, string part)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(part, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += part.Length;
        }

        return count;
    }
}
