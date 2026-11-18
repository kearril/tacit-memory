using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using TacitMemory.Core.Configuration;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Upkeep;

namespace TacitMemory.Core.Tests;

public sealed class ResidentDocumentTests : IDisposable
{
    private readonly TestHome _home = TestHome.WithManualSources();
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
        ## Red lines
        - r1

        ## Working rules
        - w1

        ## Preferences
        - p1

        ## Profile
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
    public void Without_resident_md_the_block_keeps_the_built_in_sections()
    {
        WriteFullPersona();
        _home.Bind();

        var text = Agents();

        Assert.Contains("<user_memory>\n", text);
        Assert.Contains("## Red lines\n", text);
        Assert.Contains("## Working rules\n- w1\n", text);
        Assert.Contains("## Preferences\n", text);
        Assert.Contains("- p1\n", text);
        Assert.Contains("## Profile\n- f1\n", text);
    }

    [Fact]
    public void The_built_in_situational_rules_sit_above_the_domain_rows()
    {
        WriteScopedPersona();

        _home.Bind();

        AssertInOrder(Agents(),
            "## Situational rules\n",
            "- network-proxy: 改代理、分流或测速时 (1 entry)\n");
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
        _home.Sources!.PromptsChanged();

        await service.SettledAsync();
        Assert.Contains("SHELL-NEW", Agents());
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
        _home.Sources!.PromptsChanged();

        await service.SettledAsync();
        Assert.DoesNotContain("SHELL-GONE", Agents());
        Assert.Contains("## Red lines\n", Agents());
        Assert.NotNull(service);
    }

