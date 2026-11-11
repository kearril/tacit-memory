namespace TacitMemory.Core.Tests;

/// <summary>
/// The consolidation's material for the summaries, through the facade: the completed ISO weeks and calendar months that lack a
/// summary line, the latest weekly summaries and the mainline. Dates are the test clock's; each test moves it to the day it needs.
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

    private void Mainline(string text) => File.WriteAllText(Path.Combine(Memory, "workstreams.md"), text.ReplaceLineEndings("\n"));

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

    private async Task<string> Report(MemoryService service) => await service.ConsolidationReportAsync();

    [Fact]
    public async Task A_completed_week_without_a_summary_lists_all_its_activities_grouped_by_workstream_with_the_month_to_write_in()
    {
        using var service = Open();
        // Week 2026-W44 runs from Monday 10-26 to Sunday 11-01; its Thursday is 10-29, so its summary goes into the October file.
        Month("2026-10", "## 2026-10-27\n- [tacit-memory] 整理了报告骨架\n");
        Month("2026-11", "## 2026-11-01\n- 随手记的一件事\n## 2026-11-04\n- [my-app] 发布准备\n");
        ClockOn(new DateOnly(2026, 11, 9));

        var section = Section(await Report(service), "Weeks to summarize");

        Assert.Contains("2026-W44", section);
        Assert.Contains("recent/2026-10.md", section);
        Assert.Contains("tacit-memory", section);
        Assert.Contains("整理了报告骨架", section);
        Assert.Contains("其他", section);
        Assert.Contains("随手记的一件事", section);
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

        Assert.Contains("其他", section);
        Assert.Contains("没有项目的活动", section);
        Assert.DoesNotContain("[", section);
    }

    [Fact]
    public async Task A_week_that_already_has_a_summary_line_and_the_week_in_progress_are_not_listed()
    {
        using var service = Open();
        Month("2026-10", "## 2026-10-27\n- 已经汇总的活动\n\n## 汇总\n- 2026-W44：这一周已经汇总\n");
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
        Month("2026-10", "## 2026-10-06\n- 十月初的活动\n## 2026-10-27\n- 十月底的活动\n\n## 汇总\n- 2026-W41：第二周的总结\n");
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
        Month("2026-10", "## 2026-10-06\n- 十月的活动\n\n## 汇总\n- 2026-10：十月已经汇总\n");
        ClockOn(new DateOnly(2026, 11, 9));

        var section = Section(await Report(service), "Months to summarize");

        Assert.DoesNotContain("十月已经汇总", section);
        Assert.Contains("None.", section);
    }

    [Fact]
    public async Task The_mainline_material_gives_the_latest_four_weekly_summaries_and_the_mainline_items()
    {
        using var service = Open();
        Month("2026-10",
            "## 汇总\n- 2026-W40：第一周\n- 2026-W41：第二周\n- 2026-W42：第三周\n- 2026-W43：第四周\n- 2026-W44：第五周\n");
        Mainline("## 主线\n### alpha\n背景：一号主线。\n### beta\n背景：二号主线。\n");
        ClockOn(new DateOnly(2026, 11, 9));

        var section = Section(await Report(service), "Mainline material");

        Assert.Contains("2026-W44：第五周", section);
        Assert.Contains("2026-W41：第二周", section);
        Assert.DoesNotContain("2026-W40：第一周", section);
        Assert.Contains("alpha", section);
        Assert.Contains("beta", section);
        Assert.DoesNotContain("The mainline is empty", section);
    }

    [Fact]
    public async Task An_empty_mainline_is_said_in_one_line_next_to_the_weekly_summaries()
    {
        using var service = Open();
        Month("2026-10", "## 汇总\n- 2026-W44：第五周\n");
        Mainline("## 主线\n");
        ClockOn(new DateOnly(2026, 11, 9));

        var section = Section(await Report(service), "Mainline material");

        Assert.Contains("2026-W44：第五周", section);
        Assert.Contains("The mainline is empty", section);
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
