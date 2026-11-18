using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using static TacitMemory.Core.Tests.ShortCodes;

namespace TacitMemory.Core.Tests;

public sealed partial class PersonaReplaceDeleteTests : IDisposable
{
    private readonly TestHome _home = TestHome.WithManualSources();
    private readonly MemoryService _service;

    public PersonaReplaceDeleteTests()
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

    /// <summary>A persona with every kind of content the rewrite must leave alone: an unknown section, a hand-written comment, a continuation line.</summary>
    private const string Fixture = """
        ## Red lines
        - 不要删库；改为先备份
          <!-- 2026-10-25 quote「别删库」 -->

        ## Working rules
        - 需求不清时 → 先连续追问
          <!-- 2026-10-20 quote「先问清楚再动手」 -->

        ## Preferences
        - 沟通：先要结论
          <!-- 2026-10-21 quote「先说结论」 -->
        - 反感客套和铺垫

        ## 备注
        <!-- 手写的说明 -->
        - 这一节不是四节之一

        ## Profile
        - 系统：Windows 11 x64
          另有 WSL 环境
          <!-- 2026-10-01 quote「我用 Windows」 -->
        - 主力语言：C#

        """;

    private static string Lf(string text) => text.ReplaceLineEndings("\n");

    private void WritePersona(string text) => File.WriteAllText(PersonaMd, Lf(text));

    private void CommitPersona(string text)
    {
        WritePersona(text);
        Git("add", "persona.md");
        Git("-c", "user.name=tacit", "-c", "user.email=tacit@localhost", "commit", "-q", "-m", "test: persona baseline");
    }

    private static WritePersonaRequest Replace(string? hash, string? text, string? source = "先说结论", bool? confirmNoSecret = null) => new()
    {
        Op = "replace",
        Hash = hash,
        Text = text,
        Source = source,
        ConfirmNoSecret = confirmNoSecret,
    };

    private static WritePersonaRequest Delete(string? hash) => new() { Op = "delete", Hash = hash };

    [GeneratedRegex(@"\[([0-9a-f]{6}(?:-\d+)?)\]")]
    private static partial Regex CodeInReply();

    private static string CodeOf(ToolResult result) => CodeInReply().Match(result.Text).Groups[1].Value;

    [Fact]
    public async Task Replacing_an_entry_rewrites_it_in_place_with_todays_quote_and_leaves_every_other_byte_alone()
    {
        CommitPersona(Fixture);
        File.WriteAllText(Path.Combine(Memory, "notes.txt"), "staged by the user\n");
        Git("add", "notes.txt");
        var oldCode = ShortOf("沟通：先要结论");
        var newCode = ShortOf("沟通：先说结论，再讲依据");

        var result = await _service.WritePersonaAsync(Replace(oldCode, "沟通：先说结论，再讲依据"));

        Assert.False(result.IsError, result.Text);
        Assert.True(result.Written);
        Assert.Contains($"[{newCode}]", result.Text);
        Assert.Equal(
            Lf(Fixture).Replace(
                "- 沟通：先要结论\n  <!-- 2026-10-21 quote「先说结论」 -->\n",
                $"- 沟通：先说结论，再讲依据\n  <!-- {Today} quote「先说结论」 -->\n"),
            File.ReadAllText(PersonaMd));
        Assert.Equal($"persona: replace {newCode}", Git("log", "-1", "--format=%s").Trim());
        Assert.Equal("persona.md", Git("show", "--name-only", "--format=", "HEAD").Trim());
        Assert.Equal("A  notes.txt", Git("status", "--porcelain").TrimEnd());
    }

    [Fact]
    public async Task Replacing_an_entry_without_a_quote_adds_one_for_today()
    {
        CommitPersona(Fixture);
        var code = ShortOf("反感客套和铺垫");

        var result = await _service.WritePersonaAsync(Replace(code, "反感客套、铺垫和寒暄", "别绕弯子"));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(
            Lf(Fixture).Replace(
                "- 反感客套和铺垫\n",
                $"- 反感客套、铺垫和寒暄\n  <!-- {Today} quote「别绕弯子」 -->\n"),
            File.ReadAllText(PersonaMd));
    }

