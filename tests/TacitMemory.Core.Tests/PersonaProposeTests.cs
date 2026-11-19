using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using static TacitMemory.Core.Tests.ShortCodes;

namespace TacitMemory.Core.Tests;

public sealed partial class PersonaProposeTests : IDisposable
{
    private readonly TestHome _home = TestHome.WithManualSources();
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

    private static WritePersonaRequest Propose(string? section, string? text, string? source = "先说结论", bool? confirmNoSecret = null, string? scope = null) => new()
    {
        Op = "propose",
        Section = section,
        Text = text,
        Source = source,
        ConfirmNoSecret = confirmNoSecret,
        Scope = scope,
    };

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

    [Fact]
    public async Task Proposing_a_preference_appends_a_candidate_with_its_section_and_first_occurrence_and_commits_only_candidates()
    {
        WriteCandidates(
            "## Candidates\n\n### 每次提交前都运行全量测试\nBelongs to: Working rules\n- 2026-10-27 quote「跑一下全部测试」\n\n"
            + "## Rejection list\n\n手写的注释\n");
        CommitBaselineWithStagedNotes();
        var persona = File.ReadAllText(PersonaMd);

        var result = await _service.Tools.WritePersonaAsync(Propose("preferences", "沟通：先要结论"));

        Assert.False(result.IsError, result.Text);
        Assert.True(result.Written);
        Assert.Equal(
            "## Candidates\n\n### 每次提交前都运行全量测试\nBelongs to: Working rules\n- 2026-10-27 quote「跑一下全部测试」\n\n"
            + $"### 沟通：先要结论\nBelongs to: Preferences\n- {Today} quote「先说结论」\n\n"
            + "## Rejection list\n\n手写的注释\n",
            File.ReadAllText(CandidatesMd));
        Assert.Equal(persona, File.ReadAllText(PersonaMd));
        Assert.Contains("new candidate", result.Text);
        Assert.Contains("scope: global", result.Text);
        Assert.DoesNotContain("days", result.Text);
        Assert.DoesNotMatch(ShortCodeInReply(), result.Text);
        Assert.Equal($"candidates: propose {ShortOf("沟通：先要结论")}", Git("log", "-1", "--format=%s").Trim());
        Assert.Equal("candidates.md", Git("show", "--name-only", "--format=", "HEAD").Trim());
        Assert.Equal("A  notes.txt", Git("status", "--porcelain").TrimEnd());
    }

    [Fact]
    public async Task A_proposal_with_no_candidates_section_restores_the_heading_before_the_rejection_list()
    {
        WriteCandidates("## Rejection list\n\n### 旧的\nBelongs to: Preferences\n- 2026-10-20 rejected quote「算了」\n");

        var result = await _service.Tools.WritePersonaAsync(Propose("working_rules", "需求不清时 → 先追问", "先追问"));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(
            "## Candidates\n\n### 需求不清时 → 先追问\nBelongs to: Working rules\n"
            + $"- {Today} quote「先追问」\n\n"
            + "## Rejection list\n\n### 旧的\nBelongs to: Preferences\n- 2026-10-20 rejected quote「算了」\n",
            File.ReadAllText(CandidatesMd));
    }

    [Fact]
    public async Task Without_a_rejection_list_a_restored_candidates_heading_goes_at_the_end_of_the_file()
    {
        WriteCandidates("## 其他\n- 手写\n");

        var result = await _service.Tools.WritePersonaAsync(Propose("preferences", "沟通：先要结论", "先要结论"));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(
            "## 其他\n- 手写\n\n## Candidates\n\n"
            + $"### 沟通：先要结论\nBelongs to: Preferences\n- {Today} quote「先要结论」\n",
            File.ReadAllText(CandidatesMd));
    }

    [Fact]
    public async Task Proposing_into_an_empty_candidates_section_puts_the_candidate_under_its_heading()
    {
        WriteCandidates("## Candidates\n\n## Rejection list\n");

        var result = await _service.Tools.WritePersonaAsync(Propose("profile", "系统：Windows 11", "我用 Windows"));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(
            "## Candidates\n\n### 系统：Windows 11\nBelongs to: Profile\n"
            + $"- {Today} quote「我用 Windows」\n\n## Rejection list\n",
            File.ReadAllText(CandidatesMd));
    }

    [Fact]
    public async Task A_red_line_cannot_be_proposed_and_the_refusal_sends_the_agent_to_ask_the_user()
    {
        WriteCandidates("## Candidates\n\n## Rejection list\n");
        var before = File.ReadAllText(CandidatesMd);

        var result = await _service.Tools.WritePersonaAsync(Propose("red_lines", "不要删库；改为先备份"));

        Assert.True(result.IsError);
        Assert.False(result.Written);
        Assert.StartsWith("Refused:", result.Text);
        Assert.Contains("user", result.Text);
        Assert.Contains("op=add", result.Text);
        Assert.Equal(before, File.ReadAllText(CandidatesMd));
    }

