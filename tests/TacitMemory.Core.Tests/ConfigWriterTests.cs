namespace TacitMemory.Core.Tests;

public sealed class ConfigWriterTests : IDisposable
{
    private readonly TestHome _home = new();

    public ConfigWriterTests() => _home.Tacit().InitAsync(new InitRequest { Language = MemoryLanguage.Zh }).GetAwaiter().GetResult();

    public void Dispose() => _home.Dispose();

    private string ConfigPath => Path.Combine(_home.TacitHome, "config.yaml");

    private void WriteConfig(string yaml) => File.WriteAllText(ConfigPath, yaml.ReplaceLineEndings("\n"));

    [Fact]
    public void A_config_read_and_saved_again_loads_to_the_same_settings()
    {
        WriteConfig(TestHome.Models + """
            server: { port: 19999, host: 192.168.1.10 }
            language: zh
            limits: { brief: 1500, page: { minAliases: 3 } }
            consolidation: { remindAfterDays: 5, hintEntryLength: 120 }
            decision: { samePage: { certain: 0.9, unlikely: 0.2 } }
            retrieval: { topK: 8, minRerankScore: 0.3 }
            backup: { keep: 0, webdav: { url: 'https://dav.example/tacit/', username: me, password: pw } }
            """);
        var before = _home.Tacit().LoadConfig();

        _home.Tacit().SaveConfig(_home.Tacit().ReadConfigFile());

        Assert.Equal(Normalized(before), Normalized(_home.Tacit().LoadConfig()));
    }

    [Fact]
    public void Only_the_keys_that_differ_from_their_default_are_written_in_the_reference_order_without_comments()
    {
        var file = new ConfigFile
        {
            Backup = new BackupFile(Keep: 3, WebDav: new WebDavFile("https://dav.example/tacit/", "me", null)),
            Retrieval = new RetrievalFile(Candidates: 30, TopK: 8),
            Language = "en",
            Server = new ServerFile(Port: 18888, Host: "0.0.0.0"),
            Memory = new MemoryFile(Path.Combine(_home.TacitHome, "memory")),
            Models = new ModelsFile(Reranker: new ModelFile("cloudflare", "https://api.cloudflare.com/run/rerank", "bge-reranker", "it's")),
            Limits = new LimitsFile(Brief: 3000, Page: new PageLimitsFile(MinAliases: 3)),
            Decision = new DecisionFile(SameTopic: new ThresholdFile(0.95, 0.05)),
        };
        _home.EnvironmentVariables["TACIT_WEBDAV_PASSWORD"] = "env-pw";

        _home.Tacit().SaveConfig(file);

        Assert.Equal(
            """
            server:
              host: '0.0.0.0'
            language: en
            models:
              reranker:
                provider: cloudflare
                url: https://api.cloudflare.com/run/rerank
                model: bge-reranker
                apiKey: 'it''s'
            limits:
              page:
                minAliases: 3
            decision:
              sameTopic:
                certain: 0.95
                unlikely: 0.05
            retrieval:
              topK: 8
            backup:
              keep: 3
              webdav:
                url: https://dav.example/tacit/
                username: me

            """.ReplaceLineEndings("\n"),
            File.ReadAllText(ConfigPath));
    }

    [Fact]
    public void A_config_that_does_not_validate_is_refused_with_every_wrong_key_path_and_the_file_is_left_as_it_was()
    {
        var before = File.ReadAllText(ConfigPath);
        var file = _home.Tacit().ReadConfigFile() with
        {
            Server = new ServerFile(Port: 70000),
            Models = new ModelsFile(Embedding: new ModelFile(null, "not a url", "", null)),
        };

        var error = Assert.Throws<TacitException>(() => _home.Tacit().SaveConfig(file));

        Assert.Equal(TacitError.ConfigInvalid, error.Error);
        Assert.Contains("server.port", error.Message);
        Assert.Contains("models.embedding.url", error.Message);
        Assert.Contains("models.embedding.model", error.Message);
        Assert.Equal(before, File.ReadAllText(ConfigPath));
        Assert.False(File.Exists(ConfigPath + ".tmp"));
    }

    /// <summary>The settings without the prompt documents, which are records holding lists and so never compare equal.</summary>
    private static TacitConfig Normalized(TacitConfig config) => config with { Prompts = null! };

    [Fact]
    public void Machine_name_is_omitted_when_default_and_written_when_custom_with_or_without_description()
    {
        _home.HostName = "Work-Box.local";
        var defaults = _home.Tacit().ReadConfigFile() with { Machine = new MachineFile("work-box", null) };
        _home.Tacit().SaveConfig(defaults);
        Assert.DoesNotContain("machine:", File.ReadAllText(ConfigPath));

        var custom = defaults with { Machine = new MachineFile("mac-mini", "常驻服务器") };
        _home.Tacit().SaveConfig(custom);
        var rendered = File.ReadAllText(ConfigPath);
        Assert.Contains("machine:\n  name: mac-mini\n  description: '常驻服务器'\n", rendered.ReplaceLineEndings("\n"));
        Assert.Equal(new MachineConfig("mac-mini", "常驻服务器"), _home.Tacit().LoadConfig().Machine);
    }
}
