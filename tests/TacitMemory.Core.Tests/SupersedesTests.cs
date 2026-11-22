namespace TacitMemory.Core.Tests;

/// <summary>
/// Explicit <c>supersedes</c> page evolution edges across <c>write_page</c>, consolidation format checks, page browsing
/// and workstream briefs.
/// </summary>
public sealed class SupersedesTests : IDisposable
{
    private readonly TestHome _home = new();
    private MemoryService? _service;

    public SupersedesTests() => _home.InitReady();

    public void Dispose()
    {
        _service?.Dispose();
        _home.Dispose();
    }

    private MemoryService Service => _service ??= _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

    private string Memory => _home.DefaultMemory;

    private string PagePath(string id) => Path.Combine(Memory, "pages", id + ".md");

    private Task<ToolResult> CreatePage(
        string page,
        string type,
        string title,
        string[]? supersedes = null,
        string[]? related = null,
        params (string Name, string Body)[] sections) =>
        Service.Tools.WritePageAsync(new WritePageRequest
        {
            Op = ToolValues.WritePageOps.Create,
            Page = page,
            Type = type,
            Title = title,
            Aliases = [title + "别名", title + "又名"],
            Related = related,
            Supersedes = supersedes,
            Sections = sections.Length > 0
                ? sections.Select(section => new PageSectionInput(section.Name, section.Body)).ToList()
                : [new PageSectionInput("决定", title + "的内容。")],
        });

    [Fact]
    public async Task Create_and_update_header_persist_supersedes_and_recall_page_shows_both_directions()
    {
        Assert.False((await CreatePage("my-app/old-cache", ToolValues.PageTypes.Decision, "进程内缓存方案")).IsError);
        var created = await CreatePage("my-app/new-cache", ToolValues.PageTypes.Decision, "共享 Valkey 缓存方案", supersedes: ["my-app/old-cache"]);

        Assert.False(created.IsError, created.Text);
        Assert.Contains("supersedes: [my-app/old-cache]\n", File.ReadAllText(PagePath("my-app/new-cache")));

        var readNew = await Service.Tools.RecallAsync(new RecallRequest { Page = "my-app/new-cache" });
        var readOld = await Service.Tools.RecallAsync(new RecallRequest { Page = "my-app/old-cache" });
        Assert.Contains("Supersedes: my-app/old-cache\n", readNew.Text);
        Assert.Contains("Superseded by: my-app/new-cache\n", readOld.Text);

        var cleared = await Service.Tools.WritePageAsync(new WritePageRequest
        {
            Op = ToolValues.WritePageOps.UpdateHeader,
            Page = "my-app/new-cache",
            Supersedes = [],
        });
        Assert.False(cleared.IsError, cleared.Text);
        Assert.DoesNotContain("supersedes:", File.ReadAllText(PagePath("my-app/new-cache")));

        var restored = await Service.Tools.WritePageAsync(new WritePageRequest
        {
            Op = ToolValues.WritePageOps.UpdateHeader,
            Page = "my-app/new-cache",
            Supersedes = ["my-app/old-cache"],
        });
        Assert.False(restored.IsError, restored.Text);
        Assert.Contains("supersedes: [my-app/old-cache]\n", File.ReadAllText(PagePath("my-app/new-cache")));
    }

    [Fact]
    public async Task Write_page_refuses_missing_self_cyclic_and_misplaced_supersedes()
    {
        Assert.False((await CreatePage("my-app/v1", ToolValues.PageTypes.Decision, "方案 V1")).IsError);
        Assert.False((await CreatePage("my-app/v2", ToolValues.PageTypes.Decision, "方案 V2", supersedes: ["my-app/v1"])).IsError);
        Assert.False((await CreatePage("my-app/v3", ToolValues.PageTypes.Decision, "方案 V3", supersedes: ["my-app/v2"])).IsError);

        var missing = await CreatePage("my-app/missing-ref", ToolValues.PageTypes.Decision, "引用缺失", supersedes: ["my-app/nope"]);
        Assert.True(missing.IsError);
        Assert.Contains("supersedes: page my-app/nope does not exist", missing.Text);

        var self = await Service.Tools.WritePageAsync(new WritePageRequest
        {
            Op = ToolValues.WritePageOps.UpdateHeader,
            Page = "my-app/v1",
            Supersedes = ["my-app/v1"],
        });
        Assert.True(self.IsError);
        Assert.Contains("is this page itself", self.Text);

        var cycle = await Service.Tools.WritePageAsync(new WritePageRequest
        {
            Op = ToolValues.WritePageOps.UpdateHeader,
            Page = "my-app/v1",
            Supersedes = ["my-app/v3"],
        });
        Assert.True(cycle.IsError);
        Assert.Contains("cycle", cycle.Text, StringComparison.OrdinalIgnoreCase);

        var misplaced = await Service.Tools.WritePageAsync(new WritePageRequest
        {
            Op = ToolValues.WritePageOps.UpsertSection,
            Page = "my-app/v1",
            Section = "备注",
            Body = "内容",
            Supersedes = ["my-app/v2"],
        });
        Assert.True(misplaced.IsError);
        Assert.Contains("Parameter supersedes does not apply to op upsert_section", misplaced.Text);
    }

