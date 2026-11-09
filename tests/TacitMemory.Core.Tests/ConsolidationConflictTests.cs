using System.Security.Cryptography;
using System.Text;

using static TacitMemory.Core.Tests.ShortCodes;

namespace TacitMemory.Core.Tests;

/// <summary>
/// The consolidation's conflicting persona entries through the facade: only the entries new or rewritten since the last
/// consolidation are judged, each against the other entries with the live rule-conflict question. A pair is listed once, with
/// the tier of its judgment, and a judgment the models cannot make is reported as not checked.
/// </summary>
public sealed class ConsolidationConflictTests : IDisposable
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

    private MemoryService Service => _service ??= _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

    private const string Conflicting = "Conflicting persona entries";

    private const string Old = "沟通：先要结论";

    private const string Other = "回复别太长";

    private const string Changed = "沟通：先说结论，再讲依据";

    private const string Terse = "少用表情";

    private const string Baseline =
        "## 红线\n\n## 协作规则\n\n## 偏好\n- 沟通：先要结论\n- 回复别太长\n\n## 档案\n";

    private void Configure() => _home.InitReady("", _models);

    private void Commit(string message)
    {
        TestHome.Git(Memory, "add", "-A");
        TestHome.Git(Memory, "-c", "user.name=tacit", "-c", "user.email=tacit@localhost", "commit", "--allow-empty", "-q", "-m", message);
    }

    /// <summary>Makes the persona as the last consolidation left it: the given persona, committed as a consolidation.</summary>
    private void CheckedBaseline(string persona = Baseline)
    {
        File.WriteAllText(PersonaMd, persona.ReplaceLineEndings("\n"));
        Commit("consolidate: baseline");
    }

    /// <summary>Writes the persona as it is now, with the new entries added to the preferences section; uncommitted.</summary>
    private void Edit(params string[] added) =>
        File.WriteAllText(PersonaMd, Baseline.Replace("- 回复别太长\n", "- 回复别太长\n" + string.Concat(added.Select(text => $"- {text}\n"))).ReplaceLineEndings("\n"));

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

    private static int CountOf(string text, string needle)
    {
        var count = 0;
        for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    private async Task<string> Consolidation() => await Service.ConsolidationReportAsync().WaitAsync(TimeSpan.FromSeconds(30));

    [Fact]
    public async Task Only_an_entry_changed_since_the_last_consolidation_is_judged_against_the_others()
    {
        Configure();
        CheckedBaseline();
        // Uncommitted: a manual edit still counts as changed.
        Edit(Changed);
        PickOption(Old, 0.95);

        var section = Section(await Consolidation(), Conflicting);

        Assert.Contains($"[{ShortOf(Changed)}]", section);
        Assert.Contains($"[{ShortOf(Old)}]", section);
        Assert.DoesNotContain($"[{ShortOf(Other)}]", section);
        Assert.Equal(1, _models.DecisionCalls);
    }

    [Fact]
    public async Task A_conflict_is_listed_with_both_sections_texts_codes_and_the_tier()
    {
        Configure();
        CheckedBaseline();
        File.WriteAllText(PersonaMd, Baseline.Replace("## 红线\n\n", $"## 红线\n- {Changed}\n\n").ReplaceLineEndings("\n"));
        PickOption(Old, 0.95);

        var section = Section(await Consolidation(), Conflicting);

        Assert.Contains($"Certain: [{ShortOf(Changed)}] \"{Changed}\" in section 红线", section);
        Assert.Contains($"[{ShortOf(Old)}] \"{Old}\" in section 偏好", section);
    }

    [Fact]
    public async Task A_pair_judged_from_both_sides_is_listed_once()
    {
        Configure();
        CheckedBaseline("## 红线\n\n## 协作规则\n\n## 偏好\n- 回复别太长\n\n## 档案\n");
        // Both entries are new since the consolidation, and each one's nearest other entry is the other.
        File.WriteAllText(PersonaMd, ("## 红线\n\n## 协作规则\n\n## 偏好\n- 回复别太长\n- " + Old + "\n- " + Changed + "\n\n## 档案\n").ReplaceLineEndings("\n"));
        _models.Choose = (_, options) => options.ToDictionary(
            option => option.Key,
            option => option.Value.Contains(Old, StringComparison.Ordinal) || option.Value.Contains(Changed, StringComparison.Ordinal) ? 0.95
                : option.Key == options.Keys.Last() ? 0.05
                : 0.0);

        var section = Section(await Consolidation(), Conflicting);

        Assert.Equal(1, CountOf(section, "- Certain:"));
        Assert.Equal(2, _models.DecisionCalls);
    }

    [Fact]
    public async Task A_sure_conflict_and_an_unsure_one_are_listed_with_their_own_tiers()
    {
        Configure();
        CheckedBaseline();
        File.WriteAllText(PersonaMd, Baseline.Replace("- 回复别太长\n", $"- 回复别太长\n- {Changed}\n- {Terse}\n").ReplaceLineEndings("\n"));
        _models.Choose = (state, options) => options.ToDictionary(
            option => option.Key,
            option =>
            {
                if (state.Contains(Changed, StringComparison.Ordinal))
                    return option.Value.Contains(Old, StringComparison.Ordinal) ? 0.95 : 0.0;
                if (state.Contains(Terse, StringComparison.Ordinal))
                    return option.Value.Contains(Other, StringComparison.Ordinal) ? 0.5 : option.Key == options.Keys.Last() ? 0.1 : 0.0;
                return option.Key == options.Keys.Last() ? 1.0 : 0.0;
            });

        var section = Section(await Consolidation(), Conflicting);

        Assert.Contains($"- Certain: [{ShortOf(Changed)}]", section);
        Assert.Contains($"- Unsure: [{ShortOf(Terse)}]", section);
        Assert.Contains($"[{ShortOf(Other)}]", section);
    }

    [Fact]
    public async Task A_judgment_the_model_rejects_is_not_listed()
    {
        Configure();
        CheckedBaseline();
        Edit(Changed);
        PickOption(Old, 0.05);

        var section = Section(await Consolidation(), Conflicting);

        Assert.Equal(1, _models.DecisionCalls);
        Assert.DoesNotContain("- Certain:", section);
        Assert.DoesNotContain("- Unsure:", section);
    }

    [Fact]
    public async Task With_no_other_entry_no_question_is_asked()
    {
        Configure();
        CheckedBaseline("## 红线\n\n## 协作规则\n\n## 偏好\n\n## 档案\n");
        File.WriteAllText(PersonaMd, "## 红线\n\n## 协作规则\n\n## 偏好\n- " + Changed + "\n\n## 档案\n");

        var section = Section(await Consolidation(), Conflicting);

        Assert.Equal(0, _models.DecisionCalls);
        Assert.Contains("None.", section);
    }

    [Fact]
    public async Task When_the_decision_model_is_down_the_category_says_not_checked_and_lists_nothing()
    {
        Configure();
        CheckedBaseline();
        Edit(Changed);
        _models.DecisionDown = true;

        var report = await Consolidation();

        var section = Section(report, Conflicting);
        Assert.Contains("Not checked", section);
        Assert.DoesNotContain("- Certain:", section);
        Assert.Contains("Persona candidates to remove", report);
    }
}
