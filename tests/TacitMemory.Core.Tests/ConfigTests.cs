namespace TacitMemory.Core.Tests;

public sealed class ConfigTests : IDisposable
{
    private const string Models = TestHome.Models;

    private readonly TestHome _home = new();

    public ConfigTests() => _home.Tacit().InitAsync(new InitRequest { Language = MemoryLanguage.Zh }).GetAwaiter().GetResult();

    public void Dispose() => _home.Dispose();

    private string ConfigPath => Path.Combine(_home.TacitHome, "config.yaml");

    private string RulesPath => Path.Combine(_home.TacitHome, "prompts", "rules.md");

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
        Assert.Equal(200, config.Limits.ItemLength);
        Assert.Equal(5, config.Limits.Mainline);
        Assert.Equal(new PageLimits(50, 30, 2), config.Limits.Page);
        Assert.Equal(2, config.CandidateMinDays);
        Assert.Equal(new CheckupConfig(7, 14, 20, 5), config.Checkup);
        Assert.Equal(new DecisionThresholds(null, null, null, null), config.Decision);
        Assert.Equal(new RetrievalConfig(30, 5, null), config.Retrieval);
        Assert.Equal(new BackupConfig(7, null), config.Backup);
        Assert.Equal(new Uri("http://127.0.0.1:38417/v1"), config.Models.Embedding.Endpoint);
        Assert.Equal("qwen3-reranker-0.6b", config.Models.Reranker.Model);
        Assert.Equal(new DecisionModelEndpoint(new Uri("https://api.typesafe.ai/v1"), "jev-latest", new SecretValue("test-key", SecretSource.ConfigFile, WrittenInConfig: true)), config.Models.Decision);
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
            backup: { keep: 0, webdav: { url: 'https://dav.example/tacit', username: me, password: pw } }
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
        Assert.Equal(new BackupConfig(0, new WebDavConfig(new Uri("https://dav.example/tacit/"), "me", new SecretValue("pw", SecretSource.ConfigFile, WrittenInConfig: true))), config.Backup);
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
    [InlineData("decision: { samePage: { certain: 0.2, unlikely: 0.8 } }", "decision.samePage")]
    [InlineData("decision: { samePage: { certain: 1.5, unlikely: 0.1 } }", "decision.samePage.certain")]
    [InlineData("retrieval: { minRerankScore: high }", "retrieval.minRerankScore")]
    public void A_wrong_setting_is_refused_with_its_key_path(string yaml, string keyPath)
    {
        WriteConfig(Models + yaml);

        var error = LoadFails();

        Assert.Equal(TacitError.ConfigInvalid, error.Error);
        Assert.Contains(keyPath, error.Message);
    }

    [Fact]
    public void A_decision_key_in_the_environment_overrides_the_config_and_both_are_recorded()
    {
        WriteConfig(Models);
        _home.EnvironmentVariables["TACIT_DECISION_API_KEY"] = "env-key";

        var decision = _home.Tacit().LoadConfig().Models.Decision;

        Assert.Equal(new SecretValue("env-key", SecretSource.Environment, WrittenInConfig: true), decision.ApiKey);
    }

    [Fact]
    public void A_decision_key_only_in_the_environment_needs_no_entry_in_the_config()
    {
        WriteConfig(Models.Replace(", apiKey: test-key", ""));
        _home.EnvironmentVariables["TACIT_DECISION_API_KEY"] = "env-key";

        var decision = _home.Tacit().LoadConfig().Models.Decision;

        Assert.Equal(new SecretValue("env-key", SecretSource.Environment, WrittenInConfig: false), decision.ApiKey);
    }

    [Fact]
    public void A_blank_environment_variable_does_not_override_the_config()
    {
        WriteConfig(Models);
        _home.EnvironmentVariables["TACIT_DECISION_API_KEY"] = "  ";

        var decision = _home.Tacit().LoadConfig().Models.Decision;

        Assert.Equal(new SecretValue("test-key", SecretSource.ConfigFile, WrittenInConfig: true), decision.ApiKey);
    }

    [Fact]
    public void A_decision_model_with_no_key_anywhere_is_refused_naming_the_key_path_and_the_variable()
    {
        WriteConfig(Models.Replace(", apiKey: test-key", ""));

        var message = LoadFails().Message;

        Assert.Contains("models.decision.apiKey", message);
        Assert.Contains("TACIT_DECISION_API_KEY", message);
    }

    [Fact]
    public void The_secret_value_never_shows_in_its_text_form()
    {
        var secret = new SecretValue("sk-visible-value", SecretSource.ConfigFile, WrittenInConfig: true);

        Assert.DoesNotContain("sk-visible-value", secret.ToString());
    }

    [Fact]
    public void The_removed_api_key_env_setting_is_an_unknown_setting()
    {
        WriteConfig(Models.Replace("apiKey: test-key", "apiKeyEnv: JEV_KEY"));

        Assert.Contains("models.decision.apiKeyEnv", LoadFails().Message);
    }

    [Fact]
    public void The_removed_backup_remote_setting_is_an_unknown_setting()
    {
        WriteConfig(Models + "backup: { remote: 'git@example.com:me/memory.git' }\n");

        Assert.Contains("backup.remote", LoadFails().Message);
    }

    [Fact]
    public void Without_a_username_webdav_needs_no_password_and_the_url_becomes_a_directory()
    {
        WriteConfig(Models + "backup: { webdav: { url: 'https://dav.example/tacit' } }\n");

        var webdav = _home.Tacit().LoadConfig().Backup.WebDav;

        Assert.Equal(new WebDavConfig(new Uri("https://dav.example/tacit/"), null, null), webdav);
    }

