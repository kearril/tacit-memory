namespace TacitMemory.Core.Tests;

/// <summary>The detail pages as the memory page reads them: the list, one page with its sections, and pages that read only in part.</summary>
public sealed class MemoryPagesTests : IDisposable
{
    private readonly TestHome _home = new();
    private MemoryService? _service;

    public MemoryPagesTests() => _home.InitReady();

    public void Dispose()
    {
        _service?.Dispose();
        _home.Dispose();
    }

    private MemoryService Service => _service ??= _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

    private string PagePath(string id) => Path.Combine(_home.DefaultMemory, "pages", id + ".md");

    private async Task Create(string page, string title, string type, string[]? related = null, params PageSectionInput[] sections)
    {
        var result = await Service.WritePageAsync(new WritePageRequest
        {
            Op = "create",
            Type = type,
            Page = page,
            Title = title,
            Aliases = [title + "别名", title + "又名"],
            Sections = sections,
            Related = related,
        });
        Assert.False(result.IsError, result.Text);
    }

    [Fact]
    public async Task The_list_has_every_page_with_its_header_dates_and_uses_and_topics_under_their_own_name()
    {
        await Create("ops/rollback", "部署回滚", ToolValues.PageTypes.Decision, null, new PageSectionInput("决定", "失败先回滚。"));
        await Create("_topics/pgroonga", "PGroonga", ToolValues.PageTypes.Entity, null, new PageSectionInput("现状", "暂不采用。"));
        await Service.RecallAsync(new RecallRequest { Page = "ops/rollback" });

        var list = await Service.Browse.PagesAsync();

        Assert.Equal(["_topics/pgroonga", "ops/rollback"], list.Pages.Select(page => page.Id));
        var topic = list.Pages[0];
        Assert.Equal(("_topics", "PGroonga", "entity"), (topic.Workstream, topic.Title, topic.Type));
        var rollback = list.Pages[1];
        Assert.Equal(["部署回滚别名", "部署回滚又名"], rollback.Aliases);
        Assert.Equal(TestHome.Today.ToString("yyyy-MM-dd"), rollback.Changed);
        Assert.Equal(rollback.Changed, rollback.First);
        Assert.Equal(1, rollback.Uses);
        Assert.Equal(0, topic.Uses);
    }

    [Fact]
    public async Task A_page_has_its_header_related_pages_and_sections_with_codes_dates_and_uses()
    {
        await Create("ops/deploy", "部署", ToolValues.PageTypes.Entity, null, new PageSectionInput("现状", "蓝绿部署。"));
        await Create(
            "ops/rollback", "部署回滚", ToolValues.PageTypes.Decision, ["ops/deploy"],
            new PageSectionInput("决定", "失败先回滚。\n<!-- 2026-11-01 原话「先回滚」 -->"), new PageSectionInput("理由", "最快止损。"));
        File.WriteAllText(PagePath("ops/rollback"), File.ReadAllText(PagePath("ops/rollback")).Replace("related: [ops/deploy]", "related: [ops/deploy, ops/gone]"));
        await Service.RecallAsync(new RecallRequest { Page = "ops/rollback" });

        var page = await Service.Browse.PageAsync("ops", "rollback");

        Assert.Equal(("ops/rollback", "ops", "部署回滚", "decision"), (page.Id, page.Workstream, page.Title, page.Type));
        Assert.Equal([new RelatedPage("ops/deploy", true), new RelatedPage("ops/gone", false)], page.Related);
        Assert.Equal(["决定", "理由"], page.Sections.Select(section => section.Name));
        var decision = page.Sections[0];
        Assert.Matches("^[0-9a-f]{6}$", decision.Code);
        Assert.Equal(TestHome.Today.ToString("yyyy-MM-dd"), decision.Changed);
        Assert.Equal(1, decision.Uses);
        Assert.Contains("<!-- 2026-11-01 原话「先回滚」 -->", decision.Body);
        Assert.Equal(2, page.Uses);
        Assert.EndsWith(Path.Combine("pages", "ops", "rollback.md"), page.File.Path);
        Assert.Contains(page.File.Problems, problem => problem.Kind == "related");
    }

    [Fact]
    public async Task A_page_whose_header_is_broken_or_not_utf8_reads_in_part_with_its_problems()
    {
        Directory.CreateDirectory(Path.Combine(_home.DefaultMemory, "pages", "ops"));
        File.WriteAllText(PagePath("ops/broken"), "---\ntitle: [unclosed\n---\n\n## 现状\n还能读。\n");
        File.WriteAllBytes(PagePath("ops/binary"), [0xff, 0xfe, 0x41, 0x80]);

        var broken = await Service.Browse.PageAsync("ops", "broken");
        var binary = await Service.Browse.PageAsync("ops", "binary");

        Assert.Equal("broken", broken.Title);
        Assert.Equal("还能读。", Assert.Single(broken.Sections).Body.Trim());
        Assert.Contains(broken.File.Problems, problem => problem.Kind == "yaml header");
        Assert.Equal("binary", binary.Title);
        Assert.Empty(binary.Sections);
        Assert.Equal("encoding", Assert.Single(binary.File.Problems).Kind);
        Assert.Contains((await Service.Browse.PagesAsync()).Pages, page => page.Id == "ops/binary" && page.Title == "binary");
    }

    [Fact]
    public async Task A_missing_page_is_not_found_and_a_bad_id_is_refused()
    {
        var missing = await Assert.ThrowsAsync<TacitException>(() => Service.Browse.PageAsync("ops", "nothing"));
        var bad = await Assert.ThrowsAsync<TacitException>(() => Service.Browse.PageAsync("Ops!", "x"));

        Assert.Equal(TacitError.MemoryNotFound, missing.Error);
        Assert.Equal(TacitError.RequestInvalid, bad.Error);
    }

    [Fact]
    public async Task Reading_the_list_and_a_page_counts_nothing()
    {
        await Create("ops/rollback", "部署回滚", ToolValues.PageTypes.Decision, null, new PageSectionInput("决定", "失败先回滚。"));
        var before = await Service.PageCountsAsync();

        await Service.Browse.PagesAsync();
        await Service.Browse.PageAsync("ops", "rollback");

        Assert.Equal(before, await Service.PageCountsAsync());
    }
}
