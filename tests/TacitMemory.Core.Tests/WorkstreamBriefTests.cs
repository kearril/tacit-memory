using System.Text.RegularExpressions;

namespace TacitMemory.Core.Tests;

/// <summary>
/// The workstream brief: <c>recall</c> with only <c>workstream</c> returns the workstream's conventions, open items, page index,
/// recent activities and latest weekly summary, in that order, within <c>limits.brief</c>.
/// </summary>
public sealed class WorkstreamBriefTests : IDisposable
{
    private readonly TestHome _home = new();
    private MemoryService? _service;
    private DateOnly _day = TestHome.Today;

    public void Dispose()
    {
        _service?.Dispose();
        _home.Dispose();
    }

    /// <summary>Makes the repository ready; call once per test, before the service is used.</summary>
    private void Setup(string extraConfig = "") => _home.InitReady(extraConfig);

    private MemoryService Service => _service ??= _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

    private string Memory => _home.DefaultMemory;

    /// <summary>Moves the test clock to <paramref name="date"/>, which must not be earlier than the clock's day.</summary>
    private void GoTo(string date)
    {
        var target = DateOnly.ParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        _home.Clock.Advance(TimeSpan.FromDays(target.DayNumber - _day.DayNumber));
        _day = target;
    }

    private Task<ToolResult> CreatePage(string page, string type, string title, params (string Name, string Body)[] sections) =>
        Service.WritePageAsync(new WritePageRequest
        {
            Op = "create",
            Page = page,
            Type = type,
            Title = title,
            Aliases = ["别名一", "别名二"],
            Sections = sections.Select(section => new PageSectionInput(section.Name, section.Body)).ToList(),
        });

