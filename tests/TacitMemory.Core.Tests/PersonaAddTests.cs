using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using static TacitMemory.Core.Tests.ShortCodes;

namespace TacitMemory.Core.Tests;

public sealed partial class PersonaAddTests : IDisposable
{
    private readonly TestHome _home = TestHome.WithManualSources();
    private readonly MemoryService _service;

    public PersonaAddTests()
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

    private string PersonaMd => Path.Combine(Memory, "persona.md");

    private string AgentsMd => Path.Combine(_home.UserHome, ".omp", "agent", "AGENTS.md");

    private string Git(params string[] args) => TestHome.Git(Memory, args);

    // Secret-shaped samples are assembled at run time so no scanner mistakes this file for a leak.
    private static readonly string OpenAiKey = "sk-" + "proj-Abc123def456GHI789jkl012MNO345";

    private const string Today = "2026-11-02";

    private static WritePersonaRequest Add(string? section, string? text, string? source = "发布前必须问我", bool? confirmNoSecret = null) => new()
    {
        Op = "add",
        Section = section,
        Text = text,
        Source = source,
        ConfirmNoSecret = confirmNoSecret,
    };

    [GeneratedRegex(@"\[([0-9a-f]{6}(?:-\d+)?)\]")]
    private static partial Regex CodeInReply();

    private static string CodeOf(ToolResult result) => CodeInReply().Match(result.Text).Groups[1].Value;

    private void WritePersona(string text) => File.WriteAllText(PersonaMd, text.ReplaceLineEndings("\n"));

    [Fact]
    public async Task Adding_a_red_line_appends_it_to_the_section_with_a_dated_source_comment_and_commits_only_persona()
    {
        WritePersona("## 红线\n- 不要删库；改为先备份（理由：不可逆）\n  <!-- 2026-10-25 原话「别删库」 -->\n\n## 协作规则\n\n## 偏好\n\n## 档案\n");
        Git("add", "persona.md");
        Git("-c", "user.name=tacit", "-c", "user.email=tacit@localhost", "commit", "-q", "-m", "test: persona baseline");
        File.WriteAllText(Path.Combine(Memory, "notes.txt"), "staged by the user\n");
        Git("add", "notes.txt");

        var result = await _service.WritePersonaAsync(Add("red_lines", "不要自行发布版本；改为先请用户确认"));

        Assert.False(result.IsError, result.Text);
        Assert.True(result.Written);
        Assert.Equal(
            "## 红线\n- 不要删库；改为先备份（理由：不可逆）\n  <!-- 2026-10-25 原话「别删库」 -->\n"
            + $"- 不要自行发布版本；改为先请用户确认\n  <!-- {Today} 原话「发布前必须问我」 -->\n\n## 协作规则\n\n## 偏好\n\n## 档案\n",
            File.ReadAllText(PersonaMd));
        Assert.Equal(ShortOf("不要自行发布版本；改为先请用户确认"), CodeOf(result));
        Assert.Contains("The section now has 2 entries.", result.Text);
        Assert.Equal($"persona: add {ShortOf("不要自行发布版本；改为先请用户确认")}", Git("log", "-1", "--format=%s").Trim());
        Assert.Equal("persona.md", Git("show", "--name-only", "--format=", "HEAD").Trim());
        Assert.Equal("A  notes.txt", Git("status", "--porcelain").TrimEnd());
    }

    [Fact]
    public async Task Adding_to_the_profile_writes_the_source_comment_in_the_memory_language()
    {
        WritePersona("## 档案\n- 系统：Windows 11 x64\n");

        var result = await _service.WritePersonaAsync(Add("profile", "主力语言：C#", "我主要写 C#"));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(
            $"## 档案\n- 系统：Windows 11 x64\n- 主力语言：C#\n  <!-- {Today} 原话「我主要写 C#」 -->\n",
            File.ReadAllText(PersonaMd));
    }

