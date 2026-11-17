using System.Security.Cryptography;
using System.Text;

using static TacitMemory.Core.Tests.ShortCodes;

namespace TacitMemory.Core.Tests;

/// <summary>
/// The consolidation's candidate clean-up through the facade: every candidate is judged against the persona and the rejection
/// list with the live same-preference question; a sure match is listed for removal, an unsure one is hinted, and a
/// judgment the models cannot make is reported as not checked. Answers are cached by the question's content.
/// </summary>
public sealed class ConsolidationCandidateTests : IDisposable
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

    private const string Removal = "Persona candidates to remove";

    private const string PersonaWithPreference =
        "## Red lines\n\n## Working rules\n\n## Preferences\n- 沟通：先要结论\n  <!-- 2026-10-20 quote「先要结论」 -->\n\n## Profile\n";

    private const string EmptyPersona = "## Red lines\n\n## Working rules\n\n## Preferences\n\n## Profile\n";

    private const string Candidate = "回答尽量简短";

    private void Configure(string extra = "") => _home.InitReady(extra, _models);

    private void Write(string persona, string candidates)
    {
        File.WriteAllText(PersonaMd, persona.ReplaceLineEndings("\n"));
        File.WriteAllText(CandidatesMd, candidates.ReplaceLineEndings("\n"));
    }

    /// <summary>A candidates file with the given candidate blocks and an empty rejection list.</summary>
    private static string Candidates(params string[] blocks) => "## Candidates\n\n" + string.Concat(blocks) + "## Rejection list\n";

    private static string Block(string title, params string[] days) =>
        $"### {title}\nBelongs to: Preferences\n" + string.Concat(days.Select(day => $"- {day} quote「{day}的话」\n")) + "\n";

    /// <summary>Makes the decision model pick the option whose text contains <paramref name="contains"/> with <paramref name="probability"/>.</summary>
    private void PickOption(string contains, double probability) =>
        _models.Choose = (_, options) => options.ToDictionary(
            option => option.Key,
            option => option.Value.Contains(contains, StringComparison.Ordinal) ? probability
                : option.Key == options.Keys.Last() ? 1 - probability
                : 0.0);

    /// <summary>The text of one report category: from its heading to the next heading, or to the end.</summary>
    private static string Section(string text, string title)
    {
        var start = text.IndexOf($"### {title}\n", StringComparison.Ordinal);
        Assert.True(start >= 0, $"No category \"{title}\" in the report.");
        var body = text[(start + title.Length + 5)..];
        var next = body.IndexOf("\n### ", StringComparison.Ordinal);
        return next < 0 ? body : body[..next];
    }

    private async Task<string> Consolidation() => await Service.ConsolidationReportAsync().WaitAsync(TimeSpan.FromSeconds(30));

    [Fact]
    public async Task A_candidate_the_decision_model_is_sure_repeats_a_persona_entry_is_listed_for_removal_with_its_short_code()
    {
        Configure();
        Write(PersonaWithPreference, Candidates(Block(Candidate, "2026-10-30", "2026-10-31")));
        PickOption("先要结论", 0.95);

        var report = await Consolidation();

        Assert.Contains(Candidate, Section(report, Removal));
        Assert.Contains($"[{ShortOf("沟通：先要结论")}]", Section(report, Removal));
    }

    [Fact]
    public async Task A_candidate_the_decision_model_is_sure_repeats_a_rejected_candidate_is_listed_for_removal_even_on_one_day()
    {
        Configure();
        Write(EmptyPersona, "## Candidates\n\n" + Block(Candidate, "2026-10-31") + "## Rejection list\n\n" + Block(Candidate, "2026-10-20").Replace("quote", "rejected quote", StringComparison.Ordinal));
        PickOption(Candidate, 0.95);

        var report = await Consolidation();

        Assert.Contains(Candidate, Section(report, Removal));
        Assert.Contains("rejected", Section(report, Removal));
    }

    [Fact]
    public async Task A_candidate_the_decision_model_is_not_sure_about_is_neither_removed_nor_listed()
    {
        Configure();
        Write(PersonaWithPreference, Candidates(Block(Candidate, "2026-10-30", "2026-10-31")));
        // Unsure: the entry ranks above "none of these", which is low, so the answer is neither a match nor a rejection.
        _models.Choose = (_, options) => options.ToDictionary(
            option => option.Key,
            option => option.Value.Contains("先要结论", StringComparison.Ordinal) ? 0.5 : option.Key == options.Keys.Last() ? 0.1 : 0.0);

        var report = await Consolidation();

        Assert.DoesNotContain(Candidate, Section(report, Removal));
    }

    [Fact]
    public async Task A_candidate_the_decision_model_rejects_is_neither_removed_nor_hinted()
    {
        Configure();
        Write(PersonaWithPreference, Candidates(Block(Candidate, "2026-10-30", "2026-10-31")));
        PickOption("先要结论", 0.2);

        var report = await Consolidation();

        Assert.DoesNotContain(Candidate, Section(report, Removal));
    }

    [Fact]
    public async Task Only_persona_entries_and_rejected_candidates_are_offered_to_the_decision_model()
    {
        Configure();
        Write(PersonaWithPreference, "## Candidates\n\n" + Block(Candidate, "2026-10-30", "2026-10-31") + Block("另一个候选", "2026-10-30")
            + "## Rejection list\n\n" + Block("被拒的偏好", "2026-10-20").Replace("quote", "rejected quote", StringComparison.Ordinal));

        await Consolidation();

        var offered = _models.LastDecision!.Value.Body["questions"]!.AsObject().Single().Value!["criteria"]!.AsObject()
            .Where(option => option.Key != "none")
            .Select(option => option.Value!.GetValue<string>())
            .ToList();
        Assert.NotEmpty(offered);
        Assert.All(offered, text => Assert.True(text.StartsWith("[persona |", StringComparison.Ordinal) || text.StartsWith("[rejected |", StringComparison.Ordinal), text));
    }

    [Fact]
    public async Task With_no_persona_entry_and_no_rejected_candidate_no_question_is_asked()
    {
        Configure();
        Write(EmptyPersona, Candidates(Block(Candidate, "2026-10-30", "2026-10-31")));

        var report = await Consolidation();

        Assert.Equal(0, _models.DecisionCalls);
        Assert.Contains("None.", Section(report, Removal));
    }

    [Fact]
    public async Task Without_the_decision_model_the_candidates_are_not_checked_and_the_candidates_are_listed_with_a_reminder()
    {
        Configure();
        Write(PersonaWithPreference, Candidates(Block(Candidate, "2026-10-30", "2026-10-31")));
        _models.DecisionDown = true;

        var report = await Consolidation();

        Assert.Contains("Not checked", Section(report, Removal));
    }

    [Fact]
    public async Task Without_the_embedding_model_the_candidates_are_not_checked_and_no_decision_is_asked()
    {
        Configure();
        Write(PersonaWithPreference, Candidates(Block(Candidate, "2026-10-30", "2026-10-31")));
        _models.EmbeddingDown = true;

        var report = await Consolidation();

        Assert.Equal(0, _models.DecisionCalls);
        Assert.Contains("Not checked", Section(report, Removal));
    }

    [Fact]
    public async Task A_configured_threshold_replaces_the_built_in_one_for_the_candidate_judgment()
    {
        Configure("decision:\n  samePreference: { certain: 0.99, unlikely: 0.1 }\n");
        Write(PersonaWithPreference, Candidates(Block(Candidate, "2026-10-30", "2026-10-31")));
        PickOption("先要结论", 0.95);

        var report = await Consolidation();

        Assert.DoesNotContain(Candidate, Section(report, Removal));
    }

    [Fact]
    public async Task An_unchanged_candidate_is_not_asked_again_and_its_answer_is_reused()
    {
        Configure();
        Write(PersonaWithPreference, Candidates(Block(Candidate, "2026-10-30", "2026-10-31")));
        PickOption("先要结论", 0.95);

        var first = await Consolidation();
        var second = await Consolidation();

        Assert.Equal(1, _models.DecisionCalls);
        Assert.Contains(Candidate, Section(first, Removal));
        Assert.Contains(Candidate, Section(second, Removal));
    }

    [Fact]
    public async Task A_changed_persona_asks_the_model_again()
    {
        Configure();
        Write(PersonaWithPreference, Candidates(Block(Candidate, "2026-10-30", "2026-10-31")));
        PickOption("先要结论", 0.95);
        await Consolidation();

        File.WriteAllText(PersonaMd, PersonaWithPreference.Replace("先要结论", "先给结论", StringComparison.Ordinal).ReplaceLineEndings("\n"));
        await Consolidation();

        Assert.Equal(2, _models.DecisionCalls);
    }

    [Fact]
    public async Task Changing_the_threshold_reclassifies_the_cached_answer_without_calling_the_model()
    {
        Configure();
        Write(PersonaWithPreference, Candidates(Block(Candidate, "2026-10-30", "2026-10-31")));
        PickOption("先要结论", 0.95);
        Assert.Contains(Candidate, Section(await Consolidation(), Removal));

        _service!.Dispose();
        _service = null;
        Configure("decision:\n  samePreference: { certain: 0.99, unlikely: 0.1 }\n");
        var report = await Consolidation();

        Assert.Equal(1, _models.DecisionCalls);
        Assert.DoesNotContain(Candidate, Section(report, Removal));
    }

    [Fact]
    public async Task Rebuilding_the_index_clears_the_cache_so_the_model_is_asked_again()
    {
        Configure();
        Write(PersonaWithPreference, Candidates(Block(Candidate, "2026-10-30", "2026-10-31")));
        PickOption("先要结论", 0.95);
        await Consolidation();

        _service!.Dispose();
        _service = null;
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            File.Delete(Path.Combine(Memory, ".cache", "index.db" + suffix));
        var report = await Consolidation();

        Assert.Equal(2, _models.DecisionCalls);
        Assert.Contains(Candidate, Section(report, Removal));
    }
}