    [Fact]
    public async Task The_proposed_text_and_source_are_checked_like_add()
    {
        WriteCandidates("## Candidates\n\n## Rejection list\n");
        var before = File.ReadAllText(CandidatesMd);

        var multiLine = await _service.Tools.WritePersonaAsync(Propose("preferences", "沟通：\n先要结论"));
        var multiLineSource = await _service.Tools.WritePersonaAsync(Propose("preferences", "沟通：先要结论", "第一行\n第二行"));
        var commentEnd = await _service.Tools.WritePersonaAsync(Propose("preferences", "沟通：先要结论", "原话 --> 不对"));

        Assert.True(multiLine.IsError);
        Assert.True(multiLineSource.IsError);
        Assert.True(commentEnd.IsError);
        Assert.Contains("-->", commentEnd.Text);
        Assert.All(new[] { multiLine, multiLineSource, commentEnd }, result => Assert.False(result.Written));
        Assert.Equal(before, File.ReadAllText(CandidatesMd));
    }

    [Fact]
    public async Task A_proposal_of_any_length_is_recorded_as_a_candidate()
    {
        WriteCandidates("## Candidates\n\n## Rejection list\n");

        var result = await _service.Tools.WritePersonaAsync(Propose("preferences", "偏好：" + new string('x', 2000)));

        Assert.False(result.IsError, result.Text);
        Assert.True(result.Written);
        Assert.Contains(new string('x', 2000), File.ReadAllText(CandidatesMd));
    }

    [Fact]
    public async Task Preferences_and_profile_are_not_checked_for_sentence_form_when_proposed()
    {
        Assert.False((await _service.Tools.WritePersonaAsync(Propose("preferences", "沟通：先要结论"))).IsError);
        Assert.False((await _service.Tools.WritePersonaAsync(Propose("profile", "系统 Windows 11"))).IsError);
    }

    [Fact]
    public async Task A_proposal_missing_a_required_parameter_names_it_and_an_unknown_section_lists_the_sections()
    {
        var missing = await _service.Tools.WritePersonaAsync(Propose("preferences", "沟通：先要结论", source: null));
        var unknown = await _service.Tools.WritePersonaAsync(Propose("Red Lines", "不要删库；改为先备份"));

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
        WriteCandidates("## Candidates\n\n## Rejection list\n");

        var inText = await _service.Tools.WritePersonaAsync(Propose("profile", $"密钥：{OpenAiKey}", "放心用"));
        var inSource = await _service.Tools.WritePersonaAsync(Propose("profile", "系统：Windows 11", $"密钥是 {OpenAiKey}"));

        Assert.True(inText.IsError);
        Assert.True(inSource.IsError);
        Assert.Contains("confirm_no_secret", inText.Text);
        Assert.DoesNotContain("sk-", File.ReadAllText(CandidatesMd), StringComparison.Ordinal);

        var confirmed = await _service.Tools.WritePersonaAsync(Propose("profile", $"密钥：{OpenAiKey}", "放心用", confirmNoSecret: true));

        Assert.False(confirmed.IsError, confirmed.Text);
        Assert.Contains(OpenAiKey, File.ReadAllText(CandidatesMd));
    }

    [Fact]
    public async Task A_candidates_file_that_is_not_utf8_is_refused_and_left_alone()
    {
        var bytes = new byte[] { 0x23, 0x23, 0x20, 0xFF, 0xFE, 0x0A };
        File.WriteAllBytes(CandidatesMd, bytes);

        var result = await _service.Tools.WritePersonaAsync(Propose("preferences", "沟通：先要结论"));

        Assert.True(result.IsError);
        Assert.False(result.Written);
        Assert.Equal(bytes, File.ReadAllBytes(CandidatesMd));
    }

    [Fact]
    public async Task Uncommitted_user_edits_to_candidates_are_committed_with_the_proposal_and_noted_in_the_subject()
    {
        WriteCandidates("## Candidates\n\n### 旧的\nBelongs to: Preferences\n- 2026-10-27 quote「旧」\n\n## Rejection list\n");
        CommitBaselineWithStagedNotes();
        WriteCandidates("## Candidates\n\n### 用户手写的一条\nBelongs to: Preferences\n- 2026-10-30 quote「手写」\n\n## Rejection list\n");

        var result = await _service.Tools.WritePersonaAsync(Propose("preferences", "沟通：先要结论", "先要结论"));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(
            $"candidates: propose {ShortOf("沟通：先要结论")} (with user edits)",
            Git("log", "-1", "--format=%s").Trim());
        Assert.Equal("A  notes.txt", Git("status", "--porcelain").TrimEnd());
    }

