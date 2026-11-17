using System.Security.Cryptography;
using System.Text;

using static TacitMemory.Core.Tests.ShortCodes;

namespace TacitMemory.Core.Tests;

public sealed class SamePreferenceTests : IDisposable
{
    private readonly TestHome _home = new();
    private readonly ModelDouble _models = new();
    private MemoryService? _service;

    public void Dispose()
    {
        _service?.Dispose();
        _models.Dispose();
        _home.Dispose();
    }

    private string Memory => _home.DefaultMemory;

    private string PersonaMd => Path.Combine(Memory, "persona.md");

    private string CandidatesMd => Path.Combine(Memory, "candidates.md");

    private MemoryService Service => _service ??= _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

    private string Git(params string[] args) => TestHome.Git(Memory, args);

    private const string Today = "2026-11-02";

    private void Configure(string extra = "", MemoryLanguage language = MemoryLanguage.Zh) => _home.InitReady(extra, _models, language);

    /// <summary>Writes both judgment files and commits them, so the next write's commit holds only what it changed.</summary>
    private void Baseline(string candidates, string? persona = null)
    {
        File.WriteAllText(CandidatesMd, candidates.ReplaceLineEndings("\n"));
        if (persona is not null)
            File.WriteAllText(PersonaMd, persona.ReplaceLineEndings("\n"));
        Git("add", "candidates.md", "persona.md");
        Git("-c", "user.name=tacit", "-c", "user.email=tacit@localhost", "commit", "-q", "--allow-empty", "-m", "test: baseline");
    }

    private static WritePersonaRequest Propose(string section, string text, string source = "先要结论") => new()
    {
        Op = "propose",
        Section = section,
        Text = text,
        Source = source,
    };

    /// <summary>Makes the decision model pick the option whose text contains <paramref name="contains"/> with <paramref name="probability"/>.</summary>
    private void PickOption(string contains, double probability) =>
        _models.Choose = (_, options) => options.ToDictionary(
            option => option.Key,
            option => option.Value.Contains(contains, StringComparison.Ordinal) ? probability
                : option.Key == options.Keys.Last() ? 1 - probability
                : 0.0);

    private const string WorkingRuleCandidate =
        "## Candidates\n\n### 每次提交前都运行全量测试\nBelongs to: Working rules\n- 2026-10-27 quote「跑一下全部测试」\n\n## Rejection list\n";

    [Fact]
    public async Task A_candidate_the_decision_model_is_sure_states_the_same_preference_is_merged_and_committed()
    {
        Configure();
        Baseline(WorkingRuleCandidate);
        PickOption("全量测试", 0.95);

        var result = await Service.WritePersonaAsync(Propose("working_rules", "提交前 → 先运行测试", "提交前跑全量测试"));

        Assert.False(result.IsError, result.Text);
        Assert.True(result.Written);
        Assert.Contains("scope: global", result.Text);
        Assert.DoesNotContain("consolidation", result.Text);
        Assert.DoesNotContain("days", result.Text);
        Assert.Equal(
            "## Candidates\n\n### 每次提交前都运行全量测试\nBelongs to: Working rules\n"
            + "- 2026-10-27 quote「跑一下全部测试」\n- 2026-11-02 quote「提交前跑全量测试」\n\n## Rejection list\n",
            File.ReadAllText(CandidatesMd));
        Assert.Equal($"candidates: merge {ShortOf("每次提交前都运行全量测试")}", Git("log", "-1", "--format=%s").Trim());
        Assert.Equal("candidates.md", Git("show", "--name-only", "--format=", "HEAD").Trim());
    }

    [Fact]
    public async Task A_candidate_already_recorded_today_is_neither_written_nor_committed()
    {
        Configure();
        Baseline("## Candidates\n\n### 每次提交前都运行全量测试\nBelongs to: Working rules\n- 2026-10-27 quote「跑一下全部测试」\n- 2026-11-02 quote「先跑全量」\n\n## Rejection list\n");
        var before = File.ReadAllText(CandidatesMd);
        var head = Git("rev-parse", "HEAD");
        PickOption("全量测试", 0.95);

        var result = await Service.WritePersonaAsync(Propose("working_rules", "提交前 → 先运行测试", "提交前跑全量测试"));

        Assert.False(result.IsError, result.Text);
        Assert.False(result.Written);
        Assert.Contains("Already recorded today", result.Text);
        Assert.DoesNotContain("consolidation", result.Text);
        Assert.Equal(before, File.ReadAllText(CandidatesMd));
        Assert.Equal(head, Git("rev-parse", "HEAD"));
    }

