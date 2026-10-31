using System.Text.RegularExpressions;

namespace TacitMemory.Core.Tests;

public sealed partial class PageTests : IDisposable
{
    private readonly TestHome _home = new();
    private readonly MemoryService _service;

    public PageTests()
    {
        _home.InitReady();
        _service = _home.Tacit().OpenService(_ => { });
    }

    public void Dispose()
    {
        _service.Dispose();
        _home.Dispose();
    }

    private string Memory => _home.DefaultMemory;

    private static WritePageRequest Create(string page, string title = "数据库升级后扩展丢失", string[]? aliases = null,
        PageSectionInput[]? sections = null, string[]? related = null) => new()
        {
            Op = "create",
            Page = page,
            Title = title,
            Aliases = aliases ?? ["全文检索插件失效", "升级后搜索不到中文"],
            Sections = sections ?? [new("现象", "升级后中文搜索没有结果。"), new("根因", "PGroonga 扩展没有随升级重装。")],
            Related = related,
        };

    private string Git(params string[] args) => TestHome.Git(Memory, args);

    [Fact]
    public async Task Creating_a_page_writes_it_and_commits_only_that_file()
    {
        File.WriteAllText(Path.Combine(Memory, "notes.txt"), "staged by the user\n");
        Git("add", "notes.txt");

        var result = await _service.WritePageAsync(Create("tacit-memory/db-upgrade"));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(
            """
            ---
            title: 数据库升级后扩展丢失
            aliases: [全文检索插件失效, 升级后搜索不到中文]
            dates: { first: 2026-11-02, last: 2026-11-02 }
            ---

            ## 现象
            升级后中文搜索没有结果。

            ## 根因
            PGroonga 扩展没有随升级重装。

            """.ReplaceLineEndings("\n"),
            File.ReadAllText(Path.Combine(Memory, "pages", "tacit-memory", "db-upgrade.md")));
        Assert.Equal("pages: create tacit-memory/db-upgrade", Git("log", "-1", "--format=%s").Trim());
        Assert.Equal("tacit <tacit@localhost>", Git("log", "-1", "--format=%an <%ae>").Trim());
        Assert.Equal("pages/tacit-memory/db-upgrade.md", Git("show", "--name-only", "--format=", "HEAD").Trim());
        Assert.Equal("A  notes.txt", Git("status", "--porcelain").Trim());
    }

    [Fact]
    public async Task Reading_a_page_returns_its_header_and_every_section_with_the_codes_given_at_creation()
    {
        var created = await _service.WritePageAsync(Create("tacit-memory/db-upgrade", related: null));
        var codes = CodePattern().Matches(created.Text).Select(match => match.Groups[1].Value).ToList();

        var read = await _service.RecallAsync(new RecallRequest { Page = "tacit-memory/db-upgrade" });

        Assert.False(read.IsError, read.Text);
        Assert.Equal(2, codes.Count);
        Assert.Contains("# tacit-memory/db-upgrade: 数据库升级后扩展丢失", read.Text);
        Assert.Contains("Aliases: 全文检索插件失效; 升级后搜索不到中文", read.Text);
        Assert.Contains("Dates: 2026-11-02 to 2026-11-02", read.Text);
        Assert.Contains($"## 现象 [{codes[0]}]\n升级后中文搜索没有结果。", read.Text);
        Assert.Contains($"## 根因 [{codes[1]}]\nPGroonga 扩展没有随升级重装。", read.Text);
        Assert.Empty(Git("status", "--porcelain").Trim());
    }

    [Fact]
    public async Task Header_values_that_need_quoting_read_back_unchanged()
    {
        await _service.WritePageAsync(Create("tacit-memory/quoting", title: "升级: 坑 #1", aliases: ["a, b", "\"引号\"", "- dash"]));

        var read = await _service.RecallAsync(new RecallRequest { Page = "tacit-memory/quoting" });

        Assert.Contains("# tacit-memory/quoting: 升级: 坑 #1\n", read.Text);
        Assert.Contains("Aliases: a, b; \"引号\"; - dash\n", read.Text);
    }

    [Fact]
    public async Task Every_problem_with_a_new_page_is_reported_at_once_and_nothing_is_written()
    {
        var before = Git("rev-parse", "HEAD");

        var result = await _service.WritePageAsync(Create(
            "tacit-memory/db-upgrade",
            title: new string('长', 51),
            aliases: ["只有一个"],
            sections: [new("现象", "a"), new("现象", "b")],
            related: ["tacit-memory/missing"]));

        Assert.True(result.IsError);
        Assert.StartsWith("Refused:\n", result.Text);
        Assert.Contains("51 characters; the limit is 50", result.Text);
        Assert.Contains("at least 2 distinct aliases", result.Text);
        Assert.Contains("\"现象\" is used more than once", result.Text);
        Assert.Contains("page tacit-memory/missing does not exist", result.Text);
        Assert.Contains("\nNext step: ", result.Text);
        Assert.False(File.Exists(Path.Combine(Memory, "pages", "tacit-memory", "db-upgrade.md")));
        Assert.Equal(before, Git("rev-parse", "HEAD"));
    }

