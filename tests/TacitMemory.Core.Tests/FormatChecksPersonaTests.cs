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
        Section(await service.CheckupReportAsync(), FormatTitle);

    [Fact]
    public async Task A_red_line_without_instead_is_reported_at_its_line_and_fixed_directly()
    {
        using var service = Open();
        Write("persona.md", "## 红线\n- 不要发布版本\n- 不要推送到公开仓库；改为先请用户确认\n");

        var violations = await Violations(service);

        Assert.Contains("persona.md", violations);
        Assert.Contains("line 2", violations);
        Assert.Contains("Red line without instead", violations);
        Assert.Contains("Fix directly.", violations);
        Assert.DoesNotContain("line 3", violations);
    }

    [Fact]
    public async Task A_red_line_with_instead_in_either_language_is_accepted()
    {
        using var service = Open();
        Write("persona.md", "## 红线\n- 不要发布版本；改为先请用户确认\n- Don't publish; instead ask the user first\n");

        var violations = await Violations(service);

        Assert.DoesNotContain("Red line without instead", violations);
    }

    [Fact]
    public async Task A_working_rule_without_an_arrow_is_reported()
    {
        using var service = Open();
        Write("persona.md", "## 协作规则\n- 遇到问题时先查日志\n- 需求不清时 → 先问清楚\n");

        var violations = await Violations(service);

        Assert.Contains("persona.md", violations);
        Assert.Contains("line 2", violations);
        Assert.Contains("Working rule without →", violations);
        Assert.DoesNotContain("line 3", violations);
    }

    [Fact]
    public async Task An_entry_longer_than_item_length_is_reported()
    {
        using var service = Open("limits:\n  itemLength: 10\n");
        Write("persona.md", "## 偏好\n- 短\n- " + new string('字', 11) + "\n");

        var violations = await Violations(service);

        Assert.Contains("line 3", violations);
        Assert.Contains("Entry too long", violations);
        Assert.DoesNotContain("line 2", violations);
    }

    [Fact]
    public async Task A_section_over_its_limit_is_put_to_the_user()
    {
        using var service = Open("limits:\n  persona:\n    preferences: 1\n");
        Write("persona.md", "## 偏好\n- 第一条\n- 第二条\n");

        var violations = await Violations(service);

        Assert.Contains("Section over limit", violations);
        Assert.Contains("Ask the user.", violations);
        Assert.Contains("persona.md", violations);
    }

    [Fact]
    public async Task An_unknown_section_and_text_outside_entries_are_reported()
    {
        using var service = Open();
        Write("persona.md", "## 偏好\n- 有效条目\n随手写的一行\n## 其他\n- 不认识的节\n");

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
        Write("candidates.md", "## 候选\n### 先说结论\n- 2026-10-28 原话「先说结论」\n");

        var violations = await Violations(service);

        Assert.Contains("candidates.md", violations);
        Assert.Contains("line 2", violations);
        Assert.Contains("Candidate without section", violations);
    }

    [Fact]
    public async Task An_unrecognized_occurrence_line_under_a_candidate_is_reported()
    {
        using var service = Open();
        Write("candidates.md", "## 候选\n### 先说结论\n归属：偏好\n- 2026-10-28 原话「先说结论」\n- 随便写的\n");

        var violations = await Violations(service);

        Assert.Contains("line 5", violations);
        Assert.Contains("Unrecognized occurrence line", violations);
        Assert.DoesNotContain("line 4", violations);
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
    public async Task A_mainline_longer_than_its_limit_is_put_to_the_user()
    {
        using var service = Open();
        Write("recent.md", "## 近期主线\n- 一\n- 二\n- 三\n- 四\n- 五\n- 六\n");

        var violations = await Violations(service);

        Assert.Contains("recent.md", violations);
        Assert.Contains("Mainline over limit", violations);
        Assert.Contains("Ask the user.", violations);
    }

    [Fact]
    public async Task Fixing_a_violation_by_hand_makes_it_disappear_from_the_next_report()
    {
        using var service = Open();
        Write("persona.md", "## 红线\n- 不要发布版本\n");
        Assert.Contains("Red line without instead", await Violations(service));

        Write("persona.md", "## 红线\n- 不要发布版本；改为先请用户确认\n");

        Assert.DoesNotContain("Red line without instead", await Violations(service));
    }
}