    [Fact]
    public async Task A_proposal_matching_a_rejected_candidate_is_not_recorded_and_says_when_and_in_what_words_it_was_refused()
    {
        Configure();
        Baseline("## Candidates\n\n## Rejection list\n\n### 每次提交前都运行全量测试\nBelongs to: Working rules\n- 2026-10-20 rejected quote「算了，别跑了」\n");
        var before = File.ReadAllText(CandidatesMd);
        var head = Git("rev-parse", "HEAD");
        PickOption("全量测试", 0.95);

        var result = await Service.WritePersonaAsync(Propose("working_rules", "提交前 → 先运行测试", "提交前跑全量测试"));

        Assert.False(result.IsError, result.Text);
        Assert.False(result.Written);
        Assert.Contains("每次提交前都运行全量测试", result.Text);
        Assert.Contains("2026-10-20", result.Text);
        Assert.Contains("算了，别跑了", result.Text);
        Assert.Contains("Next step", result.Text);
        Assert.Equal(before, File.ReadAllText(CandidatesMd));
        Assert.Equal(head, Git("rev-parse", "HEAD"));
    }

    [Fact]
    public async Task A_proposal_matching_a_persona_entry_is_not_recorded_and_the_reply_gives_its_short_code()
    {
        Configure();
        Baseline("## Candidates\n\n## Rejection list\n", "## Red lines\n\n## Working rules\n\n## Preferences\n- 沟通：先要结论\n  <!-- 2026-10-20 quote「先要结论」 -->\n\n## Profile\n");
        var before = File.ReadAllText(CandidatesMd);
        PickOption("先要结论", 0.95);

        var result = await Service.WritePersonaAsync(Propose("preferences", "沟通：先要结论"));

        Assert.False(result.IsError, result.Text);
        Assert.False(result.Written);
        Assert.Contains($"[{ShortOf("沟通：先要结论")}]", result.Text);
        Assert.Equal(before, File.ReadAllText(CandidatesMd));
    }

    [Fact]
    public async Task A_manual_edit_to_persona_md_is_seen_by_the_judgment_at_once()
    {
        Configure();
        Baseline("## Candidates\n\n## Rejection list\n");
        _ = Service;
        File.WriteAllText(PersonaMd, "## Preferences\n- 沟通：先要结论\n  <!-- 2026-10-20 quote「先要结论」 -->\n");
        PickOption("先要结论", 0.95);

        var result = await Service.WritePersonaAsync(Propose("preferences", "沟通：先要结论"));

        Assert.False(result.IsError, result.Text);
        Assert.False(result.Written);
        Assert.Contains($"[{ShortOf("沟通：先要结论")}]", result.Text);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(0.2)]
    public async Task A_proposal_the_decision_model_is_not_sure_about_becomes_a_new_candidate(double probability)
    {
        Configure();
        Baseline(WorkingRuleCandidate);
        PickOption("全量测试", probability);

        var result = await Service.WritePersonaAsync(Propose("preferences", "沟通：先要结论"));

        Assert.False(result.IsError, result.Text);
        Assert.True(result.Written);
        Assert.Contains("### 沟通：先要结论", File.ReadAllText(CandidatesMd));
        Assert.Contains("每次提交前都运行全量测试", File.ReadAllText(CandidatesMd));
    }

    [Fact]
    public async Task A_proposal_with_no_match_becomes_a_new_candidate_when_the_decision_model_says_none()
    {
        Configure();
        Baseline(WorkingRuleCandidate);
        PickOption("不存在的条目", 0.95);

        var result = await Service.WritePersonaAsync(Propose("preferences", "沟通：先要结论"));

        Assert.False(result.IsError, result.Text);
        Assert.True(result.Written);
        Assert.Contains("### 沟通：先要结论", File.ReadAllText(CandidatesMd));
    }

    [Fact]
    public async Task Without_the_decision_model_the_proposal_is_recorded_as_a_new_candidate_with_a_note()
    {
        Configure();
        Baseline(WorkingRuleCandidate);
        _models.DecisionDown = true;

        var result = await Service.WritePersonaAsync(Propose("preferences", "沟通：先要结论"));

        Assert.False(result.IsError, result.Text);
        Assert.True(result.Written);
        Assert.Contains("### 沟通：先要结论", File.ReadAllText(CandidatesMd));
        Assert.Contains("unavailable", result.Text);
    }

    [Fact]
    public async Task Without_the_embedding_model_the_proposal_is_recorded_as_a_new_candidate_with_a_note_and_no_decision_is_asked()
    {
        Configure();
        Baseline(WorkingRuleCandidate);
        _models.EmbeddingDown = true;

        var result = await Service.WritePersonaAsync(Propose("preferences", "沟通：先要结论"));

        Assert.False(result.IsError, result.Text);
        Assert.True(result.Written);
        Assert.Contains("### 沟通：先要结论", File.ReadAllText(CandidatesMd));
        Assert.Contains("unavailable", result.Text);
        Assert.Equal(0, _models.DecisionCalls);
    }

