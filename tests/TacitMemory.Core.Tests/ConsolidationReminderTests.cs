using System.Diagnostics;
using System.Globalization;
using TacitMemory.Core.Show;

namespace TacitMemory.Core.Tests;

public sealed class ConsolidationReminderTests : IDisposable
{
    private const string Heading = "## Consolidation reminder";

    private const string TwoDayCandidate = "## 候选\n\n### 回答尽量简短\n归属：偏好\n- 2026-10-30 原话「别写那么长」\n- 2026-10-31 原话「直接说结论」\n\n## 拒绝列表\n";

    private readonly TestHome _home = new();
    private readonly ModelDouble _models = new();

    public void Dispose()
    {
        _models.Dispose();
        _home.Dispose();
    }

    private string Memory => _home.DefaultMemory;

    private string PersonaMd => Path.Combine(Memory, "persona.md");

    private string CandidatesMd => Path.Combine(Memory, "candidates.md");

    /// <summary>The day of the init commit, which the reminder counts from when no consolidation has run.</summary>
    private DateOnly InitDay => DateOnly.ParseExact(
        TestHome.Git(Memory, "log", "-1", "--format=%as").Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>A ready memory with a non-empty workstream, so only the triggers a test sets off bring the reminder.</summary>
    private void Ready(string extraConfig = "")
    {
        _home.InitReady(extraConfig, _models);
        File.WriteAllText(Path.Combine(Memory, "workstreams.md"), "## 主线\n### alpha\n背景：有背景的主线。\n");
    }

    private MemoryService Open() => _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

    private void WriteCandidates(string text) => File.WriteAllText(CandidatesMd, text.ReplaceLineEndings("\n"));

    /// <summary>Moves the test clock to 10:00 UTC on <paramref name="day"/>.</summary>
    private void ClockOn(DateOnly day)
    {
        var target = new DateTimeOffset(day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero);
        _home.Clock.Advance(target - _home.Clock.GetUtcNow());
    }

    /// <summary>An empty commit in the memory repository, dated <paramref name="day"/> by both git dates.</summary>
    private void CommitOn(DateOnly day, string message)
    {
        var date = $"{day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}T10:00:00+00:00";
        var start = new ProcessStartInfo("git") { WorkingDirectory = Memory, UseShellExecute = false };
        foreach (var arg in new[] { "-c", "user.name=tacit", "-c", "user.email=tacit@localhost", "commit", "--allow-empty", "-m", message })
            start.ArgumentList.Add(arg);
        start.Environment["GIT_AUTHOR_DATE"] = date;
        start.Environment["GIT_COMMITTER_DATE"] = date;
        using var process = Process.Start(start)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    /// <summary>The text of one report category: from its heading to the next heading, or to the end.</summary>
    private static string Section(string text, string title)
    {
        var start = text.IndexOf($"### {title}\n", StringComparison.Ordinal);
        Assert.True(start >= 0, $"No category \"{title}\" in the report.");
        var body = text[(start + title.Length + 5)..];
        var next = body.IndexOf("\n### ", StringComparison.Ordinal);
        return next < 0 ? body : body[..next];
    }

    private static async Task Eventually(Func<Task<bool>> condition)
    {
        for (var waited = TimeSpan.Zero; waited < TimeSpan.FromSeconds(10); waited += TimeSpan.FromMilliseconds(100))
        {
            if (await condition())
                return;
            await Task.Delay(100);
        }
        Assert.Fail("condition not met within 10 s");
    }

    [Fact]
    public async Task The_reminder_appears_once_the_last_consolidation_is_seven_days_old()
    {
        Ready();
        using var service = Open();
        Assert.DoesNotContain(Heading, await service.ServerInstructionsAsync());

        ClockOn(InitDay.AddDays(6));
        Assert.DoesNotContain(Heading, await service.ServerInstructionsAsync());

        ClockOn(InitDay.AddDays(7));
        var instructions = await service.ServerInstructionsAsync();
        Assert.Contains($"{Heading}\nNo consolidation has been run yet; the repository was set up 7 days ago, on {InitDay:yyyy-MM-dd}.", instructions);
        Assert.EndsWith("suggest it when the moment fits, and do not start one unasked.", instructions);
    }

    [Fact]
    public async Task The_days_come_from_consolidation_remind_after_days()
    {
        Ready("consolidation:\n  remindAfterDays: 3\n");
        using var service = Open();

        ClockOn(InitDay.AddDays(2));
        Assert.DoesNotContain(Heading, await service.ServerInstructionsAsync());

        ClockOn(InitDay.AddDays(3));
        Assert.Contains(Heading, await service.ServerInstructionsAsync());
    }

    [Fact]
    public async Task Candidates_below_remind_candidates_do_not_bring_the_reminder()
    {
        Ready();
        WriteCandidates(TwoDayCandidate);
        using var service = Open();

        Assert.DoesNotContain(Heading, await service.ServerInstructionsAsync());
    }

    [Fact]
    public async Task Candidates_reaching_remind_candidates_bring_the_reminder_with_their_count()
    {
        Ready("consolidation:\n  remindCandidates: 2\n");
        var second = "\n### 先给结论\n归属：偏好\n- 2026-10-31 原话「直接说结论」\n";
        WriteCandidates(TwoDayCandidate.Replace("\n## 拒绝列表", second + "\n## 拒绝列表"));
        using var service = Open();

        var instructions = await service.ServerInstructionsAsync();

        Assert.Contains(Heading, instructions);
        Assert.Contains("- Persona candidates: 2.", instructions);
    }

    [Fact]
    public async Task A_candidate_written_at_remind_candidates_one_brings_the_reminder_when_the_threshold_is_one()
    {
        Ready("consolidation:\n  remindCandidates: 1\n");
        WriteCandidates(TwoDayCandidate);
        using var service = Open();

        var instructions = await service.ServerInstructionsAsync();

        Assert.Contains(Heading, instructions);
        Assert.Contains("- Persona candidates: 1.", instructions);
        Assert.Contains("No consolidation has been run yet", instructions);
    }

    [Fact]
    public async Task A_completed_week_without_a_summary_brings_the_reminder_with_the_weeks_count()
    {
        Ready();
        CommitOn(InitDay, "consolidate: pass");
        var lastWeek = TestHome.Today.AddDays(-14);
        Directory.CreateDirectory(Path.Combine(Memory, "recent"));
        File.WriteAllText(Path.Combine(Memory, "recent", lastWeek.ToString("yyyy-MM", CultureInfo.InvariantCulture) + ".md"),
            $"## {lastWeek:yyyy-MM-dd}\n- 做了一件事\n");
        using var service = Open();

        var instructions = await service.ServerInstructionsAsync();

        Assert.Contains(Heading, instructions);
        Assert.Contains("- Weeks to summarize: 1.", instructions);
    }

    [Fact]
    public async Task An_empty_workstream_brings_the_reminder_with_the_workstream_line()
    {
        Ready();
        File.WriteAllText(Path.Combine(Memory, "workstreams.md"), "## 主线\n");
        CommitOn(InitDay, "consolidate: pass");
        using var service = Open();

        var instructions = await service.ServerInstructionsAsync();

        Assert.Contains(Heading, instructions);
        Assert.Contains("- Workstream: empty.", instructions);
    }

    [Fact]
    public async Task A_candidate_written_while_running_brings_the_reminder_on_the_next_instructions()
    {
        Ready("consolidation:\n  remindCandidates: 1\n");
        using var service = Open();
        Assert.DoesNotContain(Heading, await service.ServerInstructionsAsync());

        WriteCandidates(TwoDayCandidate);

        await Eventually(async () => (await service.ServerInstructionsAsync()).Contains("- Persona candidates: 1.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Format_problems_alone_do_not_bring_the_reminder()
    {
        Ready();
        File.WriteAllText(PersonaMd, "## 红线\n- 不要删库\n\n## 偏好\n- 沟通：先要结论\n");
        using var service = Open();

        Assert.DoesNotContain(Heading, await service.ServerInstructionsAsync());
        var prompt = await service.ConsolidationReportAsync();
        Assert.DoesNotContain("None.", Section(prompt, "Format and rule violations"));
    }

    [Fact]
    public async Task Building_the_reminder_calls_no_model()
    {
        Ready("consolidation:\n  remindCandidates: 1\n");
        WriteCandidates(TwoDayCandidate);
        using var service = Open();
        await service.VectorCompletion;
        var embeddings = _models.EmbeddingCalls;
        var decisions = _models.DecisionCalls;

        Assert.Contains(Heading, await service.ServerInstructionsAsync());
        ClockOn(InitDay.AddDays(1));
        Assert.Contains(Heading, await service.ServerInstructionsAsync());

        Assert.Equal(embeddings, _models.EmbeddingCalls);
        Assert.Equal(decisions, _models.DecisionCalls);
    }

    [Fact]
    public async Task A_consolidation_commit_clears_the_reminder_in_the_next_instructions()
    {
        Ready();
        using var service = Open();
        var today = InitDay.AddDays(7);
        ClockOn(today);
        Assert.Contains(Heading, await service.ServerInstructionsAsync());

        CommitOn(today, "consolidate: no changes");

        await Eventually(async () => !(await service.ServerInstructionsAsync()).Contains(Heading, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_memory_repository_git_cannot_read_leaves_the_reminder_out_and_the_service_running()
    {
        Ready();
        using var service = Open();
        var initDay = InitDay;
        Directory.Move(Path.Combine(Memory, ".git"), Path.Combine(Memory, ".git-away"));
        ClockOn(initDay.AddDays(7));

        var instructions = await service.ServerInstructionsAsync();

        Assert.DoesNotContain(Heading, instructions);
        Assert.Contains("Memory is written in Chinese.", instructions);
    }

    [Fact]
    public async Task Show_instructions_include_the_reminder_when_one_is_due()
    {
        Ready();
        ClockOn(InitDay.AddDays(7));

        var result = await _home.Tacit().ShowAsync(ShowTarget.Instructions);

        Assert.Contains(Heading, result.Report);
        Assert.Contains(result.Parts, part => part.Key == "consolidation");
    }

    [Fact]
    public async Task Show_instructions_leave_the_reminder_out_when_none_is_due()
    {
        Ready();

        var result = await _home.Tacit().ShowAsync(ShowTarget.Instructions);

        Assert.DoesNotContain(Heading, result.Report);
        Assert.DoesNotContain(result.Parts, part => part.Key == "consolidation");
    }
}
