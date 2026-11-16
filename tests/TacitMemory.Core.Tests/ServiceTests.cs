using System.Collections.Concurrent;

namespace TacitMemory.Core.Tests;

public sealed class ServiceTests : IDisposable
{
    private readonly TestHome _home = new();
    private readonly ConcurrentQueue<string> _notices = new();

    public ServiceTests() => _home.InitReady();

    public void Dispose() => _home.Dispose();

    private string AgentsMd => Path.Combine(_home.UserHome, ".omp", "agent", "AGENTS.md");

    private string Pages => Path.Combine(_home.DefaultMemory, "pages");

    private string RulesMd => Path.Combine(_home.TacitHome, "prompts", "rules.md");

    private string ConfigYaml => Path.Combine(_home.TacitHome, "config.yaml");

    private MemoryService Open() => _home.Tacit().OpenService(_notices.Enqueue, NullLoggerFactory.Instance);

    [Fact]
    public async Task Server_instructions_hold_the_memory_rules_and_language_norms_but_no_workstreams()
    {
        Directory.CreateDirectory(Path.Combine(Pages, "tacit-memory"));
        Directory.CreateDirectory(Path.Combine(Pages, "my-app"));
        Directory.CreateDirectory(Path.Combine(Pages, "_topics"));

        using var service = Open();
        var instructions = await service.ServerInstructionsAsync();

        Assert.Contains("<user_memory>", instructions);
        Assert.Contains("## Using memory", instructions);
        Assert.Contains("## Write\n", instructions);
        Assert.Contains("Memory is written in Chinese.", instructions);
        Assert.DoesNotContain("## Workstreams", instructions);
        Assert.DoesNotContain("my-app", instructions);
        Assert.DoesNotContain("_topics", instructions);
        Assert.Contains("## Recall", instructions);
        Assert.Contains("`recall` first, then update the page", instructions);
        Assert.Contains("Write `recall` queries in Chinese too.", instructions);
    }

    [Fact]
    public async Task The_recording_criteria_of_rules_md_go_into_the_instructions_word_for_word()
    {
        File.WriteAllText(RulesMd, "## 值得记\n- 用户说「记住这个」时\n- 部署事故的根因\n\n## 不记\n- 日常闲聊\n");

        using var service = Open();

        Assert.Contains("## Worth recording\n- 用户说「记住这个」时\n- 部署事故的根因\n\n## Not recorded\n- 日常闲聊", await service.ServerInstructionsAsync());
    }

