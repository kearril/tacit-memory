namespace TacitMemory.Core.Tests;

public sealed class InitTests : IDisposable
{
    private readonly TestHome _home = new();

    public void Dispose() => _home.Dispose();

    [Fact]
    public async Task Init_creates_config_rules_and_a_committed_memory_repository()
    {
        var result = await _home.Tacit().InitAsync(new InitRequest { Language = MemoryLanguage.Zh });

        Assert.True(result.RepositoryCreated);
        Assert.Equal(_home.DefaultMemory, result.MemoryPath);
        Assert.True(File.Exists(Path.Combine(_home.TacitHome, "config.yaml")));
        Assert.StartsWith("## 值得记\n", File.ReadAllText(Path.Combine(_home.TacitHome, "prompts", "rules.md")));

        var memory = _home.DefaultMemory;
        Assert.Equal(".cache/\n", File.ReadAllText(Path.Combine(memory, ".gitignore")));
        Assert.Equal("## 红线\n\n## 协作规则\n\n## 偏好\n\n## 档案\n", File.ReadAllText(Path.Combine(memory, "persona.md")));
        Assert.Equal("## 候选\n\n## 拒绝列表\n", File.ReadAllText(Path.Combine(memory, "candidates.md")));
        Assert.Equal("## 主线\n", File.ReadAllText(Path.Combine(memory, "workstreams.md")));
        Assert.Contains("hintItemLength: 200", File.ReadAllText(Path.Combine(_home.TacitHome, "config.yaml")));

        Assert.Equal("tacit <tacit@localhost>|init: create memory repo\n", TestHome.Git(memory, "log", "--format=%an <%ae>|%s"));
        var committed = TestHome.Git(memory, "ls-files").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal([".gitignore", "candidates.md", "pages/.gitkeep", "persona.md", "recent/.gitkeep", "workstreams.md"], committed.Order());
        Assert.Equal("", TestHome.Git(memory, "status", "--porcelain"));
    }

    [Fact]
    public async Task Init_refuses_a_non_empty_directory_that_is_not_a_git_repository()
    {
        var memory = _home.PathFor("notes");
        Directory.CreateDirectory(memory);
        File.WriteAllText(Path.Combine(memory, "todo.txt"), "milk");

        var error = await Assert.ThrowsAsync<TacitException>(() => _home.Tacit().InitAsync(new InitRequest { MemoryPath = memory }));

        Assert.Equal(TacitError.MemoryDirectoryNotEmpty, error.Error);
        Assert.Contains(memory, error.Message);
        Assert.Equal(["todo.txt"], Directory.EnumerateFileSystemEntries(memory).Select(Path.GetFileName));
        Assert.False(Directory.Exists(_home.TacitHome));
    }

