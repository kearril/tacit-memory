namespace TacitMemory.Core.Tests;

public sealed class FormatViolationTests : IDisposable
{
    private const string Category = "Format and rule violations";

    private readonly TestHome _home = new();

    public void Dispose() => _home.Dispose();

    private string Memory => _home.DefaultMemory;

    private MemoryService Open()
    {
        _home.InitReady();
        return _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);
    }

    /// <summary>The text of one report category: from its heading to the next heading, or to the end.</summary>
    private static string Section(string text, string title)
    {
        var start = text.IndexOf($"### {title}\n", StringComparison.Ordinal);
        Assert.True(start >= 0, $"No category \"{title}\" in the report.");
        var body = text[(start + title.Length + 5)..];
        var next = body.IndexOf("\n### ", StringComparison.Ordinal);
        return next < 0 ? body : body[..next];
    }

    /// <summary>The finding that names <paramref name="file"/> at <paramref name="location"/>.</summary>
    private static void AssertListed(string section, string file, string location) =>
        Assert.Contains($"- {file}, {location} (", section);

    private static void AssertNotListed(string section, string file) =>
        Assert.DoesNotContain($"- {file}, ", section);

    private static string FindingOf(string section, string file) =>
        section.Split('\n').Single(line => line.StartsWith($"- {file}, ", StringComparison.Ordinal));

    private void WriteFile(string relative, string text)
    {
        var path = Path.Combine(Memory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text.ReplaceLineEndings("\n"));
    }

    /// <summary>
    /// A detail page: the title on line 2, the aliases on line 3, the related pages on line 4 when given, then the dates,
    /// the type on the line after them, and the closing fence.
    /// </summary>
    private static string Page(string title = "数据库升级", string aliases = "[数据库升级, db upgrade]", string related = "", string body = "## 要点\n- 内容\n") =>
        $"---\ntitle: {title}\naliases: {aliases}\n{(related.Length > 0 ? related + "\n" : "")}dates: {{ first: 2026-10-30, last: 2026-10-30 }}\ntype: work\n---\n\n{body}";

    private void Commit(string message)
    {
        TestHome.Git(Memory, "add", "-A");
        TestHome.Git(Memory, "-c", "user.name=tacit", "-c", "user.email=tacit@localhost", "commit", "--allow-empty", "-q", "-m", message);
    }

    [Fact]
    public async Task A_new_memory_has_no_format_violations()
    {
        using var service = Open();

        var section = Section(await service.ConsolidationReportAsync(), Category);

        Assert.Contains("None.", section);
    }

    [Fact]
    public async Task A_memory_with_no_problems_still_lists_both_parts_with_none_under_the_problem_categories()
    {
        using var service = Open();

        var prompt = await service.ConsolidationReportAsync();

        Assert.Contains("## A. Automatic\n", prompt);
        Assert.Contains("## B. For proposals\n", prompt);
        Assert.Contains("None.", Section(prompt, Category));
        Assert.DoesNotContain("No problems found", prompt);
    }

    [Fact]
    public async Task A_title_over_fifty_characters_is_listed_and_disappears_once_shortened()
    {
        using var service = Open();
        WriteFile("pages/tacit-memory/long.md", Page(title: new string('长', 51)));

        AssertListed(Section(await service.ConsolidationReportAsync(), Category), "pages/tacit-memory/long.md", "line 2");

        WriteFile("pages/tacit-memory/long.md", Page(title: new string('长', 50)));
        AssertNotListed(Section(await service.ConsolidationReportAsync(), Category), "pages/tacit-memory/long.md");
    }

    [Fact]
    public async Task A_page_with_fewer_than_two_aliases_or_a_long_alias_is_listed_and_disappears_once_fixed()
    {
        using var service = Open();
        WriteFile("pages/tacit-memory/one-alias.md", Page(aliases: "[只有一个]"));
        WriteFile("pages/tacit-memory/long-alias.md", Page(aliases: $"[数据库升级, {new string('a', 31)}]"));

        var section = Section(await service.ConsolidationReportAsync(), Category);
        AssertListed(section, "pages/tacit-memory/one-alias.md", "line 3");
        AssertListed(section, "pages/tacit-memory/long-alias.md", "line 3");

        WriteFile("pages/tacit-memory/one-alias.md", Page(aliases: "[只有一个, db upgrade]"));
        WriteFile("pages/tacit-memory/long-alias.md", Page(aliases: "[数据库升级, db upgrade]"));
        section = Section(await service.ConsolidationReportAsync(), Category);
        AssertNotListed(section, "pages/tacit-memory/one-alias.md");
        AssertNotListed(section, "pages/tacit-memory/long-alias.md");
    }

    [Fact]
    public async Task A_page_without_a_type_is_listed_and_disappears_once_it_has_one()
    {
        using var service = Open();
        WriteFile("pages/tacit-memory/untyped.md", Page().Replace("type: work\n", ""));

        var section = Section(await service.ConsolidationReportAsync(), Category);
        AssertListed(section, "pages/tacit-memory/untyped.md", "YAML header");
        Assert.Contains("(page type)", FindingOf(section, "pages/tacit-memory/untyped.md"));

        WriteFile("pages/tacit-memory/untyped.md", Page());
        AssertNotListed(Section(await service.ConsolidationReportAsync(), Category), "pages/tacit-memory/untyped.md");
    }

    [Fact]
    public async Task A_type_outside_the_four_is_listed_at_its_line_and_disappears_once_fixed()
    {
        using var service = Open();
        WriteFile("pages/tacit-memory/odd-type.md", Page().Replace("type: work", "type: note"));

        AssertListed(Section(await service.ConsolidationReportAsync(), Category), "pages/tacit-memory/odd-type.md", "line 5");

        WriteFile("pages/tacit-memory/odd-type.md", Page());
        AssertNotListed(Section(await service.ConsolidationReportAsync(), Category), "pages/tacit-memory/odd-type.md");
    }

    [Fact]
    public async Task A_missing_type_is_listed_whatever_the_consolidation_baseline()
    {
        using var service = Open();
        WriteFile("pages/tacit-memory/untyped.md", Page().Replace("type: work\n", ""));
        Commit("consolidate: baseline");

        AssertListed(Section(await service.ConsolidationReportAsync(), Category), "pages/tacit-memory/untyped.md", "YAML header");
    }

    [Fact]
    public async Task The_two_page_section_words_count_as_structure_words_in_both_languages()
    {
        using var service = Open();
        WriteFile("pages/tacit-memory/both.md", Page(body: "## 遗留\n- 未决\n\n## Overturned\n- 旧决定\n"));

        var finding = FindingOf(Section(await service.ConsolidationReportAsync(), Category), "pages/tacit-memory/both.md");
        Assert.Contains("Hint only", finding);
    }

    [Fact]
    public async Task A_related_page_that_does_not_exist_is_listed_and_disappears_once_it_exists()
    {
        using var service = Open();
        WriteFile("pages/tacit-memory/linked.md", Page(related: "related: [tacit-memory/missing]"));

        AssertListed(Section(await service.ConsolidationReportAsync(), Category), "pages/tacit-memory/linked.md", "line 4");

        WriteFile("pages/tacit-memory/missing.md", Page(title: "缺失的页"));
        AssertNotListed(Section(await service.ConsolidationReportAsync(), Category), "pages/tacit-memory/linked.md");
    }

    [Fact]
    public async Task A_page_file_name_outside_the_slug_rule_is_listed_and_disappears_once_renamed()
    {
        using var service = Open();
        WriteFile("pages/tacit-memory/Bad_Name.md", Page());

        AssertListed(Section(await service.ConsolidationReportAsync(), Category), "pages/tacit-memory/Bad_Name.md", "file name");

        File.Move(Path.Combine(Memory, "pages/tacit-memory/Bad_Name.md"), Path.Combine(Memory, "pages/tacit-memory/bad-name.md"));
        AssertNotListed(Section(await service.ConsolidationReportAsync(), Category), "pages/tacit-memory/bad-name.md");
    }

    [Fact]
    public async Task A_misnamed_page_file_still_gets_its_content_checks()
    {
        using var service = Open();
        WriteFile("pages/tacit-memory/Bad_Name.md", Page(title: new string('长', 51)));

        var section = Section(await service.ConsolidationReportAsync(), Category);
        AssertListed(section, "pages/tacit-memory/Bad_Name.md", "file name");
        AssertListed(section, "pages/tacit-memory/Bad_Name.md", "line 2");
    }

    [Fact]
    public async Task A_repeated_section_name_is_listed_at_its_second_heading()
    {
        using var service = Open();
        WriteFile("pages/tacit-memory/dup.md", Page(body: "## 要点\n- 内容\n\n## 要点\n- 另一条\n"));

        AssertListed(Section(await service.ConsolidationReportAsync(), Category), "pages/tacit-memory/dup.md", "line 11");

        WriteFile("pages/tacit-memory/dup.md", Page(body: "## 要点\n- 内容\n\n## 其他\n- 另一条\n"));
        AssertNotListed(Section(await service.ConsolidationReportAsync(), Category), "pages/tacit-memory/dup.md");
    }

    [Fact]
    public async Task A_yaml_header_that_cannot_be_parsed_is_listed_and_disappears_once_fixed()
    {
        using var service = Open();
        WriteFile("pages/tacit-memory/broken.md", "---\ntitle: [未闭合\naliases: [a, b]\n---\n\n## 要点\n- 内容\n");

        AssertListed(Section(await service.ConsolidationReportAsync(), Category), "pages/tacit-memory/broken.md", "YAML header");

        WriteFile("pages/tacit-memory/broken.md", Page());
        AssertNotListed(Section(await service.ConsolidationReportAsync(), Category), "pages/tacit-memory/broken.md");
    }

    [Fact]
    public async Task A_file_that_is_not_utf8_is_listed_and_disappears_once_resaved()
    {
        using var service = Open();
        Directory.CreateDirectory(Path.Combine(Memory, "pages", "tacit-memory"));
        File.WriteAllBytes(Path.Combine(Memory, "pages", "tacit-memory", "gbk.md"), [0xFF, 0xFE, 0x41]);

        AssertListed(Section(await service.ConsolidationReportAsync(), Category), "pages/tacit-memory/gbk.md", "file");

        WriteFile("pages/tacit-memory/gbk.md", Page());
        AssertNotListed(Section(await service.ConsolidationReportAsync(), Category), "pages/tacit-memory/gbk.md");
    }

    [Fact]
    public async Task Structure_words_in_both_languages_are_a_hint_and_disappear_once_one_language_is_used()
    {
        using var service = Open();
        WriteFile("pages/tacit-memory/mixed.md", Page(body: "## 偏好\n- 内容\n\n## Preferences\n- content\n"));

        var section = Section(await service.ConsolidationReportAsync(), Category);
        AssertListed(section, "pages/tacit-memory/mixed.md", "line 11");
        Assert.Contains("Hint only", FindingOf(section, "pages/tacit-memory/mixed.md"));

        WriteFile("pages/tacit-memory/mixed.md", Page(body: "## 偏好\n- 内容\n\n## 其他\n- content\n"));
        AssertNotListed(Section(await service.ConsolidationReportAsync(), Category), "pages/tacit-memory/mixed.md");
    }

    [Fact]
    public async Task Quote_marks_rejected_marks_and_instead_in_a_list_item_count_as_structure_words_in_both_languages()
    {
        using var service = Open();
        WriteFile("pages/tacit-memory/mixed-quote.md", Page(body: "## 偏好\n- 内容\n  <!-- 2026-10-30 原话「要点」 -->\n  <!-- 2026-10-31 quote「要点」 -->\n"));
        WriteFile("pages/tacit-memory/mixed-rejected.md", Page(body: "## 偏好\n- 内容\n  <!-- 2026-10-30 拒绝 原话「要点」 -->\n  <!-- 2026-10-31 rejected 原话「要点」 -->\n"));
        WriteFile("pages/tacit-memory/mixed-instead.md", Page(body: "- 不要删库，改为先备份\n- Never delete; instead back up first\n"));
        WriteFile("pages/tacit-memory/prose.md", Page(body: "## 偏好\n- 内容\n正文里提到 instead 但不是列表项\n"));

        var section = Section(await service.ConsolidationReportAsync(), Category);

        AssertListed(section, "pages/tacit-memory/mixed-quote.md", "line 11");
        Assert.Contains("\"quote\" on line 11", section);
        AssertListed(section, "pages/tacit-memory/mixed-rejected.md", "line 11");
        Assert.Contains("\"rejected\" on line 11", section);
        AssertListed(section, "pages/tacit-memory/mixed-instead.md", "line 9");
        Assert.Contains("\"改为\" on line 8 and \"instead\" on line 9", section);
        AssertNotListed(section, "pages/tacit-memory/prose.md");
    }

    [Fact]
    public async Task Each_category_lists_at_most_twenty_findings_and_counts_the_rest()
    {
        using var service = Open();
        foreach (var number in Enumerable.Range(1, 21))
            WriteFile($"pages/tacit-memory/long-{number:00}.md", Page(title: new string('长', 51)));

        var section = Section(await service.ConsolidationReportAsync(), Category);

        AssertListed(section, "pages/tacit-memory/long-20.md", "line 2");
        AssertNotListed(section, "pages/tacit-memory/long-21.md");
        Assert.Contains("1 more not listed", section);
    }

    [Fact]
    public async Task A_summary_section_is_not_taken_for_an_unrecognized_date_heading()
    {
        using var service = Open();
        WriteFile("recent/2026-11.md", "## 2026-11-02\n- 一件事\n\n## 汇总\n- 2026-W45：本周\n- 2026-11：本月\n");

        var section = Section(await service.ConsolidationReportAsync(), Category);

        Assert.Contains("None.", section);
    }

    [Fact]
    public async Task A_week_summary_in_the_month_file_of_another_thursday_is_listed()
    {
        using var service = Open();
        // 2026-W45's Thursday is 2026-11-05, so its summary belongs in 2026-11.md.
        WriteFile("recent/2026-10.md", "## 汇总\n- 2026-W45：写错了文件\n");

        var section = Section(await service.ConsolidationReportAsync(), Category);

        AssertListed(section, "recent/2026-10.md", "line 2");
        Assert.Contains("Summary in the wrong file", section);
    }

    [Fact]
    public async Task A_week_that_is_not_in_its_iso_year_is_listed_as_a_week_number_error()
    {
        using var service = Open();
        // 2026 has 53 ISO weeks and 2027 has 52, so 2027-W53 does not exist, and neither does 2026-W54.
        WriteFile("recent/2026-12.md", "## 汇总\n- 2026-W54：没有这一周\n");
        WriteFile("recent/2027-01.md", "## 汇总\n- 2027-W53：也没有这一周\n");

        var section = Section(await service.ConsolidationReportAsync(), Category);

        AssertListed(section, "recent/2026-12.md", "line 2");
        AssertListed(section, "recent/2027-01.md", "line 2");
        Assert.Contains("Week number error", section);
    }

    [Fact]
    public async Task Weeks_across_the_year_end_are_filed_by_their_thursday()
    {
        using var service = Open();
        // 2026-W53 (2026-12-28 to 2027-01-03) has its Thursday on 2026-12-31; 2027-W01 has its Thursday on 2027-01-07.
        WriteFile("recent/2026-12.md", "## 汇总\n- 2026-W53：跨年的一周\n");
        WriteFile("recent/2027-01.md", "## 汇总\n- 2027-W01：新年第一周\n");

        var section = Section(await service.ConsolidationReportAsync(), Category);

        Assert.Contains("None.", section);
    }

    [Fact]
    public async Task A_week_of_the_new_year_written_in_the_old_year_file_is_listed()
    {
        using var service = Open();
        WriteFile("recent/2027-01.md", "## 汇总\n- 2026-W53：这一周的周四在 2026-12-31\n");

        var section = Section(await service.ConsolidationReportAsync(), Category);

        AssertListed(section, "recent/2027-01.md", "line 2");
        Assert.Contains("Summary in the wrong file", section);
    }

    [Fact]
    public async Task Lines_in_the_summary_section_that_are_neither_week_nor_month_summaries_are_listed()
    {
        using var service = Open();
        WriteFile("recent/2026-11.md", "## 汇总\n- 2026-11-05：日期不是汇总\n- 2026-11：\n- 2026-13：不存在的月份\n");

        var section = Section(await service.ConsolidationReportAsync(), Category);

        AssertListed(section, "recent/2026-11.md", "line 2");
        AssertListed(section, "recent/2026-11.md", "line 3");
        AssertListed(section, "recent/2026-11.md", "line 4");
        Assert.Contains("Unrecognized summary line", section);
        Assert.Contains("Month number error", section);
    }

    [Fact]
    public async Task Summary_format_problems_disappear_once_fixed()
    {
        using var service = Open();
        WriteFile("recent/2026-10.md", "## 汇总\n- 2026-W45：写错了文件\n");
        Assert.Contains("Summary in the wrong file", Section(await service.ConsolidationReportAsync(), Category));

        WriteFile("recent/2026-10.md", "## 汇总\n- 2026-W42：放回正确的文件\n");

        Assert.Contains("None.", Section(await service.ConsolidationReportAsync(), Category));
    }
}