    [Fact]
    public async Task The_first_proposal_of_the_day_leaves_a_backup()
    {
        WriteCandidates("## Candidates\n\n## Rejection list\n");

        var result = await _service.Tools.WritePersonaAsync(Propose("profile", "系统：Windows 11"));
        await _service.SettledAsync();

        Assert.False(result.IsError, result.Text);
        Assert.True(File.Exists(Path.Combine(_home.TacitHome, "backups", $"auto-{Today}.zip")));
    }

    [Fact]
    public async Task A_proposal_does_not_re_render_the_resident_block_while_an_add_does()
    {
        _home.Bind();
        File.WriteAllText(AgentsMd, File.ReadAllText(AgentsMd).Replace("</user_memory>", "STALE\n</user_memory>", StringComparison.Ordinal));

        var proposed = await _service.Tools.WritePersonaAsync(Propose("preferences", "沟通：先要结论", "先要结论"));
        await _service.SettledAsync();

        Assert.False(proposed.IsError, proposed.Text);
        Assert.Contains("STALE", File.ReadAllText(AgentsMd), StringComparison.Ordinal);

        var added = await _service.Tools.WritePersonaAsync(new WritePersonaRequest
        {
            Op = "add",
            Section = "profile",
            Text = "系统：Windows 11",
            Source = "我用 Windows",
        });

        Assert.False(added.IsError, added.Text);
        await _service.SettledAsync();
        Assert.DoesNotContain("STALE", File.ReadAllText(AgentsMd));
    }

    [Theory]
    [InlineData("domain:db-migration")]
    [InlineData("workstream:my-app")]
    public async Task A_scoped_proposal_writes_its_scope_line_after_the_section_line(string scope)
    {
        WriteCandidates("## Candidates\n\n## Rejection list\n");

        var result = await _service.Tools.WritePersonaAsync(Propose("preferences", "备份：先写快照再执行", "先写快照再执行", scope: scope));

        Assert.False(result.IsError, result.Text);
        Assert.Contains($"scope: {scope}", result.Text);
        Assert.Equal(
            "## Candidates\n\n### 备份：先写快照再执行\nBelongs to: Preferences\n"
            + $"Scope: {scope}\n- {Today} quote「先写快照再执行」\n\n## Rejection list\n",
            File.ReadAllText(CandidatesMd));
    }

    [Fact]
    public async Task A_proposal_with_a_scope_that_is_not_one_is_refused_and_the_next_step_names_op_propose()
    {
        WriteCandidates("## Candidates\n\n## Rejection list\n");
        var before = File.ReadAllText(CandidatesMd);

        var result = await _service.Tools.WritePersonaAsync(Propose("preferences", "备份：先写快照再执行", scope: "domain:Bad Name"));

        Assert.True(result.IsError);
        Assert.False(result.Written);
        Assert.Contains("domain:<label>", result.Text);
        Assert.Contains("op=propose", result.Text);
        Assert.Equal(before, File.ReadAllText(CandidatesMd));
    }

    [Fact]
    public async Task A_proposal_takes_no_trigger_because_the_candidate_does_not_create_the_domain()
    {
        WriteCandidates("## Candidates\n\n## Rejection list\n");

        var request = Propose("preferences", "备份：先写快照再执行", scope: "domain:db-migration") with { Trigger = "执行迁移时" };
        var result = await _service.Tools.WritePersonaAsync(request);

        Assert.True(result.IsError);
        Assert.Contains("Parameter trigger does not apply to op propose.", result.Text);
        Assert.False(result.Written);
    }

    [Fact]
    public async Task In_an_english_memory_the_scope_line_is_written_in_english()
    {
        using var home = new TestHome();
        home.InitReady(language: MemoryLanguage.En);
        using var service = home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);
        var candidates = Path.Combine(home.DefaultMemory, "candidates.md");
        File.WriteAllText(candidates, "## Candidates\n\n## Rejection list\n");

        var result = await service.Tools.WritePersonaAsync(Propose("preferences", "Backup: snapshot before migration", "snapshot first", scope: "domain:db-migration"));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(
            "## Candidates\n\n### Backup: snapshot before migration\nBelongs to: Preferences\nScope: domain:db-migration\n"
            + $"- {Today} quote「snapshot first」\n\n## Rejection list\n",
            File.ReadAllText(candidates));
    }
}
