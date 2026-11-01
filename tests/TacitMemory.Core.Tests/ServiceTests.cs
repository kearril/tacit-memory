using System.Collections.Concurrent;
using TacitMemory.Core.Injection;

namespace TacitMemory.Core.Tests;

public sealed class ServiceTests : IDisposable
{
    private readonly TestHome _home = new();
    private readonly ConcurrentQueue<string> _notices = new();

    public ServiceTests() => _home.InitReady();

    public void Dispose() => _home.Dispose();

    private string AgentsMd => Path.Combine(_home.UserHome, ".omp", "agent", "AGENTS.md");

    private string Pages => Path.Combine(_home.DefaultMemory, "pages");

    private string RulesMd => Path.Combine(_home.TacitHome, "rules.md");

    private string ConfigYaml => Path.Combine(_home.TacitHome, "config.yaml");

    private MemoryService Open() => _home.Tacit().OpenService(_notices.Enqueue);

    [Fact]
    public void Server_instructions_hold_the_memory_rules_language_norms_and_projects()
    {
        Directory.CreateDirectory(Path.Combine(Pages, "tacit-memory"));
        Directory.CreateDirectory(Path.Combine(Pages, "my-app"));
        Directory.CreateDirectory(Path.Combine(Pages, "_topics"));

        using var service = Open();
        var instructions = service.ServerInstructions;

        Assert.Contains("<user_memory>", instructions);
        Assert.Contains("## Using memory", instructions);
        Assert.Contains("Memory is written in Chinese.", instructions);
        Assert.EndsWith("## Projects\nmy-app, tacit-memory", instructions);
        Assert.DoesNotContain("_topics", instructions);
        Assert.Contains("## When to recall", instructions);
        Assert.Contains("## Writing pages", instructions);
        Assert.Contains("Write `recall` queries in Chinese too.", instructions);
    }

    [Fact]
    public void The_recording_criteria_of_rules_md_go_into_the_instructions_word_for_word()
    {
        File.WriteAllText(RulesMd, "## 值得记\n- 用户说「记住这个」时\n- 部署事故的根因\n\n## 不记\n- 日常闲聊\n");

        using var service = Open();

        Assert.Contains("## Worth recording\n- 用户说「记住这个」时\n- 部署事故的根因\n\n## Not recorded\n- 日常闲聊", service.ServerInstructions);
    }

    [Fact]
    public async Task Editing_rules_md_while_running_changes_the_next_instructions()
    {
        using var service = Open();
        Assert.DoesNotContain("新加的一条", service.ServerInstructions);

        File.WriteAllText(RulesMd, "## 值得记\n- 新加的一条\n\n## 不记\n- 日常闲聊\n");

        await Eventually(() => service.ServerInstructions.Contains("- 新加的一条", StringComparison.Ordinal));
    }

    [Fact]
    public void Criteria_that_push_the_fixed_part_past_2500_characters_stop_the_service_and_say_by_how_much()
    {
        var item = new string('长', 200);
        File.WriteAllText(RulesMd, "## 值得记\n" + string.Concat(Enumerable.Repeat($"- {item}\n", 10)) + "## 不记\n- 日常闲聊\n");

        var error = Assert.Throws<TacitException>(() => Open());

        Assert.Equal(TacitError.RulesInvalid, error.Error);
        Assert.Contains("rules.md", error.Message);
        Assert.Matches(@"超出 \d+ 字符", error.Message);
    }

    [Fact]
    public async Task Too_long_criteria_saved_while_running_are_reported_and_the_previous_ones_kept()
    {
        using var service = Open();
        var before = service.ServerInstructions;

        File.WriteAllText(RulesMd, "## 值得记\n" + string.Concat(Enumerable.Repeat($"- {new string('长', 200)}\n", 10)));

        await Eventually(() => _notices.Any(notice => notice.Contains("rules.md", StringComparison.Ordinal)));
        Assert.Equal(before, service.ServerInstructions);
    }

    [Fact]
    public void Without_problems_there_is_no_status_part()
    {
        using var models = new ModelDouble();
        File.WriteAllText(ConfigYaml, models.ModelsYaml(decisionKey: "key").ReplaceLineEndings("\n"));

        using var service = Open();

        Assert.DoesNotContain("## Status", service.ServerInstructions);
    }

    [Fact]
    public async Task A_model_that_failed_is_named_in_the_status_until_it_answers_again()
    {
        using var models = new ModelDouble { EmbeddingDown = true };
        File.WriteAllText(ConfigYaml, models.ModelsYaml(decisionKey: "key").ReplaceLineEndings("\n"));
        using var service = Open();

        await service.RecallAsync(new RecallRequest { Query = "部署" });
        Assert.Contains("## Status\n- Unavailable auxiliary models: embedding.", service.ServerInstructions);

        models.EmbeddingDown = false;
        _home.Clock.Advance(TimeSpan.FromMinutes(1));
        await service.RecallAsync(new RecallRequest { Query = "部署" });
        Assert.DoesNotContain("## Status", service.ServerInstructions);
    }