    [Fact]
    public async Task Working_rules_must_contain_an_arrow_and_red_lines_must_name_an_alternative()
    {
        var refusedRule = await _service.WritePersonaAsync(Add("working_rules", "需求不清时先追问"));
        var refusedRedLine = await _service.WritePersonaAsync(Add("red_lines", "不要删库"));

        Assert.True(refusedRule.IsError);
        Assert.False(refusedRule.Written);
        Assert.Contains("→", refusedRule.Text);
        Assert.True(refusedRedLine.IsError);
        Assert.False(refusedRedLine.Written);
        Assert.Contains("instead", refusedRedLine.Text);
        Assert.DoesNotContain("原话", File.ReadAllText(PersonaMd));

        Assert.False((await _service.WritePersonaAsync(Add("working_rules", "需求不清时 → 先连续追问"))).IsError);
        Assert.False((await _service.WritePersonaAsync(Add("red_lines", "Don't publish without asking; instead ask the user first"))).IsError);
        Assert.False((await _service.WritePersonaAsync(Add("red_lines", "不要删库；改为先备份"))).IsError);
    }

    [Fact]
    public async Task Preferences_and_profile_are_not_checked_for_sentence_form()
    {
        Assert.False((await _service.WritePersonaAsync(Add("preferences", "沟通：先要结论"))).IsError);
        Assert.False((await _service.WritePersonaAsync(Add("profile", "系统 Windows 11"))).IsError);
    }

    [Fact]
    public async Task Text_and_source_must_be_one_line_and_source_must_not_end_a_comment()
    {
        var multiLine = await _service.WritePersonaAsync(Add("preferences", "沟通：\n先要结论"));
        var multiLineSource = await _service.WritePersonaAsync(Add("preferences", "沟通：先要结论", "第一行\n第二行"));
        var commentEnd = await _service.WritePersonaAsync(Add("preferences", "沟通：先要结论", "原话 --> 不对"));

        Assert.True(multiLine.IsError);
        Assert.True(multiLineSource.IsError);
        Assert.True(commentEnd.IsError);
        Assert.Contains("-->", commentEnd.Text);
        Assert.False(File.Exists(PersonaMd) && File.ReadAllText(PersonaMd).Contains("沟通"));
    }

    [Theory]
    [InlineData("\u2028")]
    [InlineData("\u2029")]
    [InlineData("\u0085")]
    public async Task Text_and_source_must_not_contain_a_line_break_an_editor_shows_as_a_second_line(string lineBreak)
    {
        WritePersona("## 偏好\n- 沟通：先要结论\n\n## 档案\n- 系统：Windows 11 x64\n");
        var before = File.ReadAllText(PersonaMd);

        var inText = await _service.WritePersonaAsync(Add("preferences", $"沟通：先说结论{lineBreak}再附依据"));
        var inSource = await _service.WritePersonaAsync(Add("preferences", "沟通：少用表格", $"少用表格{lineBreak}再说"));
        var inReplace = await _service.WritePersonaAsync(new WritePersonaRequest
        {
            Op = "replace",
            Hash = ShortOf("沟通：先要结论"),
            Text = $"沟通：多用{lineBreak}列表",
            Source = "多用列表",
        });

        Assert.True(inText.IsError, inText.Text);
        Assert.Contains("one line", inText.Text);
        Assert.True(inSource.IsError, inSource.Text);
        Assert.True(inReplace.IsError, inReplace.Text);
        Assert.Equal(before, File.ReadAllText(PersonaMd));
    }

    [Fact]
    public async Task Text_of_any_length_is_accepted_when_it_is_one_line()
    {
        var lengthy = "偏好：" + new string('x', 2000);

        var result = await _service.WritePersonaAsync(Add("preferences", lengthy));

        Assert.False(result.IsError, result.Text);
        Assert.True(result.Written);
        Assert.Contains(new string('x', 2000), File.ReadAllText(PersonaMd));
    }

