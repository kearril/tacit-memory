using System.Text.RegularExpressions;

namespace TacitMemory.Core.Tests;

public sealed class PersonaConflictTests : IDisposable
{
    private readonly TestHome _home = new();
    private readonly ModelDouble _models = new();
    private MemoryService? _service;

    public PersonaConflictTests() => Configure();

    public void Dispose()
    {
        _service?.Dispose();
        _models.Dispose();
        _home.Dispose();
    }

    private MemoryService Service => _service ??= _home.Tacit().OpenService(_ => { });

    private string PersonaMd => Path.Combine(_home.DefaultMemory, "persona.md");

    private string Commits => TestHome.Git(_home.DefaultMemory, "rev-list", "--count", "HEAD").Trim();

    private const string Fixture = """
        ## 红线
        - 不要删库；改为先备份
          <!-- 2026-10-25 原话「别删库」 -->

        ## 协作规则
        - 需求不清时 → 先连续追问
          <!-- 2026-10-20 原话「先问清楚再动手」 -->

        ## 偏好
        - 沟通：先要结论
          <!-- 2026-10-21 原话「先说结论」 -->

        ## 档案
        - 主力语言：C#
          <!-- 2026-10-01 原话「我用 C#」 -->

        """;

    private void Configure(string extra = "", string? decisionKey = "test-key")
    {
        _home.InitReady(models: _models);
        File.WriteAllText(Path.Combine(_home.TacitHome, "config.yaml"), (_models.ModelsYaml(decisionKey: decisionKey) + extra).ReplaceLineEndings("\n"));
    }

    /// <summary>Writes the persona before the service opens, so its index starts from this content.</summary>
    private void WritePersona(string text) => File.WriteAllText(PersonaMd, text.ReplaceLineEndings("\n"));

    private Task<ToolResult> Add(string section, string text, string source = "这是原话") => Service.WritePersonaAsync(new WritePersonaRequest
    {
        Op = "add",
        Section = section,
        Text = text,
        Source = source,
    });

    private Task<ToolResult> Replace(string hash, string text, string source = "这是新原话") => Service.WritePersonaAsync(new WritePersonaRequest
    {
        Op = "replace",
        Hash = hash,
        Text = text,
        Source = source,
    });

    /// <summary>Makes the decision model pick the option that contains <paramref name="needle"/> with <paramref name="probability"/>; "none" gets nothing.</summary>
    private void Pick(string needle, double probability) =>
        _models.Choose = (_, options) => options.ToDictionary(
            option => option.Key,
            option => option.Key != options.Keys.Last() && option.Value.Contains(needle, StringComparison.Ordinal) ? probability : 0.0);

    /// <summary>The short code the persona lists for the entry whose line contains <paramref name="line"/>, read from a refused replace.</summary>
    private async Task<string> CodeOf(string line)
    {
        var listing = await Replace("000000", "占位");
        return Regex.Match(listing.Text, @"- \[([0-9a-f]{6})\] " + Regex.Escape(line)).Groups[1].Value;
    }

    [Fact]
    public async Task An_entry_the_decision_model_is_sure_conflicts_is_refused_and_points_to_that_entry()
    {
        WritePersona(Fixture);
        Pick("先要结论", 0.95);
        var commits = Commits;

        var result = await Add("preferences", "沟通：先展开讲解，最后再给结论");

        Assert.True(result.IsError);
        Assert.StartsWith("Refused: The new entry conflicts with, duplicates or supersedes an existing persona entry.", result.Text);
        Assert.Contains("op=replace, hash=", result.Text);
        Assert.Matches(@"Preferences section:\n- \[[0-9a-f]{6}\] 沟通：先要结论", result.Text);
        Assert.Equal(Fixture.ReplaceLineEndings("\n"), File.ReadAllText(PersonaMd));
        Assert.Equal(commits, Commits);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(0.2)]
    public async Task An_entry_the_decision_model_is_not_sure_about_is_written(double probability)
    {
        WritePersona(Fixture);
        Pick("先要结论", probability);

        var result = await Add("preferences", "沟通：先展开讲解，最后再给结论");

        Assert.False(result.IsError, result.Text);
        Assert.Contains("沟通：先展开讲解，最后再给结论", File.ReadAllText(PersonaMd));
    }

