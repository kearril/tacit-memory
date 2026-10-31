namespace TacitMemory.Core.Tests;

public sealed class ConfigTests : IDisposable
{
    private const string Models = TestHome.Models;

    private readonly TestHome _home = new();

    public ConfigTests() => _home.Tacit().InitAsync(new InitRequest { Language = MemoryLanguage.Zh }).GetAwaiter().GetResult();

    public void Dispose() => _home.Dispose();

    private string ConfigPath => Path.Combine(_home.TacitHome, "config.yaml");

    private string RulesPath => Path.Combine(_home.TacitHome, "rules.md");

    private void WriteConfig(string yaml) => File.WriteAllText(ConfigPath, yaml.ReplaceLineEndings("\n"));

    private TacitException LoadFails() => Assert.Throws<TacitException>(() => _home.Tacit().LoadConfig());

    [Fact]
    public void Only_models_need_filling_in_everything_else_takes_the_documented_defaults()
    {
        WriteConfig(Models);

        var config = _home.Tacit().LoadConfig();

        Assert.Equal(18888, config.Port);
        Assert.Equal(_home.DefaultMemory, config.MemoryPath);
        Assert.Equal(MemoryLanguage.Zh, config.Language);
        Assert.Equal(new PersonaLimits(5, 20, 15, 10), config.Limits.Persona);
        Assert.Equal(100, config.Limits.ItemLength);
        Assert.Equal(5, config.Limits.Mainline);
        Assert.Equal(new PageLimits(50, 30, 2), config.Limits.Page);
        Assert.Equal(2, config.CandidateMinDays);
        Assert.Equal(new CheckupConfig(7, 14), config.Checkup);
        Assert.Equal(new DecisionThresholds(null, null, null, null, null), config.Decision);
        Assert.Equal(new RetrievalConfig(30, 5, null), config.Retrieval);
        Assert.Equal(new BackupConfig(7, null), config.Backup);
        Assert.Equal(new Uri("http://127.0.0.1:38417/v1"), config.Models.Embedding.Endpoint);
        Assert.Equal("qwen3-reranker-0.6b", config.Models.Reranker.Model);
        Assert.Equal(new DecisionModelEndpoint(new Uri("https://api.typesafe.ai/v1"), "jev-latest", null, "JEV_KEY"), config.Models.Decision);
    }

    [Fact]
    public void The_template_written_by_init_loads_once_models_are_filled_in()
    {
        var template = File.ReadAllText(ConfigPath);
        WriteConfig(template[..template.IndexOf("models:", StringComparison.Ordinal)] + Models +
                    template[template.IndexOf("limits:", StringComparison.Ordinal)..]);

        var config = _home.Tacit().LoadConfig();

        Assert.Equal(_home.DefaultMemory, config.MemoryPath);
        Assert.Equal(new PersonaLimits(5, 20, 15, 10), config.Limits.Persona);
    }

    [Fact]
    public void An_unfilled_template_names_every_missing_model_setting()
    {
        var error = LoadFails();

        Assert.Equal(TacitError.ConfigInvalid, error.Error);
        foreach (var key in new[] { "models.embedding.endpoint", "models.embedding.model", "models.reranker.endpoint", "models.decision.model", "models.decision" })
            Assert.Contains(key, error.Message);
    }

    [Fact]
    public void Configured_values_override_the_defaults()
    {
        WriteConfig(Models + """
            server: { port: 19999 }
            memory: { path: '~/notes/memory' }
            language: en
            limits: { persona: { redLines: 3 }, itemLength: 80 }
            decision: { samePage: { certain: 0.9, unlikely: 0.2 } }
            retrieval: { minRerankScore: 0.3 }
            backup: { keep: 0, remote: 'git@example.com:me/memory.git' }
            """);
        Directory.CreateDirectory(Path.Combine(_home.UserHome, "notes", "memory", ".git"));

        var config = _home.Tacit().LoadConfig();

        Assert.Equal(19999, config.Port);
        Assert.Equal(Path.Combine(_home.UserHome, "notes", "memory"), config.MemoryPath);
        Assert.Equal(MemoryLanguage.En, config.Language);
        Assert.Equal(new PersonaLimits(3, 20, 15, 10), config.Limits.Persona);
        Assert.Equal(80, config.Limits.ItemLength);
        Assert.Equal(new Threshold(0.9, 0.2), config.Decision.SamePage);
        Assert.Null(config.Decision.SamePreference);
        Assert.Equal(0.3, config.Retrieval.MinRerankScore);
        Assert.Equal(new BackupConfig(0, "git@example.com:me/memory.git"), config.Backup);
    }

