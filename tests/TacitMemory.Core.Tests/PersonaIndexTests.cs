namespace TacitMemory.Core.Tests;

public sealed class PersonaIndexTests : IDisposable
{
    private const string PersonaText = "## Red lines\n\n## Working rules\n\n## Preferences\n- 数据库升级前先备份，再动手\n\n## Profile\n";

    private const string CandidatesText = """
        ## Candidates

        ### 数据库升级前先备份
        Belongs to: Preferences
        - 2026-10-28 quote「升级数据库前先备份」

        ## Rejection list

        ### 数据库升级不写回滚脚本
        Belongs to: Working rules
        - 2026-10-27 quote「升级不用写回滚」

        """;

    private readonly TestHome _home = TestHome.WithManualSources();
    private readonly ModelDouble _models = new();
    private MemoryService? _service;

    public void Dispose()
    {
        _service?.Dispose();
        _models.Dispose();
        _home.Dispose();
    }

    private string Memory => _home.DefaultMemory;

    private MemoryService Service => _service ??= _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

    /// <summary>Runs <c>init</c> with the embedding model answering, so every unit gets a vector, and the given extra config.</summary>
    private void Ready(string extraConfig = "") => _home.InitReady(extraConfig, _models);

    private Task<ToolResult> Recall(string query, string? kind) =>
        Service.RecallAsync(new RecallRequest { Query = query, Kind = kind });

    /// <summary>A page that shares the query's words, so a search that reaches the judgment files would show them too.</summary>
    private void Page()
    {
        var path = Path.Combine(Memory, "pages", "tacit-memory", "db-upgrade.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "---\ntitle: 数据库升级后扩展丢失\naliases: [别名一, 别名二]\ndates: { first: 2026-10-01, last: 2026-10-05 }\n---\n\n## 根因\n升级数据库后 PGroonga 扩展没有重装。\n");
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("pages", true)]
    [InlineData("activities", false)]
    public async Task Recall_never_returns_persona_entries_or_candidates(string? kind, bool expectsPage)
    {
        Ready("retrieval: { candidates: 1 }\n");
        Page();
        File.WriteAllText(Path.Combine(Memory, "persona.md"), PersonaText);
        File.WriteAllText(Path.Combine(Memory, "candidates.md"), CandidatesText);

        var result = await Recall("数据库升级前先备份", kind);

        Assert.False(result.IsError, result.Text);
        Assert.DoesNotContain("先备份", result.Text);
        Assert.DoesNotContain("写回滚", result.Text);
        if (expectsPage)
            Assert.Contains("tacit-memory/db-upgrade", result.Text);
    }

    [Fact]
    public async Task Persona_entries_do_not_take_the_candidate_slots_of_a_search()
    {
        // With one slot, a persona entry that matched the query would push the page out of the search entirely.
        Ready("retrieval: { candidates: 1 }\n");
        Page();
        File.WriteAllText(Path.Combine(Memory, "persona.md"), PersonaText);

        var result = await Recall("数据库升级前先备份", null);

        Assert.Contains("tacit-memory/db-upgrade", result.Text);
    }

    [Fact]
    public async Task Malformed_lines_in_the_judgment_files_do_not_stop_the_index_or_the_search()
    {
        Ready();
        Page();
        File.WriteAllText(Path.Combine(Memory, "persona.md"), "随便写的一行\n## 不认识的节\n- 不该参与判断\n### 不是三级标题也不是候选\n");
        File.WriteAllText(Path.Combine(Memory, "candidates.md"), "没有标题的一行\n## Candidates\n### 没有归属的候选\n- 不是出现记录\n## 其他\n### 不该参与\nBelongs to: Preferences\n");

        var result = await Recall("数据库升级", null);

        Assert.False(result.IsError, result.Text);
        Assert.Contains("tacit-memory/db-upgrade", result.Text);
    }

    [Theory]
    [InlineData("persona.md")]
    [InlineData("candidates.md")]
    public async Task A_judgment_file_edited_into_non_utf8_while_running_is_reported_and_recovers(string name)
    {
        Ready();
        var service = Service;
        Assert.DoesNotContain(name, await service.ServerInstructionsAsync());
        var path = Path.Combine(Memory, name);

        File.WriteAllBytes(path, [0x2D, 0x0A, 0xFF, 0xFE, 0x0A]);
        _home.Sources!.MemoryChanged(name);
        await service.SettledAsync();
        Assert.Contains(name, (await service.ServerInstructionsAsync()));

        File.WriteAllText(path, "## Preferences\n- 先说结论\n");
        _home.Sources!.MemoryChanged(name);
        await service.SettledAsync();
        Assert.DoesNotContain(name, (await service.ServerInstructionsAsync()));
    }

    [Theory]
    [InlineData("persona.md")]
    [InlineData("candidates.md")]
    public async Task A_judgment_file_that_is_not_utf8_at_startup_is_reported_in_the_status(string name)
    {
        Ready();
        File.WriteAllBytes(Path.Combine(Memory, name), [0x2D, 0x0A, 0xFF, 0xFE, 0x0A]);

        using var service = _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

        Assert.Contains(name, await service.ServerInstructionsAsync());
    }
}