    [Fact]
    public async Task A_section_takes_entries_past_any_count_without_refusal()
    {
        var red = Enumerable.Range(1, 7).Select(i => $"不要做{i}；改为做{i}").ToList();
        WritePersona("## 红线\n" + string.Concat(red.Select(line => $"- {line}\n")) + "\n## 协作规则\n\n## 偏好\n\n## 档案\n");

        var result = await _service.WritePersonaAsync(Add("red_lines", "不要做八；改为做八"));

        Assert.False(result.IsError, result.Text);
        Assert.True(result.Written);
        Assert.Contains("The section now has 8 entries.", result.Text);
    }

    [Fact]
    public async Task A_new_domain_is_created_however_many_domains_the_persona_holds()
    {
        for (var i = 1; i <= 11; i++)
            Assert.False((await _service.WritePersonaAsync(InScope(Add("preferences", $"测速{i}：只看持续测速", "只看持续"), $"domain:area{i}", "改代理时"))).IsError);

        var persona = File.ReadAllText(PersonaMd);
        for (var i = 1; i <= 11; i++)
            Assert.Contains($"## 领域：area{i}", persona);
    }

    [Fact]
    public async Task A_missing_section_heading_is_restored_after_the_nearest_earlier_section()
    {
        WritePersona("## 红线\n- 不要删库；改为先备份\n\n## 协作规则\n- 需求不清时 → 先追问\n\n## 档案\n- 系统：Windows 11 x64\n");

        var result = await _service.WritePersonaAsync(Add("preferences", "沟通：先要结论", "先要结论"));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(
            "## 红线\n- 不要删库；改为先备份\n\n## 协作规则\n- 需求不清时 → 先追问\n\n"
            + $"## 偏好\n- 沟通：先要结论\n  <!-- {Today} 原话「先要结论」 -->\n\n"
            + "## 档案\n- 系统：Windows 11 x64\n",
            File.ReadAllText(PersonaMd));
    }

    [Fact]
    public async Task With_no_earlier_section_the_restored_heading_goes_before_the_nearest_later_section()
    {
        WritePersona("## 偏好\n- 沟通：先要结论\n\n## 档案\n- 系统：Windows 11 x64\n");

        var result = await _service.WritePersonaAsync(Add("red_lines", "不要删库；改为先备份", "别删库"));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(
            $"## 红线\n- 不要删库；改为先备份\n  <!-- {Today} 原话「别删库」 -->\n\n"
            + "## 偏好\n- 沟通：先要结论\n\n## 档案\n- 系统：Windows 11 x64\n",
            File.ReadAllText(PersonaMd));
    }

    [Fact]
    public async Task With_no_standard_section_at_all_the_restored_heading_goes_at_the_end_of_the_file()
    {
        WritePersona("## 其他\n- 随手记的一条\n");

        var result = await _service.WritePersonaAsync(Add("profile", "系统：Windows 11 x64"));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(
            $"## 其他\n- 随手记的一条\n\n## 档案\n- 系统：Windows 11 x64\n  <!-- {Today} 原话「发布前必须问我」 -->\n",
            File.ReadAllText(PersonaMd));
    }

    [Fact]
    public async Task A_section_written_under_english_headings_is_found_and_its_sentence_form_recognized()
    {
        WritePersona("## Red lines\n- Don't delete the database; instead back it up first\n  <!-- 2026-10-25 quote「never delete data」 -->\n\n## Working rules\n\n## Preferences\n\n## Profile\n");

        var result = await _service.WritePersonaAsync(Add("red_lines", "Don't publish without asking; instead ask the user", "ask me first"));

        Assert.False(result.IsError, result.Text);
        Assert.Contains(
            "- Don't publish without asking; instead ask the user\n  <!-- 2026-11-02 原话「ask me first」 -->\n\n## Working rules",
            File.ReadAllText(PersonaMd));
    }