    /// <summary>Writes a page file by hand, without a type, as a page written before types existed.</summary>
    private void HandWrittenUntypedPage(string workstream, string slug, string title)
    {
        var path = Path.Combine(Memory, "pages", workstream, slug + ".md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $"---\ntitle: {title}\naliases: [别名一, 别名二]\ndates: {{ first: 2026-10-01, last: 2026-10-05 }}\n---\n\n## 现象\n旧页的内容。\n");
    }

    private Task<ToolResult> Brief(string workstream, string? from = null, string? to = null) =>
        Service.RecallAsync(new RecallRequest { Workstream = workstream, From = from, To = to });

    private Task<ToolResult> Activity(string text, string? workstream = null, string? source = null) =>
        Service.WriteRecentAsync(new WriteRecentRequest { Text = text, Workstream = workstream, Source = source });

    private void MonthFile(string month, string text)
    {
        Directory.CreateDirectory(Path.Combine(Memory, "recent"));
        File.WriteAllText(Path.Combine(Memory, "recent", month + ".md"), text.ReplaceLineEndings("\n"));
    }

    private static int Position(string text, string marker)
    {
        var at = text.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(at >= 0, $"\"{marker}\" is missing from:\n{text}");
        return at;
    }

    [Fact]
    public async Task A_workstream_brief_groups_pages_by_type_in_order_newest_first_with_identifier_title_aliases_and_date()
    {
        Setup();
        await CreatePage("my-app/use-pgroonga", ToolValues.PageTypes.Decision, "采用 PGroonga", ("背景", "中文检索需要它。"));
        await CreatePage("my-app/db-upgrade", ToolValues.PageTypes.Incident, "数据库升级后扩展丢失", ("现象", "升级后搜不到中文。"));
        GoTo("2026-11-03");
        await CreatePage("my-app/release-0-9", ToolValues.PageTypes.Work, "发版 0.9", ("记录", "发版过程。"));
        GoTo("2026-11-04");
        await CreatePage("my-app/ci", ToolValues.PageTypes.Entity, "CI 流水线", ("入口", "见 ~/infra/ci.md。"));
        GoTo("2026-11-05");
        await CreatePage("my-app/replace-sqlite", ToolValues.PageTypes.Decision, "改用 SQLite 向量表", ("背景", "向量检索需要。"));
        HandWrittenUntypedPage("my-app", "legacy", "旧页");

        var result = await Brief("my-app");

        Assert.False(result.IsError, result.Text);
        Assert.StartsWith("Workstream brief: my-app", result.Text);
        Assert.True(Position(result.Text, "Decisions") < Position(result.Text, "Known pitfalls"));
        Assert.True(Position(result.Text, "Known pitfalls") < Position(result.Text, "\nWork:"));
        Assert.True(Position(result.Text, "\nWork:") < Position(result.Text, "Entities"));
        Assert.True(Position(result.Text, "Entities") < Position(result.Text, "Untyped"));
        Assert.True(Position(result.Text, "- my-app/replace-sqlite") < Position(result.Text, "- my-app/use-pgroonga"));
        Assert.Contains("- my-app/replace-sqlite: 改用 SQLite 向量表 (aliases: 别名一, 别名二) (last changed 2026-11-05)", result.Text);
        Assert.Contains("- my-app/db-upgrade: 数据库升级后扩展丢失 (aliases: 别名一, 别名二) (last changed 2026-11-02)", result.Text);
        Assert.Contains("- my-app/release-0-9: 发版 0.9", result.Text);
        Assert.Contains("- my-app/ci: CI 流水线", result.Text);
        Assert.Contains("- my-app/legacy: 旧页", result.Text);
    }

    [Fact]
    public async Task Pages_of_one_group_with_the_same_date_are_ordered_by_how_often_they_were_returned()
    {
        Setup();
        await CreatePage("my-app/a-choice", ToolValues.PageTypes.Decision, "甲决定", ("背景", "甲的理由。"));
        await CreatePage("my-app/b-choice", ToolValues.PageTypes.Decision, "乙决定", ("背景", "乙的理由。"));
        await Service.RecallAsync(new RecallRequest { Page = "my-app/b-choice" });

        var result = await Brief("my-app");

        Assert.True(Position(result.Text, "- my-app/b-choice") < Position(result.Text, "- my-app/a-choice"), result.Text);
    }

    [Fact]
    public async Task An_overturned_decision_leaves_the_effective_list_and_appears_only_as_an_identifier_on_one_line()
    {
        Setup();
        await CreatePage("my-app/old-choice", ToolValues.PageTypes.Decision, "旧决定", ("背景", "旧的理由。"), ("Overturned", "被 my-app/new-choice 取代。"));
        await CreatePage("my-app/new-choice", ToolValues.PageTypes.Decision, "新决定", ("背景", "新的理由。"));

        var result = await Brief("my-app");

        Assert.Contains("Overturned: my-app/old-choice", result.Text);
        Assert.DoesNotContain("- my-app/old-choice:", result.Text);
        Assert.DoesNotContain("旧决定", result.Text);
        Assert.DoesNotContain("被 my-app/new-choice 取代", result.Text);
        Assert.Contains("- my-app/new-choice: 新决定", result.Text);
    }

    [Fact]
    public async Task Open_items_are_the_leftovers_of_work_pages_with_their_page_identifiers()
    {
        Setup();
        await CreatePage("my-app/release-0-9", ToolValues.PageTypes.Work, "发版 0.9", ("记录", "发版过程。"), ("Leftovers", "- 回填任务还没跑\n- 通知用户升级"));
        await CreatePage("my-app/odd-incident", ToolValues.PageTypes.Incident, "非工作页", ("现象", "现象。"), ("Leftovers", "- 不该出现在未决事项里"));

        var result = await Brief("my-app");

        Assert.True(Position(result.Text, "Open items") < Position(result.Text, "回填任务还没跑"));
        Assert.Contains("my-app/release-0-9", result.Text[Position(result.Text, "Open items")..]);
        Assert.Contains("通知用户升级", result.Text);
        Assert.DoesNotContain("不该出现在未决事项里", result.Text);
    }

    [Fact]
    public async Task Leftovers_under_a_chinese_heading_are_content_and_not_open_items()
    {
        Setup();
        await CreatePage("my-app/release-1-0", ToolValues.PageTypes.Work, "发版 1.0", ("记录", "发版过程。"), ("遗留", "- 写成中文小节名的遗留"));

        var result = await Brief("my-app");

        Assert.DoesNotContain("Open items", result.Text);
        Assert.DoesNotContain("写成中文小节名的遗留", result.Text);
    }

    [Fact]
    public async Task Workstream_conventions_are_the_workstream_scope_persona_entries_only()
    {
        Setup();
        await CreatePage("my-app/ci", ToolValues.PageTypes.Entity, "CI 流水线", ("入口", "见 ~/infra/ci.md。"));
        await Service.WritePersonaAsync(new WritePersonaRequest
        {
            Op = "add", Section = ToolValues.PersonaSections.WorkingRules, Scope = "workstream:my-app",
            Text = "发版前先跑全量测试 → 避免漏测", Source = "发版前必须跑全量",
        });
        await Service.WritePersonaAsync(new WritePersonaRequest
        {
            Op = "add", Section = ToolValues.PersonaSections.WorkingRules, Scope = "workstream:other-app",
            Text = "other workstream rule → elsewhere", Source = "别的项目",
        });

        var result = await Brief("my-app");

        Assert.True(Position(result.Text, "发版前先跑全量测试") > Position(result.Text, "my-app"));
        Assert.DoesNotContain("other workstream rule", result.Text);
    }

    [Fact]
    public async Task The_sections_come_in_the_documented_order_name_conventions_open_items_pages_activities_summary()
    {
        Setup();
        await Service.WritePersonaAsync(new WritePersonaRequest
        {
            Op = "add", Section = ToolValues.PersonaSections.WorkingRules, Scope = "workstream:my-app",
            Text = "约定甲 → 做法甲", Source = "原话",
        });
        await CreatePage("my-app/release-0-9", ToolValues.PageTypes.Work, "发版 0.9", ("记录", "发版过程。"), ("Leftovers", "- 未决乙"));
        await Activity("最近的活动丙", workstream: "my-app");
        MonthFile("2026-11", "## 2026-11-02\n- [my-app] 最近的活动丙\n\n## Summary\n- 2026-W45：本周小结丁\n");

        var result = await Brief("my-app");

        var name = Position(result.Text, "Workstream brief: my-app");
        var conventions = Position(result.Text, "约定甲");
        var open = Position(result.Text, "未决乙");
        var pages = Position(result.Text, "- my-app/release-0-9: 发版 0.9");
        var activity = Position(result.Text, "最近的活动丙");
        var summary = Position(result.Text, "本周小结丁");
        Assert.True(name < conventions && conventions < open && open < pages && pages < activity && activity < summary, result.Text);
    }

    [Fact]
    public async Task The_last_three_activities_of_the_workstream_are_listed_newest_first_and_not_those_of_other_workstreams()
    {
        Setup();
        await Activity("活动一", workstream: "my-app");
        GoTo("2026-11-03");
        await Activity("活动二", workstream: "my-app");
        GoTo("2026-11-04");
        await Activity("活动三", workstream: "my-app");
        await Activity("别的项目的活动", workstream: "other-app");
        await Activity("没有项目的活动");
        GoTo("2026-11-05");
        await Activity("活动四", workstream: "my-app");
        await Activity("活动五", workstream: "my-app");

        var result = await Brief("my-app");

        Assert.Matches(new Regex(@"- 2026-11-05 \[[^\]]+\] 活动五"), result.Text);
        Assert.True(Position(result.Text, "活动五") < Position(result.Text, "活动四"));
        Assert.True(Position(result.Text, "活动四") < Position(result.Text, "活动三"));
        Assert.DoesNotContain("活动二", result.Text);
        Assert.DoesNotContain("活动一", result.Text);
        Assert.DoesNotContain("别的项目的活动", result.Text);
        Assert.DoesNotContain("没有项目的活动", result.Text);
    }

    [Fact]
    public async Task The_latest_weekly_summary_is_that_of_the_latest_week_with_a_summary_in_which_the_workstream_has_activity()
    {
        Setup();
        MonthFile("2026-10", "## 2026-10-28\n- [my-app] 十月末的活动\n\n## Summary\n- 2026-W44：第四十四周小结甲\n");
        MonthFile("2026-11", "## 2026-11-05\n- [my-app] 第四十五周的活动\n\n## 2026-11-10\n- [other-app] 别的项目\n\n## Summary\n- 2026-W46：第四十六周小结乙\n");

        var result = await Brief("my-app");

        Assert.Contains("2026-W44：第四十四周小结甲", result.Text);
        Assert.DoesNotContain("2026-W46", result.Text);
    }

    [Fact]
    public async Task From_and_to_limit_the_pages_and_the_activities_of_the_brief()
    {
        Setup();
        await CreatePage("my-app/early-page", ToolValues.PageTypes.Decision, "早期页", ("背景", "早期。"));
        await Activity("早期活动", workstream: "my-app");
        GoTo("2026-11-05");
        await CreatePage("my-app/recent-page", ToolValues.PageTypes.Decision, "近期页", ("背景", "近期。"));
        GoTo("2026-11-06");
        await Activity("近期活动", workstream: "my-app");

        var result = await Brief("my-app", from: "2026-11-04", to: "2026-11-06");

        Assert.Contains("近期页", result.Text);
        Assert.DoesNotContain("早期页", result.Text);
        Assert.Contains("近期活动", result.Text);
        Assert.DoesNotContain("早期活动", result.Text);
    }

    [Fact]
    public async Task A_workstream_with_no_pages_and_no_activities_says_so_plainly()
    {
        Setup();
        await CreatePage("other-app/unrelated", ToolValues.PageTypes.Decision, "别的项目", ("背景", "别的。"));

        var result = await Brief("my-app");

        Assert.False(result.IsError);
        Assert.Equal("No relevant memory.", result.Text);
    }

    [Fact]
    public async Task An_established_workstream_brief_opens_with_its_background_and_recent_lines()
    {
        Setup();
        File.WriteAllText(Path.Combine(Memory, "workstreams.md"), "## Workstream\n### my-app\nBackground: 一个应用。\nRecent: 在收尾。\n");
        await CreatePage("my-app/use-pgroonga", ToolValues.PageTypes.Decision, "采用 PGroonga", ("背景", "中文检索需要它。"));

        var result = await Brief("my-app");

        Assert.StartsWith("Workstream brief: my-app\nBackground: 一个应用。\nRecent: 在收尾。\n", result.Text);
    }

    [Fact]
    public async Task An_established_workstream_without_recent_gets_only_the_background_line()
    {
        Setup();
        File.WriteAllText(Path.Combine(Memory, "workstreams.md"), "## Workstream\n### my-app\nBackground: 一个应用。\n");
        await CreatePage("my-app/use-pgroonga", ToolValues.PageTypes.Decision, "采用 PGroonga", ("背景", "中文检索需要它。"));

        var result = await Brief("my-app");

        Assert.StartsWith("Workstream brief: my-app\nBackground: 一个应用。\n\n", result.Text);
        Assert.DoesNotContain("Recent:", result.Text);
    }

    [Fact]
    public async Task A_name_that_is_not_established_gets_its_brief_without_background_or_recent()
    {
        Setup();
        File.WriteAllText(Path.Combine(Memory, "workstreams.md"), "## Workstream\n### other\nBackground: 别的。\n");
        await CreatePage("my-app/use-pgroonga", ToolValues.PageTypes.Decision, "采用 PGroonga", ("背景", "中文检索需要它。"));

        var result = await Brief("my-app");

        Assert.StartsWith("Workstream brief: my-app\n", result.Text);
        Assert.DoesNotContain("Background:", result.Text);
        Assert.Contains("采用 PGroonga", result.Text);
    }

    [Fact]
    public async Task An_established_workstream_with_no_pages_or_activities_still_gets_its_background()
    {
        Setup();
        File.WriteAllText(Path.Combine(Memory, "workstreams.md"), "## Workstream\n### my-app\nBackground: 一个应用。\n");

        var result = await Brief("my-app");

        Assert.StartsWith("Workstream brief: my-app\nBackground: 一个应用。", result.Text);
    }

    [Fact]
    public async Task Combining_the_workstream_brief_with_kind_is_refused_with_a_next_step()
    {
        Setup();
        await CreatePage("my-app/ci", ToolValues.PageTypes.Entity, "CI 流水线", ("入口", "见 ~/infra/ci.md。"));

        var result = await Service.RecallAsync(new RecallRequest { Workstream = "my-app", Kind = ToolValues.RecallKinds.Pages });

        Assert.True(result.IsError);
        Assert.StartsWith("Refused: ", result.Text);
        Assert.Contains("kind", result.Text);
        Assert.Contains("Next step: ", result.Text);
        Assert.DoesNotContain("Workstream brief", result.Text);
    }

    [Fact]
    public async Task Each_page_listed_in_the_brief_counts_one_return_and_pages_left_out_count_none()
    {
        Setup();
        await CreatePage("my-app/early-page", ToolValues.PageTypes.Decision, "早期页", ("背景", "早期。"));
        GoTo("2026-11-05");
        await CreatePage("my-app/recent-page", ToolValues.PageTypes.Decision, "近期页", ("背景", "近期。"));

        await Brief("my-app", from: "2026-11-04");
        await Brief("my-app", from: "2026-11-04");

        var usages = await Service.PageCountsAsync();
        Assert.Equal(2, usages["my-app/recent-page"]);
        Assert.Equal(0, usages["my-app/early-page"]);
    }

    [Fact]
    public async Task A_brief_longer_than_the_limit_drops_the_oldest_page_entries_and_says_how_many()
    {
        Setup("limits: { brief: 500 }\n");
        var titles = Enumerable.Range(0, 6).Select(i => $"第{i}个很长很长的决定标题用来占满简报的长度").ToList();
        for (var i = 0; i < titles.Count; i++)
        {
            if (i > 0)
                GoTo(TestHome.Today.AddDays(i).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            await CreatePage($"my-app/choice-{i}", ToolValues.PageTypes.Decision, titles[i], ("背景", "理由。"));
        }
        await Activity("一条保留的活动", workstream: "my-app");
        await Service.WritePersonaAsync(new WritePersonaRequest
        {
            Op = "add", Section = ToolValues.PersonaSections.WorkingRules, Scope = "workstream:my-app",
            Text = "一条保留的约定 → 做法", Source = "原话",
        });

        var result = await Brief("my-app");

        Assert.False(result.IsError, result.Text);
        Assert.True(result.Text.Length <= 500, $"{result.Text.Length} characters:\n{result.Text}");
        var omitted = int.Parse(Regex.Match(result.Text, @"(\d+) page entries omitted").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        var listed = Regex.Matches(result.Text, @"^- my-app/choice-\d+:", RegexOptions.Multiline).Count;
        Assert.Equal(titles.Count, listed + omitted);
        Assert.True(omitted > 0);
        Assert.Contains("- my-app/choice-5:", result.Text);
        Assert.DoesNotContain("- my-app/choice-0:", result.Text);
        Assert.Contains("一条保留的活动", result.Text);
        Assert.Contains("一条保留的约定", result.Text);
    }

    [Fact]
    public async Task A_brief_whose_fixed_parts_exceed_the_limit_is_cut_at_the_end_and_says_so()
    {
        Setup("limits: { brief: 300 }\n");
        var filler = new string('长', 120);
        await Activity($"活动甲{filler}", workstream: "my-app");
        GoTo("2026-11-03");
        await Activity($"活动乙{filler}", workstream: "my-app");
        GoTo("2026-11-04");
        await Activity($"活动丙{filler}", workstream: "my-app");

        var result = await Brief("my-app");

        Assert.False(result.IsError, result.Text);
        Assert.StartsWith("Workstream brief: my-app", result.Text);
        Assert.True(result.Text.Length <= 300, $"{result.Text.Length} characters");
        Assert.Contains("limits.brief", result.Text);
    }
}