    [Fact]
    public async Task Replacing_takes_any_wording_in_any_section()
    {
        CommitPersona(Fixture);

        var redLine = await _service.WritePersonaAsync(Replace(ShortOf("不要删库；改为先备份"), "不要删库"));
        var workingRule = await _service.WritePersonaAsync(Replace(ShortOf("需求不清时 → 先连续追问"), "需求不清时先追问"));

        Assert.False(redLine.IsError, redLine.Text);
        Assert.False(workingRule.IsError, workingRule.Text);
        var persona = File.ReadAllText(PersonaMd);
        Assert.Contains("- 不要删库\n", persona);
        Assert.Contains("- 需求不清时先追问\n", persona);
    }

    [Fact]
    public async Task Replacing_with_the_same_text_and_source_writes_and_commits_nothing()
    {
        CommitPersona(Fixture.Replace("- 反感客套和铺垫\n", $"- 反感客套和铺垫\n  <!-- {Today} quote「别绕弯子」 -->\n"));
        var head = Git("rev-parse", "HEAD").Trim();
        var before = File.ReadAllText(PersonaMd);

        var result = await _service.WritePersonaAsync(Replace(ShortOf("反感客套和铺垫"), "反感客套和铺垫", "别绕弯子"));

        Assert.False(result.IsError, result.Text);
        Assert.False(result.Written);
        Assert.Equal(before, File.ReadAllText(PersonaMd));
        Assert.Equal(head, Git("rev-parse", "HEAD").Trim());
    }

    [Fact]
    public async Task A_section_over_its_limit_still_takes_replace_and_delete()
    {
        var red = Enumerable.Range(1, 6).Select(i => $"- 不要做{i}；改为做{i}\n").ToList();
        CommitPersona("## Red lines\n" + string.Concat(red) + "\n## Working rules\n\n## Preferences\n\n## Profile\n");

        var replaced = await _service.WritePersonaAsync(Replace(ShortOf("不要做6；改为做6"), "不要做六；改为做六"));
        var deleted = await _service.WritePersonaAsync(Delete(ShortOf("不要做5；改为做5")));

        Assert.False(replaced.IsError, replaced.Text);
        Assert.False(deleted.IsError, deleted.Text);
        Assert.True(deleted.Written);
        Assert.Contains("不要做六；改为做六", File.ReadAllText(PersonaMd));
        Assert.DoesNotContain("不要做5", File.ReadAllText(PersonaMd));
        Assert.Contains("不要做4", File.ReadAllText(PersonaMd));
    }

    [Fact]
    public async Task A_missing_hash_is_refused_with_a_next_step_to_read_the_codes_from_recall()
    {
        CommitPersona(Fixture);
        var before = File.ReadAllText(PersonaMd);

        var replaced = await _service.WritePersonaAsync(Replace(null, "沟通：先说结论", "先说结论"));
        var deleted = await _service.WritePersonaAsync(Delete(null));

        foreach (var refusal in new[] { replaced, deleted })
        {
            Assert.True(refusal.IsError);
            Assert.False(refusal.Written);
            Assert.StartsWith("Refused:", refusal.Text);
            Assert.Contains("Next step: Call recall with kind=persona to read the short codes", refusal.Text);
            Assert.DoesNotContain("Related:", refusal.Text);
            Assert.DoesNotContain($"[{ShortOf("沟通：先要结论")}]", refusal.Text);
        }

        Assert.Equal(before, File.ReadAllText(PersonaMd));
    }

