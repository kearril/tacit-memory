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
    /// A detail page: the title on line 2, the aliases on line 3, the related pages on line 4 when given, then the dates
    /// and the closing fence.
    /// </summary>
    private static string Page(string title = "数据库升级", string aliases = "[数据库升级, db upgrade]", string related = "", string body = "## 要点\n- 内容\n") =>
        $"---\ntitle: {title}\naliases: {aliases}\n{(related.Length > 0 ? related + "\n" : "")}dates: {{ first: 2026-10-30, last: 2026-10-30 }}\n---\n\n{body}";

    [Fact]
    public async Task A_new_memory_has_no_format_violations()
    {
        using var service = Open();

        var section = Section(await service.CheckupPromptAsync(), Category);

        Assert.Contains("None.", section);
    }

    [Fact]
    public async Task A_memory_with_nothing_to_report_says_so_in_the_report()
    {
        using var service = Open();

        var prompt = await service.CheckupPromptAsync();

        Assert.Contains("No problems found in any category.", prompt);
    }

    [Fact]
    public async Task A_title_over_fifty_characters_is_listed_and_disappears_once_shortened()
    {
        using var service = Open();
        WriteFile("pages/tacit-memory/long.md", Page(title: new string('长', 51)));

        AssertListed(Section(await service.CheckupPromptAsync(), Category), "pages/tacit-memory/long.md", "line 2");

        WriteFile("pages/tacit-memory/long.md", Page(title: new string('长', 50)));
        AssertNotListed(Section(await service.CheckupPromptAsync(), Category), "pages/tacit-memory/long.md");
    }

    [Fact]
    public async Task A_page_with_fewer_than_two_aliases_or_a_long_alias_is_listed_and_disappears_once_fixed()
    {
        using var service = Open();
        WriteFile("pages/tacit-memory/one-alias.md", Page(aliases: "[只有一个]"));
        WriteFile("pages/tacit-memory/long-alias.md", Page(aliases: $"[数据库升级, {new string('a', 31)}]"));

        var section = Section(await service.CheckupPromptAsync(), Category);
        AssertListed(section, "pages/tacit-memory/one-alias.md", "line 3");
        AssertListed(section, "pages/tacit-memory/long-alias.md", "line 3");

        WriteFile("pages/tacit-memory/one-alias.md", Page(aliases: "[只有一个, db upgrade]"));
        WriteFile("pages/tacit-memory/long-alias.md", Page(aliases: "[数据库升级, db upgrade]"));
        section = Section(await service.CheckupPromptAsync(), Category);
        AssertNotListed(section, "pages/tacit-memory/one-alias.md");
        AssertNotListed(section, "pages/tacit-memory/long-alias.md");
    }

    [Fact]
    public async Task A_related_page_that_does_not_exist_is_listed_and_disappears_once_it_exists()
    {
        using var service = Open();
        WriteFile("pages/tacit-memory/linked.md", Page(related: "related: [tacit-memory/missing]"));

        AssertListed(Section(await service.CheckupPromptAsync(), Category), "pages/tacit-memory/linked.md", "line 4");

        WriteFile("pages/tacit-memory/missing.md", Page(title: "缺失的页"));
        AssertNotListed(Section(await service.CheckupPromptAsync(), Category), "pages/tacit-memory/linked.md");
    }

    [Fact]
    public async Task A_page_file_name_outside_the_slug_rule_is_listed_and_disappears_once_renamed()
    {
        using var service = Open();
        WriteFile("pages/tacit-memory/Bad_Name.md", Page());

        AssertListed(Section(await service.CheckupPromptAsync(), Category), "pages/tacit-memory/Bad_Name.md", "file name");

        File.Move(Path.Combine(Memory, "pages/tacit-memory/Bad_Name.md"), Path.Combine(Memory, "pages/tacit-memory/bad-name.md"));
        AssertNotListed(Section(await service.CheckupPromptAsync(), Category), "pages/tacit-memory/bad-name.md");
    }

    [Fact]
    public async Task A_misnamed_page_file_still_gets_its_content_checks()
    {
        using var service = Open();
        WriteFile("pages/tacit-memory/Bad_Name.md", Page(title: new string('长', 51)));

        var section = Section(await service.CheckupPromptAsync(), Category);
        AssertListed(section, "pages/tacit-memory/Bad_Name.md", "file name");
        AssertListed(section, "pages/tacit-memory/Bad_Name.md", "line 2");
    }

    [Fact]
    public async Task A_repeated_section_name_is_listed_at_its_second_heading()
    {
        using var service = Open();
        WriteFile("pages/tacit-memory/dup.md", Page(body: "## 要点\n- 内容\n\n## 要点\n- 另一条\n"));

        AssertListed(Section(await service.CheckupPromptAsync(), Category), "pages/tacit-memory/dup.md", "line 10");

        WriteFile("pages/tacit-memory/dup.md", Page(body: "## 要点\n- 内容\n\n## 其他\n- 另一条\n"));
        AssertNotListed(Section(await service.CheckupPromptAsync(), Category), "pages/tacit-memory/dup.md");
    }

    [Fact]
    public async Task A_yaml_header_that_cannot_be_parsed_is_listed_and_disappears_once_fixed()
    {
        using var service = Open();
        WriteFile("pages/tacit-memory/broken.md", "---\ntitle: [未闭合\naliases: [a, b]\n---\n\n## 要点\n- 内容\n");

        AssertListed(Section(await service.CheckupPromptAsync(), Category), "pages/tacit-memory/broken.md", "YAML header");

        WriteFile("pages/tacit-memory/broken.md", Page());
        AssertNotListed(Section(await service.CheckupPromptAsync(), Category), "pages/tacit-memory/broken.md");
    }

    [Fact]
    public async Task A_file_that_is_not_utf8_is_listed_and_disappears_once_resaved()
    {
        using var service = Open();
        Directory.CreateDirectory(Path.Combine(Memory, "pages", "tacit-memory"));
        File.WriteAllBytes(Path.Combine(Memory, "pages", "tacit-memory", "gbk.md"), [0xFF, 0xFE, 0x41]);

        AssertListed(Section(await service.CheckupPromptAsync(), Category), "pages/tacit-memory/gbk.md", "file");

        WriteFile("pages/tacit-memory/gbk.md", Page());
        AssertNotListed(Section(await service.CheckupPromptAsync(), Category), "pages/tacit-memory/gbk.md");
    }

    [Fact]
    public async Task Structure_words_in_both_languages_are_a_hint_and_disappear_once_one_language_is_used()
    {
        using var service = Open();
        WriteFile("pages/tacit-memory/mixed.md", Page(body: "## 偏好\n- 内容\n\n## Preferences\n- content\n"));

        var section = Section(await service.CheckupPromptAsync(), Category);
        AssertListed(section, "pages/tacit-memory/mixed.md", "line 10");
        Assert.Contains("Hint only", FindingOf(section, "pages/tacit-memory/mixed.md"));

        WriteFile("pages/tacit-memory/mixed.md", Page(body: "## 偏好\n- 内容\n\n## 其他\n- content\n"));
        AssertNotListed(Section(await service.CheckupPromptAsync(), Category), "pages/tacit-memory/mixed.md");
    }

    [Fact]
    public async Task Quote_marks_rejected_marks_and_instead_in_a_list_item_count_as_structure_words_in_both_languages()
    {
        using var service = Open();
        WriteFile("pages/tacit-memory/mixed-quote.md", Page(body: "## 偏好\n- 内容\n  <!-- 2026-10-30 原话「要点」 -->\n  <!-- 2026-10-31 quote「要点」 -->\n"));
        WriteFile("pages/tacit-memory/mixed-rejected.md", Page(body: "## 偏好\n- 内容\n  <!-- 2026-10-30 拒绝 原话「要点」 -->\n  <!-- 2026-10-31 rejected 原话「要点」 -->\n"));
        WriteFile("pages/tacit-memory/mixed-instead.md", Page(body: "- 不要删库，改为先备份\n- Never delete; instead back up first\n"));
        WriteFile("pages/tacit-memory/prose.md", Page(body: "## 偏好\n- 内容\n正文里提到 instead 但不是列表项\n"));

        var section = Section(await service.CheckupPromptAsync(), Category);

        AssertListed(section, "pages/tacit-memory/mixed-quote.md", "line 10");
        Assert.Contains("\"quote\" on line 10", section);
        AssertListed(section, "pages/tacit-memory/mixed-rejected.md", "line 10");
        Assert.Contains("\"rejected\" on line 10", section);
        AssertListed(section, "pages/tacit-memory/mixed-instead.md", "line 8");
        Assert.Contains("\"改为\" on line 7 and \"instead\" on line 8", section);
        AssertNotListed(section, "pages/tacit-memory/prose.md");
    }

    [Fact]
    public async Task Each_category_lists_at_most_twenty_findings_and_counts_the_rest()
    {
        using var service = Open();
        foreach (var number in Enumerable.Range(1, 21))
            WriteFile($"pages/tacit-memory/long-{number:00}.md", Page(title: new string('长', 51)));

        var section = Section(await service.CheckupPromptAsync(), Category);

        AssertListed(section, "pages/tacit-memory/long-20.md", "line 2");
        AssertNotListed(section, "pages/tacit-memory/long-21.md");
        Assert.Contains("1 more not listed", section);
    }
}
