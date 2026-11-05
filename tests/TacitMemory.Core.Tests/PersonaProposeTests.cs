using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TacitMemory.Core.Tests;

public sealed partial class PersonaProposeTests : IDisposable
{
    private readonly TestHome _home = new();
    private readonly MemoryService _service;

    public PersonaProposeTests()
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

    private string CandidatesMd => Path.Combine(Memory, "candidates.md");

    private string AgentsMd => Path.Combine(_home.UserHome, ".omp", "agent", "AGENTS.md");

    private string Git(params string[] args) => TestHome.Git(Memory, args);

    // Secret-shaped samples are assembled at run time so no scanner mistakes this file for a leak.
    private static readonly string OpenAiKey = "sk-" + "proj-Abc123def456GHI789jkl012MNO345";

    private const string Today = "2026-11-02";

    private static WritePersonaRequest Propose(string? section, string? text, string? source = "先说结论", bool? confirmNoSecret = null) => new()
    {
        Op = "propose",
        Section = section,
        Text = text,
        Source = source,
        ConfirmNoSecret = confirmNoSecret,
    };

    private static string ShortOf(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\n").Trim())))[..6].ToLowerInvariant();

    [GeneratedRegex(@"\[[0-9a-f]{6}")]
    private static partial Regex ShortCodeInReply();

    private void WriteCandidates(string text) => File.WriteAllText(CandidatesMd, text.ReplaceLineEndings("\n"));

    /// <summary>Commits the current candidates.md as the baseline, then stages an unrelated file the commit must leave alone.</summary>
    private void CommitBaselineWithStagedNotes()
    {
        Git("add", "candidates.md");
        Git("-c", "user.name=tacit", "-c", "user.email=tacit@localhost", "commit", "-q", "-m", "test: candidates baseline");
        File.WriteAllText(Path.Combine(Memory, "notes.txt"), "staged by the user\n");
        Git("add", "notes.txt");
    }

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
    public async Task Proposing_a_preference_appends_a_candidate_with_its_section_and_first_occurrence_and_commits_only_candidates()
    {
        WriteCandidates(
            "## 候选\n\n### 每次提交前都运行全量测试\n归属：协作规则\n- 2026-10-27 原话「跑一下全部测试」\n\n"
            + "## 拒绝列表\n\n手写的注释\n");
        CommitBaselineWithStagedNotes();
        var persona = File.ReadAllText(PersonaMd);

        var result = await _service.WritePersonaAsync(Propose("preferences", "沟通：先要结论"));

        Assert.False(result.IsError, result.Text);
        Assert.True(result.Written);
        Assert.Equal(
            "## 候选\n\n### 每次提交前都运行全量测试\n归属：协作规则\n- 2026-10-27 原话「跑一下全部测试」\n\n"
            + $"### 沟通：先要结论\n归属：偏好\n- {Today} 原话「先说结论」\n\n"
            + "## 拒绝列表\n\n手写的注释\n",
            File.ReadAllText(CandidatesMd));
        Assert.Equal(persona, File.ReadAllText(PersonaMd));
        Assert.Contains("1/2", result.Text);
        Assert.DoesNotMatch(ShortCodeInReply(), result.Text);
        Assert.Equal($"candidates: propose {ShortOf("沟通：先要结论")}", Git("log", "-1", "--format=%s").Trim());
        Assert.Equal("candidates.md", Git("show", "--name-only", "--format=", "HEAD").Trim());
        Assert.Equal("A  notes.txt", Git("status", "--porcelain").TrimEnd());
    }

    [Fact]
    public async Task A_proposal_with_no_candidates_section_restores_the_heading_before_the_rejection_list()
    {
        WriteCandidates("## 拒绝列表\n\n### 旧的\n归属：偏好\n- 2026-10-20 拒绝 原话「算了」\n");

        var result = await _service.WritePersonaAsync(Propose("working_rules", "需求不清时 → 先追问", "先追问"));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(
            "## 候选\n\n### 需求不清时 → 先追问\n归属：协作规则\n"
            + $"- {Today} 原话「先追问」\n\n"
            + "## 拒绝列表\n\n### 旧的\n归属：偏好\n- 2026-10-20 拒绝 原话「算了」\n",
            File.ReadAllText(CandidatesMd));
    }

    [Fact]
    public async Task Without_a_rejection_list_a_restored_candidates_heading_goes_at_the_end_of_the_file()
    {
        WriteCandidates("## 其他\n- 手写\n");

        var result = await _service.WritePersonaAsync(Propose("preferences", "沟通：先要结论", "先要结论"));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(
            "## 其他\n- 手写\n\n## 候选\n\n"
            + $"### 沟通：先要结论\n归属：偏好\n- {Today} 原话「先要结论」\n",
            File.ReadAllText(CandidatesMd));
    }

    [Fact]
    public async Task Proposing_into_an_empty_candidates_section_puts_the_candidate_under_its_heading()
    {
        WriteCandidates("## 候选\n\n## 拒绝列表\n");

        var result = await _service.WritePersonaAsync(Propose("profile", "系统：Windows 11", "我用 Windows"));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(
            "## 候选\n\n### 系统：Windows 11\n归属：档案\n"
            + $"- {Today} 原话「我用 Windows」\n\n## 拒绝列表\n",
            File.ReadAllText(CandidatesMd));
    }

    [Fact]
    public async Task A_red_line_cannot_be_proposed_and_the_refusal_sends_the_agent_to_ask_the_user()
    {
        WriteCandidates("## 候选\n\n## 拒绝列表\n");
        var before = File.ReadAllText(CandidatesMd);

        var result = await _service.WritePersonaAsync(Propose("red_lines", "不要删库；改为先备份"));

        Assert.True(result.IsError);
        Assert.False(result.Written);
        Assert.StartsWith("Refused:", result.Text);
        Assert.Contains("user", result.Text);
        Assert.Contains("op=add", result.Text);
        Assert.Equal(before, File.ReadAllText(CandidatesMd));
    }

    [Fact]
    public async Task The_proposed_text_is_checked_against_its_section_and_its_length_like_add()
    {
        WriteCandidates("## 候选\n\n## 拒绝列表\n");
        var before = File.ReadAllText(CandidatesMd);

        var rule = await _service.WritePersonaAsync(Propose("working_rules", "需求不清时先追问"));
        var tooLong = await _service.WritePersonaAsync(Propose("preferences", "偏好：" + new string('x', 100)));
        var multiLine = await _service.WritePersonaAsync(Propose("preferences", "沟通：\n先要结论"));
        var multiLineSource = await _service.WritePersonaAsync(Propose("preferences", "沟通：先要结论", "第一行\n第二行"));
        var commentEnd = await _service.WritePersonaAsync(Propose("preferences", "沟通：先要结论", "原话 --> 不对"));

        Assert.True(rule.IsError);
        Assert.Contains("→", rule.Text);
        Assert.True(tooLong.IsError);
        Assert.Contains("100", tooLong.Text);
        Assert.True(multiLine.IsError);
        Assert.True(multiLineSource.IsError);
        Assert.True(commentEnd.IsError);
        Assert.Contains("-->", commentEnd.Text);
        Assert.All(new[] { rule, tooLong, multiLine, multiLineSource, commentEnd }, result => Assert.False(result.Written));
        Assert.Equal(before, File.ReadAllText(CandidatesMd));
    }

    [Fact]
    public async Task Preferences_and_profile_are_not_checked_for_sentence_form_when_proposed()
    {
        Assert.False((await _service.WritePersonaAsync(Propose("preferences", "沟通：先要结论"))).IsError);
        Assert.False((await _service.WritePersonaAsync(Propose("profile", "系统 Windows 11"))).IsError);
    }

    [Fact]
    public async Task A_proposal_missing_a_required_parameter_names_it_and_an_unknown_section_lists_the_sections()
    {
        var missing = await _service.WritePersonaAsync(Propose("preferences", "沟通：先要结论", source: null));
        var unknown = await _service.WritePersonaAsync(Propose("Red Lines", "不要删库；改为先备份"));

        Assert.True(missing.IsError);
        Assert.Contains("source", missing.Text);
        Assert.Contains("Next step:", missing.Text);
        Assert.True(unknown.IsError);
        Assert.Contains("red_lines", unknown.Text);
        Assert.Contains("profile", unknown.Text);
        Assert.False(File.Exists(CandidatesMd) && File.ReadAllText(CandidatesMd).Contains("沟通"));
    }

    [Fact]
    public async Task A_secret_in_the_text_or_source_of_a_proposal_is_refused_until_the_agent_confirms_it_is_not_one()
    {
        WriteCandidates("## 候选\n\n## 拒绝列表\n");

        var inText = await _service.WritePersonaAsync(Propose("profile", $"密钥：{OpenAiKey}", "放心用"));
        var inSource = await _service.WritePersonaAsync(Propose("profile", "系统：Windows 11", $"密钥是 {OpenAiKey}"));

        Assert.True(inText.IsError);
        Assert.True(inSource.IsError);
        Assert.Contains("confirm_no_secret", inText.Text);
        Assert.DoesNotContain("sk-", File.ReadAllText(CandidatesMd), StringComparison.Ordinal);

        var confirmed = await _service.WritePersonaAsync(Propose("profile", $"密钥：{OpenAiKey}", "放心用", confirmNoSecret: true));

        Assert.False(confirmed.IsError, confirmed.Text);
        Assert.Contains(OpenAiKey, File.ReadAllText(CandidatesMd));
    }

    [Fact]
    public async Task A_candidates_file_that_is_not_utf8_is_refused_and_left_alone()
    {
        var bytes = new byte[] { 0x23, 0x23, 0x20, 0xFF, 0xFE, 0x0A };
        File.WriteAllBytes(CandidatesMd, bytes);

        var result = await _service.WritePersonaAsync(Propose("preferences", "沟通：先要结论"));

        Assert.True(result.IsError);
        Assert.False(result.Written);
        Assert.Equal(bytes, File.ReadAllBytes(CandidatesMd));
    }

    [Fact]
    public async Task Uncommitted_user_edits_to_candidates_are_committed_with_the_proposal_and_noted_in_the_subject()
    {
        WriteCandidates("## 候选\n\n### 旧的\n归属：偏好\n- 2026-10-27 原话「旧」\n\n## 拒绝列表\n");
        CommitBaselineWithStagedNotes();
        WriteCandidates("## 候选\n\n### 用户手写的一条\n归属：偏好\n- 2026-10-30 原话「手写」\n\n## 拒绝列表\n");

        var result = await _service.WritePersonaAsync(Propose("preferences", "沟通：先要结论", "先要结论"));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(
            $"candidates: propose {ShortOf("沟通：先要结论")} (with user edits)",
            Git("log", "-1", "--format=%s").Trim());
        Assert.Equal("A  notes.txt", Git("status", "--porcelain").TrimEnd());
    }

    [Fact]
    public async Task The_first_proposal_of_the_day_leaves_a_backup()
    {
        WriteCandidates("## 候选\n\n## 拒绝列表\n");

        var result = await _service.WritePersonaAsync(Propose("profile", "系统：Windows 11"));
        await _service.BackupCompletion;

        Assert.False(result.IsError, result.Text);
        Assert.True(File.Exists(Path.Combine(_home.TacitHome, "backups", $"auto-{Today}.zip")));
    }

    [Fact]
    public async Task A_proposal_does_not_re_render_the_resident_block_while_an_add_does()
    {
        _home.Tacit().Connect(ClientKind.Omp);
        File.WriteAllText(AgentsMd, File.ReadAllText(AgentsMd).Replace("</user_memory>", "STALE\n</user_memory>", StringComparison.Ordinal));

        var proposed = await _service.WritePersonaAsync(Propose("preferences", "沟通：先要结论", "先要结论"));
        await Task.Delay(1000);

        Assert.False(proposed.IsError, proposed.Text);
        Assert.Contains("STALE", File.ReadAllText(AgentsMd), StringComparison.Ordinal);

        var added = await _service.WritePersonaAsync(new WritePersonaRequest
        {
            Op = "add",
            Section = "profile",
            Text = "系统：Windows 11",
            Source = "我用 Windows",
        });

        Assert.False(added.IsError, added.Text);
        Assert.True(await EventuallyAsync(() => !File.ReadAllText(AgentsMd).Contains("STALE", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task The_minimum_days_setting_is_read_for_the_reply_and_reaching_it_says_it_can_be_confirmed()
    {
        using var home = new TestHome();
        home.InitReady("candidates:\n  minDays: 1\n");
        using var service = home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

        var result = await service.WritePersonaAsync(Propose("preferences", "沟通：先要结论"));

        Assert.False(result.IsError, result.Text);
        Assert.Contains("1/1", result.Text);
        Assert.Contains("checkup", result.Text);
    }
}