    [Fact]
    public async Task A_stale_hash_is_refused_with_the_whole_persona_and_the_current_codes()
    {
        CommitPersona(Fixture);
        var staleCode = ShortOf("反感客套和铺垫");
        // The user edits the entry by hand, so its code expires.
        WritePersona(Fixture.Replace("反感客套和铺垫", "反感客套、铺垫和寒暄"));

        var result = await _service.WritePersonaAsync(Replace(staleCode, "反感客套、铺垫和寒暄", "别绕弯子"));

        Assert.True(result.IsError);
        Assert.False(result.Written);
        Assert.Contains("Next step: Call recall with kind=persona to read the short codes", result.Text);
        Assert.DoesNotContain("Related:", result.Text);
        Assert.DoesNotContain($"[{staleCode}]", result.Text);
    }

    [Fact]
    public async Task Entries_with_the_same_text_are_addressed_by_their_suffixed_short_code()
    {
        CommitPersona(Fixture.Replace("- 反感客套和铺垫\n", "- 反感客套和铺垫\n- 反感客套和铺垫\n"));
        var second = ShortOf("反感客套和铺垫") + "-2";

        var result = await _service.WritePersonaAsync(Delete(second));

        Assert.False(result.IsError, result.Text);
        Assert.Single(Regex.Matches(File.ReadAllText(PersonaMd), "反感客套和铺垫"));
    }

    [Fact]
    public async Task Replace_refuses_a_persona_section_and_a_missing_text_or_source_by_name()
    {
        CommitPersona(Fixture);
        var before = File.ReadAllText(PersonaMd);
        var code = ShortOf("反感客套和铺垫");

        var withSection = await _service.WritePersonaAsync(Replace(code, "反感客套和铺垫", "别绕弯子") with { Section = "preferences" });
        var noText = await _service.WritePersonaAsync(Replace(code, null, "别绕弯子"));
        var noSource = await _service.WritePersonaAsync(Replace(code, "反感客套和铺垫", null));

        Assert.True(withSection.IsError);
        Assert.Contains("persona_section", withSection.Text);
        Assert.True(noText.IsError);
        Assert.Contains("text", noText.Text);
        Assert.True(noSource.IsError);
        Assert.Contains("source", noSource.Text);
        Assert.Equal(before, File.ReadAllText(PersonaMd));
    }

    [Fact]
    public async Task Delete_refuses_text_source_and_confirm_no_secret_by_name()
    {
        CommitPersona(Fixture);
        var before = File.ReadAllText(PersonaMd);
        var code = ShortOf("反感客套和铺垫");

        var withText = await _service.WritePersonaAsync(Delete(code) with { Text = "反感客套和铺垫" });
        var withSource = await _service.WritePersonaAsync(Delete(code) with { Source = "别绕弯子" });
        var withConfirm = await _service.WritePersonaAsync(Delete(code) with { ConfirmNoSecret = true });

        Assert.True(withText.IsError);
        Assert.Contains("text", withText.Text);
        Assert.True(withSource.IsError);
        Assert.Contains("source", withSource.Text);
        Assert.True(withConfirm.IsError);
        Assert.Contains("confirm_no_secret", withConfirm.Text);
        Assert.Equal(before, File.ReadAllText(PersonaMd));
    }

    [Fact]
    public async Task A_secret_in_the_new_text_or_source_is_refused_until_the_agent_confirms_it_is_not_one()
    {
        CommitPersona(Fixture);
        var code = ShortOf("反感客套和铺垫");

        var inText = await _service.WritePersonaAsync(Replace(code, $"密钥：{OpenAiKey}", "放心用"));
        var inSource = await _service.WritePersonaAsync(Replace(code, "反感客套和铺垫", $"密钥是 {OpenAiKey}"));

        Assert.True(inText.IsError);
        Assert.True(inSource.IsError);
        Assert.Contains("confirm_no_secret", inText.Text);
        Assert.DoesNotContain("sk-", File.ReadAllText(PersonaMd), StringComparison.Ordinal);

        var confirmed = await _service.WritePersonaAsync(Replace(code, $"密钥：{OpenAiKey}", "放心用", confirmNoSecret: true));

        Assert.False(confirmed.IsError, confirmed.Text);
        Assert.Contains(OpenAiKey, File.ReadAllText(PersonaMd));
    }