    [Fact]
    public async Task Entries_with_the_same_text_get_a_suffixed_short_code_in_file_order()
    {
        var first = await _service.WritePersonaAsync(Add("preferences", "沟通：先要结论", "先要结论"));
        var second = await _service.WritePersonaAsync(Add("preferences", "沟通：先要结论", "再说一遍"));

        Assert.Equal(ShortOf("沟通：先要结论"), CodeOf(first));
        Assert.Equal($"{ShortOf("沟通：先要结论")}-2", CodeOf(second));
    }

    [Fact]
    public async Task A_secret_in_text_or_source_is_refused_until_the_agent_confirms_it_is_not_one()
    {
        var inText = await _service.WritePersonaAsync(Add("profile", $"密钥：{OpenAiKey}", "放心用"));
        var inSource = await _service.WritePersonaAsync(Add("profile", "系统：Windows 11", $"密钥是 {OpenAiKey}"));

        Assert.True(inText.IsError);
        Assert.True(inSource.IsError);
        Assert.Contains("confirm_no_secret", inText.Text);
        Assert.False(File.ReadAllText(PersonaMd).Contains("sk-", StringComparison.Ordinal));

        var confirmed = await _service.WritePersonaAsync(Add("profile", $"密钥：{OpenAiKey}", "放心用", confirmNoSecret: true));

        Assert.False(confirmed.IsError, confirmed.Text);
        Assert.Contains(OpenAiKey, File.ReadAllText(PersonaMd));
    }

    [Fact]
    public async Task A_persona_that_is_not_utf8_is_refused_and_left_alone()
    {
        var bytes = new byte[] { 0x23, 0x23, 0x20, 0xFF, 0xFE, 0x0A };
        File.WriteAllBytes(PersonaMd, bytes);

        var result = await _service.WritePersonaAsync(Add("profile", "系统：Windows 11"));

        Assert.True(result.IsError);
        Assert.False(result.Written);
        Assert.Equal(bytes, File.ReadAllBytes(PersonaMd));
    }

    [Fact]
    public async Task Uncommitted_user_edits_to_persona_are_committed_with_the_write_and_noted_in_the_subject()
    {
        WritePersona("## 红线\n\n## 协作规则\n\n## 偏好\n- 用户手写的一条\n\n## 档案\n");

        var result = await _service.WritePersonaAsync(Add("preferences", "沟通：先要结论", "先要结论"));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(
            $"persona: add {ShortOf("沟通：先要结论")} (with user edits)",
            Git("log", "-1", "--format=%s").Trim());
        Assert.Equal(string.Empty, Git("status", "--porcelain").Trim());
    }

    [Fact]
    public async Task The_first_write_of_the_day_leaves_a_backup()
    {
        var result = await _service.WritePersonaAsync(Add("profile", "系统：Windows 11"));
        await _service.SettledAsync();

        Assert.False(result.IsError, result.Text);
        Assert.True(File.Exists(Path.Combine(_home.TacitHome, "backups", $"auto-{Today}.zip")));
    }

    [Fact]
    public async Task A_write_re_renders_the_bound_resident_block_in_the_background()
    {
        _home.Bind();

        var result = await _service.WritePersonaAsync(Add("preferences", "沟通：先要结论", "先要结论"));

        Assert.False(result.IsError, result.Text);
        await _service.SettledAsync();
        Assert.Contains("- 沟通：先要结论\n", File.ReadAllText(AgentsMd));
        Assert.DoesNotContain(ShortOf("沟通：先要结论"), File.ReadAllText(AgentsMd));
    }

    [Fact]
    public async Task Missing_parameters_are_named_together_in_one_refusal()
    {
        var result = await _service.WritePersonaAsync(Add(null, null, null));

        Assert.True(result.IsError);
        Assert.Contains("persona_section", result.Text);
        Assert.Contains("text", result.Text);
        Assert.Contains("source", result.Text);
        Assert.Contains("Next step:", result.Text);
    }

