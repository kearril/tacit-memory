using Microsoft.Data.Sqlite;

namespace TacitMemory.Core.Tests;

public sealed class RecallTests : IDisposable
{
    private readonly TestHome _home = new();
    private MemoryService? _service;

    public RecallTests() => _home.InitReady();

    public void Dispose()
    {
        _service?.Dispose();
        _home.Dispose();
    }

    private MemoryService Service => _service ??= _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

    private string PagePath(string project, string slug) => Path.Combine(_home.DefaultMemory, "pages", project, slug + ".md");

    /// <summary>Writes a page file by hand, as the user would.</summary>
    private void Hand(string project, string slug, string title, string body, string dates = "{ first: 2026-10-01, last: 2026-10-05 }", string aliases = "[别名一, 别名二]")
    {
        var path = PagePath(project, slug);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $"---\ntitle: {title}\naliases: {aliases}\ndates: {dates}\n---\n\n{body.ReplaceLineEndings("\n")}\n");
    }

    private Task<ToolResult> Recall(string query, string? project = null, string? from = null, string? to = null, string? kind = null) =>
        Service.RecallAsync(new RecallRequest { Query = query, Project = project, From = from, To = to, Kind = kind });

    private void DbUpgrade(string project = "tacit-memory") => Hand(project, "db-upgrade", "数据库升级后扩展丢失", """
        ## 现象
        升级 PostgreSQL 后中文搜索没有结果。

        ## 根因
        PGroonga 扩展没有随升级重装。

        ## 解法
        重新执行 CREATE EXTENSION pgroonga。
        """, aliases: "[全文检索插件失效, 升级后搜索不到中文]");

    [Fact]
    public async Task A_chinese_sentence_finds_the_sections_that_share_words_with_it()
    {
        DbUpgrade();

        var result = await Recall("上次数据库升级以后中文搜索没有结果");

        Assert.False(result.IsError, result.Text);
        Assert.Contains("[1] page tacit-memory/db-upgrade: 数据库升级后扩展丢失", result.Text);
        Assert.Matches(@"### 现象 \[[0-9a-f]{6}\]\n升级 PostgreSQL 后中文搜索没有结果。", result.Text);
        Assert.Contains("Dates: 2026-10-01 to 2026-10-05", result.Text);
    }

    [Fact]
    public async Task Several_matching_sections_of_one_page_make_one_result_that_lists_the_rest()
    {
        DbUpgrade();

        var result = await Recall("PGroonga 扩展 pgroonga");

        Assert.Contains("1 result(s).", result.Text);
        Assert.Contains("### 根因 [", result.Text);
        Assert.Contains("### 解法 [", result.Text);
        Assert.DoesNotContain("### 现象", result.Text);
        Assert.Contains("Other sections: 现象", result.Text);
    }

    [Fact]
    public async Task A_match_on_the_title_or_aliases_shows_the_first_two_sections()
    {
        DbUpgrade();

        var result = await Recall("插件失效");

        Assert.Contains("### 现象 [", result.Text);
        Assert.Contains("### 根因 [", result.Text);
        Assert.Contains("Other sections: 解法", result.Text);
    }

    [Fact]
    public async Task A_single_character_query_matches_as_a_substring()
    {
        Hand("tacit-memory", "pit", "又踩坑了", "## 经过\n这次的坑在路径转义。");

        var result = await Recall("坑");

        Assert.Contains("tacit-memory/pit", result.Text);
    }

    [Theory]
    [InlineData("env.ps1")]
    [InlineData("UTF-8")]
    [InlineData("utf-8 编码")]
    public async Task File_names_and_terms_with_punctuation_match_as_written(string query)
    {
        Hand("tacit-memory", "encoding", "脚本编码", "## 现象\n运行 env.ps1 时报错，文件不是 UTF-8 编码。");
        Hand("tacit-memory", "other", "无关", "## 内容\nenv ps1 utf 8 分开写的词。".Replace("env ps1 utf 8", "ps1 的 env，8 的 utf", StringComparison.Ordinal));

        var result = await Recall(query);

        Assert.Contains("tacit-memory/encoding", result.Text);
        if (query != "utf-8 编码")
            Assert.DoesNotContain("tacit-memory/other", result.Text);
    }

    [Fact]
    public async Task Project_limits_the_search_to_that_project()
    {
        DbUpgrade("tacit-memory");
        DbUpgrade("my-app");

        var result = await Recall("数据库升级", project: "My-App");

        Assert.Contains("my-app/db-upgrade", result.Text);
        Assert.DoesNotContain("tacit-memory/db-upgrade", result.Text);
    }

    [Fact]
    public async Task A_date_range_keeps_pages_whose_dates_overlap_it_and_drops_undated_pages()
    {
        Hand("p", "early", "部署问题早", "## 经过\n部署失败", dates: "{ first: 2026-09-01, last: 2026-09-10 }");
        Hand("p", "spanning", "部署问题跨", "## 经过\n部署失败", dates: "{ first: 2026-09-05, last: 2026-10-20 }");
        Hand("p", "late", "部署问题晚", "## 经过\n部署失败", dates: "{ first: 2026-10-25, last: 2026-10-30 }");
        var undated = PagePath("p", "undated");
        File.WriteAllText(undated, "---\ntitle: 部署问题无日期\naliases: [a, b]\n---\n\n## 经过\n部署失败\n");

        var ranged = await Recall("部署失败", from: "2026-10-01", to: "2026-10-24");
        var all = await Recall("部署失败");

        Assert.Contains("p/spanning", ranged.Text);
        Assert.DoesNotContain("p/early", ranged.Text);
        Assert.DoesNotContain("p/late", ranged.Text);
        Assert.DoesNotContain("p/undated", ranged.Text);
        Assert.Contains("p/undated", all.Text);
    }

    [Fact]
    public async Task Only_top_k_results_are_returned()
    {
        _home.InitReady("retrieval: { topK: 2 }\n");
        foreach (var slug in (string[])["a", "b", "c"])
            Hand("p", slug, $"部署问题 {slug}", "## 经过\n部署失败");

        var result = await Recall("部署失败");

        Assert.Contains("2 result(s).", result.Text);
        Assert.DoesNotContain("[3]", result.Text);
    }

    [Fact]
    public async Task Nothing_matching_says_so_plainly()
    {
        DbUpgrade();

        var result = await Recall("天气预报");

        Assert.False(result.IsError);
        Assert.StartsWith("No relevant memory.", result.Text);
    }

    [Theory]
    [InlineData("q", "p", null, null, "Give query or page, not both.")]
    [InlineData(null, null, null, null, "Missing parameter query or page.")]
    [InlineData("q", null, "2026-13-01", null, "from \"2026-13-01\" must be a date written YYYY-MM-DD.")]
    [InlineData("q", null, null, "events", "Unknown kind \"events\"")]
    public async Task Malformed_arguments_are_refused(string? query, string? page, string? from, string? kind, string expected)
    {
        var result = await Service.RecallAsync(new RecallRequest { Query = query, Page = page, From = from, Kind = kind });

        Assert.True(result.IsError);
        Assert.Contains(expected, result.Text);
    }

    [Fact]
    public async Task An_unknown_parameter_is_refused_by_name_on_a_query_and_on_a_page_read()
    {
        var query = await Service.RecallAsync(new RecallRequest { Query = "数据库", UnknownParameters = ["limit"] });
        var read = await Service.RecallAsync(new RecallRequest { Page = "tacit-memory/db-upgrade", UnknownParameters = ["top_k"] });

        Assert.StartsWith("Refused: Unknown parameter limit.\nNext step: Call recall again with only these parameters: query, page, project, from, to, kind; give exactly one of query or page.", query.Text);
        Assert.StartsWith("Refused: Unknown parameter top_k.\nNext step: ", read.Text);
    }

    [Fact]
    public async Task An_unknown_parameter_is_listed_before_the_query_or_page_problem()
    {
        var result = await Service.RecallAsync(new RecallRequest { UnknownParameters = ["top_k"] });

        Assert.StartsWith("Refused:\n- Unknown parameter top_k.\n- Missing parameter query or page.\nNext step: ", result.Text);
    }

    [Fact]
    public async Task A_page_written_through_the_service_is_found_at_once()
    {
        await Service.WritePageAsync(new WritePageRequest
        {
            Op = "create",
            Type = ToolValues.PageTypes.Work,
            Page = "tacit-memory/deploy",
            Title = "部署脚本超时",
            Aliases = ["发布卡住", "上线超时"],
            Sections = [new("现象", "部署脚本在拉取镜像时超时。")],
        });

        var result = await Recall("拉取镜像超时");

        Assert.Contains("tacit-memory/deploy", result.Text);
    }

    [Fact]
    public async Task A_page_without_a_type_is_found_like_any_other()
    {
        Hand("tacit-memory", "untyped", "数据库升级后扩展丢失", "## 现象\n升级 PostgreSQL 后中文搜索没有结果。");

        var result = await Recall("上次数据库升级以后中文搜索没有结果");

        Assert.False(result.IsError, result.Text);
        Assert.Contains("tacit-memory/untyped", result.Text);
    }

    [Fact]
    public async Task Pages_edited_by_hand_while_running_are_searched_with_their_new_content()
    {
        DbUpgrade();
        Assert.Contains("db-upgrade", (await Recall("数据库升级")).Text);

        Hand("tacit-memory", "db-upgrade", "数据库升级后扩展丢失", "## 现象\n改成了关于证书过期的内容。");
        Hand("tacit-memory", "cert", "证书", "## 现象\n证书续期失败。");

        await Eventually(async () => (await Recall("证书过期")).Text.Contains("tacit-memory/db-upgrade", StringComparison.Ordinal));
        await Eventually(async () => (await Recall("续期失败")).Text.Contains("tacit-memory/cert", StringComparison.Ordinal));

        File.Delete(PagePath("tacit-memory", "cert"));
        await Eventually(async () => (await Recall("续期失败")).Text.StartsWith("No relevant memory.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Pages_changed_while_the_service_was_stopped_are_synced_at_start()
    {
        DbUpgrade();
        Assert.Contains("db-upgrade", (await Recall("数据库升级")).Text);
        _service!.Dispose();
        _service = null;

        File.Delete(PagePath("tacit-memory", "db-upgrade"));
        Hand("tacit-memory", "cert", "证书", "## 现象\n证书续期失败。");

        Assert.StartsWith("No relevant memory.", (await Recall("数据库升级")).Text);
        Assert.Contains("tacit-memory/cert", (await Recall("续期失败")).Text);
    }

    [Fact]
    public async Task An_unreadable_index_is_deleted_and_rebuilt_from_the_pages()
    {
        DbUpgrade();
        var database = Path.Combine(_home.DefaultMemory, ".cache", "index.db");
        Directory.CreateDirectory(Path.GetDirectoryName(database)!);
        File.WriteAllText(database, "not a database");

        var result = await Recall("数据库升级");

        Assert.Contains("tacit-memory/db-upgrade", result.Text);
    }

    [Fact]
    public async Task An_index_of_another_schema_version_is_deleted_and_rebuilt()
    {
        DbUpgrade();
        var database = Path.Combine(_home.DefaultMemory, ".cache", "index.db");
        Directory.CreateDirectory(Path.GetDirectoryName(database)!);
        using (var old = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString()))
        {
            old.Open();
            var command = old.CreateCommand();
            command.CommandText = "CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL); INSERT INTO meta (key, value) VALUES ('schema', '2');";
            command.ExecuteNonQuery();
        }

        var result = await Recall("数据库升级");

        Assert.Contains("tacit-memory/db-upgrade", result.Text);
    }

    [Fact]
    public async Task A_page_that_is_not_utf8_is_skipped_and_the_others_are_still_searched()
    {
        DbUpgrade();
        var broken = PagePath("tacit-memory", "broken");
        File.WriteAllBytes(broken, [0x2D, 0x2D, 0x2D, 0x0A, 0xFF, 0xFE, 0x0A]);

        var result = await Recall("数据库升级");

        Assert.Contains("tacit-memory/db-upgrade", result.Text);
    }

    [Fact]
    public async Task The_index_is_kept_out_of_git()
    {
        DbUpgrade();
        await Recall("数据库升级");

        Assert.True(File.Exists(Path.Combine(_home.DefaultMemory, ".cache", "index.db")));
        Assert.DoesNotContain(".cache", TestHome.Git(_home.DefaultMemory, "status", "--porcelain", "--ignored=no"));
    }

    private static async Task Eventually(Func<Task<bool>> condition)
    {
        for (var waited = TimeSpan.Zero; waited < TimeSpan.FromSeconds(10); waited += TimeSpan.FromMilliseconds(100))
        {
            if (await condition())
                return;
            await Task.Delay(100);
        }

        Assert.Fail("The condition did not hold within 10 seconds.");
    }

    [Fact]
    public async Task Every_result_says_whether_it_is_a_page_or_an_activity()
    {
        DbUpgrade();
        await Service.WriteRecentAsync(new WriteRecentRequest { Op = "add_activity", Text = "数据库升级后中文搜索又没有结果" });

        var result = await Recall("数据库升级 中文搜索");

        Assert.False(result.IsError, result.Text);
        var headers = result.Text.Split('\n').Where(line => System.Text.RegularExpressions.Regex.IsMatch(line, @"^\[\d+\] ")).ToList();
        Assert.Equal(2, headers.Count);
        Assert.All(headers, line => Assert.Matches(@"^\[\d+\] (page|activity) ", line));
    }
}