    [Fact]
    public async Task A_persona_that_is_not_utf8_is_refused_by_replace_and_delete_and_left_alone()
    {
        var bytes = new byte[] { 0x23, 0x23, 0x20, 0xFF, 0xFE, 0x0A };
        File.WriteAllBytes(PersonaMd, bytes);

        var replaced = await _service.WritePersonaAsync(Replace("abc123", "反感客套和铺垫", "别绕弯子"));
        var deleted = await _service.WritePersonaAsync(Delete("abc123"));

        Assert.True(replaced.IsError);
        Assert.True(deleted.IsError);
        Assert.False(replaced.Written);
        Assert.False(deleted.Written);
        Assert.Equal(bytes, File.ReadAllBytes(PersonaMd));
    }

    [Fact]
    public async Task Uncommitted_user_edits_are_committed_with_a_replace_and_noted_in_the_subject()
    {
        CommitPersona(Fixture);
        WritePersona(Fixture.Replace("- 主力语言：C#\n", "- 主力语言：C# 与 Python\n"));
        var code = ShortOf("反感客套和铺垫");

        var result = await _service.WritePersonaAsync(Replace(code, "反感客套、铺垫和寒暄", "别绕弯子"));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(
            $"persona: replace {ShortOf("反感客套、铺垫和寒暄")} (with user edits)",
            Git("log", "-1", "--format=%s").Trim());
        Assert.Equal(string.Empty, Git("status", "--porcelain").Trim());
    }

