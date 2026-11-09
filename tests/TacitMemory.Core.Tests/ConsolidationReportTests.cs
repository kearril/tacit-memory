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

        var report = await service.ConsolidationReportAsync();

        Assert.Contains(Memory, report);
        Assert.Contains($"git -C \"{Memory}\"", report);
    }

    [Fact]
    public async Task The_report_has_part_a_then_part_b_and_each_category_under_its_heading_in_that_order()
    {
        using var service = Open();

        var report = await service.ConsolidationReportAsync();

        var headings = new[]
        {
            "## A. Automatic\n",
            "### Format and rule violations\n",
            "### Persona candidates to remove\n",
            "### Weeks to summarize\n",
            "### Months to summarize\n",
            "### Mainline material\n",
            "## B. For proposals\n",
            "### Suspected duplicate pages\n",
            "### Conflicting persona entries\n",
            "### Sections over their limit\n",
        };
        var positions = headings.Select(heading => report.IndexOf(heading, StringComparison.Ordinal)).ToArray();
        Assert.All(positions, position => Assert.True(position >= 0));
        Assert.Equal(positions.Order().ToArray(), positions);
    }

    [Fact]
    public async Task A_category_with_nothing_to_report_says_none()
    {
        using var service = Open();

        var report = await service.ConsolidationReportAsync();

        Assert.Contains("None.", Section(report, "Persona candidates to remove"));
        Assert.Contains("None.", Section(report, "Suspected duplicate pages"));
        Assert.Contains("None.", Section(report, "Sections over their limit"));
    }

    [Fact]
    public async Task The_header_gives_the_budget_for_the_model_judgments()
    {
        using var service = Open();

        var report = await service.ConsolidationReportAsync();

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

        var prompt = await service.ConsolidationReportAsync();

        Assert.Contains(second, prompt);
        Assert.DoesNotContain(first, prompt);
    }

    [Fact]
    public async Task Without_a_consolidation_the_init_commit_is_the_baseline()
    {
        using var service = Open();
        var init = HeadHash();
        await service.WriteRecentAsync(new WriteRecentRequest { Text = "做了一件事" });

        var prompt = await service.ConsolidationReportAsync();

        Assert.Contains(init, prompt);
    }

    [Fact]
    public async Task With_neither_consolidation_nor_init_the_repository_has_never_been_checked()
    {
        _home.InitReady();
        var root = HeadHash();
        TestHome.Git(Memory, "-c", "user.name=tacit", "-c", "user.email=tacit@localhost", "commit", "--amend", "--allow-empty", "-m", "setup memory");
        using var service = _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

        var prompt = await service.ConsolidationReportAsync();

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

        var section = Section(await service.ConsolidationReportAsync(), "Format and rule violations");

        Assert.Contains("untyped.md", section);
        Assert.Contains("page type", section);
    }

    [Fact]
    public async Task Each_category_lists_at_most_twenty_items_oldest_first_and_counts_the_rest()
    {
        using var service = Open();
        // Twenty-one completed weeks, from ISO 2026-W02 (Monday 2026-01-05) to 2026-W22, each with one activity.
        WriteActivityLog(Enumerable.Range(0, 21).Select(week =>
            (new DateOnly(2026, 1, 5).AddDays(7 * week), "每周一件事")));
        ClockOn(new DateOnly(2026, 6, 1));

        var section = Section(await service.ConsolidationReportAsync(), "Weeks to summarize");

        Assert.Contains("2026-W02", section);
        Assert.Contains("2026-W21", section);
        Assert.DoesNotContain("2026-W22", section);
        Assert.Contains("1 more not listed", section);
        Assert.Contains("tacit-consolidate", section);
    }

    [Fact]
    public async Task A_cancelled_request_throws_and_writes_nothing()
    {
        using var service = Open();
        var before = TestHome.Git(Memory, "status", "--porcelain");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ConsolidationReportAsync(cancelled.Token));

        Assert.Equal(before, TestHome.Git(Memory, "status", "--porcelain"));
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