    [Fact]
    public async Task With_nothing_in_candidates_rejection_list_or_persona_the_decision_model_is_not_asked()
    {
        Configure();
        Baseline("## Candidates\n\n## Rejection list\n", "## Red lines\n\n## Working rules\n\n## Preferences\n\n## Profile\n");

        var result = await Service.WritePersonaAsync(Propose("preferences", "沟通：先要结论"));

        Assert.False(result.IsError, result.Text);
        Assert.True(result.Written);
        Assert.Contains("### 沟通：先要结论", File.ReadAllText(CandidatesMd));
        Assert.Equal(0, _models.DecisionCalls);
    }

    [Fact]
    public async Task The_question_offers_at_most_nine_nearest_entries_and_none()
    {
        Configure();
        var entries = string.Concat(Enumerable.Range(0, 12).Select(i => $"### 偏好 {i}\nBelongs to: Preferences\n- 2026-10-2{i % 9} quote「第{i}条」\n\n"));
        Baseline($"## Candidates\n\n{entries}## 拒绝列表\n");
        IReadOnlyDictionary<string, string>? offered = null;
        _models.Choose = (_, options) =>
        {
            offered = options;
            return options.Keys.ToDictionary(key => key, key => key == options.Keys.Last() ? 1.0 : 0.0);
        };

        var result = await Service.WritePersonaAsync(Propose("preferences", "沟通：先要结论"));

        Assert.False(result.IsError, result.Text);
        Assert.NotNull(offered);
        Assert.Equal(10, offered!.Count);
    }

    [Fact]
    public async Task A_configured_threshold_replaces_the_built_in_one()
    {
        Configure("decision:\n  samePreference: { certain: 0.99, unlikely: 0.1 }\n");
        Baseline(WorkingRuleCandidate);
        PickOption("全量测试", 0.95);

        var result = await Service.WritePersonaAsync(Propose("working_rules", "提交前 → 先运行测试", "提交前跑全量测试"));

        Assert.False(result.IsError, result.Text);
        Assert.True(result.Written);
        Assert.Contains("### 提交前 → 先运行测试", File.ReadAllText(CandidatesMd));
    }

    [Fact]
    public async Task A_merge_keeps_every_other_byte_of_the_candidates_file()
    {
        Configure();
        var handwritten = "# 手写标题\n<!-- 我的注释 -->\n\n## Candidates\n\n### 每次提交前都运行全量测试\nBelongs to: Working rules\n- 2026-10-27 quote「跑一下全部测试」\n\n手写的段落\n\n## Rejection list\n\n尾部注释\n";
        Baseline(handwritten);
        PickOption("全量测试", 0.95);

        var result = await Service.WritePersonaAsync(Propose("working_rules", "提交前 → 先运行测试", "提交前跑全量测试"));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(
            handwritten.Replace("- 2026-10-27 quote「跑一下全部测试」\n", "- 2026-10-27 quote「跑一下全部测试」\n- 2026-11-02 quote「提交前跑全量测试」\n", StringComparison.Ordinal),
            File.ReadAllText(CandidatesMd));
    }

    [Fact]
    public async Task The_question_does_not_name_a_language_for_the_preferences_in_an_english_memory()
    {
        Configure(language: MemoryLanguage.En);
        Baseline(WorkingRuleCandidate);

        await Service.WritePersonaAsync(Propose("working_rules", "提交前 → 先运行测试", "提交前跑全量测试"));

        var question = _models.LastDecision!.Value.Body["questions"]!.AsObject().Single().Value!;
        Assert.DoesNotContain("Chinese", question["instructions"]!.GetValue<string>());
    }

    private const string DomainCandidate =
        "## Candidates\n\n### 每次提交前都运行全量测试\nBelongs to: Working rules\nScope: domain:network-proxy\n- 2026-10-27 quote「跑一下全部测试」\n\n## Rejection list\n";

    private const string DomainPersona =
        "## Red lines\n\n## Working rules\n\n## Preferences\n\n## Profile\n\n"
        + "## Domain: network-proxy\nTrigger: 改代理、分流或测速时\n### Working rules\n- 提交前 → 先运行测试\n  <!-- 2026-10-20 quote「先跑测试」 -->\n";

    /// <summary>The question's instructions as the check states them; the wording is kept as it was.</summary>
    private const string ExpectedInstructions =
        "The state is a new preference or working rule proposed from a conversation, with its section in brackets. "
        + "Each option is an existing entry, with its source and section in brackets: candidate (a pending preference not yet confirmed), "
        + "rejected (a preference the user declined), or persona (a confirmed entry of the user's persona file). "
        + "Entries are kept as written, in whatever language they are in. "
        + "Choose the option if it states the same preference or rule as the new one: the same thing asked for, even if phrased differently. "
        + "Entries on the same topic that ask for different things are different, and so are opposite preferences. "
        + "If no option states the same preference or rule, choose the none option. Answer with the option key only.";

