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
        Assert.Equal(new PersonaLimits(5, 15, 10, 10), config.Limits.Persona);
        Assert.Equal(200, config.Limits.ItemLength);
        Assert.Equal(5, config.Limits.Mainline);
        Assert.Equal(new PageLimits(50, 30, 2), config.Limits.Page);
        Assert.Equal(10, config.Limits.Domains);
        Assert.Equal(10, config.Limits.DomainEntries);
        Assert.Equal(60, config.Limits.TriggerLength);
        Assert.Equal(new ConsolidationConfig(7, 14, 20, 5), config.Consolidation);
        Assert.Equal(new DecisionThresholds(null, null, null, null), config.Decision);
        Assert.Equal(new RetrievalConfig(30, 5, null), config.Retrieval);
        Assert.Equal(new BackupConfig(7, null), config.Backup);
        Assert.Equal(new Uri("http://127.0.0.1:38417/v1/embeddings"), config.Models.Embedding!.Url);
        Assert.Equal("qwen3-reranker-0.6b", config.Models.Reranker!.Model);
        Assert.Equal(
            new ModelConfig(new Uri("https://api.typesafe.ai/v1/systemone"), "jev-latest", new SecretValue("test-key", SecretSource.ConfigFile, WrittenInConfig: true), ModelProvider.Generic),
            config.Models.Decision);
    }

    [Fact]
    public void The_template_written_by_init_loads_as_it_is_with_every_model_unconfigured()
    {
        var config = _home.Tacit().LoadConfig();

        Assert.Equal(_home.DefaultMemory, config.MemoryPath);
        Assert.Equal(new PersonaLimits(5, 15, 10, 10), config.Limits.Persona);
        Assert.Null(config.Models.Embedding);
        Assert.Null(config.Models.Reranker);
        Assert.Null(config.Models.Decision);
    }

    [Fact]
    public void The_template_has_its_models_section_commented_out_with_its_setting_names_kept()
    {
        var lines = File.ReadAllLines(ConfigPath);

        Assert.DoesNotContain(lines, line => line.StartsWith("models:", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("# models:", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("#     url:", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("#     model:", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("#     apiKey:", StringComparison.Ordinal));
    }

    [Fact]
    public void The_template_loads_once_models_are_filled_in_at_its_end()
    {
        WriteConfig(File.ReadAllText(ConfigPath) + "\n" + Models);

        var config = _home.Tacit().LoadConfig();

        Assert.Equal(new Uri("http://127.0.0.1:38417/v1/embeddings"), config.Models.Embedding!.Url);
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
        Assert.Equal(new PersonaLimits(3, 15, 10, 10), config.Limits.Persona);
        Assert.Equal(80, config.Limits.ItemLength);
        Assert.Equal(new Threshold(0.9, 0.2), config.Decision.SamePage);
        Assert.Null(config.Decision.SamePreference);
        Assert.Equal(0.3, config.Retrieval.MinRerankScore);
        Assert.Equal(new BackupConfig(0, new WebDavConfig(new Uri("https://dav.example/tacit/"), "me", new SecretValue("pw", SecretSource.ConfigFile, WrittenInConfig: true))), config.Backup);
    }

    [Fact]
    public void The_domain_limits_are_read_from_the_limits_section()
    {
        WriteConfig(Models + "limits: { domains: 3, domainEntries: 4, triggerLength: 20 }\n");

        var config = _home.Tacit().LoadConfig();

        Assert.Equal(3, config.Limits.Domains);
        Assert.Equal(4, config.Limits.DomainEntries);
        Assert.Equal(20, config.Limits.TriggerLength);
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
    public void The_endpoint_setting_is_an_unknown_setting_named_with_its_key_path()
    {
        WriteConfig(Models.Replace("url: 'http://127.0.0.1:38417/v1/embeddings'", "endpoint: 'http://127.0.0.1:38417/v1'"));

        var message = LoadFails().Message;

        Assert.Contains("models.embedding.endpoint", message);
        Assert.Contains("models.embedding.url", message);
    }

    [Theory]
    [InlineData("url: 'ftp://127.0.0.1/v1/embeddings'")]
    [InlineData("url: '/v1/embeddings'")]
    [InlineData("url: 'not a url'")]
    [InlineData("url: ''")]
    public void A_url_that_is_not_an_absolute_http_or_https_address_is_refused_with_its_key_path(string setting)
    {
        WriteConfig(Models.Replace("url: 'http://127.0.0.1:38417/v1/embeddings'", setting));

        var error = LoadFails();

        Assert.Equal(TacitError.ConfigInvalid, error.Error);
        Assert.Contains("models.embedding.url", error.Message);
    }

    [Fact]
    public void A_url_is_kept_exactly_as_written_with_its_trailing_slash_and_no_path_is_added()
    {
        WriteConfig(Models.Replace("http://127.0.0.1:38417/v1/embeddings", "http://127.0.0.1:38417/v1/embeddings/")
            .Replace("https://api.typesafe.ai/v1/systemone", "https://api.typesafe.ai/v1"));

        var config = _home.Tacit().LoadConfig();

        Assert.Equal("http://127.0.0.1:38417/v1/embeddings/", config.Models.Embedding!.Url.OriginalString);
        Assert.Equal("https://api.typesafe.ai/v1", config.Models.Decision!.Url.OriginalString);
    }

    [Fact]
    public void A_model_with_an_empty_model_name_is_refused_with_its_key_path()
    {
        WriteConfig(Models.Replace("model: qwen3-embedding-0.6b", "model: ''"));

        Assert.Contains("models.embedding.model", LoadFails().Message);
    }

    [Fact]
    public void A_decision_key_in_the_environment_overrides_the_config_and_both_are_recorded()
    {
        WriteConfig(Models);
        _home.EnvironmentVariables["TACIT_DECISION_API_KEY"] = "env-key";

        var decision = _home.Tacit().LoadConfig().Models.Decision!;

        Assert.Equal(new SecretValue("env-key", SecretSource.Environment, WrittenInConfig: true), decision.ApiKey);
    }

    [Fact]
    public void A_decision_key_only_in_the_environment_needs_no_entry_in_the_config()
    {
        WriteConfig(Models.Replace(", apiKey: test-key", ""));
        _home.EnvironmentVariables["TACIT_DECISION_API_KEY"] = "env-key";

        var decision = _home.Tacit().LoadConfig().Models.Decision!;

        Assert.Equal(new SecretValue("env-key", SecretSource.Environment, WrittenInConfig: false), decision.ApiKey);
    }

    [Fact]
    public void A_blank_environment_variable_does_not_override_the_config()
    {
        WriteConfig(Models);
        _home.EnvironmentVariables["TACIT_DECISION_API_KEY"] = "  ";

        var decision = _home.Tacit().LoadConfig().Models.Decision!;

        Assert.Equal(new SecretValue("test-key", SecretSource.ConfigFile, WrittenInConfig: true), decision.ApiKey);
    }

    [Fact]
    public void The_embedding_and_reranker_keys_come_from_their_own_variables_or_the_config()
    {
        WriteConfig(Models
            .Replace("model: qwen3-embedding-0.6b }", "model: qwen3-embedding-0.6b, apiKey: emb-config }")
            .Replace("model: qwen3-reranker-0.6b }", "model: qwen3-reranker-0.6b, apiKey: rer-config }"));
        _home.EnvironmentVariables["TACIT_EMBEDDING_API_KEY"] = "emb-env";

        var config = _home.Tacit().LoadConfig();

        Assert.Equal(new SecretValue("emb-env", SecretSource.Environment, WrittenInConfig: true), config.Models.Embedding!.ApiKey);
        Assert.Equal(new SecretValue("rer-config", SecretSource.ConfigFile, WrittenInConfig: true), config.Models.Reranker!.ApiKey);
    }

    [Fact]
    public void A_reranker_key_only_in_its_variable_needs_no_entry_in_the_config()
    {
        WriteConfig(Models);
        _home.EnvironmentVariables["TACIT_RERANKER_API_KEY"] = "rer-env";

        var config = _home.Tacit().LoadConfig();

        Assert.Equal(new SecretValue("rer-env", SecretSource.Environment, WrittenInConfig: false), config.Models.Reranker!.ApiKey);
    }

    [Fact]
    public void A_model_with_no_key_anywhere_loads_with_no_key()
    {
        WriteConfig(Models.Replace(", apiKey: test-key", ""));

        var config = _home.Tacit().LoadConfig();

        Assert.Null(config.Models.Decision!.ApiKey);
        Assert.Null(config.Models.Embedding!.ApiKey);
        Assert.Null(config.Models.Reranker!.ApiKey);
    }

    [Fact]
    public void A_model_without_a_provider_speaks_the_generic_protocol()
    {
        WriteConfig(Models);

        var config = _home.Tacit().LoadConfig();

        Assert.Equal(ModelProvider.Generic, config.Models.Embedding!.Provider);
        Assert.Equal(ModelProvider.Generic, config.Models.Reranker!.Provider);
        Assert.Equal(ModelProvider.Generic, config.Models.Decision!.Provider);
    }

    [Theory]
    [InlineData("cloudflare")]
    [InlineData("Cloudflare")]
    [InlineData("CLOUDFLARE")]
    public void A_provider_of_cloudflare_in_any_letter_case_selects_cloudflare(string provider)
    {
        WriteConfig(Models
            .Replace("model: qwen3-reranker-0.6b", $"model: qwen3-reranker-0.6b, provider: {provider}")
            .Replace("model: jev-latest", $"model: jev-latest, provider: {provider}"));

        var config = _home.Tacit().LoadConfig();

        Assert.Equal(ModelProvider.Cloudflare, config.Models.Reranker!.Provider);
        Assert.Equal(ModelProvider.Cloudflare, config.Models.Decision!.Provider);
    }

    [Fact]
    public void The_embedding_model_accepts_the_cloudflare_provider()
    {
        WriteConfig(Models.Replace("model: qwen3-embedding-0.6b", "model: qwen3-embedding-0.6b, provider: cloudflare"));

        var config = _home.Tacit().LoadConfig();

        Assert.Equal(ModelProvider.Cloudflare, config.Models.Embedding!.Provider);
        Assert.Equal(ModelProvider.Generic, config.Models.Reranker!.Provider);
    }

    [Theory]
    [InlineData("clouflare")]
    [InlineData("generic")]
    [InlineData("openai")]
    public void An_unknown_provider_is_refused_with_its_key_path_and_the_values_it_may_take(string provider)
    {
        WriteConfig(Models.Replace("model: qwen3-reranker-0.6b", $"model: qwen3-reranker-0.6b, provider: {provider}"));

        var message = LoadFails().Message;

        Assert.Contains("models.reranker.provider", message);
        Assert.Contains("cloudflare", message);
        Assert.Contains(provider, message);
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
    public void The_old_checkup_section_is_an_unknown_setting()
    {
        WriteConfig(Models + "checkup: { remindAfterDays: 7 }\n");

        var error = LoadFails();

        Assert.Equal(TacitError.ConfigInvalid, error.Error);
        Assert.Contains("checkup", error.Message);
    }

    [Fact]
    public void The_candidates_section_is_an_unknown_setting_and_its_key_path_is_named()
    {
        WriteConfig(Models + "candidates: { minDays: 2 }\n");

        var error = LoadFails();

        Assert.Equal(TacitError.ConfigInvalid, error.Error);
        Assert.Contains("candidates", error.Message);
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

        Assert.Contains(lines, line => line.Contains("apiKey:", StringComparison.Ordinal) && line.Contains("TACIT_DECISION_API_KEY", StringComparison.Ordinal) && line.Contains("可省略", StringComparison.Ordinal));
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

        Assert.Contains(lines, line => line.Contains("apiKey:", StringComparison.Ordinal) && line.Contains("TACIT_DECISION_API_KEY", StringComparison.Ordinal) && line.Contains("left out", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.TrimStart().StartsWith("# webdav:", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("TACIT_WEBDAV_PASSWORD", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("apiKeyEnv", StringComparison.Ordinal) || line.Contains("remote", StringComparison.Ordinal));
    }

    [Fact]
    public void The_templates_name_each_model_url_as_a_complete_request_address_and_its_key_variable()
    {
        var zh = File.ReadAllLines(ConfigPath);

        Assert.Contains(zh, line => line.Contains("url:", StringComparison.Ordinal) && line.Contains("完整请求地址", StringComparison.Ordinal));
        Assert.Contains(zh, line => line.Contains("TACIT_EMBEDDING_API_KEY", StringComparison.Ordinal));
        Assert.Contains(zh, line => line.Contains("TACIT_RERANKER_API_KEY", StringComparison.Ordinal));
        Assert.DoesNotContain(zh, line => line.Contains("endpoint", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_english_template_names_each_model_url_as_a_complete_request_address_and_its_key_variable()
    {
        using var home = new TestHome();
        await home.Tacit().InitAsync(new InitRequest { Language = MemoryLanguage.En });

        var en = File.ReadAllLines(Path.Combine(home.TacitHome, "config.yaml"));

        Assert.Contains(en, line => line.Contains("url:", StringComparison.Ordinal) && line.Contains("complete request address", StringComparison.Ordinal));
        Assert.Contains(en, line => line.Contains("TACIT_EMBEDDING_API_KEY", StringComparison.Ordinal));
        Assert.Contains(en, line => line.Contains("TACIT_RERANKER_API_KEY", StringComparison.Ordinal));
        Assert.DoesNotContain(en, line => line.Contains("endpoint", StringComparison.Ordinal));
    }

    [Fact]
    public void A_missing_models_section_loads_with_every_model_unconfigured()
    {
        WriteConfig("server: { port: 19999 }\n");

        var config = _home.Tacit().LoadConfig();

        Assert.Equal(19999, config.Port);
        Assert.Null(config.Models.Embedding);
        Assert.Null(config.Models.Reranker);
        Assert.Null(config.Models.Decision);
    }

    [Fact]
    public void An_empty_models_mapping_loads_with_every_model_unconfigured()
    {
        WriteConfig("models: {}\n");

        var config = _home.Tacit().LoadConfig();

        Assert.Null(config.Models.Embedding);
        Assert.Null(config.Models.Reranker);
        Assert.Null(config.Models.Decision);
    }

    [Fact]
    public void A_model_left_out_loads_as_unconfigured_while_the_others_stay_configured()
    {
        WriteConfig(Models.Replace("  reranker: { url: 'http://127.0.0.1:38417/v1/rerank', model: qwen3-reranker-0.6b }\n", ""));

        var config = _home.Tacit().LoadConfig();

        Assert.Null(config.Models.Reranker);
        Assert.Equal("qwen3-embedding-0.6b", config.Models.Embedding!.Model);
        Assert.NotNull(config.Models.Decision);
    }

    [Fact]
    public void A_model_with_a_url_but_no_model_name_is_refused_with_its_key_path()
    {
        WriteConfig("models:\n  embedding: { url: 'http://127.0.0.1:38417/v1/embeddings' }\n");

        Assert.Contains("models.embedding.model", LoadFails().Message);
    }

    [Fact]
    public void A_model_with_a_model_name_but_no_url_is_refused_with_its_key_path()
    {
        WriteConfig("models:\n  embedding: { model: qwen3-embedding-0.6b }\n");

        Assert.Contains("models.embedding.url", LoadFails().Message);
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
    public void Configured_consolidation_budget_and_concurrency_override_the_defaults()
    {
        WriteConfig(Models + "consolidation: { budgetSeconds: 5, maxConcurrency: 2 }\n");

        var config = _home.Tacit().LoadConfig();

        Assert.Equal(new ConsolidationConfig(7, 14, 5, 2), config.Consolidation);
    }

    [Theory]
    [InlineData("budgetSeconds", "0")]
    [InlineData("budgetSeconds", "-3")]
    [InlineData("budgetSeconds", "2.5")]
    [InlineData("maxConcurrency", "0")]
    [InlineData("maxConcurrency", "nine")]
    public void A_consolidation_budget_or_concurrency_that_is_not_a_positive_whole_number_is_refused(string key, string value)
    {
        WriteConfig(Models + $"consolidation: {{ {key}: {value} }}\n");

        var error = LoadFails();

        Assert.Equal(TacitError.ConfigInvalid, error.Error);
        Assert.Contains($"consolidation.{key}", error.Message);
    }

    [Fact]
    public void The_chinese_template_documents_the_consolidation_budget_and_concurrency()
    {
        var lines = File.ReadAllLines(ConfigPath);

        Assert.Contains(lines, line => line.TrimStart().StartsWith("budgetSeconds: 20", StringComparison.Ordinal) && line.Contains('#'));
        Assert.Contains(lines, line => line.TrimStart().StartsWith("maxConcurrency: 5", StringComparison.Ordinal) && line.Contains('#'));
    }

    [Fact]
    public async Task The_english_template_documents_the_consolidation_budget_and_concurrency()
    {
        using var home = new TestHome();
        await home.Tacit().InitAsync(new InitRequest { Language = MemoryLanguage.En });

        var lines = File.ReadAllLines(Path.Combine(home.TacitHome, "config.yaml"));

        Assert.Contains(lines, line => line.TrimStart().StartsWith("budgetSeconds: 20", StringComparison.Ordinal) && line.Contains("seconds", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.TrimStart().StartsWith("maxConcurrency: 5", StringComparison.Ordinal) && line.Contains("at once", StringComparison.Ordinal));
    }
}
