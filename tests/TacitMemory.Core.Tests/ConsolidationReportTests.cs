namespace TacitMemory.Core.Tests;

public sealed class ConsolidationReportTests : IDisposable
{
    private readonly TestHome _home = new();

    public void Dispose() => _home.Dispose();

    private string Memory => _home.DefaultMemory;

    private MemoryService Open()
    {
        _home.InitReady();
        return _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);
    }

    private void Commit(string message) =>
        TestHome.Git(Memory, "-c", "user.name=tacit", "-c", "user.email=tacit@localhost", "commit", "--allow-empty", "-m", message);

    /// <summary>Stages every change of the memory repository and commits it, so the files written so far are in the baseline.</summary>
    private void CommitAll(string message)
    {
        TestHome.Git(Memory, "add", "-A");
        Commit(message);
    }

    private string HeadHash() => TestHome.Git(Memory, "rev-parse", "--short", "HEAD").Trim();

    /// <summary>
    /// The text of one category: from its heading to the next heading of the second or third level, or to the end. A part
    /// heading ends the category above it, so a category never reads into the next part.
    /// </summary>
    private static string Section(string text, string title)
    {
        var start = text.IndexOf($"### {title}\n", StringComparison.Ordinal);
        Assert.True(start >= 0, $"No category \"{title}\" in the report.");
        var body = text[(start + title.Length + 5)..];
        var ends = new[] { "\n### ", "\n## " }
            .Select(marker => body.IndexOf(marker, StringComparison.Ordinal))
            .Where(index => index >= 0)
            .ToList();
        return ends.Count == 0 ? body : body[..ends.Min()];
    }

    /// <summary>Moves the test clock to 10:00 UTC on <paramref name="day"/>.</summary>
    private void ClockOn(DateOnly day)
    {
        var target = new DateTimeOffset(day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero);
        _home.Clock.Advance(target - _home.Clock.GetUtcNow());
    }

    [Fact]
    public async Task The_report_gives_the_absolute_memory_path_and_how_to_run_git_there()
    {
        using var service = Open();

        var report = await service.Tools.ConsolidationReportAsync();

        Assert.Contains(Memory, report);
        Assert.Contains($"git -C \"{Memory}\"", report);
    }

    [Fact]
    public async Task The_report_has_part_a_then_part_b_and_each_category_under_its_heading_in_that_order()
    {
        using var service = Open();

        var report = await service.Tools.ConsolidationReportAsync();

        var headings = new[]
        {
            "## A. Automatic\n",
            "### Format and rule violations\n",
            "### Persona candidates to remove\n",
            "### Weeks to summarize\n",
            "### Months to summarize\n",
            "### Workstream material\n",
            "## B. For proposals\n",
            "### Workstream proposal material\n",
            "### Persona evidence\n",
            "### Incident lessons\n",
            "### Changed pages\n",
            "### Suspected duplicate pages\n",
            "### Conflicting persona entries\n",
            "### Domain hygiene\n",
            "### Length hints\n",
        };
        var positions = headings.Select(heading => report.IndexOf(heading, StringComparison.Ordinal)).ToArray();
        Assert.All(positions, position => Assert.True(position >= 0));
        Assert.Equal(positions.Order().ToArray(), positions);
    }

    [Fact]
    public async Task A_category_with_nothing_to_report_says_none()
    {
        using var service = Open();

        var report = await service.Tools.ConsolidationReportAsync();

        Assert.Contains("None.", Section(report, "Persona candidates to remove"));
        Assert.Contains("None.", Section(report, "Suspected duplicate pages"));
        Assert.Contains("None.", Section(report, "Domain hygiene"));
        Assert.Contains("None.", Section(report, "Length hints"));
        Assert.Contains("None.", Section(report, "Workstream proposal material"));
    }

    [Fact]
    public async Task Unestablished_names_from_page_folders_and_activity_labels_are_listed_with_their_page_and_activity_counts()
    {
        _home.InitReady();
        Workstreams("## Workstream\n### alpha\nBackground: 已成立的主线。\n");
        WritePage("a1", "甲", "## 现象\n- 内容\n", workstream: "alpha");
        WritePage("n1", "乙", "## 现象\n- 内容\n", workstream: "newone");
        WritePage("n2", "丙", "## 现象\n- 内容\n", workstream: "newone");
        WriteMonth("2026-11", "## 2026-11-03\n- [newone] 新名字的活动\n- [newone] 另一件事\n- [labelonly] 只在活动里出现\n- [alpha] 已成立的活动\n");
        using var service = _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

        var section = Section(await service.Tools.ConsolidationReportAsync(), "Workstream proposal material");

        Assert.Contains("newone (unestablished): pages 2, activities 2", section);
        Assert.Contains("labelonly (unestablished): pages 0, activities 1", section);
        Assert.DoesNotContain("alpha", section);
    }

    [Fact]
    public async Task Topic_pages_sharing_a_prefix_of_three_or_more_are_listed_with_the_longest_shared_prefix_of_the_group()
    {
        _home.InitReady();
        foreach (var slug in new[] { "home-server", "home-server-proxy", "home-server-proxy-scope" })
            WritePage(slug, slug, "## 现象\n- 内容\n", workstream: "_topics");
        foreach (var slug in new[] { "cloud-a-x", "cloud-a-y", "cloud-b" })
            WritePage(slug, slug, "## 现象\n- 内容\n", workstream: "_topics");
        foreach (var slug in new[] { "pgroonga", "pgroonga-x" })
            WritePage(slug, slug, "## 现象\n- 内容\n", workstream: "_topics");
        using var service = _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

        var section = Section(await service.Tools.ConsolidationReportAsync(), "Workstream proposal material");

        Assert.Contains("prefix home-server: _topics/home-server, _topics/home-server-proxy, _topics/home-server-proxy-scope", section);
        Assert.Contains("prefix cloud: _topics/cloud-a-x, _topics/cloud-a-y, _topics/cloud-b", section);
        Assert.DoesNotContain("pgroonga", section);
    }

    [Fact]
    public async Task Activities_without_a_workstream_since_the_baseline_are_listed_and_older_ones_are_not()
    {
        _home.InitReady();
        WriteMonth("2026-10", "## 2026-10-20\n- 旧的无标签活动\n");
        CommitAll("consolidate: earlier pass");
        WriteMonth("2026-11", "## 2026-11-03\n- 新的无标签活动\n");
        using var service = _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

        var section = Section(await service.Tools.ConsolidationReportAsync(), "Workstream proposal material");

        Assert.Contains("新的无标签活动", section);
        Assert.DoesNotContain("旧的无标签活动", section);
    }

    [Fact]
    public async Task A_name_merged_into_another_workstream_is_no_longer_listed_once_the_consolidation_commit_follows_its_activities()
    {
        _home.InitReady();
        WritePage("a1", "甲", "## 现象\n- 内容\n", workstream: "oldname");
        WriteMonth("2026-11", "## 2026-11-03\n- [oldname] 旧标签的活动\n");
        using var service = _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);
        Assert.Contains("oldname (unestablished)", Section(await service.Tools.ConsolidationReportAsync(), "Workstream proposal material"));

        Directory.Move(Path.Combine(Memory, "pages", "oldname"), Path.Combine(Memory, "pages", "target"));
        Workstreams("## Workstream\n### target\nBackground: 合并后的主线。\n");
        CommitAll("consolidate: merge oldname into target");

        var section = Section(await service.Tools.ConsolidationReportAsync(), "Workstream proposal material");

        Assert.DoesNotContain("oldname", section);
    }

    [Fact]
    public async Task A_page_folder_emptied_by_moving_its_pages_out_is_not_listed_as_an_unestablished_name()
    {
        _home.InitReady();
        WritePage("a1", "甲", "## 现象\n- 内容\n", workstream: "emptied");
        WritePage("a2", "乙", "## 现象\n- 内容\n", workstream: "emptied");
        WritePage("n1", "丙", "## 现象\n- 内容\n", workstream: "stillhere");
        var stash = Path.Combine(Memory, "stash");
        Directory.CreateDirectory(stash);
        using var service = _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

        foreach (var file in Directory.EnumerateFiles(Path.Combine(Memory, "pages", "emptied"), "*.md").ToList())
            File.Move(file, Path.Combine(stash, Path.GetFileName(file)));
        Assert.True(Directory.Exists(Path.Combine(Memory, "pages", "emptied")));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(Memory, "pages", "emptied")));

        var section = Section(await service.Tools.ConsolidationReportAsync(), "Workstream proposal material");

        Assert.Contains("stillhere (unestablished): pages 1, activities 0", section);
        Assert.DoesNotContain("emptied", section);
    }

    [Fact]
    public async Task No_category_puts_a_section_over_its_limit_to_the_user()
    {
        _home.InitReady();
        File.WriteAllText(Path.Combine(Memory, "persona.md"), "## Preferences\n" + string.Concat(Enumerable.Range(1, 15).Select(i => $"- 第{i}条\n")));

        using var service = _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);
        var report = await service.Tools.ConsolidationReportAsync();

        Assert.Contains("None.", Section(report, "Length hints"));
    }

    [Fact]
    public async Task A_persona_entry_longer_than_the_hint_length_is_listed_with_its_place_and_length()
    {
        _home.InitReady("consolidation: { hintEntryLength: 20 }\n");
        File.WriteAllText(Path.Combine(Memory, "persona.md"), "## Preferences\n- 短\n- " + new string('长', 30) + "\n");
        using var service = _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

        var section = Section(await service.Tools.ConsolidationReportAsync(), "Length hints");

        Assert.Contains("persona.md", section);
        Assert.Contains("line 3", section);
        Assert.Contains("30 characters", section);
        Assert.DoesNotContain("line 2", section);
    }

    [Fact]
    public async Task An_entry_within_the_default_hint_length_is_not_listed()
    {
        _home.InitReady();
        File.WriteAllText(Path.Combine(Memory, "persona.md"), "## Preferences\n- " + new string('长', 120) + "\n");
        using var service = _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

        var section = Section(await service.Tools.ConsolidationReportAsync(), "Length hints");

        Assert.Contains("None.", section);
    }

    [Fact]
    public async Task An_activity_since_the_baseline_longer_than_the_hint_length_is_listed_with_its_place_and_length()
    {
        _home.InitReady("consolidation: { hintEntryLength: 20 }\n");
        WriteMonth("2026-11", "## 2026-11-02\n- 旧活动：短\n");
        CommitAll("consolidate: earlier pass");
        WriteMonth("2026-11", "## 2026-11-02\n- 旧活动：短\n- 新的活动" + new string('长', 30) + "\n");
        using var service = _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

        var section = Section(await service.Tools.ConsolidationReportAsync(), "Length hints");

        Assert.Contains("recent/2026-11.md", section);
        Assert.Contains("line 3", section);
        Assert.Contains("34 characters", section);
        Assert.DoesNotContain("旧活动", section);
    }

    [Fact]
    public async Task A_resident_block_longer_than_the_hint_length_gets_one_line_saying_so()
    {
        _home.InitReady("consolidation: { hintResidentLength: 100 }\n");
        File.WriteAllText(Path.Combine(Memory, "persona.md"), "## Preferences\n- " + new string('长', 120) + "\n");
        using var service = _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

        var section = Section(await service.Tools.ConsolidationReportAsync(), "Length hints");

        Assert.Contains("resident block", section);
        Assert.Contains("hint length of 100", section);
    }

    [Fact]
    public async Task Raising_the_hint_length_removes_the_hint_and_nothing_is_refused()
    {
        _home.InitReady("consolidation: { hintEntryLength: 500 }\n");
        File.WriteAllText(Path.Combine(Memory, "persona.md"), "## Preferences\n- " + new string('长', 300) + "\n");
        using var service = _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

        var section = Section(await service.Tools.ConsolidationReportAsync(), "Length hints");

        Assert.Contains("None.", section);
    }

    [Fact]
    public async Task The_header_gives_the_budget_for_the_model_judgments()
    {
        using var service = Open();

        var report = await service.Tools.ConsolidationReportAsync();

        Assert.Contains("Budget: 20 seconds for the model judgments", report);
    }

    [Fact]
    public async Task With_a_consolidation_commit_the_latest_one_is_the_baseline_even_when_empty()
    {
        using var service = Open();
        Commit("consolidate: first pass");
        var first = HeadHash();
        Commit("pages: create tacit-memory/db-upgrade");
        Commit("consolidate: second pass");
        var second = HeadHash();

        var prompt = await service.Tools.ConsolidationReportAsync();

        Assert.Contains(second, prompt);
        Assert.DoesNotContain(first, prompt);
    }

    [Fact]
    public async Task A_partial_consolidation_commit_leaves_the_baseline_and_its_increments_in_place()
    {
        _home.InitReady();
        CommitAll("consolidate: first pass");
        var baseline = HeadHash();
        WritePage("after-baseline", "基准之后的页", "## 现象\n- 内容\n");
        CommitAll("consolidate(partial): only summaries");
        using var service = _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

        var report = await service.Tools.ConsolidationReportAsync();

        Assert.Contains($"commit {baseline}", report);
        Assert.Contains("tacit-memory/after-baseline", Section(report, "Changed pages"));
    }

    [Fact]
    public async Task Without_a_consolidation_the_init_commit_is_the_baseline()
    {
        using var service = Open();
        var init = HeadHash();
        await service.Tools.WriteRecentAsync(new WriteRecentRequest { Text = "做了一件事" });

        var prompt = await service.Tools.ConsolidationReportAsync();

        Assert.Contains(init, prompt);
    }

    [Fact]
    public async Task With_neither_consolidation_nor_init_the_repository_has_never_been_checked()
    {
        _home.InitReady();
        var root = HeadHash();
        TestHome.Git(Memory, "-c", "user.name=tacit", "-c", "user.email=tacit@localhost", "commit", "--amend", "--allow-empty", "-m", "setup memory");
        using var service = _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

        var prompt = await service.Tools.ConsolidationReportAsync();

        Assert.Contains("Last consolidate: never", prompt);
        Assert.DoesNotContain(root, prompt);
    }

    [Fact]
    public async Task A_page_without_a_type_is_reported_in_every_report_even_after_a_consolidation_commit()
    {
        using var service = Open();
        var path = Path.Combine(Memory, "pages", "tacit-memory", "untyped.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "---\ntitle: 无类型的页\naliases: [无类型]\ndates: { first: 2026-10-30, last: 2026-10-30 }\n---\n\n## 要点\n- 内容\n");
        Commit("consolidate: earlier pass");

        var section = Section(await service.Tools.ConsolidationReportAsync(), "Format and rule violations");

        Assert.Contains("untyped.md", section);
        Assert.Contains("page type", section);
    }

    [Fact]
    public async Task A_category_not_tied_to_the_baseline_lists_at_most_twenty_items_oldest_first_counts_the_rest_and_makes_the_report_incomplete()
    {
        using var service = Open();
        // Twenty-one completed weeks, from ISO 2026-W02 (Monday 2026-01-05) to 2026-W22, each with one activity.
        WriteActivityLog(Enumerable.Range(0, 21).Select(week =>
            (new DateOnly(2026, 1, 5).AddDays(7 * week), "每周一件事")));
        ClockOn(new DateOnly(2026, 6, 1));

        var report = await service.Tools.ConsolidationReportAsync();
        var section = Section(report, "Weeks to summarize");

        Assert.Contains("2026-W02", section);
        Assert.Contains("2026-W21", section);
        Assert.DoesNotContain("2026-W22", section);
        Assert.Contains("1 more not listed", section);
        Assert.Contains("tacit-consolidate", section);
        Assert.Contains("Complete: no; Weeks to summarize lists only 20 findings.", report);
        Assert.Contains("consolidate(partial):", report);
    }

    [Fact]
    public async Task A_cancelled_request_throws_and_writes_nothing()
    {
        using var service = Open();
        var before = TestHome.Git(Memory, "status", "--porcelain");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.Tools.ConsolidationReportAsync(cancelled.Token));

        Assert.Equal(before, TestHome.Git(Memory, "status", "--porcelain"));
    }

    [Fact]
    public async Task Persona_evidence_lists_every_candidate_with_its_scope_section_and_occurrences_whatever_the_baseline()
    {
        _home.InitReady();
        File.WriteAllText(Path.Combine(Memory, "candidates.md"),
            "## Candidates\n\n### 回答尽量简短\nBelongs to: Preferences\nScope: workstream:tacit-memory\n- 2026-10-30 quote「别写那么长」\n- 2026-10-31 quote「直接说结论」\n\n## Rejection list\n");
        CommitAll("consolidate: earlier pass");
        using var service = _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

        var section = Section(await service.Tools.ConsolidationReportAsync(), "Persona evidence");

        Assert.Contains("回答尽量简短", section);
        Assert.Contains("workstream:tacit-memory", section);
        Assert.Contains("Preferences", section);
        Assert.Contains("2026-10-30", section);
        Assert.Contains("别写那么长", section);
        Assert.Contains("直接说结论", section);
    }

    [Fact]
    public async Task Quote_annotations_are_listed_only_when_their_activity_is_new_since_the_baseline_by_date_with_code_and_workstream()
    {
        _home.InitReady();
        WriteMonth("2026-10", "## 2026-10-20\n- [tacit-memory] 旧活动\n  <!-- quote「旧原话」 -->\n");
        CommitAll("consolidate: earlier pass");
        WriteMonth("2026-10", "## 2026-10-20\n- [tacit-memory] 旧活动\n  <!-- quote「旧原话」 -->\n\n## 2026-10-31\n- [tacit-memory] 新的做法\n  <!-- on win-main quote「新原话二」 -->\n\n## 2026-10-29\n- 无项目的活动\n  <!-- quote「新原话一」 -->\n");
        using var service = _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

        var section = Section(await service.Tools.ConsolidationReportAsync(), "Persona evidence");

        Assert.DoesNotContain("旧原话", section);
        Assert.Contains("新原话一", section);
        Assert.Contains("新原话二", section);
        Assert.DoesNotContain("win-main", section);
        Assert.Contains("tacit-memory", section);
        Assert.True(section.IndexOf("新原话一", StringComparison.Ordinal) < section.IndexOf("新原话二", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Lessons_of_incident_pages_are_quoted_only_when_the_page_changed_since_the_baseline()
    {
        _home.InitReady();
        WritePage("old-incident", "旧的故障", "## Lesson\n- 旧教训\n");
        CommitAll("consolidate: earlier pass");
        WritePage("new-incident", "新的故障", "## 现象\n- 出错了\n\n## Lesson\n- 先看日志再改配置\n");
        using var service = _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

        var section = Section(await service.Tools.ConsolidationReportAsync(), "Incident lessons");

        Assert.Contains("tacit-memory/new-incident", section);
        Assert.Contains("先看日志再改配置", section);
        Assert.DoesNotContain("旧教训", section);
    }

    [Fact]
    public async Task A_lesson_under_a_chinese_heading_is_content_and_not_quoted()
    {
        _home.InitReady();
        CommitAll("consolidate: earlier pass");
        WritePage("chinese-incident", "中文小节的故障", "## 教训\n- 写成中文小节名的教训\n");
        using var service = _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

        var section = Section(await service.Tools.ConsolidationReportAsync(), "Incident lessons");

        Assert.DoesNotContain("写成中文小节名的教训", section);
    }

    [Fact]
    public async Task Changed_pages_give_path_type_size_section_dates_related_and_uses_but_no_body_and_only_when_changed()
    {
        _home.InitReady();
        WritePage("unchanged-page", "没有变化的页", "## 现象\n- 旧内容\n");
        CommitAll("consolidate: earlier pass");
        WritePage("changed-page", "变化的页", "## 现象\n- 正文不应出现在报告里\n\n## 根因\n- 原因\n", related: "[tacit-memory/other-page]");
        WritePage("untyped-page", "没有类型的页", "## 要点\n- 内容\n", type: null);
        using var service = _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

        var section = Section(await service.Tools.ConsolidationReportAsync(), "Changed pages");

        Assert.DoesNotContain("unchanged-page", section);
        Assert.Contains("tacit-memory/changed-page", section);
        Assert.Contains("pages/tacit-memory/changed-page.md", section);
        Assert.Contains("type: incident", section);
        Assert.Contains("现象", section);
        Assert.Contains("根因", section);
        Assert.Contains("related: [tacit-memory/other-page]", section);
        Assert.Contains("uses: 0", section);
        Assert.DoesNotContain("正文不应出现在报告里", section);
        Assert.Contains("untyped-page", section);
        Assert.Contains("type: untyped;", section);
    }

    [Fact]
    public async Task Every_increment_since_the_baseline_is_listed_past_twenty_and_does_not_count_as_cut_short()
    {
        _home.InitReady();
        for (var i = 0; i < 21; i++)
            WritePage($"page-{i:00}", $"页 {i:00}", "## 现象\n- 内容\n");
        using var service = _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

        var report = await service.Tools.ConsolidationReportAsync();
        var section = Section(report, "Changed pages");

        Assert.Contains("tacit-memory/page-00", section);
        Assert.Contains("tacit-memory/page-20", section);
        Assert.DoesNotContain("more not listed", section);
        Assert.DoesNotContain("only 20 findings", report);
    }

    [Fact]
    public async Task A_domain_with_one_entry_is_listed_under_domain_hygiene()
    {
        _home.InitReady();
        File.WriteAllText(Path.Combine(Memory, "persona.md"),
            "## Red lines\n- 不要删库\n\n## Domain: network-proxy\nTrigger: 代理与分流相关的问题\n\n### Working rules\n- 先看分流规则\n- 再看回退方案\n\n## Domain: lonely\nTrigger: 只有一条的领域\n\n### Working rules\n- 唯一的一条\n");
        using var service = _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

        var section = Section(await service.Tools.ConsolidationReportAsync(), "Domain hygiene");

        Assert.Contains("domain:lonely", section);
        Assert.DoesNotContain("domain:network-proxy", section);
    }

    /// <summary>Writes the workstreams file, replacing any earlier text.</summary>
    private void Workstreams(string text) => File.WriteAllText(Path.Combine(Memory, "workstreams.md"), text.ReplaceLineEndings("\n"));

    /// <summary>
    /// Writes one page of the workstream tacit-memory unless <paramref name="workstream"/> names another, dated 2026-10-30; its type
    /// is written unless <paramref name="type"/> is null.
    /// </summary>
    private void WritePage(string slug, string title, string body, string? type = "incident", string related = "[]", string workstream = "tacit-memory")
    {
        var header = $"---\ntitle: {title}\naliases: [{title}, {title}别名]\ndates: {{ first: 2026-10-30, last: 2026-10-30 }}\n"
            + (type is null ? "" : $"type: {type}\n")
            + $"related: {related}\n---\n\n";
        var path = Path.Combine(Memory, "pages", workstream, slug + ".md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, (header + body).ReplaceLineEndings("\n"));
    }

    /// <summary>Writes one month file of the activity log, replacing any earlier text of that month.</summary>
    private void WriteMonth(string month, string text)
    {
        Directory.CreateDirectory(Path.Combine(Memory, "recent"));
        File.WriteAllText(Path.Combine(Memory, "recent", month + ".md"), text.ReplaceLineEndings("\n"));
    }

    /// <summary>Writes the activity log: one month file per month, each day under its heading.</summary>
    private void WriteActivityLog(IEnumerable<(DateOnly Day, string Text)> activities)
    {
        foreach (var month in activities.GroupBy(activity => activity.Day.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture)))
        {
            var text = string.Concat(month.GroupBy(activity => activity.Day).Select(day =>
                $"## {day.Key:yyyy-MM-dd}\n" + string.Concat(day.Select(activity => $"- {activity.Text}\n")) + "\n"));
            Directory.CreateDirectory(Path.Combine(Memory, "recent"));
            File.WriteAllText(Path.Combine(Memory, "recent", month.Key + ".md"), text.ReplaceLineEndings("\n"));
        }
    }
}
