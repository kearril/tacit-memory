using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using TacitMemory.Core.Configuration;
using TacitMemory.Core.Localization;

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

    /// <summary>Reads AGENTS.md without denying the service its replacement of the file.</summary>
    private string Agents()
    {
        using var stream = new FileStream(AgentsMd, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void Without_resident_md_the_block_keeps_the_built_in_shell_and_guidance()
    {
        WriteFullPersona();
        _home.Bind();

        var text = Agents();

        Assert.Contains("Background on the user from earlier sessions.", text);
        Assert.Contains("## Red lines\nHold each unless the user lifts it", text);
        Assert.Contains("## Preferences\nTastes to weigh against the task.\n- p1\n", text);
        Assert.Contains("## Profile\n- f1\n", text);
    }

    [Fact]
    public void The_built_in_shell_states_the_user_priority_once_and_names_no_banned_phrasing()
    {
        WriteFullPersona();

        _home.Bind();
        var text = Agents();

        Assert.Equal(1, Count(text, "when they differ, follow the user"));
        Assert.DoesNotContain("according to your memory", text);
        Assert.DoesNotContain("per your red lines", text);
        Assert.DoesNotContain("Ignore any entry", text);
        Assert.DoesNotContain("Use them where they help", text);
    }

    [Fact]
    public void The_built_in_situational_rules_guidance_sits_above_the_domain_rows()
    {
        WriteScopedPersona();

        _home.Bind();

        AssertInOrder(Agents(),
            "## Situational rules\nEach line is a trigger; when it matches, read the rules with `recall kind=rules domain=<label>`.\n",
            "- network-proxy：改代理、分流或测速时（1 条）\n");
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
        Assert.DoesNotContain("Background on the user from earlier sessions.", text);
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
        Assert.Contains("Background on the user from earlier sessions.", Agents());
        Assert.NotNull(service);
    }

    [Fact]
    public async Task A_render_that_meets_a_reader_of_agents_md_is_retried_after_the_reader_lets_go()
    {
        WriteFullPersona();
        _home.Bind();
        using var service = Open();

        // The debounced render runs while this reader holds AGENTS.md, so the replacement is refused.
        using (new FileStream(AgentsMd, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            File.WriteAllText(PersonaMd, File.ReadAllText(PersonaMd).Replace("- p1", "- p2", StringComparison.Ordinal));
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        await Eventually(() => Agents().Contains("p2", StringComparison.Ordinal));
        Assert.NotNull(service);
    }

    [Fact]
    public async Task A_leftover_temporary_file_held_by_another_process_does_not_block_a_render()
    {
        WriteFullPersona();
        _home.Bind();
        using var service = Open();

        // Another renderer holds the old fixed temporary name; the render must not depend on that name.
        using var leftover = new FileStream(AgentsMd + ".tacit-tmp", FileMode.Create, FileAccess.Write, FileShare.None);
        File.WriteAllText(PersonaMd, File.ReadAllText(PersonaMd).Replace("- p1", "- p2", StringComparison.Ordinal));

        await Eventually(() => Agents().Contains("p2", StringComparison.Ordinal));
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

    private string WorkstreamsMd => Path.Combine(_home.DefaultMemory, "workstreams.md");

    private string Pages => Path.Combine(_home.DefaultMemory, "pages");

    private void WriteMonth(string month, string text)
    {
        Directory.CreateDirectory(Path.Combine(_home.DefaultMemory, "recent"));
        File.WriteAllText(Path.Combine(_home.DefaultMemory, "recent", $"{month}.md"), text.ReplaceLineEndings("\n"));
    }

    private void Configure(string extraConfig) =>
        File.WriteAllText(Path.Combine(_home.TacitHome, "config.yaml"), (ModelDouble.Quiet.ModelsYaml(decisionKey: "test-key") + extraConfig).ReplaceLineEndings("\n"));

    /// <summary>The global four sections, a domain with one entry, and a workstream block with one entry.</summary>
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

        ## 主线：alpha
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
    public void The_block_lists_the_domain_index_and_the_workstreams_after_the_global_sections()
    {
        WriteScopedPersona();
        File.WriteAllText(WorkstreamsMd, "## 主线\n### alpha\n背景：一号主线。\n近况：在收敛。\n");
        WriteMonth("2026-11", "## 2026-11-02\n- [alpha] 今天的事\n");

        _home.Bind();

        AssertInOrder(Agents(),
            "- f1\n\n## Situational rules\n",
            "- network-proxy：改代理、分流或测速时（1 条）\n",
            "## Workstreams\n- alpha：一号主线。 近况：在收敛。\n",
            "</user_memory>");
        Assert.DoesNotContain("## Mainline", Agents());
        Assert.DoesNotContain("## Recent weeks", Agents());
    }

    [Fact]
    public void Active_workstreams_come_first_newest_activity_first_and_quiet_ones_follow_by_name()
    {
        WriteFullPersona();
        File.WriteAllText(WorkstreamsMd, """
            ## 主线
            ### gamma
            背景：三号。
            近况：静止的近况。
            ### alpha
            背景：一号。
            近况：较早的近况。
            ### beta
            背景：二号。
            近况：最新的近况。
            ### delta
            背景：四号。

            """.ReplaceLineEndings("\n"));
        WriteMonth("2026-11", "## 2026-11-01\n- [alpha] 较早的事\n## 2026-11-02\n- [beta] 最新的事\n");

        _home.Bind();

        AssertInOrder(Agents(),
            "## Workstreams\n",
            "- beta：二号。 近况：最新的近况。\n",
            "- alpha：一号。 近况：较早的近况。\n",
            "- delta：四号。\n",
            "- gamma：三号。\n");
    }

    [Fact]
    public void A_quiet_workstream_shows_only_its_name_and_background()
    {
        WriteFullPersona();
        File.WriteAllText(WorkstreamsMd, "## 主线\n### alpha\n背景：一号主线。\n近况：在收敛。\n");

        _home.Bind();

        Assert.Contains("## Workstreams\n- alpha：一号主线。\n", Agents());
        Assert.DoesNotContain("在收敛", Agents());
    }

    [Fact]
    public void A_workstream_without_a_background_line_or_with_a_bad_name_stays_out_of_the_block()
    {
        WriteFullPersona();
        File.WriteAllText(WorkstreamsMd, "## 主线\n### nobg\n近况：没有背景。\n### Bad-Name\n背景：名字不合规。\n");

        _home.Bind();

        Assert.DoesNotContain("## Workstreams", Agents());
    }

    [Fact]
    public void Workstreams_that_only_appear_as_directories_or_activity_labels_stay_out_of_the_block()
    {
        WriteFullPersona();
        File.WriteAllText(WorkstreamsMd, "## 主线\n### alpha\n背景：一号主线。\n");
        Directory.CreateDirectory(Path.Combine(Pages, "zeta"));
        WriteMonth("2026-11", "## 2026-11-02\n- [zeta] 未成立的事\n");

        _home.Bind();

        Assert.Contains("## Workstreams\n- alpha：一号主线。\n", Agents());
        Assert.DoesNotContain("zeta", Agents());
    }

    [Fact]
    public void Every_domain_is_listed_in_the_index()
    {
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
        Assert.Contains("- dom-c：C时（1 条）", text);
    }

    [Fact]
    public void A_workstream_leaves_the_active_part_when_the_28_day_window_passes_it()
    {
        WriteFullPersona();
        File.WriteAllText(WorkstreamsMd, "## 主线\n### alpha\n背景：一号主线。\n近况：在做。\n");
        WriteMonth("2026-10", "## 2026-10-06\n- [alpha] 窗口的第一天\n");
        _home.Bind();
        Assert.Contains("- alpha：一号主线。 近况：在做。\n", Agents());

        // 2026-11-02 counts back 28 days to 2026-10-06; the next day the window starts on 2026-10-07.
        _home.Clock.Advance(TimeSpan.FromDays(1));
        _home.Bind();

        Assert.Contains("## Workstreams\n- alpha：一号主线。\n", Agents());
        Assert.DoesNotContain("在做", Agents());
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
        Assert.DoesNotContain("## 主线", text);
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
        Assert.DoesNotContain("## Workstreams", text);
    }

    [Fact]
    public void Resident_md_gives_guidance_for_the_new_sections_and_it_is_shown_above_their_data()
    {
        File.WriteAllText(ResidentMd, "SHELL\n\n## situational_rules\nRULES-GUIDE\n\n## workstreams\nWORKSTREAMS-GUIDE\n");
        WriteScopedPersona();
        File.WriteAllText(WorkstreamsMd, "## 主线\n### alpha\n背景：一号主线。\n");

        _home.Bind();
        var text = Agents();

        AssertInOrder(text, "## Situational rules\nRULES-GUIDE\n- network-proxy", "## Workstreams\nWORKSTREAMS-GUIDE\n- alpha：一号主线。\n</user_memory>");
    }

    [Fact]
    public void Resident_md_naming_a_removed_section_is_refused_and_the_name_is_given()
    {
        File.WriteAllText(ResidentMd, "SHELL-OK\n\n## mainline\nX\n");
        WriteFullPersona();

        var refused = Assert.Throws<TacitException>(() => ResidentFile.Load(ResidentMd, MemoryLanguage.Zh));

        Assert.Contains("mainline", refused.Message);
    }

    [Fact]
    public async Task Editing_the_workstreams_or_the_activity_log_rerenders_the_block()
    {
        WriteFullPersona();
        File.WriteAllText(WorkstreamsMd, "## 主线\n### fresh\n背景：说明。\n近况：在做。\n");
        _home.Bind();
        using var service = Open();
        Assert.Contains("## Workstreams\n- fresh：说明。\n", Agents());

        WriteMonth("2026-11", "## 2026-11-02\n- [fresh] 今天的事\n");
        await Eventually(() => Agents().Contains("- fresh：说明。 近况：在做。\n", StringComparison.Ordinal));

        File.WriteAllText(WorkstreamsMd, "## 主线\n### fresh\n背景：改过的说明。\n");
        await Eventually(() => Agents().Contains("- fresh：改过的说明。\n", StringComparison.Ordinal));
        Assert.NotNull(service);
    }

    [Fact]
    public async Task The_block_is_rendered_again_when_the_local_date_changes()
    {
        WriteFullPersona();
        File.WriteAllText(WorkstreamsMd, "## 主线\n### alpha\n背景：一号主线。\n近况：在做。\n");
        WriteMonth("2026-10", "## 2026-10-06\n- [alpha] 窗口的第一天\n");
        _home.Bind();
        using var service = Open();
        Assert.Contains("- alpha：一号主线。 近况：在做。\n", Agents());

        // Let the vector fill of the service's start finish first: Elapse would otherwise fire the timeout of a call still in flight.
        await service.VectorCompletion;
        _home.Clock.Elapse(TimeSpan.FromDays(1));

        await Eventually(() => Agents().Contains("## Workstreams\n- alpha：一号主线。\n", StringComparison.Ordinal));
        Assert.NotNull(service);
    }

    private static int Count(string text, string value) =>
        Regex.Matches(text, Regex.Escape(value)).Count;

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