    [Theory]
    [InlineData("db-upgrade", "must be <project>/<slug>")]
    [InlineData("tacit-memory/DB_Upgrade", "for example db-upgrade")]
    [InlineData("_private/db-upgrade", "must not start with '.' or '_'")]
    [InlineData(".hidden/db-upgrade", "must not start with '.' or '_'")]
    public async Task Malformed_page_ids_are_refused_with_a_compliant_example(string page, string expected)
    {
        var result = await _service.WritePageAsync(Create(page));

        Assert.True(result.IsError);
        Assert.Contains(expected, result.Text);
    }

    [Fact]
    public async Task Project_names_are_lowercased_and_topics_are_a_project()
    {
        Assert.False((await _service.WritePageAsync(Create("Tacit-Memory/db-upgrade"))).IsError);
        Assert.False((await _service.WritePageAsync(Create("_topics/pgroonga"))).IsError);

        Assert.True(File.Exists(Path.Combine(Memory, "pages", "tacit-memory", "db-upgrade.md")));
        Assert.True(File.Exists(Path.Combine(Memory, "pages", "_topics", "pgroonga.md")));
    }

    [Fact]
    public async Task Creating_an_existing_page_is_refused_and_lists_its_sections()
    {
        await _service.WritePageAsync(Create("tacit-memory/db-upgrade"));

        var result = await _service.WritePageAsync(Create("tacit-memory/db-upgrade", sections: [new("别的", "内容")]));

        Assert.True(result.IsError);
        Assert.Contains("Page tacit-memory/db-upgrade already exists.", result.Text);
        Assert.Matches(@"Related:\n- 现象 \[[0-9a-f]{6}\]\n- 根因 \[[0-9a-f]{6}\]", result.Text);
    }

    [Fact]
    public async Task Related_pages_that_exist_are_written_into_the_header()
    {
        await _service.WritePageAsync(Create("_topics/pgroonga"));

        await _service.WritePageAsync(Create("tacit-memory/db-upgrade", related: ["_topics/pgroonga"]));

        Assert.Contains("related: [_topics/pgroonga]\n", File.ReadAllText(Path.Combine(Memory, "pages", "tacit-memory", "db-upgrade.md")));
    }

    [Fact]
    public async Task A_body_heading_that_would_split_the_page_is_refused_but_headings_in_code_are_fine()
    {
        var split = await _service.WritePageAsync(Create("tacit-memory/split", sections: [new("现象", "前文\n## 新节\n后文")]));
        var fenced = await _service.WritePageAsync(Create("tacit-memory/fenced", sections: [new("脚本", "```bash\n# comment\n## also comment\n```\n### 细节")]));

        Assert.True(split.IsError);
        Assert.Contains("'#' or '##' heading", split.Text);
        Assert.False(fenced.IsError, fenced.Text);
    }

    [Fact]
    public async Task A_page_without_readable_front_matter_is_still_read_titled_after_its_file()
    {
        var path = Path.Combine(Memory, "pages", "tacit-memory", "broken.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "---\ntitle: [unclosed\n---\n\n## 现象\n内容\n");

        var read = await _service.RecallAsync(new RecallRequest { Page = "tacit-memory/broken" });

        Assert.False(read.IsError, read.Text);
        Assert.Contains("# tacit-memory/broken: broken\n", read.Text);
        Assert.Contains("header cannot be read", read.Text);
        Assert.Matches(@"## 现象 \[[0-9a-f]{6}\]\n内容", read.Text);
    }

    [Fact]
    public async Task Reading_a_missing_page_is_refused()
    {
        var read = await _service.RecallAsync(new RecallRequest { Page = "tacit-memory/nothing" });

        Assert.True(read.IsError);
        Assert.StartsWith("Refused: Page tacit-memory/nothing does not exist.", read.Text);
    }

    [Theory]
    [InlineData(null, "Missing parameter op.")]
    [InlineData("rewrite", "Unknown op \"rewrite\".")]
    public async Task A_missing_or_unknown_op_is_refused(string? op, string expected)
    {
        var result = await _service.WritePageAsync(Create("tacit-memory/db-upgrade") with { Op = op });

        Assert.True(result.IsError);
        Assert.StartsWith($"Refused: {expected}", result.Text);
    }

    [Fact]
    public async Task Writes_arriving_together_run_one_after_another()
    {
        var writes = Enumerable.Range(1, 5).Select(n => _service.WritePageAsync(Create($"tacit-memory/page-{n}")));

        var results = await Task.WhenAll(writes);

        Assert.All(results, result => Assert.False(result.IsError, result.Text));
        Assert.Equal("6", Git("rev-list", "--count", "HEAD").Trim());
    }

    [GeneratedRegex(@"\[([0-9a-f]{6})\]")]
    private static partial Regex CodePattern();
}
