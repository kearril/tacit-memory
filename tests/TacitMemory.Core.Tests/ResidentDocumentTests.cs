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
        _home.Bind();

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

        _home.Bind();
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

        _home.Bind();
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

        _home.Bind();

        Assert.Contains("外壳用中文写。", Agents());
        Assert.Contains("红线的分量最重。", Agents());
    }

    [Fact]
    public void Connect_uses_the_resident_md_on_disk()
    {
        WriteFullPersona();
        File.WriteAllText(ResidentMd, "SHELL-FIRST\n");

        _home.Bind();

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
        Assert.Throws<TacitException>(() => _home.Bind());
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
        _home.Bind();

        Assert.Contains(new string('x', 1500), Agents());
        Assert.NotNull(service);
    }

    [Fact]
    public async Task Editing_resident_md_while_running_rerenders_the_bound_block()
    {
        WriteFullPersona();
        _home.Bind();
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
        _home.Bind();
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
        _home.Bind();
        using var service = Open();

        File.WriteAllText(ResidentMd, "SHELL-BROKEN\n\n## red_line\nX\n");

        await Eventually(() => _notices.Any(notice => notice.Contains("resident.md", StringComparison.Ordinal)));
        Assert.Contains("SHELL-OK", Agents());
        Assert.DoesNotContain("SHELL-BROKEN", Agents());
        Assert.NotNull(service);
    }

    private string RecentMd => Path.Combine(_home.DefaultMemory, "recent.md");

    private string Pages => Path.Combine(_home.DefaultMemory, "pages");

    private void WriteMonth(string month, string text)
    {
        Directory.CreateDirectory(Path.Combine(_home.DefaultMemory, "recent"));
        File.WriteAllText(Path.Combine(_home.DefaultMemory, "recent", $"{month}.md"), text.ReplaceLineEndings("\n"));
    }

    private void Configure(string extraConfig) =>
        File.WriteAllText(Path.Combine(_home.TacitHome, "config.yaml"), (ModelDouble.Quiet.ModelsYaml(decisionKey: "test-key") + extraConfig).ReplaceLineEndings("\n"));

    /// <summary>The global four sections, a domain with one entry, and a project block with one entry.</summary>
    private void WriteScopedPersona() => File.WriteAllText(PersonaMd, """
        ## 红线
        - r1

        ## 协作规则
        - w1

        ## 偏好
        - p1

        ## 档案
        - f1

        ## 领域：network-proxy
        触发：改代理、分流或测速时
        ### 协作规则
        - 域内规则XYZ

        ## 项目：alpha
        ### 协作规则
        - 项目规则QRS

        """.ReplaceLineEndings("\n"));

    /// <summary>Asserts each text occurs, and the occurrences come in the order given.</summary>
    private static void AssertInOrder(string text, params string[] parts)
    {
        var position = -1;
        foreach (var part in parts)
        {
            var found = text.IndexOf(part, position + 1, StringComparison.Ordinal);
            Assert.True(found > position, $"\"{part}\" is missing or out of order in:\n{text}");
            position = found;
        }
    }

    [Fact]
    public void The_block_lists_the_domain_index_mainline_recent_weeks_and_projects_after_the_global_sections()
    {
        WriteScopedPersona();
        File.WriteAllText(RecentMd, "## 近期主线\n- 主线一\n");
        WriteMonth("2026-10", "## 2026-10-20\n- [alpha] 周二的事\n## 2026-10-21\n- 没有项目的事\n");
        Directory.CreateDirectory(Path.Combine(Pages, "alpha"));

        _home.Bind();

        AssertInOrder(Agents(),
            "- f1\n\n## Situational rules\n",
            "- network-proxy：改代理、分流或测速时（1 条）\n",
            "## Mainline\n- 主线一\n",
            "## Recent weeks\n",
            "- 2026-W43：2 条活动，涉及项目 alpha，其他 1 条\n",
            "## Projects\nalpha\n</user_memory>");
    }

    [Fact]
    public void Scoped_entries_stay_out_of_the_block_and_a_domain_shows_only_its_index_row()
    {
        WriteScopedPersona();

        _home.Bind();
        var text = Agents();

        Assert.Contains("- network-proxy：改代理、分流或测速时（1 条）", text);
        Assert.DoesNotContain("域内规则XYZ", text);
        Assert.DoesNotContain("项目规则QRS", text);
        Assert.DoesNotContain("## 领域", text);
        Assert.DoesNotContain("## 项目", text);
    }

    [Fact]
    public void Domain_rows_stop_at_the_domain_limit()
    {
        Configure("limits: { domains: 2 }\n");
        File.WriteAllText(PersonaMd, """
            ## 领域：dom-a
            触发：A时
            ### 偏好
            - a1

            ## 领域：dom-b
            触发：B时
            ### 偏好
            - b1

            ## 领域：dom-c
            触发：C时
            ### 偏好
            - c1

            """.ReplaceLineEndings("\n"));

        _home.Bind();
        var text = Agents();

        Assert.Contains("- dom-a：A时（1 条）", text);
        Assert.Contains("- dom-b：B时（1 条）", text);
        Assert.DoesNotContain("dom-c", text);
    }

    [Fact]
    public void A_week_with_a_summary_line_shows_the_summary_instead_of_the_counts()
    {
        WriteMonth("2026-10", "## 2026-10-20\n- [alpha] 周二的事\n## 2026-10-21\n- 没有项目的事\n\n## 汇总\n- 2026-W43：这一周主要在做排错\n");
        WriteFullPersona();

        _home.Bind();
        var text = Agents();

        Assert.Contains("- 2026-W43：这一周主要在做排错\n", text);
        Assert.DoesNotContain("2 条活动", text);
    }

    [Fact]
    public void The_recent_weeks_are_the_three_latest_completed_weeks_that_have_anything_in_them()
    {
        // W43, W41, W40 and W38 hold activities; W42 holds none and is skipped, so W38 is the fourth week with content.
        WriteMonth("2026-10", "## 2026-10-20\n- 第四十三周\n## 2026-10-06\n- 第四十一周\n");
        WriteMonth("2026-09", "## 2026-09-29\n- 第四十周\n## 2026-09-15\n- 第三十八周\n");
        WriteFullPersona();

        _home.Bind();
        var text = Agents();

        Assert.Contains("- 2026-W43：1 条活动，其他 1 条\n", text);
        Assert.Contains("- 2026-W41：1 条活动，其他 1 条\n", text);
        Assert.Contains("- 2026-W40：1 条活动，其他 1 条\n", text);
        Assert.DoesNotContain("2026-W38", text);
        Assert.DoesNotContain("2026-W42", text);
    }

    [Fact]
    public void Sections_with_nothing_to_show_are_left_out()
    {
        WriteFullPersona();

        _home.Bind();
        var text = Agents();

        Assert.DoesNotContain("## Situational rules", text);
        Assert.DoesNotContain("## Mainline", text);
        Assert.DoesNotContain("## Recent weeks", text);
        Assert.DoesNotContain("## Projects", text);
    }

    [Fact]
    public void Resident_md_gives_guidance_for_the_new_sections_and_it_is_shown_above_their_data()
    {
        File.WriteAllText(ResidentMd, "SHELL\n\n## situational_rules\nRULES-GUIDE\n\n## projects\nPROJECTS-GUIDE\n");
        WriteScopedPersona();
        Directory.CreateDirectory(Path.Combine(Pages, "alpha"));

        _home.Bind();
        var text = Agents();

        AssertInOrder(text, "## Situational rules\nRULES-GUIDE\n- network-proxy", "## Projects\nPROJECTS-GUIDE\nalpha\n</user_memory>");
    }

    [Fact]
    public async Task Editing_the_mainline_or_the_activity_log_rerenders_the_block()
    {
        WriteFullPersona();
        _home.Bind();
        using var service = Open();

        File.WriteAllText(RecentMd, "## 近期主线\n- 新的主线\n");
        await Eventually(() => Agents().Contains("## Mainline\n- 新的主线\n", StringComparison.Ordinal));

        WriteMonth("2026-10", "## 2026-10-20\n- 第四十三周的事\n");
        await Eventually(() => Agents().Contains("- 2026-W43：1 条活动，其他 1 条", StringComparison.Ordinal));
        Assert.NotNull(service);
    }

    [Fact]
    public async Task A_new_project_directory_appears_in_the_block()
    {
        WriteFullPersona();
        _home.Bind();
        using var service = Open();
        Assert.DoesNotContain("## Projects", Agents());

        Directory.CreateDirectory(Path.Combine(Pages, "db-migration"));

        await Eventually(() => Agents().Contains("## Projects\ndb-migration\n", StringComparison.Ordinal));
        Assert.NotNull(service);
    }

    [Fact]
    public async Task Removing_the_whole_pages_directory_drops_the_projects_section()
    {
        WriteFullPersona();
        Directory.CreateDirectory(Path.Combine(Pages, "db-migration"));
        _home.Bind();
        using var service = Open();
        Assert.Contains("## Projects\ndb-migration\n", Agents());

        Directory.Delete(Pages, recursive: true);

        await Eventually(() => !Agents().Contains("## Projects", StringComparison.Ordinal));
        Assert.NotNull(service);
    }

    [Fact]
    public async Task The_block_is_rendered_again_when_the_local_date_changes()
    {
        WriteFullPersona();
        WriteMonth("2026-10", "## 2026-10-26\n- 周一的事\n");
        _home.Bind();
        using var service = Open();
        // 2026-W44 ends on 2026-11-01, inside the seven-day window until 2026-11-09.
        Assert.DoesNotContain("2026-W44", Agents());

        // Let the vector fill of the service's start finish first: Elapse would otherwise fire the timeout of a call still in flight.
        await service.VectorCompletion;
        _home.Clock.Elapse(TimeSpan.FromDays(7));

        await Eventually(() => Agents().Contains("- 2026-W44：1 条活动，其他 1 条", StringComparison.Ordinal));
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
