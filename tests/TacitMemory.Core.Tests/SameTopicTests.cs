using System.Text.RegularExpressions;

namespace TacitMemory.Core.Tests;

public sealed class SameTopicTests : IDisposable
{
    private const string NoneText = "None of the earlier activity notes is about the same thing as the new note.";

    private static readonly Regex ReplyCode = new(@"\[([0-9a-f]{6}(?:-\d+)?)\]");

    private readonly TestHome _home = new();
    private readonly ModelDouble _models = new();
    private MemoryService? _service;

    public SameTopicTests() => _home.InitReady(models: _models);

    public void Dispose()
    {
        _service?.Dispose();
        _models.Dispose();
        _home.Dispose();
    }

    private MemoryService Service => _service ??= _home.Tacit().OpenService(_ => { });

    private string MonthFile => Path.Combine(_home.DefaultMemory, "recent", "2026-11.md");

    private static WriteRecentRequest Add(string text, string? project = null, string? replaces = null) => new()
    {
        Op = "add_activity",
        Text = text,
        Project = project,
        Replaces = replaces,
    };

    private static string CodeOf(ToolResult result) => ReplyCode.Match(result.Text).Groups[1].Value;

    private int EntryCount() => File.ReadAllLines(MonthFile).Count(line => line.StartsWith("- ", StringComparison.Ordinal));

    /// <summary>
    /// Makes the decision model pick the option whose text contains <paramref name="text"/> with
    /// <paramref name="probability"/>; the "none" option gets nothing, so the threshold alone decides.
    /// </summary>
    private void Match(string text, double probability) => _models.Choose = (_, options) => options.ToDictionary(
        option => option.Key,
        option => option.Key != options.Keys.Last() && option.Value.Contains(text, StringComparison.Ordinal) ? probability : 0.0);

    /// <summary>Writes an activity and returns the state and the options the decision model was asked with.</summary>
    private async Task<(string State, IReadOnlyDictionary<string, string> Options)> AskedFor(string text, string? project = null)
    {
        string? state = null;
        IReadOnlyDictionary<string, string>? options = null;
        _models.Choose = (asked, offered) =>
        {
            state = asked;
            options = offered;
            return offered.Keys.ToDictionary(key => key, key => key == offered.Keys.Last() ? 1.0 : 0.0);
        };
        await Service.WriteRecentAsync(Add(text, project));
        return (state ?? throw new InvalidOperationException("The decision model was not asked."),
            options ?? throw new InvalidOperationException("The decision model was not asked."));
    }

    [Fact]
    public async Task A_repeat_of_an_activity_from_today_is_not_written_and_the_reply_names_it_for_merging()
    {
        var first = await Service.WriteRecentAsync(Add("先查了索引"));
        var code = CodeOf(first);
        Match("先查了索引", 0.95);
        var before = File.ReadAllText(MonthFile);

        var result = await Service.WriteRecentAsync(Add("索引查完了，问题在分词器"));

        Assert.False(result.IsError, result.Text);
        Assert.Contains($"[{code}]", result.Text);
        Assert.Contains("先查了索引", result.Text);
        Assert.Contains("one merged sentence", result.Text);
        Assert.Contains($"replaces set to {code}", result.Text);
        Assert.Equal(before, File.ReadAllText(MonthFile));
    }

    [Fact]
    public async Task A_same_topic_refusal_makes_no_commit_and_schedules_no_backup()
    {
        var backup = Path.Combine(_home.TacitHome, "backups", "auto-2026-11-02.zip");
        await Service.WriteRecentAsync(Add("先查了索引"));
        await Service.BackupCompletion;
        File.Delete(backup);
        Match("先查了索引", 0.95);
        var head = TestHome.Git(_home.DefaultMemory, "rev-parse", "HEAD");

        var result = await Service.WriteRecentAsync(Add("索引查完了，问题在分词器"));

        await Service.BackupCompletion;
        Assert.False(result.IsError, result.Text);
        Assert.Equal(head, TestHome.Git(_home.DefaultMemory, "rev-parse", "HEAD"));
        Assert.False(File.Exists(backup));
    }

