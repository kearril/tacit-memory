namespace TacitMemory.Core.Tests;

/// <summary>The workstreams as the memory page reads them: the list, and a brief whole and structured, gathered as <c>recall</c> gathers it.</summary>
public sealed class MemoryWorkstreamsTests : IDisposable
{
    private readonly TestHome _home = new();
    private MemoryService? _service;

    public void Dispose()
    {
        _service?.Dispose();
        _home.Dispose();
    }

    /// <summary>Makes the repository ready; call once per test, before the service is used.</summary>
    private void Setup(string extraConfig = "") => _home.InitReady(extraConfig);

    private MemoryService Service => _service ??= _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

    private string Memory => _home.DefaultMemory;

    private void Establish(string text) => File.WriteAllText(Path.Combine(Memory, "workstreams.md"), text.ReplaceLineEndings("\n"));

    private async Task CreatePage(string page, string type, string title, params (string Name, string Body)[] sections)
    {
        var result = await Service.WritePageAsync(new WritePageRequest
        {
            Op = "create",
            Page = page,
            Type = type,
            Title = title,
            Aliases = ["别名一", "别名二"],
            Sections = sections.Select(section => new PageSectionInput(section.Name, section.Body)).ToList(),
        });
        Assert.False(result.IsError, result.Text);
    }

    private async Task Activity(string text, string? workstream)
    {
        var result = await Service.WriteRecentAsync(new WriteRecentRequest { Text = text, Workstream = workstream });
        Assert.False(result.IsError, result.Text);
    }

    [Fact]
    public async Task The_list_has_the_established_workstreams_and_the_names_in_use_that_are_not()
    {
        Setup();
        Establish("## Workstream\n\n### my-app\nBackground: 一个应用。\nRecent: 在做发版。\n\n### quiet\nBackground: 还没有内容。\n");
        await CreatePage("my-app/release", ToolValues.PageTypes.Work, "发版", ("产出", "发了 0.9。"));
        await CreatePage("side/idea", ToolValues.PageTypes.Entity, "点子", ("现状", "还在想。"));
        await Activity("my-app 发版完成，因为测试都过了。", "my-app");
        await Activity("记了一个旁支的想法，因为以后可能用到。", "loose");

        var list = await Service.Browse.WorkstreamsAsync();

        Assert.Equal(["my-app", "quiet"], list.Established.Select(item => item.Name));
        var app = list.Established[0];
        Assert.Equal(("一个应用。", "在做发版。", 1, TestHome.Today.ToString("yyyy-MM-dd")), (app.Background, app.Recent, app.Pages, app.LastActivity));
        Assert.Equal((0, (string?)null), (list.Established[1].Pages, list.Established[1].LastActivity));
        Assert.Equal(["loose", "side"], list.Unestablished.Select(item => item.Name));
        Assert.Null(list.Unestablished[0].Background);
        Assert.Equal(1, list.Unestablished[1].Pages);
    }

    [Fact]
    public async Task Field_words_match_whatever_their_case_and_with_spaces_before_the_colon()
    {
        Setup();
        Establish("## workstream\n\n### my-app\nbackground : 一个应用。\nRECENT:在做发版。\n");

        var list = await Service.Browse.WorkstreamsAsync();

        Assert.Equal(("my-app", "一个应用。", "在做发版。"), (list.Established[0].Name, list.Established[0].Background, list.Established[0].Recent));
    }

    [Fact]
    public async Task The_list_gives_the_workstreams_file_with_its_problems()
    {
        Setup();
        Establish("## Workstream\n\n### my-app\nBackground: 一个应用。\n\n### no-background\nRecent: 缺背景。\n");

        var list = await Service.Browse.WorkstreamsAsync();

        Assert.Equal(Path.Combine(Memory, "workstreams.md"), list.File.Path);
        Assert.Equal(6, Assert.Single(list.File.Problems).Line);
        Assert.Equal(["my-app"], list.Established.Select(item => item.Name));
    }

    [Fact]
    public async Task The_brief_holds_what_recall_lists_whole_by_type_with_uses_and_counts_nothing()
    {
        Setup();
        Establish("## Workstream\n\n### my-app\nBackground: 一个应用。\n");
        File.WriteAllText(Path.Combine(Memory, "persona.md"), "## Workstream: my-app\n### Working rules\n- 发版 → 先跑全量测试\n  <!-- 2026-11-01 quote「先全量」 -->\n");
        await CreatePage("my-app/use-pg", ToolValues.PageTypes.Decision, "采用 PG", ("决定", "用 PG。"));
        await CreatePage("my-app/old-db", ToolValues.PageTypes.Decision, "旧库", ("决定", "用旧库。"), ("Overturned", "被 my-app/use-pg 推翻。"));
        await CreatePage("my-app/release", ToolValues.PageTypes.Work, "发版", ("产出", "发了 0.9。"), ("Leftovers", "补发布说明\n\n补签名"));
        for (var i = 0; i < 4; i++)
            await Activity($"第 {i} 条进展，因为要测试最近活动只列三条。", "my-app");
        await Service.RecallAsync(new RecallRequest { Page = "my-app/use-pg" });
        var before = await Service.PageCountsAsync();

        var brief = await Service.Browse.WorkstreamBriefAsync("my-app");

        Assert.True(brief.Established);
        Assert.Equal("一个应用。", brief.Background);
        var convention = Assert.Single(Assert.Single(brief.Conventions).Entries);
        Assert.Equal("发版 → 先跑全量测试", convention.Text);
        Assert.Contains(new SourceView("2026-11-01", "先全量"), convention.Sources);
        var open = Assert.Single(brief.OpenItems);
        Assert.Equal("my-app/release", open.Page);
        Assert.Equal(["补发布说明", "补签名"], open.Lines);
        Assert.Equal([PageType.Decision, PageType.Work], brief.Groups.Select(group => group.Type));
        var decision = Assert.Single(brief.Groups[0].Pages);
        Assert.Equal(("my-app/use-pg", 1L), (decision.Id, decision.Uses));
        Assert.Equal(["my-app/old-db"], brief.Overturned);
        Assert.Equal(3, brief.Activities.Count);
        Assert.Equal(before, await Service.PageCountsAsync());

        var text = (await Service.RecallAsync(new RecallRequest { Workstream = "my-app" })).Text;
        foreach (var page in brief.Groups.SelectMany(group => group.Pages))
            Assert.Contains($"- {page.Id}: {page.Title}", text);
        foreach (var activity in brief.Activities)
            Assert.Contains($"[{activity.Code}]", text);
        Assert.NotEqual(before, await Service.PageCountsAsync());
    }

    [Fact]
    public async Task A_brief_is_not_cut_where_the_recall_brief_is()
    {
        Setup("limits: { brief: 600 }\n");
        for (var i = 0; i < 12; i++)
            await CreatePage($"my-app/page-{i:00}", ToolValues.PageTypes.Entity, $"第 {i} 页的标题写得长一些", ("现状", "内容。"));

        var brief = await Service.Browse.WorkstreamBriefAsync("my-app");
        var text = (await Service.RecallAsync(new RecallRequest { Workstream = "my-app" })).Text;

        Assert.Equal(12, brief.Groups.Sum(group => group.Pages.Count));
        Assert.Contains("page entries omitted", text);
    }

    [Theory]
    [InlineData("nothing")]
    [InlineData("../pages")]
    public async Task A_workstream_with_nothing_is_not_found(string name)
    {
        Setup();
        var error = await Assert.ThrowsAsync<TacitException>(() => Service.Browse.WorkstreamBriefAsync(name));

        Assert.Equal(TacitError.MemoryNotFound, error.Error);
    }
}