    [Fact]
    public async Task On_windows_a_render_that_meets_a_reader_of_agents_md_is_retried_after_the_reader_lets_go()
    {
        // Only Windows refuses to replace or move a file that another handle holds open; elsewhere nothing fails.
        if (!OperatingSystem.IsWindows())
            return;
        WriteFullPersona();
        _home.Bind();
        using var service = Open();

        // The render runs while this reader holds AGENTS.md, so the replacement is refused and tried again later.
        using (new FileStream(AgentsMd, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await service.WritePersonaAsync(AddProfile("系统：Windows 11"));
            await service.SettledAsync();
            Assert.DoesNotContain("系统：Windows 11", Agents());
        }

        _home.Clock.Elapse(DerivedUpkeep.Debounce);
        await service.SettledAsync();

        Assert.Contains("- 系统：Windows 11\n", Agents());
        Assert.DoesNotContain(_notices, notice => notice.Contains("渲染常驻区块失败", StringComparison.Ordinal));
    }

    [Fact]
    public async Task On_windows_a_render_still_refused_after_five_retries_is_reported_once_and_a_later_change_renders_again()
    {
        // Only Windows refuses to replace or move a file that another handle holds open; elsewhere nothing fails.
        if (!OperatingSystem.IsWindows())
            return;
        WriteFullPersona();
        _home.Bind();
        using var service = Open();

        using (new FileStream(AgentsMd, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await service.WritePersonaAsync(AddProfile("系统：Windows 11"));
            await service.SettledAsync();
            for (var retry = 1; retry <= DerivedUpkeep.RetryLimit; retry++)
            {
                Assert.DoesNotContain(_notices, notice => notice.Contains("渲染常驻区块失败", StringComparison.Ordinal));
                _home.Clock.Elapse(DerivedUpkeep.Debounce);
                await service.SettledAsync();
            }
        }

        Assert.Single(_notices, notice => notice.Contains("渲染常驻区块失败", StringComparison.Ordinal));
        Assert.DoesNotContain("系统：Windows 11", Agents());

        await service.WritePersonaAsync(AddProfile("编辑器：VS Code"));
        await service.SettledAsync();

        Assert.Contains("- 系统：Windows 11\n", Agents());
        Assert.Contains("- 编辑器：VS Code\n", Agents());
    }

    [Fact]
    public async Task A_render_still_waiting_when_the_service_is_disposed_does_not_run()
    {
        WriteFullPersona();
        _home.Bind();
        var service = Open();

        await service.WritePersonaAsync(AddProfile("系统：Windows 11"));
        service.Dispose();
        _home.Clock.Elapse(DerivedUpkeep.Debounce);

        Assert.DoesNotContain("系统：Windows 11", Agents());
    }

    private static WritePersonaRequest AddProfile(string text) =>
        new() { Op = "add", Section = "profile", Text = text, Source = text };

    [Fact]
    public async Task A_leftover_temporary_file_held_by_another_process_does_not_block_a_render()
    {
        WriteFullPersona();
        _home.Bind();
        using var service = Open();

        // Another renderer holds the old fixed temporary name; the render must not depend on that name.
        using var leftover = new FileStream(AgentsMd + ".tacit-tmp", FileMode.Create, FileAccess.Write, FileShare.None);
        File.WriteAllText(PersonaMd, File.ReadAllText(PersonaMd).Replace("- p1", "- p2", StringComparison.Ordinal));
        _home.Sources!.MemoryChanged("persona.md");

        await service.SettledAsync();
        Assert.Contains("p2", Agents());
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
        _home.Sources!.PromptsChanged();

        await service.SettledAsync();
        Assert.Contains(_notices, notice => notice.Contains("resident.md", StringComparison.Ordinal));
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
        ## Red lines
        - r1

        ## Working rules
        - w1

        ## Preferences
        - p1

        ## Profile
        - f1

        ## Domain: network-proxy
        Trigger: 改代理、分流或测速时
        ### Working rules
        - 域内规则XYZ

        ## Workstream: alpha
        ### Working rules
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
        File.WriteAllText(WorkstreamsMd, "## Workstream\n### alpha\nBackground: 一号主线。\nRecent: 在收敛。\n");
        WriteMonth("2026-11", "## 2026-11-02\n- [alpha] 今天的事\n");

        _home.Bind();

        AssertInOrder(Agents(),
            "- f1\n\n## Situational rules\n",
            "- network-proxy: 改代理、分流或测速时 (1 entry)\n",
            "- alpha: 一号主线。 Recent: 在收敛。\n",
            "</user_memory>");
        Assert.DoesNotContain("## Recent weeks", Agents());
    }

    [Fact]
    public void Active_workstreams_come_first_newest_activity_first_and_quiet_ones_follow_by_name()
    {
        WriteFullPersona();
        File.WriteAllText(WorkstreamsMd, """
            ## Workstream
            ### gamma
            Background: 三号。
            Recent: 静止的近况。
            ### alpha
            Background: 一号。
            Recent: 较早的近况。
            ### beta
            Background: 二号。
            Recent: 最新的近况。
            ### delta
            Background: 四号。

            """.ReplaceLineEndings("\n"));
        WriteMonth("2026-11", "## 2026-11-01\n- [alpha] 较早的事\n## 2026-11-02\n- [beta] 最新的事\n");

        _home.Bind();

        AssertInOrder(Agents(),
            "- beta: 二号。 Recent: 最新的近况。\n",
            "- alpha: 一号。 Recent: 较早的近况。\n",
            "- delta: 四号。\n",
            "- gamma: 三号。\n");
    }

    [Fact]
    public void A_quiet_workstream_shows_only_its_name_and_background()
    {
        WriteFullPersona();
        File.WriteAllText(WorkstreamsMd, "## Workstream\n### alpha\nBackground: 一号主线。\nRecent: 在收敛。\n");

        _home.Bind();

        Assert.Contains("- alpha: 一号主线。\n", Agents());
        Assert.DoesNotContain("在收敛", Agents());
    }

    [Fact]
    public void A_workstream_without_a_background_line_or_with_a_bad_name_stays_out_of_the_block()
    {
        WriteFullPersona();
        File.WriteAllText(WorkstreamsMd, "## Workstream\n### nobg\nRecent: 没有背景。\n### Bad-Name\nBackground: 名字不合规。\n");

        _home.Bind();

        Assert.DoesNotContain("## Workstreams", Agents());
    }

    [Fact]
    public void Workstreams_that_only_appear_as_directories_or_activity_labels_stay_out_of_the_block()
    {
        WriteFullPersona();
        File.WriteAllText(WorkstreamsMd, "## Workstream\n### alpha\nBackground: 一号主线。\n");
        Directory.CreateDirectory(Path.Combine(Pages, "zeta"));
        WriteMonth("2026-11", "## 2026-11-02\n- [zeta] 未成立的事\n");

        _home.Bind();

        Assert.Contains("- alpha: 一号主线。\n", Agents());
        Assert.DoesNotContain("zeta", Agents());
    }

    [Fact]
    public void Every_domain_gets_an_index_row_however_many_there_are()
    {
        File.WriteAllText(PersonaMd, """
            ## Domain: dom-a
            Trigger: A时
            ### Preferences
            - a1

            ## Domain: dom-b
            Trigger: B时
            ### Preferences
            - b1

            ## Domain: dom-c
            Trigger: C时
            ### Preferences
            - c1

            """.ReplaceLineEndings("\n"));

        _home.Bind();
        var text = Agents();

        Assert.Contains("- dom-a: A时 (1 entry)", text);
        Assert.Contains("- dom-b: B时 (1 entry)", text);
        Assert.Contains("- dom-c: C时 (1 entry)", text);
    }

    [Fact]
    public void A_workstream_leaves_the_active_part_when_the_28_day_window_passes_it()
    {
        WriteFullPersona();
        File.WriteAllText(WorkstreamsMd, "## Workstream\n### alpha\nBackground: 一号主线。\nRecent: 在做。\n");
        WriteMonth("2026-10", "## 2026-10-06\n- [alpha] 窗口的第一天\n");
        _home.Bind();
        Assert.Contains("- alpha: 一号主线。 Recent: 在做。\n", Agents());

        // 2026-11-02 counts back 28 days to 2026-10-06; the next day the window starts on 2026-10-07.
        _home.Clock.Advance(TimeSpan.FromDays(1));
        _home.Bind();

        Assert.Contains("- alpha: 一号主线。\n", Agents());
        Assert.DoesNotContain("在做", Agents());
    }

    [Fact]
    public void A_future_dated_activity_does_not_make_a_workstream_quiet_while_it_has_activity_inside_the_window()
    {
        WriteFullPersona();
        File.WriteAllText(WorkstreamsMd, "## Workstream\n### alpha\nBackground: 一号主线。\nRecent: 在做。\n### beta\nBackground: 二号主线。\nRecent: 最近在做。\n");
        WriteMonth("2026-10", "## 2026-10-20\n- [alpha] 窗口内的事\n");
        WriteMonth("2026-11", "## 2026-11-01\n- [beta] 较新的事\n## 2026-11-20\n- [alpha] 日期写错的未来活动\n");

        _home.Bind();
        var text = Agents();

        // Today is 2026-11-02: alpha has an activity inside the window, and its newest day not after today is 2026-10-20,
        // so beta, whose newest day is 2026-11-01, comes first.
        Assert.Contains("- alpha: 一号主线。 Recent: 在做。\n", text);
        AssertInOrder(text,
            "- beta: 二号主线。 Recent: 最近在做。\n",
            "- alpha: 一号主线。 Recent: 在做。\n");
    }

    [Fact]
    public void Scoped_entries_stay_out_of_the_block_and_a_domain_shows_only_its_index_row()
    {
        WriteScopedPersona();

        _home.Bind();
        var text = Agents();

        Assert.Contains("- network-proxy: 改代理、分流或测速时 (1 entry)", text);
        Assert.DoesNotContain("域内规则XYZ", text);
        Assert.DoesNotContain("项目规则QRS", text);
        Assert.DoesNotContain("## Domain:", text);
        Assert.DoesNotContain("## Workstream:", text);
    }

    [Fact]
    public void Sections_with_nothing_to_show_are_left_out()
    {
        WriteFullPersona();

        _home.Bind();
        var text = Agents();

        Assert.DoesNotContain("## Situational rules", text);
        Assert.DoesNotContain("## Recent weeks", text);
        Assert.DoesNotContain("## Workstreams", text);
    }

    [Fact]
    public void Resident_md_gives_guidance_for_the_new_sections_and_it_is_shown_above_their_data()
    {
        File.WriteAllText(ResidentMd, "SHELL\n\n## situational_rules\nRULES-GUIDE\n\n## workstreams\nWORKSTREAMS-GUIDE\n");
        WriteScopedPersona();
        File.WriteAllText(WorkstreamsMd, "## Workstream\n### alpha\nBackground: 一号主线。\n");

        _home.Bind();
        var text = Agents();

        AssertInOrder(text, "## Situational rules\nRULES-GUIDE\n- network-proxy", "## Workstreams\nWORKSTREAMS-GUIDE\n- alpha: 一号主线。\n</user_memory>");
    }

    [Fact]
    public void Resident_md_naming_a_removed_section_is_refused_and_the_name_is_given()
    {
        File.WriteAllText(ResidentMd, "SHELL-OK\n\n## projects\nX\n");
        WriteFullPersona();

        var refused = Assert.Throws<TacitException>(() => ResidentFile.Load(ResidentMd, MemoryLanguage.Zh));

        Assert.Contains("projects", refused.Message);
    }

    [Fact]
    public async Task Editing_the_workstreams_or_the_activity_log_rerenders_the_block()
    {
        WriteFullPersona();
        File.WriteAllText(WorkstreamsMd, "## Workstream\n### fresh\nBackground: 说明。\nRecent: 在做。\n");
        _home.Bind();
        using var service = Open();
        Assert.Contains("- fresh: 说明。\n", Agents());

        WriteMonth("2026-11", "## 2026-11-02\n- [fresh] 今天的事\n");
        _home.Sources!.MemoryChanged("recent/2026-11.md");
        await service.SettledAsync();
        Assert.Contains("- fresh: 说明。 Recent: 在做。\n", Agents());

        File.WriteAllText(WorkstreamsMd, "## Workstream\n### fresh\nBackground: 改过的说明。\n");
        _home.Sources!.MemoryChanged("workstreams.md");
        await service.SettledAsync();
        Assert.Contains("- fresh: 改过的说明。\n", Agents());
        Assert.NotNull(service);
    }

    [Fact]
    public async Task The_block_is_rendered_again_when_the_local_date_changes()
    {
        WriteFullPersona();
        File.WriteAllText(WorkstreamsMd, "## Workstream\n### alpha\nBackground: 一号主线。\nRecent: 在做。\n");
        WriteMonth("2026-10", "## 2026-10-06\n- [alpha] 窗口的第一天\n");
        _home.Bind();
        using var service = Open();
        Assert.Contains("- alpha: 一号主线。 Recent: 在做。\n", Agents());

        // Let the vector fill of the service's start finish first: Elapse would otherwise fire the timeout of a call still in flight.
        await service.SettledAsync();
        _home.Clock.Elapse(TimeSpan.FromDays(1));

        await service.SettledAsync();
        Assert.Contains("- alpha: 一号主线。\n", Agents());
        Assert.NotNull(service);
    }

}
