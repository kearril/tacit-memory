using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TacitMemory.Core.Tests;

public sealed partial class PersonaAddTests : IDisposable
{
    private readonly TestHome _home = new();
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

    private static string ShortOf(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\n").Trim())))[..6].ToLowerInvariant();

    [GeneratedRegex(@"\[([0-9a-f]{6}(?:-\d+)?)\]")]
    private static partial Regex CodeInReply();

    private static string CodeOf(ToolResult result) => CodeInReply().Match(result.Text).Groups[1].Value;

    private void WritePersona(string text) => File.WriteAllText(PersonaMd, text.ReplaceLineEndings("\n"));

    private async Task<bool> EventuallyAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition())
                return true;
            await Task.Delay(50);
        }

        return condition();
    }

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
        Assert.Contains("2 of 5", result.Text);
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
    public async Task Text_longer_than_the_item_limit_is_refused_and_the_limit_is_read_from_config()
    {
        var tooLong = "偏好：" + new string('x', 100);

        var refused = await _service.WritePersonaAsync(Add("preferences", tooLong));

        Assert.True(refused.IsError);
        Assert.Contains("100", refused.Text);
        Assert.False(File.Exists(PersonaMd) && File.ReadAllText(PersonaMd).Contains("xxxx"));

        using var home = new TestHome();
        home.InitReady("limits:\n  itemLength: 120\n");
        using var service = home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

        Assert.False((await service.WritePersonaAsync(Add("preferences", tooLong))).IsError);
    }

    [Fact]
    public async Task A_section_already_at_its_limit_refuses_add_and_lists_its_entries_with_short_codes()
    {
        var red = Enumerable.Range(1, 5).Select(i => $"不要做{i}；改为做{i}").ToList();
        WritePersona("## 红线\n" + string.Concat(red.Select(line => $"- {line}\n")) + "\n## 协作规则\n\n## 偏好\n\n## 档案\n");
        var before = File.ReadAllText(PersonaMd);

        var result = await _service.WritePersonaAsync(Add("red_lines", "不要做六；改为做六"));

        Assert.True(result.IsError);
        Assert.False(result.Written);
        Assert.StartsWith("Refused:", result.Text);
        Assert.Contains("Next step:", result.Text);
        Assert.Contains("replace", result.Text);
        Assert.Contains("delete", result.Text);
        foreach (var line in red)
            Assert.Contains($"[{ShortOf(line)}]", result.Text);
        Assert.Equal(before, File.ReadAllText(PersonaMd));

        Assert.False((await _service.WritePersonaAsync(Add("preferences", "沟通：先要结论"))).IsError);
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
        await _service.BackupCompletion;

        Assert.False(result.IsError, result.Text);
        Assert.True(File.Exists(Path.Combine(_home.TacitHome, "backups", $"auto-{Today}.zip")));
    }

    [Fact]
    public async Task A_write_re_renders_the_bound_resident_block_in_the_background()
    {
        _home.Bind();

        var result = await _service.WritePersonaAsync(Add("preferences", "沟通：先要结论", "先要结论"));

        Assert.False(result.IsError, result.Text);
        Assert.True(await EventuallyAsync(() => File.ReadAllText(AgentsMd).Contains("- 沟通：先要结论\n", StringComparison.Ordinal)));
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
        Assert.Contains("op=add", unknown.Text);
        Assert.True(missing.IsError);
        Assert.Contains("op", missing.Text);
        Assert.DoesNotContain("删除", File.ReadAllText(PersonaMd));
    }
}
