using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace TacitMemory.Core.Tests;

public sealed class ResidentDocumentTests : IDisposable
{
    private readonly TestHome _home = new();
    private readonly ConcurrentQueue<string> _notices = new();

    public ResidentDocumentTests() => _home.InitReady();

    public void Dispose() => _home.Dispose();

    private string Prompts => Path.Combine(_home.TacitHome, "prompts");

    private string ResidentMd => Path.Combine(Prompts, "resident.md");

    private string AgentsMd => Path.Combine(_home.UserHome, ".omp", "agent", "AGENTS.md");

    private string PersonaMd => Path.Combine(_home.DefaultMemory, "persona.md");

    private MemoryService Open() => _home.Tacit().OpenService(_notices.Enqueue, NullLoggerFactory.Instance);

    /// <summary>A persona with one entry under each of the four sections, so every guidance line has something under it.</summary>
    private void WriteFullPersona() => File.WriteAllText(PersonaMd, """
        ## 红线
        - r1

        ## 协作规则
        - w1

        ## 偏好
        - p1

        ## 档案
        - f1

        """.ReplaceLineEndings("\n"));

    private string Agents() => File.ReadAllText(AgentsMd);

    [Fact]
    public void Without_resident_md_the_block_keeps_the_built_in_shell_and_guidance()
    {
        WriteFullPersona();
        _home.Tacit().Connect(ClientKind.Omp);

        var text = Agents();

        Assert.Contains("is memory about the user", text);
        Assert.Contains("## Red lines\nThings the user will not accept", text);
        Assert.Contains("## Profile\nFacts about the user", text);
    }

    [Fact]
    public void Resident_md_replaces_the_shell_and_each_guidance_and_the_structure_stays_generated()
    {
        File.WriteAllText(ResidentMd, """
            SHELL-CUSTOM paragraph.

            ## red_lines
            RED-CUSTOM

            ## working_rules
            WORK-CUSTOM

            ## preferences
            PREF-CUSTOM

            ## profile
            PROF-CUSTOM
            """.ReplaceLineEndings("\n"));
        WriteFullPersona();

        _home.Tacit().Connect(ClientKind.Omp);
        var text = Agents();

        Assert.Contains("<!-- tacit:begin", text);
        Assert.Contains("<user_memory>\nSHELL-CUSTOM paragraph.\n\n## Red lines\nRED-CUSTOM\n- r1\n", text);
        Assert.Contains("## Working rules\nWORK-CUSTOM\n- w1\n", text);
        Assert.Contains("## Preferences\nPREF-CUSTOM\n- p1\n", text);
        Assert.Contains("## Profile\nPROF-CUSTOM\n- f1\n</user_memory>\n<!-- tacit:end -->", text);
        Assert.DoesNotContain("is memory about the user", text);
    }

    [Fact]
    public void A_section_missing_from_resident_md_shows_its_items_without_guidance()
    {
        File.WriteAllText(ResidentMd, "SHELL-CUSTOM\n\n## red_lines\nRED-CUSTOM\n");
        WriteFullPersona();

        _home.Tacit().Connect(ClientKind.Omp);
        var text = Agents();

        Assert.Contains("## Red lines\nRED-CUSTOM\n- r1\n", text);
        Assert.Contains("## Profile\n- f1\n", text);
        Assert.Contains("## Preferences\n- p1\n", text);
    }

    [Fact]
    public void Resident_md_is_not_checked_for_language()
    {
        File.WriteAllText(ResidentMd, "外壳用中文写。\n\n## red_lines\n红线的分量最重。\n");
        WriteFullPersona();

        _home.Tacit().Connect(ClientKind.Omp);

        Assert.Contains("外壳用中文写。", Agents());
        Assert.Contains("红线的分量最重。", Agents());
    }

    [Fact]
    public void Connect_uses_the_resident_md_on_disk()
    {
        WriteFullPersona();
        File.WriteAllText(ResidentMd, "SHELL-FIRST\n");

        _home.Tacit().Connect(ClientKind.Omp);

        Assert.Contains("SHELL-FIRST", Agents());
    }

