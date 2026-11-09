using System.Text.RegularExpressions;

namespace TacitMemory.Core.Tests;

public sealed partial class PageTests : IDisposable
{
    private readonly TestHome _home = new();
    private MemoryService _service;

    public PageTests()
    {
        _home.InitReady();
        _service = _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);
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
            Type = ToolValues.PageTypes.Incident,
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
            type: incident
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
        Assert.Contains($"## 现象 [{codes[0]}] (last changed 2026-11-02)\n升级后中文搜索没有结果。", read.Text);
        Assert.Contains($"## 根因 [{codes[1]}] (last changed 2026-11-02)\nPGroonga 扩展没有随升级重装。", read.Text);
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
    public async Task A_page_without_a_readable_header_is_still_read_titled_after_its_file()
    {
        var path = Path.Combine(Memory, "pages", "tacit-memory", "broken.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "---\ntitle: [unclosed\n---\n\n## 现象\n内容\n");

        var read = await _service.RecallAsync(new RecallRequest { Page = "tacit-memory/broken" });

        Assert.False(read.IsError, read.Text);
        Assert.Contains("# tacit-memory/broken: broken\n", read.Text);
        Assert.Contains("header cannot be read", read.Text);
        Assert.Matches(@"## 现象 \[[0-9a-f]{6}\] \(last changed \d{4}-\d{2}-\d{2}\)\n内容", read.Text);
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
    public async Task An_unknown_parameter_is_refused_by_name_and_nothing_is_written()
    {
        const string page = "tacit-memory/db-upgrade";
        await _service.WritePageAsync(Create(page));
        var before = PageText(page);
        var commits = Git("rev-list", "--count", "HEAD").Trim();

        var result = await _service.WritePageAsync(Upsert(page, "根因", "新的根因。") with { UnknownParameters = ["content"] });

        Assert.True(result.IsError);
        Assert.StartsWith("Refused: Unknown parameter content.\nNext step: ", result.Text);
        Assert.Equal(before, PageText(page));
        Assert.Equal(commits, Git("rev-list", "--count", "HEAD").Trim());
        Assert.Empty(Git("status", "--porcelain").Trim());
    }

    [Fact]
    public async Task Unknown_names_come_first_with_the_op_and_misplaced_problems_in_one_refusal()
    {
        var invalid = await _service.WritePageAsync(new WritePageRequest { Op = "rewrite", Page = "tacit-memory/db-upgrade", UnknownParameters = ["content", "limit"] });
        var misplaced = await _service.WritePageAsync(Create("tacit-memory/db-upgrade") with { Section = "根因", UnknownParameters = ["content"] });

        Assert.StartsWith("Refused:\n- Unknown parameter content.\n- Unknown parameter limit.\n- Unknown op \"rewrite\".\nNext step: ", invalid.Text);
        Assert.StartsWith("Refused:\n- Unknown parameter content.\n- Parameter section does not apply to op create.\nNext step: ", misplaced.Text);
    }

    [Fact]
    public async Task The_next_step_lists_the_parameters_of_a_valid_op_or_every_parameter_when_the_op_is_missing()
    {
        var withOp = await _service.WritePageAsync(Upsert("tacit-memory/db-upgrade", "根因", "新的根因。") with { UnknownParameters = ["content"] });
        var withoutOp = await _service.WritePageAsync(new WritePageRequest { Page = "tacit-memory/db-upgrade", UnknownParameters = ["content"] });

        Assert.Contains("Call write_page again with only the parameters for op upsert_section: page, section, body, hash, confirm_no_secret.", withOp.Text);
        Assert.Contains("Call write_page again with op set to one of: create, upsert_section, delete_section, update_header; its parameters are: op, page, title, type, aliases, sections, related, confirm_no_secret, section, body, hash, add_aliases.", withoutOp.Text);
    }

    [Fact]
    public async Task Writes_arriving_together_run_one_after_another()
    {
        var writes = Enumerable.Range(1, 5).Select(n => _service.WritePageAsync(Create($"tacit-memory/page-{n}")));

        var results = await Task.WhenAll(writes);

        Assert.All(results, result => Assert.False(result.IsError, result.Text));
        Assert.Equal("6", Git("rev-list", "--count", "HEAD").Trim());
    }


    private string PageText(string page) => File.ReadAllText(Path.Combine(Memory, "pages", page + ".md"));

    private async Task<string> CodeOf(string page, string section)
    {
        var read = await _service.RecallAsync(new RecallRequest { Page = page });
        var code = Regex.Match(read.Text, $@"## {Regex.Escape(section)} \[([0-9a-f]{{6}}(?:-\d+)?)\]").Groups[1].Value;
        Assert.Matches(@"^[0-9a-f]{6}(-\d+)?$", code);
        return code;
    }

    private static WritePageRequest Upsert(string page, string section, string body, string? hash = null) =>
        new() { Op = "upsert_section", Page = page, Section = section, Body = body, Hash = hash };

    private static WritePageRequest Delete(string page, string section, string? hash = null) =>
        new() { Op = "delete_section", Page = page, Section = section, Hash = hash };

    private static WritePageRequest Header(string page, string? title = null, string[]? addAliases = null, string[]? related = null) =>
        new() { Op = "update_header", Page = page, Title = title, AddAliases = addAliases, Related = related };

    private void MoveClockTo(DateOnly day) =>
        _home.Clock.Advance(new DateTimeOffset(day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero) - _home.Clock.GetUtcNow());

    [Fact]
    public async Task Replacing_a_section_with_its_hash_changes_only_that_section()
    {
        const string page = "tacit-memory/db-upgrade";
        await _service.WritePageAsync(Create(page));
        var before = PageText(page);
        var hash = await CodeOf(page, "根因");

        var result = await _service.WritePageAsync(Upsert(page, "根因", "新的根因。", hash));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(before.Replace("PGroonga 扩展没有随升级重装。", "新的根因。"), PageText(page));
        var code = await CodeOf(page, "根因");
        Assert.NotEqual(hash, code);
        Assert.Contains($"Replaced section 根因 [{code}].", result.Text);
        Assert.Equal("pages: upsert tacit-memory/db-upgrade 根因", Git("log", "-1", "--format=%s").Trim());
        Assert.Empty(Git("status", "--porcelain").Trim());
    }

    [Fact]
    public async Task Every_successful_write_sets_dates_last_to_today()
    {
        const string page = "tacit-memory/db-upgrade";
        await _service.WritePageAsync(Create(page));
        MoveClockTo(new DateOnly(2026, 11, 5));
        var hash = await CodeOf(page, "根因");

        var result = await _service.WritePageAsync(Upsert(page, "根因", "新的根因。", hash));

        Assert.False(result.IsError, result.Text);
        Assert.Contains("dates: { first: 2026-11-02, last: 2026-11-05 }\n", PageText(page));
    }

    [Fact]
    public async Task Replacing_a_section_without_its_hash_is_refused_and_shows_it_with_its_code()
    {
        const string page = "tacit-memory/db-upgrade";
        await _service.WritePageAsync(Create(page));
        var before = PageText(page);

        var result = await _service.WritePageAsync(Upsert(page, "现象", "覆盖。"));

        var code = await CodeOf(page, "现象");
        Assert.True(result.IsError);
        Assert.Contains("Section 现象 exists; give its hash to replace it.", result.Text);
        Assert.Contains($"Related:\n## 现象 [{code}]\n升级后中文搜索没有结果。", result.Text);
        Assert.Equal(before, PageText(page));
    }

    [Fact]
    public async Task A_hash_that_no_longer_matches_is_refused_and_shows_the_section_as_it_is_now()
    {
        const string page = "tacit-memory/db-upgrade";
        await _service.WritePageAsync(Create(page));
        var before = PageText(page);

        var result = await _service.WritePageAsync(Upsert(page, "现象", "覆盖。", "000000"));

        var code = await CodeOf(page, "现象");
        Assert.True(result.IsError);
        Assert.Contains("The hash for section 现象 does not match: it changed since it was read.", result.Text);
        Assert.Contains($"Related:\n## 现象 [{code}]\n升级后中文搜索没有结果。", result.Text);
        Assert.Equal(before, PageText(page));
    }

    [Fact]
    public async Task A_section_the_page_lacks_is_appended_at_the_end()
    {
        const string page = "tacit-memory/db-upgrade";
        await _service.WritePageAsync(Create(page));
        var before = PageText(page);

        var result = await _service.WritePageAsync(Upsert(page, "后续", "待办。"));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(before + "\n## 后续\n待办。\n", PageText(page));
        var code = await CodeOf(page, "后续");
        Assert.Contains($"Added section 后续 [{code}].", result.Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Upsert_refuses_a_short_code_given_as_the_section_name_and_says_which_section_it_is(bool withHash)
    {
        const string page = "tacit-memory/db-upgrade";
        await _service.WritePageAsync(Create(page));
        var before = PageText(page);
        var head = Git("rev-parse", "HEAD");
        var code = await CodeOf(page, "根因");

        var result = await _service.WritePageAsync(Upsert(page, code, "覆盖。", withHash ? code : null));

        Assert.True(result.IsError);
        Assert.Contains("Parameter section \"" + code + "\" is the short code of section 根因, not a section name.", result.Text);
        Assert.Contains($"Call write_page op=upsert_section again with section set to the name 根因 and hash set to the short code {code}.", result.Text);
        Assert.Equal(before, PageText(page));
        Assert.Equal(head, Git("rev-parse", "HEAD"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("## 另一节\n内容。")]
    public async Task Upsert_refuses_a_short_code_as_the_section_name_before_checking_the_body(string body)
    {
        const string page = "tacit-memory/db-upgrade";
        await _service.WritePageAsync(Create(page));
        var before = PageText(page);
        var head = Git("rev-parse", "HEAD");
        var code = await CodeOf(page, "根因");

        var result = await _service.WritePageAsync(Upsert(page, code, body));

        Assert.True(result.IsError);
        Assert.StartsWith($"Refused: Parameter section \"{code}\" is the short code of section 根因, not a section name.", result.Text);
        Assert.Equal(before, PageText(page));
        Assert.Equal(head, Git("rev-parse", "HEAD"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Delete_refuses_a_short_code_given_as_the_section_name_and_says_which_section_it_is(bool withHash)
    {
        const string page = "tacit-memory/db-upgrade";
        await _service.WritePageAsync(Create(page));
        var before = PageText(page);
        var head = Git("rev-parse", "HEAD");
        var code = await CodeOf(page, "现象");

        var result = await _service.WritePageAsync(Delete(page, code, withHash ? code : null));

        Assert.True(result.IsError);
        Assert.Contains("is the short code of section 现象, not a section name.", result.Text);
        Assert.Contains($"Call write_page op=delete_section again with section set to the name 现象 and hash set to the short code {code}.", result.Text);
        Assert.Equal(before, PageText(page));
        Assert.Equal(head, Git("rev-parse", "HEAD"));
    }

    [Fact]
    public async Task A_short_code_in_capitals_or_padded_with_spaces_is_still_recognised()
    {
        const string page = "tacit-memory/db-upgrade";
        await _service.WritePageAsync(Create(page));
        var before = PageText(page);
        var code = await CodeOf(page, "根因");

        var upper = await _service.WritePageAsync(Upsert(page, "  " + code.ToUpperInvariant() + " ", "覆盖。"));
        var deleted = await _service.WritePageAsync(Delete(page, code.ToUpperInvariant()));

        Assert.Contains("is the short code of section 根因, not a section name.", upper.Text);
        Assert.Contains("is the short code of section 根因, not a section name.", deleted.Text);
        Assert.Equal(before, PageText(page));
    }

    [Fact]
    public async Task A_section_actually_named_like_a_code_is_rewritten_by_its_name()
    {
        const string page = "tacit-memory/db-upgrade";
        await _service.WritePageAsync(Create(page));
        var code = await CodeOf(page, "根因");
        const string other = "tacit-memory/code-named";
        await _service.WritePageAsync(Create(other, sections: [new("根因", "PGroonga 扩展没有随升级重装。"), new(code, "旧内容。")]));
        var hash = await CodeOf(other, code);

        var result = await _service.WritePageAsync(Upsert(other, code, "新内容。", hash));

        Assert.False(result.IsError, result.Text);
        Assert.Contains("新内容。", PageText(other));
        Assert.DoesNotContain("旧内容。", PageText(other));
    }

    [Fact]
    public async Task A_value_shaped_like_a_code_that_no_section_has_is_a_section_name_as_before()
    {
        const string page = "tacit-memory/db-upgrade";
        await _service.WritePageAsync(Create(page));

        var added = await _service.WritePageAsync(Upsert(page, "a1b2c3", "待办。"));
        var deleted = await _service.WritePageAsync(Delete(page, "fedcba"));

        Assert.False(added.IsError, added.Text);
        Assert.Contains("Added section a1b2c3", added.Text);
        Assert.True(deleted.IsError);
        Assert.Contains("Section \"fedcba\" does not exist on page", deleted.Text);
    }

    [Fact]
    public async Task Writing_a_section_with_its_current_text_changes_nothing_and_commits_nothing()
    {
        const string page = "tacit-memory/db-upgrade";
        await _service.WritePageAsync(Create(page));
        var hash = await CodeOf(page, "现象");
        var head = Git("rev-parse", "HEAD");

        var result = await _service.WritePageAsync(Upsert(page, "现象", "升级后中文搜索没有结果。", hash));

        Assert.False(result.IsError, result.Text);
        Assert.Contains("Nothing changed", result.Text);
        Assert.Equal(head, Git("rev-parse", "HEAD"));
    }

    [Fact]
    public async Task Deleting_a_middle_section_removes_it_with_its_separating_line()
    {
        const string page = "tacit-memory/db-upgrade";
        await _service.WritePageAsync(Create(page, sections: [new("现象", "升级后中文搜索没有结果。"), new("根因", "PGroonga 扩展没有随升级重装。"), new("方案", "重装扩展。")]));
        var before = PageText(page);
        var hash = await CodeOf(page, "根因");

        var result = await _service.WritePageAsync(Delete(page, "根因", hash));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(before.Replace("## 根因\nPGroonga 扩展没有随升级重装。\n\n", ""), PageText(page));
    }

    [Fact]
    public async Task Deleting_the_last_section_removes_it_and_the_line_before_it()
    {
        const string page = "tacit-memory/db-upgrade";
        await _service.WritePageAsync(Create(page));
        var before = PageText(page);
        var hash = await CodeOf(page, "根因");

        var result = await _service.WritePageAsync(Delete(page, "根因", hash));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(before.Replace("\n## 根因\nPGroonga 扩展没有随升级重装。\n", ""), PageText(page));
    }

    [Fact]
    public async Task Deleting_a_section_needs_its_current_hash()
    {
        const string page = "tacit-memory/db-upgrade";
        await _service.WritePageAsync(Create(page));
        var before = PageText(page);

        var withoutHash = await _service.WritePageAsync(Delete(page, "根因"));
        var stale = await _service.WritePageAsync(Delete(page, "根因", "000000"));

        var code = await CodeOf(page, "根因");
        Assert.True(withoutHash.IsError);
        Assert.Contains("Section 根因 exists; give its hash to delete it.", withoutHash.Text);
        Assert.Contains($"Related:\n## 根因 [{code}]\nPGroonga 扩展没有随升级重装。", withoutHash.Text);
        Assert.True(stale.IsError);
        Assert.Contains("The hash for section 根因 does not match", stale.Text);
        Assert.Equal(before, PageText(page));
    }

    [Fact]
    public async Task Deleting_a_missing_section_is_refused_and_lists_the_sections()
    {
        const string page = "tacit-memory/db-upgrade";
        await _service.WritePageAsync(Create(page));

        var result = await _service.WritePageAsync(Delete(page, "别的", "000000"));

        Assert.True(result.IsError);
        Assert.Contains("Section \"别的\" does not exist on page tacit-memory/db-upgrade.", result.Text);
        Assert.Matches(@"Related:\n- 现象 \[[0-9a-f]{6}\]\n- 根因 \[[0-9a-f]{6}\]", result.Text);
    }

    [Fact]
    public async Task Title_aliases_and_related_are_changed_in_the_header_and_duplicate_aliases_are_dropped()
    {
        const string page = "tacit-memory/db-upgrade";
        await _service.WritePageAsync(Create("tacit-memory/pgroonga"));
        await _service.WritePageAsync(Create(page));

        var result = await _service.WritePageAsync(Header(page, title: "升级后扩展丢失", addAliases: ["升级后搜索不到中文", "pgroonga 失效"], related: ["tacit-memory/pgroonga"]));

        Assert.False(result.IsError, result.Text);
        var read = await _service.RecallAsync(new RecallRequest { Page = page });
        Assert.Contains("# tacit-memory/db-upgrade: 升级后扩展丢失\n", read.Text);
        Assert.Contains("Aliases: 全文检索插件失效; 升级后搜索不到中文; pgroonga 失效\n", read.Text);
        Assert.Contains("Related: tacit-memory/pgroonga\n", read.Text);
        Assert.Contains("related: [tacit-memory/pgroonga]\n", PageText(page));
        Assert.Equal("pages: header tacit-memory/db-upgrade", Git("log", "-1", "--format=%s").Trim());
    }

    [Fact]
    public async Task Related_replaces_the_whole_list_and_an_empty_list_removes_it()
    {
        const string page = "tacit-memory/db-upgrade";
        await _service.WritePageAsync(Create("tacit-memory/pgroonga"));
        await _service.WritePageAsync(Create("tacit-memory/other"));
        await _service.WritePageAsync(Create(page, related: ["tacit-memory/pgroonga"]));

        await _service.WritePageAsync(Header(page, related: ["tacit-memory/other"]));
        Assert.Contains("related: [tacit-memory/other]\n", PageText(page));
        Assert.DoesNotContain("tacit-memory/pgroonga", PageText(page));

        await _service.WritePageAsync(Header(page, related: []));
        Assert.DoesNotContain("related:", PageText(page));
    }

    [Fact]
    public async Task An_update_header_needs_a_field_to_change_and_cannot_replace_aliases()
    {
        const string page = "tacit-memory/db-upgrade";
        await _service.WritePageAsync(Create(page));
        var before = PageText(page);

        var empty = await _service.WritePageAsync(Header(page));
        var replace = await _service.WritePageAsync(Header(page) with { Aliases = ["别名一", "别名二"] });

        Assert.True(empty.IsError);
        Assert.Contains("Give at least one of title, type, add_aliases or related.", empty.Text);
        Assert.True(replace.IsError);
        Assert.Contains("update_header does not replace aliases; pass add_aliases to add some.", replace.Text);
        Assert.Equal(before, PageText(page));
    }

    [Fact]
    public async Task Header_values_meet_the_same_limits_as_create()
    {
        const string page = "tacit-memory/db-upgrade";
        await _service.WritePageAsync(Create(page));
        var before = PageText(page);

        var result = await _service.WritePageAsync(Header(page, title: new string('长', 51), addAliases: [new string('a', 31)]));

        Assert.True(result.IsError);
        Assert.Contains("51 characters; the limit is 50", result.Text);
        Assert.Contains("at most 30 characters", result.Text);
        Assert.Equal(before, PageText(page));
    }

    [Fact]
    public async Task A_page_with_a_broken_header_is_read_but_every_write_to_it_is_refused()
    {
        const string page = "tacit-memory/broken";
        var path = Path.Combine(Memory, "pages", "tacit-memory", "broken.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "---\ntitle: [unclosed\n---\n\n## 现象\n内容\n");
        var before = File.ReadAllText(path);
        var section = await _service.WritePageAsync(Upsert(page, "现象", "改。", "000000"));

        var header = await _service.WritePageAsync(Header(page, title: "新标题"));

        Assert.True(section.IsError);
        Assert.Contains("cannot be read", section.Text);
        Assert.Contains("\nNext step: Ask the user to fix the YAML header", section.Text);
        Assert.True(header.IsError);
        Assert.Contains("\nNext step: Ask the user to fix the YAML header", header.Text);
        Assert.Equal(before, File.ReadAllText(path));
    }

    [Fact]
    public async Task Lines_the_service_does_not_own_stay_byte_identical_and_user_edits_are_marked_in_the_commit()
    {
        const string page = "tacit-memory/hand";
        var path = Path.Combine(Memory, "pages", "tacit-memory", "hand.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var original = """
            ---
            title: 手写标题
            status: draft
            aliases: [a1, a2]
            dates: { first: 2026-10-01, last: 2026-10-01 }
            # 手写的页头注释
            ---

            前言：人工写的段落。

            ## 现象
            ```bash
            ## 不是小节
            ```

            ## 根因
            内容。
            """.ReplaceLineEndings("\n") + "\n";
        File.WriteAllText(path, original);

        var result = await _service.WritePageAsync(Header(page, addAliases: ["新别名"]));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(
            original.Replace("aliases: [a1, a2]", "aliases: [a1, a2, 新别名]").Replace("last: 2026-10-01", "last: 2026-11-02"),
            File.ReadAllText(path));
        Assert.Equal("pages: header tacit-memory/hand (with user edits)", Git("log", "-1", "--format=%s").Trim());
        Assert.Empty(Git("status", "--porcelain").Trim());
    }

    [Fact]
    public async Task A_write_on_a_later_day_moves_dates_last_and_keeps_dates_first()
    {
        await _service.WritePageAsync(Create("tacit-memory/dated"));
        _home.Clock.Advance(TimeSpan.FromDays(1));

        var result = await _service.WritePageAsync(new WritePageRequest { Op = "update_header", Page = "tacit-memory/dated", Title = "升级后的标题" });

        Assert.False(result.IsError, result.Text);
        var text = File.ReadAllText(Path.Combine(Memory, "pages", "tacit-memory", "dated.md"));
        Assert.Contains("first: 2026-11-02", text);
        Assert.Contains("last: 2026-11-03", text);

        _home.Clock.Advance(TimeSpan.FromDays(1));
        var unchanged = await _service.WritePageAsync(new WritePageRequest { Op = "update_header", Page = "tacit-memory/dated", Title = "升级后的标题" });
        Assert.StartsWith("Nothing changed: the page already has this content.", unchanged.Text);
        Assert.Contains("last: 2026-11-03", File.ReadAllText(Path.Combine(Memory, "pages", "tacit-memory", "dated.md")));
    }

    [Theory]
    [InlineData(ToolValues.PageTypes.Incident)]
    [InlineData(ToolValues.PageTypes.Decision)]
    [InlineData(ToolValues.PageTypes.Entity)]
    [InlineData(ToolValues.PageTypes.Work)]
    public async Task Creating_a_page_writes_the_type_it_was_given_in_the_header(string type)
    {
        var result = await _service.WritePageAsync(Create("tacit-memory/typed") with { Type = type });

        Assert.False(result.IsError, result.Text);
        Assert.Contains($"\ntype: {type}\n---\n", PageText("tacit-memory/typed"));
    }

    [Fact]
    public async Task Creating_a_page_without_a_type_is_refused_with_the_four_types_and_writes_nothing()
    {
        var result = await _service.WritePageAsync(Create("tacit-memory/untyped") with { Type = null });

        Assert.True(result.IsError);
        Assert.Contains("Missing parameter type.", result.Text);
        Assert.Contains("incident, decision, entity, work", result.Text);
        Assert.False(File.Exists(Path.Combine(Memory, "pages", "tacit-memory", "untyped.md")));
    }

    [Fact]
    public async Task Creating_a_page_with_a_type_outside_the_four_is_refused_and_writes_nothing()
    {
        var result = await _service.WritePageAsync(Create("tacit-memory/odd") with { Type = "note" });

        Assert.True(result.IsError);
        Assert.Contains("Type \"note\" is not one of: incident, decision, entity, work.", result.Text);
        Assert.False(File.Exists(Path.Combine(Memory, "pages", "tacit-memory", "odd.md")));
    }

    [Fact]
    public async Task Update_header_changes_only_the_type_line()
    {
        const string page = "tacit-memory/db-upgrade";
        await _service.WritePageAsync(Create(page));
        var before = PageText(page);

        var result = await _service.WritePageAsync((Header(page) with { Type = ToolValues.PageTypes.Decision }));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(before.Replace("type: incident\n", "type: decision\n"), PageText(page));
        Assert.Equal("pages: header tacit-memory/db-upgrade", Git("log", "-1", "--format=%s").Trim());
    }

    [Fact]
    public async Task Update_header_with_the_type_the_page_has_changes_nothing()
    {
        const string page = "tacit-memory/db-upgrade";
        await _service.WritePageAsync(Create(page));

        var result = await _service.WritePageAsync((Header(page) with { Type = ToolValues.PageTypes.Incident }));

        Assert.StartsWith("Nothing changed: the page already has this content.", result.Text);
    }

    [Fact]
    public async Task Update_header_refuses_a_type_outside_the_four_and_leaves_the_page_alone()
    {
        const string page = "tacit-memory/db-upgrade";
        await _service.WritePageAsync(Create(page));
        var before = PageText(page);

        var result = await _service.WritePageAsync((Header(page) with { Type = "note" }));

        Assert.True(result.IsError);
        Assert.Contains("is not one of: incident, decision, entity, work.", result.Text);
        Assert.Equal(before, PageText(page));
    }

    [Fact]
    public async Task Update_header_accepts_a_type_on_a_page_that_has_none_and_adds_the_line()
    {
        Directory.CreateDirectory(Path.Combine(Memory, "pages", "tacit-memory"));
        File.WriteAllText(Path.Combine(Memory, "pages", "tacit-memory", "legacy.md"),
            "---\ntitle: 旧页\naliases: [旧页, legacy]\ndates: { first: 2026-10-01, last: 2026-10-01 }\n---\n\n## 要点\n- 内容\n");

        var result = await _service.WritePageAsync((Header("tacit-memory/legacy") with { Type = ToolValues.PageTypes.Entity }));

        Assert.False(result.IsError, result.Text);
        Assert.Contains("\ntype: entity\n---\n\n## 要点\n- 内容\n", PageText("tacit-memory/legacy"));
    }

    [Fact]
    public async Task Section_writes_keep_the_type_line()
    {
        const string page = "tacit-memory/db-upgrade";
        await _service.WritePageAsync(Create(page) with { Type = ToolValues.PageTypes.Decision });
        var hash = await CodeOf(page, "根因");

        await _service.WritePageAsync(Upsert(page, "根因", "新的根因。", hash));
        await _service.WritePageAsync(Upsert(page, "补充", "补充说明。"));

        Assert.Contains("\ntype: decision\n---\n", PageText(page));
    }

    [Fact]
    public async Task Type_is_refused_on_an_op_that_does_not_take_it()
    {
        const string page = "tacit-memory/db-upgrade";
        await _service.WritePageAsync(Create(page));

        var result = await _service.WritePageAsync((Upsert(page, "根因", "x") with { Type = "decision" }));

        Assert.True(result.IsError);
        Assert.Contains("Parameter type does not apply to op upsert_section.", result.Text);
    }

    [GeneratedRegex(@"\[([0-9a-f]{6})\]")]
    private static partial Regex CodePattern();
}