    [Fact]
    public void A_webdav_username_without_any_password_is_refused_naming_the_key_path_and_the_variable()
    {
        WriteConfig(Models + "backup: { webdav: { url: 'https://dav.example/tacit/', username: me } }\n");

        var message = LoadFails().Message;

        Assert.Contains("backup.webdav.password", message);
        Assert.Contains("TACIT_WEBDAV_PASSWORD", message);
    }

    [Fact]
    public void The_webdav_password_comes_from_the_environment_when_it_is_set()
    {
        WriteConfig(Models + "backup: { webdav: { url: 'https://dav.example/tacit/', username: me, password: pw } }\n");
        _home.EnvironmentVariables["TACIT_WEBDAV_PASSWORD"] = "env-pw";

        var password = _home.Tacit().LoadConfig().Backup.WebDav!.Password;

        Assert.Equal(new SecretValue("env-pw", SecretSource.Environment, WrittenInConfig: true), password);
    }

    [Theory]
    [InlineData("backup: { webdav: { username: me, password: pw } }", "backup.webdav.url")]
    [InlineData("backup: { webdav: { url: 'ftp://dav.example/tacit/' } }", "backup.webdav.url")]
    [InlineData("backup: { webdav: { url: 'https://dav.example/tacit/', token: x } }", "backup.webdav.token")]
    public void A_webdav_section_is_checked_key_by_key(string yaml, string keyPath)
    {
        WriteConfig(Models + yaml + "\n");

        var error = LoadFails();

        Assert.Equal(TacitError.ConfigInvalid, error.Error);
        Assert.Contains(keyPath, error.Message);
    }

    [Fact]
    public void The_chinese_template_names_the_decision_key_variable_and_documents_webdav()
    {
        var lines = File.ReadAllLines(ConfigPath);

        Assert.Contains(lines, line => line.TrimStart().StartsWith("apiKey:", StringComparison.Ordinal) && line.Contains("TACIT_DECISION_API_KEY", StringComparison.Ordinal) && line.Contains("可省略", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.TrimStart().StartsWith("# webdav:", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("TACIT_WEBDAV_PASSWORD", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("apiKeyEnv", StringComparison.Ordinal) || line.Contains("remote", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_english_template_names_the_decision_key_variable_and_documents_webdav()
    {
        using var home = new TestHome();
        await home.Tacit().InitAsync(new InitRequest { Language = MemoryLanguage.En });

        var lines = File.ReadAllLines(Path.Combine(home.TacitHome, "config.yaml"));

        Assert.Contains(lines, line => line.TrimStart().StartsWith("apiKey:", StringComparison.Ordinal) && line.Contains("TACIT_DECISION_API_KEY", StringComparison.Ordinal) && line.Contains("left out", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.TrimStart().StartsWith("# webdav:", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("TACIT_WEBDAV_PASSWORD", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("apiKeyEnv", StringComparison.Ordinal) || line.Contains("remote", StringComparison.Ordinal));
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

        var rules = _home.Tacit().LoadConfig().Prompts.Rules;

        Assert.Equal(["用户纠正 Agent", "环境事实"], rules.WorthRecording);
        Assert.Equal(["常规改动"], rules.NotRecorded);
    }

    [Fact]
    public void A_deleted_rules_md_is_written_again_from_the_template_in_the_memory_language()
    {
        WriteConfig(Models + "language: en\n");
        File.Delete(RulesPath);

        var rules = _home.Tacit().LoadConfig().Prompts.Rules;

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

        Assert.Equal(TacitError.PromptInvalid, error.Error);
        Assert.Contains("值得记", error.Message);
        Assert.Contains("2", error.Message);
    }

    [Fact]
    public void Configured_checkup_budget_and_concurrency_override_the_defaults()
    {
        WriteConfig(Models + "checkup: { budgetSeconds: 5, maxConcurrency: 2 }\n");

        var config = _home.Tacit().LoadConfig();

        Assert.Equal(new CheckupConfig(7, 14, 5, 2), config.Checkup);
    }

    [Theory]
    [InlineData("budgetSeconds", "0")]
    [InlineData("budgetSeconds", "-3")]
    [InlineData("budgetSeconds", "2.5")]
    [InlineData("maxConcurrency", "0")]
    [InlineData("maxConcurrency", "nine")]
    public void A_checkup_budget_or_concurrency_that_is_not_a_positive_whole_number_is_refused(string key, string value)
    {
        WriteConfig(Models + $"checkup: {{ {key}: {value} }}\n");

        var error = LoadFails();

        Assert.Equal(TacitError.ConfigInvalid, error.Error);
        Assert.Contains($"checkup.{key}", error.Message);
    }

    [Fact]
    public void The_chinese_template_documents_the_checkup_budget_and_concurrency()
    {
        var lines = File.ReadAllLines(ConfigPath);

        Assert.Contains(lines, line => line.TrimStart().StartsWith("budgetSeconds: 20", StringComparison.Ordinal) && line.Contains('#'));
        Assert.Contains(lines, line => line.TrimStart().StartsWith("maxConcurrency: 5", StringComparison.Ordinal) && line.Contains('#'));
    }

    [Fact]
    public async Task The_english_template_documents_the_checkup_budget_and_concurrency()
    {
        using var home = new TestHome();
        await home.Tacit().InitAsync(new InitRequest { Language = MemoryLanguage.En });

        var lines = File.ReadAllLines(Path.Combine(home.TacitHome, "config.yaml"));

        Assert.Contains(lines, line => line.TrimStart().StartsWith("budgetSeconds: 20", StringComparison.Ordinal) && line.Contains("seconds", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.TrimStart().StartsWith("maxConcurrency: 5", StringComparison.Ordinal) && line.Contains("at once", StringComparison.Ordinal));
    }
}