    private const string NoneOption = "None of the options states the same preference or rule as the new one.";

    [Fact]
    public async Task A_proposal_matching_a_candidate_of_another_scope_merges_into_it_and_keeps_that_scope()
    {
        Configure();
        Baseline(DomainCandidate);
        PickOption("全量测试", 0.95);

        var result = await Service.WritePersonaAsync(Propose("working_rules", "提交前 → 先运行测试", "提交前跑全量测试"));

        Assert.False(result.IsError, result.Text);
        Assert.True(result.Written);
        Assert.Contains("domain:network-proxy", result.Text);
        Assert.Equal(
            "## Candidates\n\n### 每次提交前都运行全量测试\nBelongs to: Working rules\nScope: domain:network-proxy\n"
            + "- 2026-10-27 quote「跑一下全部测试」\n- 2026-11-02 quote「提交前跑全量测试」\n\n## Rejection list\n",
            File.ReadAllText(CandidatesMd));
    }

    [Fact]
    public async Task The_english_scope_line_of_a_candidate_is_read_when_a_proposal_merges_into_it()
    {
        using var home = new TestHome();
        using var models = new ModelDouble();
        home.InitReady(models: models, language: MemoryLanguage.En);
        var candidates = Path.Combine(home.DefaultMemory, "candidates.md");
        File.WriteAllText(
            candidates,
            "## Candidates\n\n### Run the full tests before committing\nBelongs to: Working rules\nScope: domain:network-proxy\n"
            + "- 2026-10-27 quote「run all tests」\n\n## Rejection list\n");
        models.Choose = (_, options) => options.ToDictionary(
            option => option.Key,
            option => option.Value.Contains("full tests", StringComparison.Ordinal) ? 0.95 : option.Key == options.Keys.Last() ? 0.05 : 0.0);
        using var service = home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

        var result = await service.WritePersonaAsync(Propose("working_rules", "Before committing → run the tests", "run all of them"));

        Assert.False(result.IsError, result.Text);
        Assert.Contains("domain:network-proxy", result.Text);
        Assert.Contains("- 2026-10-27 quote「run all tests」\n- ", File.ReadAllText(candidates));
    }

    [Fact]
    public async Task The_question_offers_entries_of_every_scope_with_their_scope_and_section_and_keeps_its_wording()
    {
        Configure();
        Baseline(DomainCandidate, DomainPersona);

        var result = await Service.WritePersonaAsync(Propose("preferences", "沟通：先要结论"));

        Assert.False(result.IsError, result.Text);
        var question = _models.LastDecision!.Value.Body["questions"]!.AsObject().Single().Value!;
        var criteria = question["criteria"]!.AsObject();
        var offered = criteria.Select(option => option.Value!.GetValue<string>()).ToList();
        Assert.Contains(offered, option => option.StartsWith("[candidate | Working rules | domain:network-proxy]", StringComparison.Ordinal));
        Assert.Contains(offered, option => option.StartsWith("[persona | Working rules | domain:network-proxy]", StringComparison.Ordinal));
        Assert.Equal(ExpectedInstructions, question["instructions"]!.GetValue<string>());
        Assert.Equal(NoneOption, criteria.Single(option => option.Key == "none").Value!.GetValue<string>());
    }

    [Fact]
    public async Task A_proposal_matching_a_persona_entry_of_a_domain_gives_that_entrys_short_code()
    {
        Configure();
        Baseline("## Candidates\n\n## Rejection list\n", DomainPersona);
        PickOption("先运行测试", 0.95);

        var result = await Service.WritePersonaAsync(Propose("working_rules", "提交前 → 先运行测试", "先跑测试"));

        Assert.False(result.IsError, result.Text);
        Assert.False(result.Written);
        Assert.Contains($"[{ShortOf("提交前 → 先运行测试")}]", result.Text);
    }

    [Fact]
    public async Task A_proposal_matching_a_rejected_candidate_of_a_domain_is_refused_and_says_so()
    {
        Configure();
        Baseline("## Candidates\n\n## Rejection list\n\n### 每次提交前都运行全量测试\nBelongs to: Working rules\nScope: domain:network-proxy\n- 2026-10-20 rejected quote「算了，别跑了」\n");
        PickOption("全量测试", 0.95);

        var result = await Service.WritePersonaAsync(Propose("working_rules", "提交前 → 先运行测试", "提交前跑全量测试"));

        Assert.False(result.IsError, result.Text);
        Assert.False(result.Written);
        Assert.Contains("算了，别跑了", result.Text);
    }
}