    [Theory]
    [InlineData(0.2, false)]
    [InlineData(0.6, false)]
    [InlineData(0.85, true)]
    [InlineData(0.95, true)]
    public async Task Only_a_match_the_model_is_certain_of_is_refused(double probability, bool refused)
    {
        await Service.WriteRecentAsync(Add("先查了索引"));
        Match("先查了索引", probability);

        var result = await Service.WriteRecentAsync(Add("索引之外的另一件事"));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(refused ? 1 : 2, EntryCount());
    }

    [Fact]
    public async Task The_decision_model_is_asked_only_when_today_already_has_activities()
    {
        await Service.WriteRecentAsync(Add("先查了索引"));
        Assert.Equal(0, _models.DecisionCalls);

        await Service.WriteRecentAsync(Add("另一件事"));

        Assert.Equal(1, _models.DecisionCalls);
    }

    [Fact]
    public async Task Every_project_of_today_is_offered_and_none_comes_last()
    {
        await Service.WriteRecentAsync(Add("先查了索引", project: "alpha"));

        var (_, options) = await AskedFor("另一件事", project: "beta");

        Assert.Equal(2, options.Count);
        Assert.Contains("[alpha] 先查了索引", options.Values);
        Assert.Equal(NoneText, options.Values.Last());
    }

    [Fact]
    public async Task The_state_is_the_new_entry_with_its_project_label()
    {
        await Service.WriteRecentAsync(Add("先查了索引"));

        var (state, _) = await AskedFor("另一件事", project: "beta");

        Assert.Equal("[beta] 另一件事", state);
    }

    [Fact]
    public async Task At_most_nine_of_todays_activities_are_offered_and_they_are_the_latest()
    {
        for (var i = 0; i < 10; i++)
            await Service.WriteRecentAsync(Add($"第 {i} 件事"));

        var (_, options) = await AskedFor("另一件事");

        Assert.Equal(10, options.Count);
        Assert.DoesNotContain("第 0 件事", options.Values);
        Assert.Contains("第 9 件事", options.Values);
    }

    [Fact]
    public async Task Replacing_an_activity_skips_the_same_activity_check()
    {
        var first = await Service.WriteRecentAsync(Add("先查了索引"));
        Match("先查了索引", 0.95);

        var result = await Service.WriteRecentAsync(Add("索引查完了，问题在分词器", replaces: CodeOf(first)));

        Assert.False(result.IsError, result.Text);
        Assert.StartsWith("Replaced activity [", result.Text);
        Assert.Equal(0, _models.DecisionCalls);
    }

    [Fact]
    public async Task With_the_decision_model_down_the_activity_is_written_and_the_skipped_check_is_noted()
    {
        await Service.WriteRecentAsync(Add("先查了索引"));
        _models.DecisionDown = true;

        var result = await Service.WriteRecentAsync(Add("先查了索引，结论是分词器"));

        Assert.False(result.IsError, result.Text);
        Assert.StartsWith("Added activity [", result.Text);
        Assert.Contains("was not checked", result.Text);
        Assert.Equal(2, EntryCount());
    }

    [Fact]
    public async Task The_configured_same_activity_threshold_replaces_the_default()
    {
        using var home = new TestHome();
        home.InitReady("decision:\n  sameTopic: { certain: 0.5, unlikely: 0.1 }\n", models: _models);
        using var service = home.Tacit().OpenService(_ => { });
        await service.WriteRecentAsync(Add("先查了索引"));
        Match("先查了索引", 0.6);

        var result = await service.WriteRecentAsync(Add("索引之外的另一件事"));

        Assert.False(result.IsError, result.Text);
        Assert.StartsWith("Not added:", result.Text);
        Assert.Equal(1, File.ReadAllLines(Path.Combine(home.DefaultMemory, "recent", "2026-11.md"))
            .Count(line => line.StartsWith("- ", StringComparison.Ordinal)));
    }
}