    [Fact]
    public async Task Editing_rules_md_while_running_changes_the_next_instructions()
    {
        using var service = Open();
        Assert.DoesNotContain("新加的一条", await service.ServerInstructionsAsync());

        File.WriteAllText(RulesMd, "## 值得记\n- 新加的一条\n\n## 不记\n- 日常闲聊\n");

        await Eventually(service, async () => (await service.ServerInstructionsAsync()).Contains("- 新加的一条", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(MemoryLanguage.Zh, "zh")]
    [InlineData(MemoryLanguage.En, "en")]
    public async Task A_freshly_initialized_home_starts_in_either_memory_language(MemoryLanguage language, string key)
    {
        using var home = new TestHome();
        home.InitReady($"language: {key}\n", language: language);

        using var service = home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

        Assert.Contains("## Worth recording", await service.ServerInstructionsAsync());
    }

    [Fact]
    public void Criteria_that_push_the_fixed_part_past_3000_characters_stop_the_service_and_say_by_how_much()
    {
        var item = new string('长', 200);
        File.WriteAllText(RulesMd, "## 值得记\n" + string.Concat(Enumerable.Repeat($"- {item}\n", 10)) + "## 不记\n- 日常闲聊\n");

        var error = Assert.Throws<TacitException>(() => Open());

        Assert.Equal(TacitError.PromptInvalid, error.Error);
        Assert.Contains("rules.md", error.Message);
        Assert.Matches(@"超出 \d+ 字符", error.Message);
    }

    [Fact]
    public async Task Too_long_criteria_saved_while_running_are_reported_and_the_previous_ones_kept()
    {
        using var service = Open();
        var before = await service.ServerInstructionsAsync();

        File.WriteAllText(RulesMd, "## 值得记\n" + string.Concat(Enumerable.Repeat($"- {new string('长', 200)}\n", 10)));

        await Eventually(service, () => _notices.Any(notice => notice.Contains("rules.md", StringComparison.Ordinal)));
        Assert.Equal(before, await service.ServerInstructionsAsync());
    }

    [Fact]
    public async Task Without_problems_there_is_no_status_part()
    {
        using var models = new ModelDouble();
        File.WriteAllText(ConfigYaml, models.ModelsYaml(decisionKey: "key").ReplaceLineEndings("\n"));

        using var service = Open();

        Assert.DoesNotContain("## Status", await service.ServerInstructionsAsync());
    }

    [Fact]
    public async Task A_model_that_failed_is_named_in_the_status_until_it_answers_again()
    {
        using var models = new ModelDouble { EmbeddingDown = true };
        File.WriteAllText(ConfigYaml, models.ModelsYaml(decisionKey: "key").ReplaceLineEndings("\n"));
        using var service = Open();

        await service.RecallAsync(new RecallRequest { Query = "部署" });
        Assert.Contains("## Status\n- Unavailable auxiliary models: embedding.", await service.ServerInstructionsAsync());

        models.EmbeddingDown = false;
        _home.Clock.Advance(TimeSpan.FromMinutes(1));
        await service.RecallAsync(new RecallRequest { Query = "部署" });
        Assert.DoesNotContain("## Status", await service.ServerInstructionsAsync());
    }

    [Fact]
    public async Task Page_files_that_are_not_utf8_are_named_in_the_status()
    {
        Directory.CreateDirectory(Path.Combine(Pages, "demo"));
        File.WriteAllBytes(Path.Combine(Pages, "demo", "gbk.md"), [0xD6, 0xD0, 0xCE, 0xC4, 0xFF, 0xFE]);

        using var service = Open();

        Assert.Contains("- Files not searchable because they are not UTF-8: pages/demo/gbk.md.", await service.ServerInstructionsAsync());
    }

    [Fact]
    public async Task Without_workstreams_the_workstreams_part_is_left_out()
    {
        using var service = Open();

        Assert.DoesNotContain("## Workstreams", await service.ServerInstructionsAsync());
    }

    [Fact]
    public async Task English_memory_language_is_named_in_the_language_norms()
    {
        File.AppendAllText(Path.Combine(_home.TacitHome, "config.yaml"), "language: en\n");

        using var service = Open();

        Assert.Contains("Memory is written in English.", await service.ServerInstructionsAsync());
    }

    [Fact]
    public void Starting_the_service_renders_every_binding()
    {
        _home.Bind();
        File.WriteAllText(Path.Combine(_home.DefaultMemory, "persona.md"), "## 偏好\n- 沟通：先要结论\n");

        using var service = Open();

        Assert.Equal(BlockState.Written, Assert.Single(service.StartupRender).State);
        Assert.Contains("- 沟通：先要结论\n", File.ReadAllText(AgentsMd));
    }

    [Fact]
    public void Starting_the_service_renders_a_context_file_two_clients_share_once()
    {
        _home.Bind();
        _home.Bind("beta");
        File.WriteAllText(Path.Combine(_home.DefaultMemory, "persona.md"), "## 偏好\n- 沟通：先要结论\n");

        using var service = Open();

        Assert.Equal(BlockState.Written, Assert.Single(service.StartupRender).State);
        Assert.Equal(1, File.ReadAllText(AgentsMd).Split("<!-- tacit:begin").Length - 1);
    }

    [Fact]
    public async Task Saving_persona_md_while_running_updates_the_resident_block()
    {
        _home.Bind();
        using var service = Open();

        File.WriteAllText(Path.Combine(_home.DefaultMemory, "persona.md"), "## 档案\n- 系统：Windows 11\n");

        await Eventually(service, () => File.ReadAllText(AgentsMd).Contains("- 系统：Windows 11\n"));
        await Eventually(service, () => _notices.Any(notice => notice.Contains("已写入常驻区块")));
    }

    [Fact]
    public async Task A_broken_bindings_json_is_reported_and_the_service_keeps_running()
    {
        _home.Bind();
        using var service = Open();
        File.WriteAllText(Path.Combine(_home.TacitHome, "bindings.json"), "{ not json");

        File.WriteAllText(Path.Combine(_home.DefaultMemory, "persona.md"), "## 档案\n- 系统：Windows 11\n");

        await Eventually(service, () => _notices.Any(notice => notice.Contains("bindings.json")));
        File.Delete(Path.Combine(_home.TacitHome, "bindings.json"));
        Assert.Contains("Memory is written in Chinese.", await service.ServerInstructionsAsync());
    }

    [Fact]
    public async Task A_bad_skill_md_saved_while_running_does_not_hold_back_a_valid_rules_md()
    {
        using var service = Open();
        File.WriteAllText(Path.Combine(_home.TacitHome, "prompts", "skill.md"), new string('长', 3001));
        File.WriteAllText(RulesMd, "## 值得记\n- 新加的一条\n\n## 不记\n- 日常闲聊\n");

        await Eventually(service, async () => (await service.ServerInstructionsAsync()).Contains("- 新加的一条", StringComparison.Ordinal));
        await Eventually(service, () => _notices.Any(notice => notice.Contains("skill.md", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Rules_md_and_instructions_md_that_exceed_the_limit_together_both_keep_their_previous_text()
    {
        Directory.CreateDirectory(Path.Combine(_home.TacitHome, "prompts"));
        File.WriteAllText(Path.Combine(_home.TacitHome, "prompts", "instructions.md"), "## 核心\n- 旧的核心规则\n");
        using var service = Open();
        var before = await service.ServerInstructionsAsync();

        File.WriteAllText(Path.Combine(_home.TacitHome, "prompts", "instructions.md"), "## 核心\n" + new string('核', 2000) + "\n");
        File.WriteAllText(RulesMd, "## 值得记\n" + string.Concat(Enumerable.Repeat($"- {new string('长', 90)}\n", 8)) + "## 不记\n- 日常闲聊\n");

        await Eventually(service, () => _notices.Any(notice => notice.Contains("rules.md", StringComparison.Ordinal)));
        Assert.Equal(before, await service.ServerInstructionsAsync());
    }

    [Fact]
    public async Task Deleting_the_prompts_folder_while_running_recreates_it_and_keeps_watching_it()
    {
        using var service = Open();
        Directory.Delete(Path.Combine(_home.TacitHome, "prompts"), recursive: true);

        await Eventually(service, () => Directory.Exists(Path.Combine(_home.TacitHome, "prompts")));
        File.WriteAllText(Path.Combine(_home.TacitHome, "prompts", "skill.md"), "Use Chinese page titles.");

        var skill = Path.Combine(_home.TacitHome, "skills", "tacit-memory", "SKILL.md");
        await Eventually(service, () => File.ReadAllText(skill).Contains("Use Chinese page titles.", StringComparison.Ordinal));
    }

    /// <summary>Waits until the condition holds, letting the service run the refreshes it has scheduled on the test clock.</summary>
    private static async Task Eventually(MemoryService service, Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (true)
        {
            await service.SettledAsync();
            if (condition())
                return;
            Assert.True(DateTime.UtcNow < deadline, "condition not met within 10 s");
            await Task.Delay(50);
        }
    }
    /// <summary>Waits until the condition holds, letting the service run the refreshes it has scheduled on the test clock.</summary>
    private static async Task Eventually(MemoryService service, Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (true)
        {
            await service.SettledAsync();
            if (await condition())
                return;
            Assert.True(DateTime.UtcNow < deadline, "condition not met within 10 s");
            await Task.Delay(50);
        }
    }

}
