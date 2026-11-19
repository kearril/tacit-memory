namespace TacitMemory.Core.Tests;

public sealed class FormatChecksPersonaTests : IDisposable
{
    private const string FormatTitle = "Format and rule violations";

    private readonly TestHome _home = new();

    public void Dispose() => _home.Dispose();

    private string Memory => _home.DefaultMemory;

    private MemoryService Open(string extraConfig = "")
    {
        _home.InitReady(extraConfig);
        return _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);
    }

    private void Write(string relative, string text)
    {
        var path = Path.Combine(Memory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text.ReplaceLineEndings("\n"));
    }

    /// <summary>The text of one report category: from its heading to the next heading, or to the end.</summary>
    private static string Section(string prompt, string title)
    {
        var start = prompt.IndexOf($"### {title}\n", StringComparison.Ordinal);
        Assert.True(start >= 0, $"No category \"{title}\" in the report.");
        var body = prompt[(start + title.Length + 5)..];
        var next = body.IndexOf("\n### ", StringComparison.Ordinal);
        return next < 0 ? body : body[..next];
    }

    private async Task<string> Violations(MemoryService service) =>
        Section(await service.Tools.ConsolidationReportAsync(), FormatTitle);

    /// <summary>The whole report: no category puts a section over its limit to the user any more, so the tests look at all of it.</summary>
    private async Task<string> OverLimit(MemoryService service) => await service.Tools.ConsolidationReportAsync();

    [Fact]
    public async Task An_entry_of_any_length_is_not_reported()
    {
        using var service = Open();
        Write("persona.md", "## Preferences\n- 短\n- " + new string('字', 400) + "\n");

        var violations = await Violations(service);

        Assert.DoesNotContain("too long", violations);
        Assert.DoesNotContain("line 3", violations);
    }

    [Fact]
    public async Task A_section_with_many_entries_is_not_put_to_the_user()
    {
        using var service = Open();
        Write("persona.md", "## Preferences\n" + string.Concat(Enumerable.Range(1, 15).Select(i => $"- 第{i}条\n")));

        var violations = await OverLimit(service);

        Assert.DoesNotContain("Section over limit", violations);
    }

    [Fact]
    public async Task An_unknown_section_and_text_outside_entries_are_reported()
    {
        using var service = Open();
        Write("persona.md", "## Preferences\n- 有效条目\n随手写的一行\n## 其他\n- 不认识的节\n");

        var violations = await Violations(service);

        Assert.Contains("Unrecognized content", violations);
        Assert.Contains("line 3", violations);
        Assert.Contains("Unrecognized section", violations);
        Assert.Contains("section \"其他\"", violations);
    }

    [Fact]
    public async Task A_candidate_without_its_section_line_is_reported()
    {
        using var service = Open();
        Write("candidates.md", "## Candidates\n### 先说结论\n- 2026-10-28 quote「先说结论」\n");

        var violations = await Violations(service);

        Assert.Contains("candidates.md", violations);
        Assert.Contains("line 2", violations);
        Assert.Contains("Candidate without section", violations);
    }

    [Fact]
    public async Task An_unrecognized_occurrence_line_under_a_candidate_is_reported()
    {
        using var service = Open();
        Write("candidates.md", "## Candidates\n### 先说结论\nBelongs to: Preferences\n- 2026-10-28 quote「先说结论」\n- 随便写的\n");

        var violations = await Violations(service);

        Assert.Contains("line 5", violations);
        Assert.Contains("Unrecognized occurrence line", violations);
        Assert.DoesNotContain("line 4", violations);
    }

    [Fact]
    public async Task A_candidate_scope_line_that_is_not_a_scope_is_reported_and_a_valid_one_is_not()
    {
        using var service = Open();
        Write("candidates.md", "## Candidates\n### 先说结论\nBelongs to: Preferences\nScope: domain:Bad Name\n- 2026-10-28 quote「先说结论」\n### 再说结论\nBelongs to: Preferences\nScope: domain:network-proxy\n- 2026-10-28 quote「再说结论」\n");

        var violations = await Violations(service);

        Assert.Contains("Invalid scope", violations);
        Assert.Contains("line 4", violations);
        Assert.DoesNotContain("line 8", violations);
        Assert.DoesNotContain("Unrecognized occurrence line", violations);
    }

    [Fact]
    public async Task Entries_outside_a_date_heading_and_bad_date_headings_are_reported()
    {
        using var service = Open();
        Write("recent/2026-10.md", "- 没有日期标题的条目\n## 随便的标题\n- 落在随便标题下\n## 2026-10-5\n- 日期格式错\n## 2026-10-28\n- 正常条目\n");

        var violations = await Violations(service);

        Assert.Contains("recent/2026-10.md", violations);
        Assert.Contains("Entry outside a date heading", violations);
        Assert.Contains("Unrecognized date heading", violations);
        Assert.Contains("Date format error", violations);
        Assert.Contains("line 1", violations);
        Assert.Contains("line 2", violations);
        Assert.Contains("line 4", violations);
        Assert.DoesNotContain("line 6", violations);
        Assert.DoesNotContain("line 7", violations);
    }

    [Fact]
    public async Task A_machine_mark_that_is_not_a_machine_name_is_reported_and_an_unmarked_activity_is_not()
    {
        using var service = Open();
        Write("recent/2026-10.md", "## 2026-10-28\n- 标对的\n  <!-- on win-main -->\n- 标错的\n  <!-- on Win_Main quote「原话」 -->\n- 没有标记的\n");

        var violations = await Violations(service);

        Assert.Contains("line 5 (Invalid machine)", violations);
        Assert.DoesNotContain("line 3", violations);
        Assert.DoesNotContain("line 6", violations);
    }

    [Fact]
    public async Task A_workstream_entry_without_a_background_line_is_a_format_problem_fixed_by_hand()
    {
        using var service = Open();
        Write("workstreams.md", "## Workstream\n### alpha\nRecent: 没有背景。\n");

        var problems = await Violations(service);

        Assert.Contains("workstreams.md", problems);
        Assert.Contains("Missing background", problems);
        Assert.Contains("Fix directly.", problems);
    }

    [Fact]
    public async Task A_workstream_name_that_is_not_valid_duplicate_or_a_line_no_entry_reads_is_reported()
    {
        using var service = Open();
        Write("workstreams.md", "## Workstream\n随便写的一行\n### Bad-Name\nBackground: 不合规。\n### dup\nBackground: 一。\n### dup\nBackground: 二。\n");

        var problems = await Violations(service);

        Assert.Contains("Unrecognized line", problems);
        Assert.Contains("Workstream name", problems);
        Assert.Contains("Duplicate workstream", problems);
    }

    [Fact]
    public async Task Fixing_a_workstream_format_problem_makes_it_disappear()
    {
        using var service = Open();
        Write("workstreams.md", "## Workstream\n### alpha\nRecent: 没有背景。\n");
        Assert.Contains("Missing background", await Violations(service));

        Write("workstreams.md", "## Workstream\n### alpha\nBackground: 有背景了。\n");

        Assert.DoesNotContain("workstreams.md", await Violations(service));
    }

    [Fact]
    public async Task The_number_of_workstreams_is_not_limited()
    {
        using var service = Open();
        Write("workstreams.md", "## Workstream\n" + string.Concat(Enumerable.Range(1, 12).Select(i => $"### w{i}\nBackground: 第{i}条。\n")));

        Assert.DoesNotContain("workstreams.md", await Violations(service));
    }

    [Fact]
    public async Task A_persona_section_with_a_chinese_heading_is_an_unrecognized_section()
    {
        using var service = Open();
        Write("persona.md", "## 红线\n- 不要删库\n");

        var violations = await Violations(service);

        Assert.Contains("Unrecognized section", violations);
        Assert.Contains("section \"红线\"", violations);
    }

    [Fact]
    public async Task Fixing_a_violation_by_hand_makes_it_disappear_from_the_next_report()
    {
        using var service = Open();
        Write("persona.md", "## Domain: network-proxy\n### Working rules\n- 测速取多次 → 再下结论\n");
        Assert.Contains("Domain without trigger", await Violations(service));

        Write("persona.md", "## Domain: network-proxy\nTrigger: 改代理时\n### Working rules\n- 测速取多次 → 再下结论\n");

        Assert.DoesNotContain("Domain without trigger", await Violations(service));
    }

    [Fact]
    public async Task A_domain_without_a_trigger_is_reported_at_its_heading_and_fixed_directly()
    {
        using var service = Open();
        Write("persona.md", "## Preferences\n- 沟通：先要结论\n\n## Domain: network-proxy\n### Preferences\n- 测速：只看持续测速\n");

        var violations = await Violations(service);

        Assert.Contains("persona.md", violations);
        Assert.Contains("line 4", violations);
        Assert.Contains("Domain without trigger", violations);
        Assert.DoesNotContain("line 6", violations);
    }

    [Fact]
    public async Task A_long_trigger_is_not_reported_and_an_unrecognized_section_in_a_domain_is()
    {
        using var service = Open();
        Write("persona.md", "## Domain: network-proxy\nTrigger: " + new string('改', 200) + "\n### Preferences\n- 测速：只看持续测速\n### 其他\n- 不认识的节\n");

        var violations = await Violations(service);

        Assert.DoesNotContain("Trigger too long", violations);
        Assert.Contains("Unrecognized section", violations);
        Assert.Contains("section \"其他\"", violations);
    }

    [Fact]
    public async Task Red_lines_and_working_rules_in_any_wording_are_not_reported()
    {
        using var service = Open();
        Write("persona.md", "## Red lines\n- 不要发布版本\n## Working rules\n- 遇到问题时先查日志\n"
            + "## Domain: network-proxy\nTrigger: 改代理时\n### Red lines\n- 不要改全局代理\n");

        Assert.DoesNotContain("persona.md", await Violations(service));
    }

    [Fact]
    public async Task A_domain_with_many_entries_and_many_domains_are_not_put_to_the_user()
    {
        using var service = Open();
        Write("persona.md", "## Domain: network-proxy\nTrigger: 改代理时\n### Preferences\n- 第一条\n- 第二条\n\n## Domain: writing\nTrigger: 写文章时\n### Preferences\n- 写作\n");

        var violations = await OverLimit(service);

        Assert.DoesNotContain("Domain over limit", violations);
        Assert.DoesNotContain("Too many domains", violations);
    }

    [Fact]
    public async Task A_domain_name_that_breaks_the_naming_rule_is_reported()
    {
        using var service = Open();
        Write("persona.md", "## Domain: Network\nTrigger: 改代理时\n### Preferences\n- 测速：只看持续测速\n");

        Assert.Contains("Invalid scope name", await Violations(service));
    }

}