    [Theory]
    [InlineData("server: { prot: 19999 }", "server.prot")]
    [InlineData("colour: blue", "colour")]
    [InlineData("server: { port: abc }", "server.port")]
    [InlineData("server: { port: 70000 }", "server.port")]
    [InlineData("server: 18888", "server")]
    [InlineData("language: fr", "language")]
    [InlineData("limits: { itemLength: -1 }", "limits.itemLength")]
    [InlineData("backup: { keep: 1.5 }", "backup.keep")]
    [InlineData("decision: { credential: { certain: 0.2, unlikely: 0.8 } }", "decision.credential")]
    [InlineData("decision: { credential: { certain: 1.5, unlikely: 0.1 } }", "decision.credential.certain")]
    [InlineData("retrieval: { minRerankScore: high }", "retrieval.minRerankScore")]
    public void A_wrong_setting_is_refused_with_its_key_path(string yaml, string keyPath)
    {
        WriteConfig(Models + yaml);

        var error = LoadFails();

        Assert.Equal(TacitError.ConfigInvalid, error.Error);
        Assert.Contains(keyPath, error.Message);
    }

    [Theory]
    [InlineData("endpoint: 'https://api.typesafe.ai/v1', model: jev-latest, apiKey: secret, apiKeyEnv: JEV_KEY")]
    [InlineData("endpoint: 'https://api.typesafe.ai/v1', model: jev-latest")]
    public void The_decision_model_needs_exactly_one_of_api_key_and_api_key_env(string decision)
    {
        WriteConfig(Models.Replace("endpoint: 'https://api.typesafe.ai/v1', model: jev-latest, apiKeyEnv: JEV_KEY", decision));

        var error = LoadFails();

        Assert.Contains("models.decision", error.Message);
        Assert.Contains("apiKeyEnv", error.Message);
    }

    [Fact]
    public void A_missing_model_section_is_refused()
    {
        WriteConfig(Models.Replace("  reranker: { endpoint: 'http://127.0.0.1:38417/v1', model: qwen3-reranker-0.6b }\n", ""));

        Assert.Contains("models.reranker", LoadFails().Message);
    }

    [Fact]
    public void All_errors_are_reported_at_once()
    {
        WriteConfig(Models + "server: { port: abc }\nlanguage: fr\n");

        var message = LoadFails().Message;

        Assert.Contains("server.port", message);
        Assert.Contains("language", message);
    }

    [Fact]
    public void Broken_yaml_is_refused_with_its_position()
    {
        WriteConfig(Models + "server: { port: 1\n");

        var error = LoadFails();

        Assert.Equal(TacitError.ConfigInvalid, error.Error);
        Assert.Contains("config.yaml", error.Message);
    }

    [Fact]
    public void Without_config_yaml_the_user_is_told_to_run_init()
    {
        File.Delete(ConfigPath);

        var error = LoadFails();

        Assert.Equal(TacitError.NotInitialized, error.Error);
        Assert.Contains("tacit init", error.Message);
    }

    [Fact]
    public void Without_the_memory_repository_the_user_is_told_to_run_init()
    {
        WriteConfig(Models + "memory: { path: '~/missing' }\n");

        var error = LoadFails();

        Assert.Equal(TacitError.NotInitialized, error.Error);
        Assert.Contains("tacit init", error.Message);
    }

    [Fact]
    public void The_recording_criteria_come_from_rules_md()
    {
        WriteConfig(Models);
        File.WriteAllText(RulesPath, "## 值得记\n- 用户纠正 Agent\n  补充说明\n- 环境事实\n\n## Not recorded\n- 常规改动\n");

        var rules = _home.Tacit().LoadConfig().Rules;

        Assert.Equal(["用户纠正 Agent", "环境事实"], rules.WorthRecording);
        Assert.Equal(["常规改动"], rules.NotRecorded);
    }

    [Fact]
    public void A_deleted_rules_md_is_written_again_from_the_template_in_the_memory_language()
    {
        WriteConfig(Models + "language: en\n");
        File.Delete(RulesPath);

        var rules = _home.Tacit().LoadConfig().Rules;

        Assert.StartsWith("## Worth recording\n", File.ReadAllText(RulesPath));
        Assert.Equal(5, rules.WorthRecording.Count);
        Assert.Equal(2, rules.NotRecorded.Count);
    }

    [Fact]
    public void A_rules_section_with_more_than_ten_items_is_refused_with_the_excess()
    {
        WriteConfig(Models);
        File.WriteAllText(RulesPath, "## 值得记\n" + string.Concat(Enumerable.Range(1, 12).Select(i => $"- 规则 {i}\n")) + "\n## 不记\n- 常规改动\n");

        var error = LoadFails();

        Assert.Equal(TacitError.RulesInvalid, error.Error);
        Assert.Contains("值得记", error.Message);
        Assert.Contains("2", error.Message);
    }
}