    [Fact]
    public async Task Init_again_keeps_edited_files_and_only_restores_missing_skeleton_without_committing()
    {
        await _home.Tacit().InitAsync(new InitRequest { Language = MemoryLanguage.Zh });
        var config = Path.Combine(_home.TacitHome, "config.yaml");
        var persona = Path.Combine(_home.DefaultMemory, "persona.md");
        File.WriteAllText(config, "language: zh\n");
        File.WriteAllText(persona, "## 偏好\n- 沟通：先要结论\n");
        File.Delete(Path.Combine(_home.DefaultMemory, "workstreams.md"));

        var result = await _home.Tacit().InitAsync(new InitRequest());

        Assert.False(result.RepositoryCreated);
        Assert.Equal("language: zh\n", File.ReadAllText(config));
        Assert.Equal("## 偏好\n- 沟通：先要结论\n", File.ReadAllText(persona));
        Assert.Equal([Path.Combine(_home.DefaultMemory, "workstreams.md")], result.Created);
        Assert.Contains(config, result.Existing);
        Assert.Contains(persona, result.Existing);
        Assert.Single(TestHome.Git(_home.DefaultMemory, "log", "--format=%s").Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(" M persona.md\n", TestHome.Git(_home.DefaultMemory, "status", "--porcelain"));
    }

    [Fact]
    public async Task Init_into_an_empty_directory_given_by_memory_records_it_in_the_config()
    {
        var memory = Path.Combine(_home.PathFor("sync"), "记忆");
        Directory.CreateDirectory(memory);

        var result = await _home.Tacit().InitAsync(new InitRequest { MemoryPath = memory });

        Assert.True(result.RepositoryCreated);
        Assert.True(File.Exists(Path.Combine(memory, "persona.md")));
        Assert.False(Directory.Exists(_home.DefaultMemory));
        Assert.Contains($"  path: '{memory}'\n", File.ReadAllText(Path.Combine(_home.TacitHome, "config.yaml")));

        var again = await _home.Tacit().InitAsync(new InitRequest());
        Assert.Equal(memory, again.MemoryPath);
        Assert.False(again.RepositoryCreated);
    }

    [Fact]
    public async Task Init_refuses_options_that_contradict_the_existing_config()
    {
        await _home.Tacit().InitAsync(new InitRequest { Language = MemoryLanguage.Zh });

        var error = await Assert.ThrowsAsync<TacitException>(
            () => _home.Tacit().InitAsync(new InitRequest { MemoryPath = _home.PathFor("elsewhere") }));

        Assert.Equal(TacitError.ConflictsWithConfig, error.Error);
        Assert.False(Directory.Exists(_home.PathFor("elsewhere")));
    }

    [Theory]
    [InlineData("zh-CN", "## 红线\n")]
    [InlineData("zh-TW", "## 红线\n")]
    [InlineData("en-US", "## Red lines\n")]
    [InlineData("ja-JP", "## Red lines\n")]
    public async Task Init_without_a_language_follows_the_system_ui_language(string uiCulture, string personaStart)
    {
        var result = await _home.Tacit(uiCulture).InitAsync(new InitRequest());

        Assert.StartsWith(personaStart, File.ReadAllText(Path.Combine(result.MemoryPath, "persona.md")));
        Assert.Contains($"language: {(personaStart.Contains("红线") ? "zh" : "en")} ", File.ReadAllText(Path.Combine(_home.TacitHome, "config.yaml")));
    }

    [Fact]
    public async Task Init_in_english_writes_english_templates_and_report()
    {
        var result = await _home.Tacit().InitAsync(new InitRequest { Language = MemoryLanguage.En });

        Assert.StartsWith("## Worth recording\n", File.ReadAllText(Path.Combine(_home.TacitHome, "prompts", "rules.md")));
        Assert.Equal("## Candidates\n\n## Rejection list\n", File.ReadAllText(Path.Combine(result.MemoryPath, "candidates.md")));
        Assert.StartsWith("# TacitMemory configuration.", File.ReadAllText(Path.Combine(_home.TacitHome, "config.yaml")));
        Assert.Contains("hintItemLength: 200", File.ReadAllText(Path.Combine(_home.TacitHome, "config.yaml")));
        Assert.StartsWith("Created the memory repository: ", result.Report);
    }

    [Theory]
    [InlineData(MemoryLanguage.Zh, "启动服务后，可在浏览器打开 Web 界面继续配置：http://127.0.0.1:18888/")]
    [InlineData(MemoryLanguage.En, "Once the service is running, continue the setup in the Web interface: http://127.0.0.1:18888/")]
    public async Task Init_ends_with_the_web_interface_address(MemoryLanguage language, string lastLine)
    {
        var result = await _home.Tacit().InitAsync(new InitRequest { Language = language });

        Assert.Equal(lastLine, result.Report.Split('\n')[^1]);
    }

    [Fact]
    public async Task Init_gives_the_web_interface_address_on_the_port_of_an_existing_config()
    {
        Directory.CreateDirectory(_home.TacitHome);
        File.WriteAllText(Path.Combine(_home.TacitHome, "config.yaml"), "language: en\nserver:\n  port: 19001\n");

        var result = await _home.Tacit().InitAsync(new InitRequest());

        Assert.EndsWith("http://127.0.0.1:19001/", result.Report);
    }
}