    [Fact]
    public void An_unknown_section_refuses_startup_and_connect_and_points_at_its_line()
    {
        File.WriteAllText(ResidentMd, "SHELL\n\n## red_line\nX\n");

        var error = Assert.Throws<TacitException>(() => Open());
        Assert.Equal(TacitError.PromptInvalid, error.Error);
        Assert.Contains("resident.md", error.Message);
        Assert.Matches(@"(第 3 行|line 3)", error.Message);
        Assert.Throws<TacitException>(() => _home.Tacit().Connect(ClientKind.Omp));
        Assert.False(File.Exists(AgentsMd));
    }

    [Fact]
    public void A_section_that_appears_twice_refuses_startup_and_points_at_the_second()
    {
        File.WriteAllText(ResidentMd, "SHELL\n\n## profile\nA\n\n## profile\nB\n");

        var error = Assert.Throws<TacitException>(() => Open());
        Assert.Equal(TacitError.PromptInvalid, error.Error);
        Assert.Contains("resident.md", error.Message);
        Assert.Matches(@"(第 6 行|line 6)", error.Message);
    }

    [Fact]
    public void Resident_md_over_1500_characters_refuses_startup_and_names_the_file()
    {
        File.WriteAllText(ResidentMd, new string('x', 1501));

        var error = Assert.Throws<TacitException>(() => Open());

        Assert.Equal(TacitError.PromptInvalid, error.Error);
        Assert.Contains("resident.md", error.Message);
        var match = Regex.Match(error.Message, @"共 (\d+) 字符.*超出 (\d+) 字符");
        Assert.True(match.Success, error.Message);
        Assert.Equal("1501", match.Groups[1].Value);
        Assert.Equal("1", match.Groups[2].Value);
    }

    [Fact]
    public void Resident_md_of_exactly_1500_characters_is_accepted()
    {
        File.WriteAllText(ResidentMd, new string('x', 1500));
        WriteFullPersona();

        using var service = Open();
        _home.Tacit().Connect(ClientKind.Omp);

        Assert.Contains(new string('x', 1500), Agents());
        Assert.NotNull(service);
    }

    [Fact]
    public async Task Editing_resident_md_while_running_rerenders_the_bound_block()
    {
        WriteFullPersona();
        _home.Tacit().Connect(ClientKind.Omp);
        using var service = Open();

        File.WriteAllText(ResidentMd, "SHELL-NEW\n\n## profile\nPROF-NEW\n");

        await Eventually(() => Agents().Contains("SHELL-NEW", StringComparison.Ordinal));
        Assert.Contains("## Profile\nPROF-NEW\n- f1\n", Agents());
        Assert.Contains("## Red lines\n- r1\n", Agents());
        Assert.NotNull(service);
    }

    [Fact]
    public async Task Deleting_resident_md_while_running_restores_the_built_in_block()
    {
        File.WriteAllText(ResidentMd, "SHELL-GONE\n");
        WriteFullPersona();
        _home.Tacit().Connect(ClientKind.Omp);
        using var service = Open();

        File.Delete(ResidentMd);

        await Eventually(() => !Agents().Contains("SHELL-GONE", StringComparison.Ordinal));
        Assert.Contains("is memory about the user", Agents());
        Assert.NotNull(service);
    }

    [Fact]
    public async Task A_resident_md_that_breaks_the_rules_while_running_is_reported_and_the_previous_one_kept()
    {
        File.WriteAllText(ResidentMd, "SHELL-OK\n");
        WriteFullPersona();
        _home.Tacit().Connect(ClientKind.Omp);
        using var service = Open();

        File.WriteAllText(ResidentMd, "SHELL-BROKEN\n\n## red_line\nX\n");

        await Eventually(() => _notices.Any(notice => notice.Contains("resident.md", StringComparison.Ordinal)));
        Assert.Contains("SHELL-OK", Agents());
        Assert.DoesNotContain("SHELL-BROKEN", Agents());
        Assert.NotNull(service);
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