    [Fact]
    public async Task Format_check_reports_missing_self_and_cyclic_supersedes_and_clears_once_fixed()
    {
        Assert.False((await CreatePage("my-app/a", ToolValues.PageTypes.Decision, "决定甲")).IsError);
        Assert.False((await CreatePage("my-app/b", ToolValues.PageTypes.Decision, "决定乙", supersedes: ["my-app/a"])).IsError);

        // Inject broken, self and cyclic supersedes directly on disk as hand edits.
        File.WriteAllText(
            PagePath("my-app/a"),
            File.ReadAllText(PagePath("my-app/a")).Replace("type: decision\n", "supersedes: [my-app/missing, my-app/a, my-app/b]\ntype: decision\n"));

        var report = await Service.Tools.ConsolidationReportAsync();
        Assert.Contains("pages/my-app/a.md", report);
        Assert.Contains("my-app/missing", report);
        Assert.Contains("this page itself", report);
        Assert.Contains("cycle", report, StringComparison.OrdinalIgnoreCase);

        // Fix the hand edit: problems disappear from the format violations section.
        File.WriteAllText(
            PagePath("my-app/a"),
            File.ReadAllText(PagePath("my-app/a")).Replace("supersedes: [my-app/missing, my-app/a, my-app/b]\n", ""));
        var after = await Service.Tools.ConsolidationReportAsync();
        var heading = "### Format and rule violations\n";
        var start = after.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0, after);
        var rest = after[(start + heading.Length)..];
        var next = rest.IndexOf("\n### ", StringComparison.Ordinal);
        var violations = next < 0 ? rest : rest[..next];
        Assert.DoesNotContain("pages/my-app/a.md", violations);
    }

    [Fact]
    public async Task Page_browse_exposes_supersedes_superseded_by_and_effective_overturned_status()
    {
        Assert.False((await CreatePage("my-app/old-choice", ToolValues.PageTypes.Decision, "旧方案")).IsError);
        Assert.False((await CreatePage("my-app/new-choice", ToolValues.PageTypes.Decision, "新方案", supersedes: ["my-app/old-choice"])).IsError);
        Assert.False((await CreatePage("my-app/abandoned", ToolValues.PageTypes.Decision, "单独废止", sections: [("决定", "旧做法。"), ("Overturned", "已取消，不再使用。")])).IsError);

        var oldView = await Service.Browse.PageAsync("my-app", "old-choice");
        var newView = await Service.Browse.PageAsync("my-app", "new-choice");
        var abandonedView = await Service.Browse.PageAsync("my-app", "abandoned");
        var list = await Service.Browse.PagesAsync();

        Assert.True(oldView.Overturned);
        Assert.Empty(oldView.Supersedes);
        Assert.Equal([new RelatedPage("my-app/new-choice", true)], oldView.SupersededBy);

        Assert.False(newView.Overturned);
        Assert.Equal([new RelatedPage("my-app/old-choice", true)], newView.Supersedes);
        Assert.Empty(newView.SupersededBy);

        Assert.True(abandonedView.Overturned);
        Assert.Empty(abandonedView.SupersededBy);

        var byId = list.Pages.ToDictionary(p => p.Id);
        Assert.True(byId["my-app/old-choice"].Overturned);
        Assert.False(byId["my-app/new-choice"].Overturned);
        Assert.True(byId["my-app/abandoned"].Overturned);
    }

    [Fact]
    public async Task Workstream_brief_moves_decisions_superseded_by_another_page_into_overturned()
    {
        Assert.False((await CreatePage("my-app/old-choice", ToolValues.PageTypes.Decision, "旧决定")).IsError);
        Assert.False((await CreatePage("my-app/new-choice", ToolValues.PageTypes.Decision, "新决定", supersedes: ["my-app/old-choice"])).IsError);

        var result = await Service.Tools.RecallAsync(new RecallRequest { Workstream = "my-app" });

        Assert.False(result.IsError, result.Text);
        Assert.Contains("Overturned: my-app/old-choice", result.Text);
        Assert.DoesNotContain("- my-app/old-choice:", result.Text);
        Assert.Contains("- my-app/new-choice: 新决定", result.Text);
    }
}
