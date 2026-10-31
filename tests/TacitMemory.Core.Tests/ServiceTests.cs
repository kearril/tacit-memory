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
        Assert.DoesNotContain("recall", instructions);
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

        Assert.True(instructions.Length <= 4000, $"length {instructions.Length}");
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
