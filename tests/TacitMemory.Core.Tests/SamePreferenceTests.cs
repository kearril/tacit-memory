using System.Security.Cryptography;
using System.Text;

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

    private void Configure(string extra = "") => _home.InitReady(extra, _models);

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

    private static string ShortOf(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\n").Trim())))[..6].ToLowerInvariant();

    /// <summary>Makes the decision model pick the option whose text contains <paramref name="contains"/> with <paramref name="probability"/>.</summary>
    private void PickOption(string contains, double probability) =>
        _models.Choose = (_, options) => options.ToDictionary(
            option => option.Key,
            option => option.Value.Contains(contains, StringComparison.Ordinal) ? probability
                : option.Key == options.Keys.Last() ? 1 - probability
                : 0.0);

    private const string WorkingRuleCandidate =
        "## 候选\n\n### 每次提交前都运行全量测试\n归属：协作规则\n- 2026-10-27 原话「跑一下全部测试」\n\n## 拒绝列表\n";

    [Fact]
    public async Task A_candidate_the_decision_model_is_sure_states_the_same_preference_is_merged_and_committed()
    {
        Configure();
        Baseline(WorkingRuleCandidate);
        PickOption("全量测试", 0.95);

        var result = await Service.WritePersonaAsync(Propose("working_rules", "提交前 → 先运行测试", "提交前跑全量测试"));

        Assert.False(result.IsError, result.Text);
        Assert.True(result.Written);
        Assert.Contains("2/2", result.Text);
        Assert.Contains("consolidation", result.Text);
        Assert.Equal(
            "## 候选\n\n### 每次提交前都运行全量测试\n归属：协作规则\n"
            + "- 2026-10-27 原话「跑一下全部测试」\n- 2026-11-02 原话「提交前跑全量测试」\n\n## 拒绝列表\n",
            File.ReadAllText(CandidatesMd));
        Assert.Equal($"candidates: merge {ShortOf("每次提交前都运行全量测试")}", Git("log", "-1", "--format=%s").Trim());
        Assert.Equal("candidates.md", Git("show", "--name-only", "--format=", "HEAD").Trim());
    }

    [Fact]
    public async Task A_merge_counts_distinct_days_against_the_configured_minimum_and_stays_quiet_below_it()
    {
        Configure("candidates:\n  minDays: 3\n");
        Baseline(WorkingRuleCandidate);
        PickOption("全量测试", 0.95);

        var result = await Service.WritePersonaAsync(Propose("working_rules", "提交前 → 先运行测试", "提交前跑全量测试"));

        Assert.False(result.IsError, result.Text);
        Assert.Contains("2/3", result.Text);
        Assert.DoesNotContain("consolidation", result.Text);
    }

    [Fact]
    public async Task A_candidate_already_recorded_today_is_neither_written_nor_committed()
    {
        Configure();
        Baseline("## 候选\n\n### 每次提交前都运行全量测试\n归属：协作规则\n- 2026-10-27 原话「跑一下全部测试」\n- 2026-11-02 原话「先跑全量」\n\n## 拒绝列表\n");
        var before = File.ReadAllText(CandidatesMd);
        var head = Git("rev-parse", "HEAD");
        PickOption("全量测试", 0.95);

        var result = await Service.WritePersonaAsync(Propose("working_rules", "提交前 → 先运行测试", "提交前跑全量测试"));

        Assert.False(result.IsError, result.Text);
        Assert.False(result.Written);
        Assert.Contains("Already recorded today", result.Text);
        Assert.Contains("consolidation", result.Text);
        Assert.Equal(before, File.ReadAllText(CandidatesMd));
        Assert.Equal(head, Git("rev-parse", "HEAD"));
    }

    [Fact]
    public async Task A_candidate_already_recorded_today_does_not_mention_the_consolidation_below_the_minimum_days()
    {
        Configure("candidates:\n  minDays: 3\n");
        Baseline("## 候选\n\n### 每次提交前都运行全量测试\n归属：协作规则\n- 2026-10-27 原话「跑一下全部测试」\n- 2026-11-02 原话「先跑全量」\n\n## 拒绝列表\n");
        PickOption("全量测试", 0.95);

        var result = await Service.WritePersonaAsync(Propose("working_rules", "提交前 → 先运行测试", "提交前跑全量测试"));

        Assert.False(result.IsError, result.Text);
        Assert.Contains("Already recorded today", result.Text);
        Assert.Contains("2/3", result.Text);
        Assert.DoesNotContain("consolidation", result.Text);
    }

    [Fact]
    public async Task A_proposal_matching_a_rejected_candidate_is_not_recorded_and_says_when_and_in_what_words_it_was_refused()
    {
        Configure();
        Baseline("## 候选\n\n## 拒绝列表\n\n### 每次提交前都运行全量测试\n归属：协作规则\n- 2026-10-20 拒绝 原话「算了，别跑了」\n");
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
        Baseline("## 候选\n\n## 拒绝列表\n", "## 红线\n\n## 协作规则\n\n## 偏好\n- 沟通：先要结论\n  <!-- 2026-10-20 原话「先要结论」 -->\n\n## 档案\n");
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
        Baseline("## 候选\n\n## 拒绝列表\n");
        _ = Service;
        File.WriteAllText(PersonaMd, "## 偏好\n- 沟通：先要结论\n  <!-- 2026-10-20 原话「先要结论」 -->\n");
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
        Baseline("## 候选\n\n## 拒绝列表\n", "## 红线\n\n## 协作规则\n\n## 偏好\n\n## 档案\n");

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
        var entries = string.Concat(Enumerable.Range(0, 12).Select(i => $"### 偏好 {i}\n归属：偏好\n- 2026-10-2{i % 9} 原话「第{i}条」\n\n"));
        Baseline($"## 候选\n\n{entries}## 拒绝列表\n");
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
        var handwritten = "# 手写标题\n<!-- 我的注释 -->\n\n## 候选\n\n### 每次提交前都运行全量测试\n归属：协作规则\n- 2026-10-27 原话「跑一下全部测试」\n\n手写的段落\n\n## 拒绝列表\n\n尾部注释\n";
        Baseline(handwritten);
        PickOption("全量测试", 0.95);

        var result = await Service.WritePersonaAsync(Propose("working_rules", "提交前 → 先运行测试", "提交前跑全量测试"));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(
            handwritten.Replace("- 2026-10-27 原话「跑一下全部测试」\n", "- 2026-10-27 原话「跑一下全部测试」\n- 2026-11-02 原话「提交前跑全量测试」\n", StringComparison.Ordinal),
            File.ReadAllText(CandidatesMd));
    }

    [Fact]
    public async Task The_question_does_not_name_a_language_for_the_preferences_in_an_english_memory()
    {
        Configure("language: en\n");
        Baseline(WorkingRuleCandidate);

        await Service.WritePersonaAsync(Propose("working_rules", "提交前 → 先运行测试", "提交前跑全量测试"));

        var question = _models.LastDecision!.Value.Body["questions"]!.AsObject().Single().Value!;
        Assert.DoesNotContain("Chinese", question["instructions"]!.GetValue<string>());
    }
}
