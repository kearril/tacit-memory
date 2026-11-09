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

    /// <summary>The text of one report category: from its heading to the next heading, or to the end.</summary>
    private static string Section(string text, string title)
    {
        var start = text.IndexOf($"### {title}\n", StringComparison.Ordinal);
        Assert.True(start >= 0, $"No category \"{title}\" in the report.");
        var body = text[(start + title.Length + 5)..];
        var next = body.IndexOf("\n### ", StringComparison.Ordinal);
        return next < 0 ? body : body[..next];
    }

    private void WriteCandidates(string candidates) =>
        File.WriteAllText(Path.Combine(Memory, "candidates.md"), candidates.ReplaceLineEndings("\n"));

    private static string Candidate(string title, string section, params string[] days) =>
        $"### {title}\n归属：{section}\n" + string.Concat(days.Select(day => $"- {day} 原话「{day}的话」\n")) + "\n";

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
    public async Task The_report_lists_the_six_categories_under_their_headings()
    {
        using var service = Open();

        var prompt = await service.ConsolidationReportAsync();

        foreach (var title in new[]
        {
            "Suspected duplicate pages",
            "Conflicting persona entries",
            "Confirmable persona candidates",
            "Persona candidates to remove",
            "Format and rule violations",
            "Stale mainline",
        })
        {
            Assert.Contains($"### {title}\n", prompt);
        }
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
    public async Task A_candidate_on_one_day_is_listed_because_there_is_no_day_threshold()
    {
        using var service = Open();
        WriteCandidates("# 候选\n\n## 候选\n"
            + Candidate("一天的候选", "偏好", "2026-10-30", "2026-10-30")
            + Candidate("两天的候选", "偏好", "2026-10-30", "2026-10-31")
            + "\n## 拒绝列表\n");

        var section = Section(await service.ConsolidationReportAsync(), "Confirmable persona candidates");

        Assert.Contains("两天的候选", section);
        Assert.Contains("「2026-10-31的话」", section);
        Assert.Contains("一天的候选", section);
    }

    [Fact]
    public async Task A_stale_mainline_is_reported_only_past_the_configured_days()
    {
        using var service = Open();
        var committed = DateOnly.Parse(TestHome.Git(Memory, "log", "-1", "--format=%as", "--", "recent.md").Trim(), System.Globalization.CultureInfo.InvariantCulture);

        ClockOn(committed.AddDays(14));
        var fresh = Section(await service.ConsolidationReportAsync(), "Stale mainline");
        Assert.DoesNotContain(committed.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), fresh);

        ClockOn(committed.AddDays(15));
        var stale = Section(await service.ConsolidationReportAsync(), "Stale mainline");
        Assert.Contains(committed.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), stale);
    }

    [Fact]
    public async Task Each_category_lists_at_most_twenty_items_and_counts_the_rest()
    {
        using var service = Open();
        WriteCandidates("## 候选\n"
            + string.Concat(Enumerable.Range(1, 21).Select(number =>
                Candidate($"habit-{number:00}", "偏好", "2026-10-30", "2026-10-31"))));

        var section = Section(await service.ConsolidationReportAsync(), "Confirmable persona candidates");

        Assert.Contains("habit-20", section);
        Assert.DoesNotContain("habit-21", section);
        Assert.Contains("1 more not listed", section);
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
}