    [Fact]
    public void A_decision_model_without_a_key_is_named_from_the_start()
    {
        using var models = new ModelDouble();
        File.WriteAllText(ConfigYaml, models.ModelsYaml().ReplaceLineEndings("\n"));

        using var service = Open();

        Assert.Contains("- Unavailable auxiliary models: decision.", service.ServerInstructions);
    }

    [Fact]
    public void Page_files_that_are_not_utf8_are_named_in_the_status()
    {
        Directory.CreateDirectory(Path.Combine(Pages, "demo"));
        File.WriteAllBytes(Path.Combine(Pages, "demo", "gbk.md"), [0xD6, 0xD0, 0xCE, 0xC4, 0xFF, 0xFE]);

        using var service = Open();

        Assert.Contains("- Pages not searchable because they are not UTF-8: pages/demo/gbk.md.", service.ServerInstructions);
    }

    [Fact]
    public void Without_projects_the_projects_part_is_left_out()
    {
        using var service = Open();

        Assert.DoesNotContain("## Projects", service.ServerInstructions);
    }

    [Fact]
    public void Too_many_projects_are_cut_from_the_end_and_the_rules_stay_whole()
    {
        var names = Enumerable.Range(0, 200).Select(i => $"project-{i:D3}-with-a-rather-long-name").ToList();
        foreach (var name in names)
            Directory.CreateDirectory(Path.Combine(Pages, name));

        using var service = Open();
        var instructions = service.ServerInstructions;

        Assert.True(instructions.Length <= ServerInstructionText.TotalLimit, $"length {instructions.Length}");
        Assert.Contains("## Using memory", instructions);
        Assert.Contains("Memory is written in Chinese.", instructions);
        Assert.Contains(names[0], instructions);
        Assert.DoesNotContain(names[^1], instructions);
    }

    [Fact]
    public void English_memory_language_is_named_in_the_language_norms()
    {
        File.AppendAllText(Path.Combine(_home.TacitHome, "config.yaml"), "language: en\n");

        using var service = Open();

        Assert.Contains("Memory is written in English.", service.ServerInstructions);
    }

    [Fact]
    public void Starting_the_service_renders_every_binding()
    {
        _home.Tacit().Connect(ClientKind.Omp);
        File.WriteAllText(Path.Combine(_home.DefaultMemory, "persona.md"), "## 偏好\n- 沟通：先要结论\n");

        using var service = Open();

        Assert.Equal(BlockState.Written, Assert.Single(service.StartupRender).State);
        Assert.Contains("- 沟通：先要结论\n", File.ReadAllText(AgentsMd));
    }

    [Fact]
    public async Task Saving_persona_md_while_running_updates_the_resident_block()
    {
        _home.Tacit().Connect(ClientKind.Omp);
        using var service = Open();

        File.WriteAllText(Path.Combine(_home.DefaultMemory, "persona.md"), "## 档案\n- 系统：Windows 11\n");

        await Eventually(() => File.ReadAllText(AgentsMd).Contains("- 系统：Windows 11\n"));
        await Eventually(() => _notices.Any(notice => notice.Contains("已写入常驻区块")));
    }

    [Fact]
    public async Task A_new_project_directory_shows_up_in_the_next_server_instructions()
    {
        using var service = Open();
        Assert.DoesNotContain("## Projects", service.ServerInstructions);

        Directory.CreateDirectory(Path.Combine(Pages, "db-migration"));

        await Eventually(() => service.ServerInstructions.EndsWith("## Projects\ndb-migration", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Removing_the_whole_pages_directory_drops_the_projects_part()
    {
        Directory.CreateDirectory(Path.Combine(Pages, "db-migration"));
        using var service = Open();
        Assert.Contains("## Projects", service.ServerInstructions);

        Directory.Delete(Pages, recursive: true);

        await Eventually(() => !service.ServerInstructions.Contains("## Projects", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_broken_bindings_json_is_reported_and_the_service_keeps_running()
    {
        _home.Tacit().Connect(ClientKind.Omp);
        using var service = Open();
        File.WriteAllText(Path.Combine(_home.TacitHome, "bindings.json"), "{ not json");

        File.WriteAllText(Path.Combine(_home.DefaultMemory, "persona.md"), "## 档案\n- 系统：Windows 11\n");

        await Eventually(() => _notices.Any(notice => notice.Contains("bindings.json")));
        File.Delete(Path.Combine(_home.TacitHome, "bindings.json"));
        Assert.Contains("Memory is written in Chinese.", service.ServerInstructions);
    }

    private static async Task Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "condition not met within 10 s");
            await Task.Delay(50);
        }
    }
}