    [Fact]
    public async Task A_replace_re_renders_the_bound_resident_block_in_the_background()
    {
        _home.Bind();
        CommitPersona(Fixture);

        var result = await _service.WritePersonaAsync(Replace(ShortOf("反感客套和铺垫"), "反感客套、铺垫和寒暄", "别绕弯子"));

        Assert.False(result.IsError, result.Text);
        await _service.SettledAsync();
        Assert.Contains("- 反感客套、铺垫和寒暄\n", File.ReadAllText(AgentsMd));
        Assert.DoesNotContain("反感客套和铺垫", File.ReadAllText(AgentsMd), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Delete_removes_the_entry_with_its_continuation_and_quote_and_returns_its_original_text()
    {
        CommitPersona(Fixture);
        var code = ShortOf("系统：Windows 11 x64\n另有 WSL 环境");

        var result = await _service.WritePersonaAsync(Delete(code));

        Assert.False(result.IsError, result.Text);
        Assert.True(result.Written);
        Assert.Contains("系统：Windows 11 x64", result.Text);
        Assert.Contains("另有 WSL 环境", result.Text);
        Assert.Equal(
            Lf(Fixture).Replace("- 系统：Windows 11 x64\n  另有 WSL 环境\n  <!-- 2026-10-01 quote「我用 Windows」 -->\n", ""),
            File.ReadAllText(PersonaMd));
        Assert.Equal($"persona: delete {code}", Git("log", "-1", "--format=%s").Trim());
        Assert.Equal("persona.md", Git("show", "--name-only", "--format=", "HEAD").Trim());
    }

    [Fact]
    public async Task Delete_needs_no_source_and_the_first_write_of_the_day_leaves_a_backup()
    {
        CommitPersona(Fixture);

        var result = await _service.WritePersonaAsync(Delete(ShortOf("反感客套和铺垫")));
        await _service.SettledAsync();

        Assert.False(result.IsError, result.Text);
        Assert.DoesNotContain("反感客套和铺垫", File.ReadAllText(PersonaMd));
        Assert.True(File.Exists(Path.Combine(_home.TacitHome, "backups", $"auto-{Today}.zip")));
    }

    [Fact]
    public async Task Deleting_the_last_entry_of_a_section_keeps_its_heading()
    {
        CommitPersona("## Red lines\n\n## Working rules\n\n## Preferences\n- 沟通：先要结论\n\n## Profile\n");

        var result = await _service.WritePersonaAsync(Delete(ShortOf("沟通：先要结论")));

        Assert.False(result.IsError, result.Text);
        Assert.Equal("## Red lines\n\n## Working rules\n\n## Preferences\n\n## Profile\n", File.ReadAllText(PersonaMd));
    }

    [Fact]
    public async Task Delete_takes_the_entries_of_a_section_over_its_limit_down_without_refusal()
    {
        var profile = Enumerable.Range(1, 11).Select(i => $"- 字段{i}：事实{i}\n").ToList();
        CommitPersona("## Red lines\n\n## Working rules\n\n## Preferences\n\n## Profile\n" + string.Concat(profile));

        var result = await _service.WritePersonaAsync(Delete(ShortOf("字段11：事实11")));

        Assert.False(result.IsError, result.Text);
        Assert.DoesNotContain("字段11", File.ReadAllText(PersonaMd));
        Assert.Contains("字段10：事实10", File.ReadAllText(PersonaMd));
    }

    [Fact]
    public async Task Delete_with_an_unknown_hash_is_refused_and_writes_nothing()
    {
        CommitPersona(Fixture);
        var before = File.ReadAllText(PersonaMd);
        var head = Git("rev-parse", "HEAD").Trim();

        var result = await _service.WritePersonaAsync(Delete("ffffff"));

        Assert.True(result.IsError);
        Assert.False(result.Written);
        Assert.DoesNotContain("Related:", result.Text);
        Assert.Contains("Call recall with kind=persona", result.Text);
        Assert.Equal(before, File.ReadAllText(PersonaMd));
        Assert.Equal(head, Git("rev-parse", "HEAD").Trim());
    }

    [Fact]
    public async Task Replace_finds_an_entry_in_a_domain_and_keeps_its_block_and_section()
    {
        CommitPersona("## Preferences\n- 沟通：先要结论\n\n## Domain: network-proxy\nTrigger: 改代理时\n### Working rules\n- 切换节点 → 先备份\n  <!-- 2026-10-25 quote「先备份」 -->\n");
        var code = ShortOf("切换节点 → 先备份");

        var result = await _service.WritePersonaAsync(Replace(code, "切换节点 → 先看延迟", "先看延迟"));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(
            "## Preferences\n- 沟通：先要结论\n\n## Domain: network-proxy\nTrigger: 改代理时\n### Working rules\n- 切换节点 → 先看延迟\n"
            + $"  <!-- {Today} quote「先看延迟」 -->\n",
            File.ReadAllText(PersonaMd));
        Assert.Contains("of domain network-proxy", result.Text);
    }

    [Fact]
    public async Task Delete_finds_an_entry_in_a_workstream_block_and_the_block_keeps_its_heading()
    {
        CommitPersona("## Preferences\n- 沟通：先要结论\n\n## Workstream: photo-sorter\n### Working rules\n- 发版 → 先问我\n  <!-- 2026-10-25 quote「发版前问我」 -->\n");
        var code = ShortOf("发版 → 先问我");

        var result = await _service.WritePersonaAsync(Delete(code));

        Assert.False(result.IsError, result.Text);
        Assert.Equal("## Preferences\n- 沟通：先要结论\n\n## Workstream: photo-sorter\n### Working rules\n", File.ReadAllText(PersonaMd));
        Assert.Contains("of workstream photo-sorter", result.Text);
    }

    [Fact]
    public async Task A_short_code_is_refused_without_listing_the_scope_blocks_and_points_to_recall()
    {
        CommitPersona("## Preferences\n- 沟通：先要结论\n\n## Domain: network-proxy\nTrigger: 改代理时\n### Working rules\n- 切换节点 → 先备份\n");
        var before = File.ReadAllText(PersonaMd);

        var result = await _service.WritePersonaAsync(Delete("ffffff"));

        Assert.True(result.IsError);
        Assert.DoesNotContain($"[{ShortOf("切换节点 → 先备份")}]", result.Text);
        Assert.DoesNotContain("Domain network-proxy", result.Text);
        Assert.Contains("Call recall with kind=persona to read the short codes", result.Text);
        Assert.Equal(before, File.ReadAllText(PersonaMd));
    }
}