    [Fact]
    public async Task An_unknown_section_is_refused_with_the_list_of_sections()
    {
        var result = await _service.WritePersonaAsync(Add("Red Lines", "不要删库；改为先备份"));

        Assert.True(result.IsError);
        Assert.Contains("red_lines", result.Text);
        Assert.Contains("working_rules", result.Text);
        Assert.Contains("preferences", result.Text);
        Assert.Contains("profile", result.Text);
    }

    [Fact]
    public async Task A_parameter_that_does_not_apply_to_add_is_named()
    {
        var request = Add("profile", "系统：Windows 11") with { Hash = "a3f9c2" };

        var result = await _service.WritePersonaAsync(request);

        Assert.True(result.IsError);
        Assert.Contains("hash", result.Text);
        Assert.Contains("Next step:", result.Text);
    }

    [Fact]
    public async Task Unknown_ops_and_a_missing_op_are_refused_clearly()
    {
        var unknown = await _service.WritePersonaAsync(new WritePersonaRequest { Op = "rewrite" });
        var missing = await _service.WritePersonaAsync(new WritePersonaRequest());

        Assert.True(unknown.IsError);
        Assert.Contains("add, propose, replace, delete", unknown.Text);
        Assert.True(missing.IsError);
        Assert.Contains("op", missing.Text);
        Assert.DoesNotContain("删除", File.ReadAllText(PersonaMd));
    }

    [Fact]
    public async Task An_unknown_parameter_is_refused_by_name_and_leaves_persona_and_history_alone()
    {
        var before = File.ReadAllText(PersonaMd);
        var commits = Git("rev-list", "--count", "HEAD").Trim();

        var result = await _service.WritePersonaAsync(Add("preferences", "沟通：先要结论") with { UnknownParameters = ["content"] });

        Assert.True(result.IsError);
        Assert.StartsWith("Refused: Unknown parameter content.\nNext step: Call write_persona again with only the parameters for op add: persona_section, text, source, scope, trigger, confirm_no_secret.", result.Text);
        Assert.Equal(before, File.ReadAllText(PersonaMd));
        Assert.Equal(commits, Git("rev-list", "--count", "HEAD").Trim());
    }

    [Fact]
    public async Task Unknown_names_come_first_with_the_op_problems_and_a_missing_op_lists_every_parameter()
    {
        var missing = await _service.WritePersonaAsync(new WritePersonaRequest { Section = "preferences", Text = "沟通：先要结论", UnknownParameters = ["content", "limit"] });
        var misplaced = await _service.WritePersonaAsync(new WritePersonaRequest { Op = "delete", Text = "沟通：先要结论", UnknownParameters = ["content"] });

        Assert.StartsWith("Refused:\n- Unknown parameter content.\n- Unknown parameter limit.\n- Missing parameter op.\nNext step: Call write_persona again with op set to one of: add, propose, replace, delete; its parameters are: op, persona_section, text, source, scope, trigger, confirm_no_secret, hash.", missing.Text);
        Assert.StartsWith("Refused:\n- Unknown parameter content.\n- Parameter text does not apply to op delete.\nNext step: Call write_persona again with only the parameters for op delete: hash.", misplaced.Text);
    }

    private static WritePersonaRequest InScope(WritePersonaRequest request, string scope, string? trigger = null) =>
        request with { Scope = scope, Trigger = trigger };