    [Fact]
    public async Task The_candidates_cover_all_four_sections()
    {
        WritePersona(Fixture);
        var offered = new List<string>();
        _models.Choose = (_, options) =>
        {
            offered.AddRange(options.Values);
            return options.Keys.ToDictionary(key => key, key => key == options.Keys.Last() ? 1.0 : 0.0);
        };

        var result = await Add("preferences", "先说明影响范围");

        Assert.False(result.IsError, result.Text);
        Assert.Contains(offered, option => option.Contains("需求不清时 → 先连续追问", StringComparison.Ordinal));
        Assert.Contains(offered, option => option.Contains("不要删库；改为先备份", StringComparison.Ordinal));
        Assert.Contains(offered, option => option.Contains("主力语言：C#", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Replace_never_offers_the_entry_it_replaces()
    {
        WritePersona(Fixture);
        var hash = await CodeOf("沟通：先要结论");
        var offered = new List<string>();
        _models.Choose = (_, options) =>
        {
            offered.AddRange(options.Values);
            return options.Keys.ToDictionary(key => key, key => key == options.Keys.Last() ? 1.0 : 0.0);
        };

        var result = await Replace(hash, "沟通：先要结论并附依据");

        Assert.False(result.IsError, result.Text);
        Assert.DoesNotContain(offered, option => option.Contains("沟通：先要结论", StringComparison.Ordinal));
        Assert.Contains("沟通：先要结论并附依据", File.ReadAllText(PersonaMd));
    }

    [Fact]
    public async Task Replace_is_refused_when_the_new_text_conflicts_with_another_entry()
    {
        WritePersona(Fixture);
        var hash = await CodeOf("沟通：先要结论");
        Pick("先连续追问", 0.95);

        var result = await Replace(hash, "需求不清时直接动手");

        Assert.True(result.IsError);
        Assert.Matches(@"Working rules section:\n- \[[0-9a-f]{6}\] 需求不清时 → 先连续追问", result.Text);
        Assert.Contains("沟通：先要结论", File.ReadAllText(PersonaMd));
    }

    [Fact]
    public async Task Without_other_entries_the_decision_model_is_not_asked()
    {
        WritePersona("## 偏好\n\n## 档案\n");

        var add = await Add("preferences", "先说明影响范围");
        Assert.False(add.IsError, add.Text);
        Assert.Equal(0, _models.DecisionCalls);

        var hash = await CodeOf("先说明影响范围");
        var replaced = await Replace(hash, "先说明影响范围和风险");
        Assert.False(replaced.IsError, replaced.Text);
        Assert.Equal(0, _models.DecisionCalls);
    }

    [Fact]
    public async Task A_write_is_judged_against_the_persona_as_it_is_right_after_the_previous_write()
    {
        WritePersona(Fixture);
        Assert.False((await Add("preferences", "回复要带时间戳")).IsError);
        Pick("回复要带时间戳", 0.95);

        var result = await Add("preferences", "回复一律带时间戳");

        Assert.True(result.IsError);
        Assert.Matches(@"Preferences section:\n- \[[0-9a-f]{6}\] 回复要带时间戳", result.Text);
    }

    [Fact]
    public async Task The_question_is_in_english_and_carries_the_new_entry_with_its_section()
    {
        WritePersona(Fixture);

        await Add("preferences", "先说明影响范围");

        var (body, authorization) = _models.LastDecision!.Value;
        Assert.Equal("Bearer test-key", authorization);
        var question = body["questions"]!.AsObject().Single().Value!;
        Assert.Equal("choice", question["type"]!.GetValue<string>());
        Assert.StartsWith("The state is a new entry for the user's persona file", question["instructions"]!.GetValue<string>());
        Assert.Contains("[偏好] 先说明影响范围", body["state"]!.GetValue<string>());
    }

    [Fact]
    public async Task When_the_decision_model_is_unavailable_the_entry_is_written_and_the_reply_says_so()
    {
        WritePersona(Fixture);
        _models.DecisionDown = true;

        var result = await Add("preferences", "沟通：先展开讲解");

        Assert.False(result.IsError, result.Text);
        Assert.Contains("沟通：先展开讲解", File.ReadAllText(PersonaMd));
        Assert.Contains("the decision model is unavailable", result.Text);
    }

    [Fact]
    public async Task When_the_decision_model_has_no_key_the_entry_is_written_and_the_reply_says_so()
    {
        Configure(decisionKey: null);
        WritePersona(Fixture);

        var result = await Add("preferences", "沟通：先展开讲解");

        Assert.False(result.IsError, result.Text);
        Assert.Contains("沟通：先展开讲解", File.ReadAllText(PersonaMd));
        Assert.Contains("the decision model is unavailable", result.Text);
    }

    [Fact]
    public async Task When_the_embedding_model_is_unavailable_the_entry_is_written_and_the_reply_says_so()
    {
        WritePersona(Fixture);
        _models.EmbeddingDown = true;

        var result = await Add("preferences", "沟通：先展开讲解");

        Assert.False(result.IsError, result.Text);
        Assert.Contains("沟通：先展开讲解", File.ReadAllText(PersonaMd));
        Assert.Contains("the embedding model is unavailable", result.Text);
        Assert.Equal(0, _models.DecisionCalls);
    }

    [Fact]
    public async Task A_configured_threshold_replaces_the_built_in_one()
    {
        Configure("decision:\n  ruleConflict: { certain: 0.99, unlikely: 0.1 }\n");
        WritePersona(Fixture);
        Pick("先要结论", 0.95);

        var result = await Add("preferences", "沟通：先展开讲解，最后再给结论");

        Assert.False(result.IsError, result.Text);
    }

    [Fact]
    public async Task The_question_does_not_name_a_language_for_the_entries_in_an_english_memory()
    {
        using var home = new TestHome();
        using var models = new ModelDouble();
        home.InitReady("language: en\n", models);
        File.WriteAllText(Path.Combine(home.DefaultMemory, "persona.md"), Fixture.ReplaceLineEndings("\n"));
        using var service = home.Tacit().OpenService(_ => { });

        await service.WritePersonaAsync(new WritePersonaRequest
        {
            Op = "add",
            Section = "preferences",
            Text = "沟通：先展开讲解",
            Source = "先展开讲解",
        });

        var question = models.LastDecision!.Value.Body["questions"]!.AsObject().Single().Value!;
        Assert.DoesNotContain("Chinese", question["instructions"]!.GetValue<string>());
    }
}
