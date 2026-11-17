
namespace TacitMemory.Core.Tests;

public sealed class InitTests : IDisposable
{
    private readonly TestHome _home = new();

    public void Dispose() => _home.Dispose();

    [Fact]
    public async Task Init_creates_the_config_and_a_committed_memory_repository_and_no_prompt_document()
    {
        var result = await _home.Tacit().InitAsync(new InitRequest { Language = MemoryLanguage.Zh });

        Assert.True(result.RepositoryCreated);
        Assert.Equal(_home.DefaultMemory, result.MemoryPath);
        Assert.True(File.Exists(Path.Combine(_home.TacitHome, "config.yaml")));
        Assert.False(File.Exists(Path.Combine(_home.TacitHome, "prompts", "rules.md")));

        var memory = _home.DefaultMemory;
        Assert.Equal(".cache/\n", File.ReadAllText(Path.Combine(memory, ".gitignore")));
        Assert.Equal("## Red lines\n\n## Working rules\n\n## Preferences\n\n## Profile\n", File.ReadAllText(Path.Combine(memory, "persona.md")));
        Assert.Equal("## Candidates\n\n## Rejection list\n", File.ReadAllText(Path.Combine(memory, "candidates.md")));
        Assert.Equal("## Workstream\n", File.ReadAllText(Path.Combine(memory, "workstreams.md")));
        Assert.Equal("language: zh\n", File.ReadAllText(Path.Combine(_home.TacitHome, "config.yaml")));

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

        var error = await Assert.ThrowsAsync<TacitException>(() => _home.Tacit().InitAsync(new InitRequest { Language = MemoryLanguage.Zh, MemoryPath = memory }));

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
        File.WriteAllText(persona, "## Preferences\n- 沟通：先要结论\n");
        File.Delete(Path.Combine(_home.DefaultMemory, "workstreams.md"));

        var result = await _home.Tacit().InitAsync(new InitRequest());

        Assert.False(result.RepositoryCreated);
        Assert.Equal("language: zh\n", File.ReadAllText(config));
        Assert.Equal("## Preferences\n- 沟通：先要结论\n", File.ReadAllText(persona));
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

        var result = await _home.Tacit().InitAsync(new InitRequest { Language = MemoryLanguage.Zh, MemoryPath = memory });

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
    [InlineData(MemoryLanguage.Zh, "## Red lines\n")]
    [InlineData(MemoryLanguage.En, "## Red lines\n")]
    public async Task Init_without_a_language_asks_for_one(MemoryLanguage answer, string personaStart)
    {
        var asked = 0;

        var result = await _home.Tacit().InitAsync(new InitRequest { AskLanguage = () => { asked++; return answer; } });

        Assert.Equal(1, asked);
        Assert.StartsWith(personaStart, File.ReadAllText(Path.Combine(result.MemoryPath, "persona.md")));
        Assert.Contains($"language: {answer.ToString().ToLowerInvariant()}\n", File.ReadAllText(Path.Combine(_home.TacitHome, "config.yaml")));
    }

    [Fact]
    public async Task Init_does_not_ask_when_the_request_or_the_config_names_the_language()
    {
        Func<MemoryLanguage> ask = () => throw new InvalidOperationException("asked");

        await _home.Tacit().InitAsync(new InitRequest { Language = MemoryLanguage.En, AskLanguage = ask });
        var again = await _home.Tacit().InitAsync(new InitRequest { AskLanguage = ask });

        Assert.Equal(MemoryLanguage.En, again.Language);
    }

    [Fact]
    public async Task Init_refuses_an_existing_config_without_a_language_and_changes_nothing()
    {
        Directory.CreateDirectory(_home.TacitHome);
        var configPath = Path.Combine(_home.TacitHome, "config.yaml");
        File.WriteAllText(configPath, "server:\n  port: 19001\n");

        var error = await Assert.ThrowsAsync<TacitException>(
            () => _home.Tacit().InitAsync(new InitRequest { Language = MemoryLanguage.En, AskLanguage = () => throw new InvalidOperationException("asked") }));

        Assert.Equal(TacitError.ConfigInvalid, error.Error);
        Assert.Contains("language", error.Message, StringComparison.Ordinal);
        Assert.Equal("server:\n  port: 19001\n", File.ReadAllText(configPath));
        Assert.False(Directory.Exists(Path.Combine(_home.TacitHome, "memory")));
    }

    [Fact]
    public async Task Init_refuses_a_memory_folder_that_is_not_empty_before_asking_for_the_language()
    {
        var memory = Path.Combine(_home.TacitHome, "memory");
        Directory.CreateDirectory(memory);
        File.WriteAllText(Path.Combine(memory, "notes.txt"), "mine");
        var asked = 0;

        var error = await Assert.ThrowsAsync<TacitException>(
            () => _home.Tacit().InitAsync(new InitRequest { AskLanguage = () => { asked++; return MemoryLanguage.Zh; } }));

        Assert.Equal(TacitError.MemoryDirectoryNotEmpty, error.Error);
        Assert.Equal(0, asked);
        Assert.StartsWith("The directory ", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Init_in_english_writes_english_templates_and_report()
    {
        var result = await _home.Tacit().InitAsync(new InitRequest { Language = MemoryLanguage.En });

        Assert.Equal("## Candidates\n\n## Rejection list\n", File.ReadAllText(Path.Combine(result.MemoryPath, "candidates.md")));
        Assert.Equal("language: en\n", File.ReadAllText(Path.Combine(_home.TacitHome, "config.yaml")));
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