    [Fact]
    public async Task A_new_domain_gets_a_block_with_its_trigger_after_the_global_sections()
    {
        WritePersona("## 红线\n- 不要删库；改为先备份\n\n## 协作规则\n\n## 偏好\n\n## 档案\n- 系统：Windows 11 x64\n");
        Git("add", "persona.md");
        Git("-c", "user.name=tacit", "-c", "user.email=tacit@localhost", "commit", "-q", "-m", "test: persona baseline");

        var result = await _service.WritePersonaAsync(
            InScope(Add("working_rules", "性能比较 → 不拿单次测速当结论", "别拿单次测速下结论"), "domain:network-proxy", "改代理、分流或测速时"));

        Assert.False(result.IsError, result.Text);
        Assert.True(result.Written);
        Assert.Equal(
            "## 红线\n- 不要删库；改为先备份\n\n## 协作规则\n\n## 偏好\n\n## 档案\n- 系统：Windows 11 x64\n\n"
            + "## 领域：network-proxy\n触发：改代理、分流或测速时\n### 协作规则\n- 性能比较 → 不拿单次测速当结论\n"
            + $"  <!-- {Today} 原话「别拿单次测速下结论」 -->\n",
            File.ReadAllText(PersonaMd));
        Assert.Equal(ShortOf("性能比较 → 不拿单次测速当结论"), CodeOf(result));
        Assert.Contains("domain network-proxy", result.Text);
        Assert.Contains("created", result.Text);
        Assert.Equal($"persona: add {ShortOf("性能比较 → 不拿单次测速当结论")}", Git("log", "-1", "--format=%s").Trim());
        Assert.Equal("persona.md", Git("show", "--name-only", "--format=", "HEAD").Trim());
    }

    [Fact]
    public async Task An_existing_domain_takes_entries_under_its_section_and_ignores_a_trigger_given_again()
    {
        WritePersona("## 偏好\n- 沟通：先要结论\n\n## 领域：network-proxy\n触发：改代理时\n### 协作规则\n- 旧规则 → 旧做法\n### 偏好\n- 测速：只看持续测速\n");

        var result = await _service.WritePersonaAsync(InScope(Add("working_rules", "切换节点 → 先备份配置", "先备份"), "domain:network-proxy", "完全不同的触发"));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(
            "## 偏好\n- 沟通：先要结论\n\n## 领域：network-proxy\n触发：改代理时\n### 协作规则\n- 旧规则 → 旧做法\n"
            + $"- 切换节点 → 先备份配置\n  <!-- {Today} 原话「先备份」 -->\n### 偏好\n- 测速：只看持续测速\n",
            File.ReadAllText(PersonaMd));
        Assert.DoesNotContain("完全不同的触发", File.ReadAllText(PersonaMd));
    }

    [Fact]
    public async Task A_project_scope_is_refused_and_the_next_step_gives_the_workstream_form()
    {
        var result = await _service.WritePersonaAsync(InScope(Add("preferences", "日志：少打印", "少打印"), "project:alpha"));

        Assert.True(result.IsError);
        Assert.False(result.Written);
        Assert.Contains("Next step:", result.Text);
        Assert.Contains("workstream:<name>", result.Text);
    }

    [Fact]
    public async Task Scope_blocks_follow_the_global_sections_with_domains_first_and_each_kind_sorted_by_name()
    {
        WritePersona("## 红线\n\n## 偏好\n- 沟通：先要结论\n\n## 领域：zeta\n触发：收尾时\n### 偏好\n- 收尾：先列清单\n\n## 主线：my-app\n### 偏好\n- 发布：先打标签\n");

        Assert.False((await _service.WritePersonaAsync(InScope(Add("preferences", "记录：先写结论", "先写结论"), "domain:alpha", "写记录时"))).IsError);
        Assert.False((await _service.WritePersonaAsync(InScope(Add("preferences", "日志：少打印", "少打印"), "workstream:alpha"))).IsError);

        var text = File.ReadAllText(PersonaMd);
        var headings = new[] { "\n## 偏好", "\n## 领域：alpha", "\n## 领域：zeta", "\n## 主线：alpha", "\n## 主线：my-app" };
        var positions = headings.Select(heading => text.IndexOf(heading, StringComparison.Ordinal)).ToArray();
        Assert.All(positions, position => Assert.True(position >= 0, text));
        Assert.Equal(positions.Order().ToArray(), positions);
    }

    [Fact]
    public async Task A_missing_section_inside_a_block_is_restored_in_the_standard_order_within_the_block()
    {
        WritePersona("## 领域：network-proxy\n触发：改代理时\n### 偏好\n- 测速：只看持续测速\n");

        var result = await _service.WritePersonaAsync(
            InScope(Add("red_lines", "不要用单次测速下结论；改为先看持续测速", "别下结论"), "domain:network-proxy"));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(
            "## 领域：network-proxy\n触发：改代理时\n\n### 红线\n- 不要用单次测速下结论；改为先看持续测速\n"
            + $"  <!-- {Today} 原话「别下结论」 -->\n\n### 偏好\n- 测速：只看持续测速\n",
            File.ReadAllText(PersonaMd));
    }

    [Fact]
    public async Task A_block_with_red_lines_and_working_rules_keeps_both_and_restores_the_missing_preferences_last()
    {
        WritePersona("## 领域：network-proxy\n触发：改代理时\n### 红线\n- r0\n\n### 协作规则\n- 先给结论\n");

        var result = await _service.WritePersonaAsync(InScope(Add("preferences", "测速：只看持续测速", "只看持续"), "domain:network-proxy"));

        Assert.False(result.IsError, result.Text);
        var text = File.ReadAllText(PersonaMd);
        Assert.Contains("### 红线\n- r0\n", text, StringComparison.Ordinal);
        Assert.Contains("### 协作规则\n- 先给结论\n", text, StringComparison.Ordinal);
        Assert.True(text.IndexOf("### 红线", StringComparison.Ordinal) < text.IndexOf("### 协作规则", StringComparison.Ordinal));
        Assert.True(text.IndexOf("### 协作规则", StringComparison.Ordinal) < text.IndexOf("### 偏好", StringComparison.Ordinal));
        Assert.Contains("- 测速：只看持续测速", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_workstream_block_needs_no_trigger_and_holds_its_entries_under_the_workstream_heading()
    {
        WritePersona("## 偏好\n- 沟通：先要结论\n");

        var result = await _service.WritePersonaAsync(InScope(Add("working_rules", "发版 → 先问我", "发版前问我"), "workstream:my-app"));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(
            "## 偏好\n- 沟通：先要结论\n\n## 主线：my-app\n### 协作规则\n- 发版 → 先问我\n"
            + $"  <!-- {Today} 原话「发版前问我」 -->\n",
            File.ReadAllText(PersonaMd));
        Assert.Contains("workstream my-app", result.Text);
    }

    [Fact]
    public async Task A_new_domain_without_a_trigger_is_refused_with_the_next_step_and_writes_nothing()
    {
        WritePersona("## 偏好\n- 沟通：先要结论\n");
        var before = File.ReadAllText(PersonaMd);

        var result = await _service.WritePersonaAsync(InScope(Add("preferences", "测速：只看持续测速", "只看持续"), "domain:network-proxy"));

        Assert.True(result.IsError);
        Assert.False(result.Written);
        Assert.Contains("trigger", result.Text);
        Assert.Contains("Next step:", result.Text);
        Assert.Equal(before, File.ReadAllText(PersonaMd));
    }

    [Fact]
    public async Task A_trigger_must_be_one_line_and_has_no_length_limit()
    {
        var request = InScope(Add("preferences", "测速：只看持续测速"), "domain:network-proxy");

        var twoLines = await _service.WritePersonaAsync(request with { Trigger = "改代理\n测速" });
        var lengthy = await _service.WritePersonaAsync(request with { Trigger = new string('改', 300) + "时" });

        Assert.True(twoLines.IsError);
        Assert.Contains("one line", twoLines.Text);
        Assert.False(lengthy.IsError, lengthy.Text);
    }

    [Theory]
    [InlineData("area:network")]
    [InlineData("domain:Network")]
    [InlineData("domain:")]
    [InlineData("workstream:.hidden")]
    [InlineData("domain")]
    public async Task A_scope_that_is_not_global_a_workstream_or_a_domain_with_a_valid_name_is_refused(string scope)
    {
        var result = await _service.WritePersonaAsync(InScope(Add("preferences", "测速：只看持续测速"), scope, "改代理时"));

        Assert.True(result.IsError);
        Assert.False(result.Written);
        Assert.Contains("global, workstream:<name> or domain:<label>", result.Text);
        Assert.Contains("Next step:", result.Text);
    }

    [Fact]
    public async Task A_domain_takes_entries_past_any_count_and_reports_its_count()
    {
        Assert.False((await _service.WritePersonaAsync(InScope(Add("preferences", "测速：只看持续测速"), "domain:network-proxy", "改代理时"))).IsError);
        Assert.False((await _service.WritePersonaAsync(InScope(Add("preferences", "分流：先看规则"), "domain:network-proxy"))).IsError);

        var third = await _service.WritePersonaAsync(InScope(Add("red_lines", "不要单次下结论；改为看持续数据"), "domain:network-proxy"));

        Assert.False(third.IsError, third.Text);
        Assert.Contains("The domain has 3 entries.", third.Text);
    }

    [Fact]
    public async Task Global_sections_take_entries_past_any_count()
    {
        for (var i = 1; i <= 12; i++)
            Assert.False((await _service.WritePersonaAsync(Add("preferences", $"节奏{i}：短句优先"))).IsError);

        Assert.False((await _service.WritePersonaAsync(InScope(Add("preferences", "测速：只看持续测速"), "domain:network-proxy", "改代理时"))).IsError);
    }

    [Fact]
    public async Task A_domain_entry_repeating_a_global_text_is_written_with_a_suffixed_code_once_the_model_judges_it_distinct()
    {
        // The models are available here, so the duplicate judgment runs; the decision model picks "none" by default,
        // which means the two entries are distinct and the write goes through.
        using var models = new ModelDouble();
        using var home = new TestHome();
        home.InitReady(models: models);
        using var service = home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);
        var persona = Path.Combine(home.DefaultMemory, "persona.md");
        Assert.False((await service.WritePersonaAsync(Add("preferences", "沟通：先要结论", "先要结论"))).IsError);

        var result = await service.WritePersonaAsync(InScope(Add("preferences", "沟通：先要结论", "再说一遍"), "domain:writing", "写文章时"));

        Assert.False(result.IsError, result.Text);
        // Without the judgment the suffix would still be written; a skipped check must fail here, not pass unnoticed.
        Assert.True(models.DecisionCalls >= 1, "the duplicate judgment did not run");
        Assert.Equal($"{ShortOf("沟通：先要结论")}-2", CodeOf(result));
        Assert.Contains("再说一遍", File.ReadAllText(persona));

        // The suffixed code finds the domain entry for a replacement, and the replaced entry then finds by its own code.
        var replaced = await service.WritePersonaAsync(new WritePersonaRequest
        {
            Op = "replace",
            Hash = $"{ShortOf("沟通：先要结论")}-2",
            Text = "沟通：写文章先给提纲",
            Source = "写文章先给提纲",
        });
        Assert.False(replaced.IsError, replaced.Text);
        Assert.DoesNotContain("再说一遍", File.ReadAllText(persona));
        Assert.Contains("沟通：写文章先给提纲", File.ReadAllText(persona));

        var deleted = await service.WritePersonaAsync(new WritePersonaRequest { Op = "delete", Hash = ShortOf("沟通：写文章先给提纲") });
        Assert.False(deleted.IsError, deleted.Text);
        Assert.DoesNotContain("沟通：写文章先给提纲", File.ReadAllText(persona));
        Assert.Contains("沟通：先要结论", File.ReadAllText(persona));
    }

    [Fact]
    public async Task A_secret_in_the_trigger_is_refused_like_one_in_the_text()
    {
        var result = await _service.WritePersonaAsync(InScope(Add("preferences", "测速：只看持续测速"), "domain:network-proxy", $"密钥 {OpenAiKey}"));

        Assert.True(result.IsError);
        Assert.Contains("confirm_no_secret", result.Text);
        Assert.False(File.Exists(PersonaMd) && File.ReadAllText(PersonaMd).Contains("sk-", StringComparison.Ordinal));
    }
}
