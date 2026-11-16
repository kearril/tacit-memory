using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using static TacitMemory.Core.Tests.ShortCodes;

namespace TacitMemory.Core.Tests;

public sealed partial class RecentActivityTests : IDisposable
{
    private readonly TestHome _home = new();
    private MemoryService _service;

    public RecentActivityTests()
    {
        _home.InitReady();
        _service = _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);
    }

    public void Dispose()
    {
        _service.Dispose();
        _home.Dispose();
    }

    private string Memory => _home.DefaultMemory;

    private string MonthFile => Path.Combine(Memory, "recent", "2026-11.md");

    // Secret-shaped samples are assembled at run time so no scanner mistakes this file for a leak.
    private static readonly string OpenAiKey = "sk-" + "proj-Abc123def456GHI789jkl012MNO345";

    private static WriteRecentRequest Add(string text, string? workstream = null, string? source = null,
        bool? confirmNoSecret = null) => new()
        {
            Text = text,
            Workstream = workstream,
            Source = source,
            ConfirmNoSecret = confirmNoSecret,
        };

    private string Git(params string[] args) => TestHome.Git(Memory, args);

    [GeneratedRegex(@"\[([0-9a-f]{6}(?:-\d+)?)\]")]
    private static partial Regex CodeInReply();

    private static string CodeOf(ToolResult result) => CodeInReply().Match(result.Text).Groups[1].Value;

    [Fact]
    public async Task Adding_an_activity_creates_the_month_file_and_the_day_heading_and_commits_only_that_file()
    {
        File.WriteAllText(Path.Combine(Memory, "notes.txt"), "staged by the user\n");
        Git("add", "notes.txt");

        var result = await _service.WriteRecentAsync(Add("想清楚了画像里缺的是人的倾向"));

        Assert.False(result.IsError, result.Text);
        Assert.Equal("## 2026-11-02\n- 想清楚了画像里缺的是人的倾向\n", File.ReadAllText(MonthFile));
        Assert.Equal($"recent: add {ShortOf("想清楚了画像里缺的是人的倾向")}", Git("log", "-1", "--format=%s").Trim());
        Assert.Equal("recent/2026-11.md", Git("show", "--name-only", "--format=", "HEAD").Trim());
        Assert.Equal("A  notes.txt", Git("status", "--porcelain").TrimEnd());
    }

    [Fact]
    public async Task Entries_of_one_day_are_appended_under_one_heading_in_write_order()
    {
        await _service.WriteRecentAsync(Add("先查了索引"));
        await _service.WriteRecentAsync(Add("再换了分词器"));

        Assert.Equal("## 2026-11-02\n- 先查了索引\n- 再换了分词器\n", File.ReadAllText(MonthFile));
    }

    [Fact]
    public async Task A_new_day_gets_its_heading_appended_at_the_end_of_the_file()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MonthFile)!);
        File.WriteAllText(MonthFile, "## 2026-11-01\n- 昨天的事\n");

        await _service.WriteRecentAsync(Add("今天的事"));

        Assert.Equal("## 2026-11-01\n- 昨天的事\n## 2026-11-02\n- 今天的事\n", File.ReadAllText(MonthFile));
    }

    [Fact]
    public async Task Writing_under_a_day_keeps_every_other_byte_of_the_file()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MonthFile)!);
        File.WriteAllText(MonthFile, "## 2026-11-02\n- 早上的事\n\n手写备注  \n## 2026-11-01\n- 昨天的事\n");

        await _service.WriteRecentAsync(Add("下午的事"));

        Assert.Equal(
            "## 2026-11-02\n- 早上的事\n\n手写备注  \n- 下午的事\n## 2026-11-01\n- 昨天的事\n",
            File.ReadAllText(MonthFile));
    }

    [Fact]
    public async Task A_repeated_day_heading_receives_the_entry_under_the_first_one()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MonthFile)!);
        File.WriteAllText(MonthFile, "## 2026-11-02\n- 第一段\n## 2026-11-01\n- 昨天\n## 2026-11-02\n- 第二段\n");

        await _service.WriteRecentAsync(Add("新的一条"));

        Assert.Equal(
            "## 2026-11-02\n- 第一段\n- 新的一条\n## 2026-11-01\n- 昨天\n## 2026-11-02\n- 第二段\n",
            File.ReadAllText(MonthFile));
    }

    [Fact]
    public async Task Workstream_is_written_as_a_bracketed_prefix_in_lowercase()
    {
        var result = await _service.WriteRecentAsync(Add("决定先不做同主题判断", workstream: "Tacit-Memory"));

        Assert.False(result.IsError, result.Text);
        Assert.Equal("## 2026-11-02\n- [tacit-memory] 决定先不做同主题判断\n", File.ReadAllText(MonthFile));
    }

    [Fact]
    public async Task A_workstream_without_a_detail_page_is_accepted()
    {
        var result = await _service.WriteRecentAsync(Add("第一条", workstream: "brand-new.workstream"));

        Assert.False(result.IsError, result.Text);
        Assert.Contains("- [brand-new.workstream] 第一条", File.ReadAllText(MonthFile));
    }

    [Theory]
    [InlineData("my app")]
    [InlineData(".hidden")]
    [InlineData("_topics")]
    [InlineData("_private")]
    public async Task A_workstream_outside_the_naming_rule_is_refused_and_nothing_is_written(string workstream)
    {
        var result = await _service.WriteRecentAsync(Add("一件事", workstream: workstream));

        Assert.True(result.IsError);
        Assert.StartsWith("Refused:", result.Text);
        Assert.Contains("Next step:", result.Text);
        Assert.False(File.Exists(MonthFile));
    }

    [Fact]
    public async Task Source_is_written_as_an_indented_comment_under_the_entry()
    {
        var result = await _service.WriteRecentAsync(Add("记忆库本身就是项目", source: "其实可以直接把记忆库当成项目"));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(
            "## 2026-11-02\n- 记忆库本身就是项目\n  <!-- 原话「其实可以直接把记忆库当成项目」 -->\n",
            File.ReadAllText(MonthFile));
    }

    [Fact]
    public async Task Text_of_any_length_is_accepted_and_written_in_full()
    {
        var text = new string('字', 1500);

        var result = await _service.WriteRecentAsync(Add(text, workstream: "tacit-memory", source: new string('源', 150)));

        Assert.False(result.IsError, result.Text);
        Assert.Contains($"- [tacit-memory] {text}", File.ReadAllText(MonthFile));
    }

    [Fact]
    public async Task Text_starting_with_a_bracket_is_refused_and_the_next_step_names_workstream()
    {
        var result = await _service.WriteRecentAsync(Add("[tacit-memory] 已经标注过了"));

        Assert.True(result.IsError);
        Assert.StartsWith("Refused:", result.Text);
        Assert.Contains("Next step: Call write_recent again with the workstream parameter instead of a [workstream] prefix in text.", result.Text);
        Assert.False(File.Exists(MonthFile));
    }

    [Fact]
    public async Task Missing_text_is_refused()
    {
        var result = await _service.WriteRecentAsync(new WriteRecentRequest());

        Assert.True(result.IsError);
        Assert.StartsWith("Refused: Missing parameter text.\nNext step: ", result.Text);
    }

    [Fact]
    public async Task The_reply_carries_the_short_code_of_the_text_with_its_workstream_label()
    {
        var result = await _service.WriteRecentAsync(Add("决定先不做同主题判断", workstream: "tacit-memory"));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(ShortOf("[tacit-memory] 决定先不做同主题判断"), CodeOf(result));
    }

    [Fact]
    public async Task The_source_is_not_part_of_the_short_code()
    {
        var result = await _service.WriteRecentAsync(Add("同一件事", source: "原话一"));

        Assert.Equal(ShortOf("同一件事"), CodeOf(result));
    }

    [Fact]
    public async Task Identical_entries_on_the_same_month_get_a_numbered_suffix()
    {
        var first = await _service.WriteRecentAsync(Add("反复出现的一句"));
        var second = await _service.WriteRecentAsync(Add("反复出现的一句"));

        Assert.Equal(ShortOf("反复出现的一句"), CodeOf(first));
        Assert.Equal($"{ShortOf("反复出现的一句")}-2", CodeOf(second));
    }

    [Fact]
    public async Task Secret_in_text_is_refused_and_nothing_is_written()
    {
        var result = await _service.WriteRecentAsync(Add($"用的 key 是 {OpenAiKey}"));

        Assert.True(result.IsError);
        Assert.Contains("sk-pro", result.Text);
        Assert.Contains("confirm_no_secret=true", result.Text);
        Assert.False(File.Exists(MonthFile));
    }

    [Fact]
    public async Task Secret_in_source_is_refused()
    {
        var result = await _service.WriteRecentAsync(Add("普通的一句", source: $"我说过 {OpenAiKey}"));

        Assert.True(result.IsError);
        Assert.Contains("sk-pro", result.Text);
        Assert.False(File.Exists(MonthFile));
    }

    [Fact]
    public async Task Confirm_no_secret_lets_a_flagged_write_through()
    {
        var result = await _service.WriteRecentAsync(Add($"示例 {OpenAiKey}", confirmNoSecret: true));

        Assert.False(result.IsError, result.Text);
        Assert.Contains($"- 示例 {OpenAiKey}", File.ReadAllText(MonthFile));
    }

    [Fact]
    public async Task Replaces_rewrites_the_activity_in_place_and_keeps_its_source()
    {
        await _service.WriteRecentAsync(Add("先做的事", workstream: "tacit-memory", source: "原话一"));
        await _service.WriteRecentAsync(Add("第二件事"));
        var first = await _service.WriteRecentAsync(Add("第三件事"));

        var result = await _service.WriteRecentAsync(new WriteRecentRequest
        {
            Text = "改写后的先做的事",
            Workstream = "tacit-memory",
            Replaces = ShortOf("[tacit-memory] 先做的事"),
        });

        Assert.False(result.IsError, result.Text);
        Assert.Equal(ShortOf("[tacit-memory] 改写后的先做的事"), CodeOf(result));
        Assert.Equal(
            "## 2026-11-02\n- [tacit-memory] 改写后的先做的事\n  <!-- 原话「原话一」 -->\n- 第二件事\n- 第三件事\n",
            File.ReadAllText(MonthFile));
        Assert.Equal($"recent: replace {ShortOf("[tacit-memory] 改写后的先做的事")}", Git("log", "-1", "--format=%s").Trim());
        Assert.NotEqual(CodeOf(first), ShortOf("[tacit-memory] 改写后的先做的事"));
    }

    [Fact]
    public async Task Replaces_with_a_source_replaces_the_comment_and_without_one_keeps_it()
    {
        var added = await _service.WriteRecentAsync(Add("一件事", source: "旧话"));

        await _service.WriteRecentAsync(new WriteRecentRequest
        {
            Text = "一件事改写",
            Replaces = CodeOf(added),
            Source = "新话",
        });
        Assert.Equal("## 2026-11-02\n- 一件事改写\n  <!-- 原话「新话」 -->\n", File.ReadAllText(MonthFile));

        var kept = await _service.WriteRecentAsync(new WriteRecentRequest
        {
            Text = "一件事再改",
            Replaces = ShortOf("一件事改写"),
        });

        Assert.False(kept.IsError, kept.Text);
        Assert.Equal("## 2026-11-02\n- 一件事再改\n  <!-- 原话「新话」 -->\n", File.ReadAllText(MonthFile));
    }

    [Fact]
    public async Task Replaces_takes_the_workstream_label_from_the_new_call()
    {
        var added = await _service.WriteRecentAsync(Add("同一件事", workstream: "old-one"));

        var result = await _service.WriteRecentAsync(new WriteRecentRequest
        {
            Text = "同一件事",
            Workstream = "new-one",
            Replaces = CodeOf(added),
        });

        Assert.False(result.IsError, result.Text);
        Assert.Equal("## 2026-11-02\n- [new-one] 同一件事\n", File.ReadAllText(MonthFile));
    }

    [Fact]
    public async Task A_code_from_another_day_is_refused_and_today_s_activities_are_listed()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MonthFile)!);
        File.WriteAllText(MonthFile, "## 2026-11-01\n- 昨天的事\n");
        var today = await _service.WriteRecentAsync(Add("今天的事"));
        var before = File.ReadAllText(MonthFile);

        var result = await _service.WriteRecentAsync(new WriteRecentRequest
        {
            Text = "误改昨天",
            Replaces = ShortOf("昨天的事"),
        });

        Assert.True(result.IsError);
        Assert.Contains("Parameter replaces does not match an activity from 2026-11-02.", result.Text);
        Assert.Contains($"[{CodeOf(today)}] 今天的事", result.Text);
        Assert.DoesNotContain("昨天的事", result.Text);
        Assert.Equal(before, File.ReadAllText(MonthFile));
    }

    [Fact]
    public async Task An_expired_code_is_refused_and_the_current_activities_are_listed()
    {
        var added = await _service.WriteRecentAsync(Add("旧的说法"));
        await _service.WriteRecentAsync(new WriteRecentRequest
        {
            Text = "新的说法",
            Replaces = CodeOf(added),
        });

        var result = await _service.WriteRecentAsync(new WriteRecentRequest
        {
            Text = "又改一次",
            Replaces = CodeOf(added),
        });

        Assert.True(result.IsError);
        Assert.Contains($"[{ShortOf("新的说法")}] 新的说法", result.Text);
        Assert.Equal("## 2026-11-02\n- 新的说法\n", File.ReadAllText(MonthFile));
    }

    [Fact]
    public async Task Replaces_with_no_activity_today_is_refused_and_creates_no_file()
    {
        var result = await _service.WriteRecentAsync(new WriteRecentRequest
        {
            Text = "一件事",
            Replaces = "abc123",
        });

        Assert.True(result.IsError);
        Assert.Contains("No activities are recorded for 2026-11-02.", result.Text);
        Assert.False(File.Exists(MonthFile));
    }

    [Fact]
    public async Task A_secret_in_the_replacement_is_refused_and_the_activity_is_kept()
    {
        var added = await _service.WriteRecentAsync(Add("普通的一句"));

        var result = await _service.WriteRecentAsync(new WriteRecentRequest
        {
            Text = $"用的 key 是 {OpenAiKey}",
            Replaces = CodeOf(added),
        });

        Assert.True(result.IsError);
        Assert.Contains("confirm_no_secret=true", result.Text);
        Assert.Equal("## 2026-11-02\n- 普通的一句\n", File.ReadAllText(MonthFile));
    }

    [Fact]
    public async Task Replacing_with_the_same_text_changes_nothing_and_makes_no_commit()
    {
        var added = await _service.WriteRecentAsync(Add("不变的一句"));
        var commits = Git("rev-list", "--count", "HEAD");

        var result = await _service.WriteRecentAsync(new WriteRecentRequest
        {
            Text = "不变的一句",
            Replaces = CodeOf(added),
        });

        Assert.False(result.IsError, result.Text);
        Assert.Equal(commits, Git("rev-list", "--count", "HEAD"));
    }

    [Fact]
    public async Task The_reply_is_one_line_with_the_short_code_of_the_new_activity()
    {
        var result = await _service.WriteRecentAsync(Add("一件事", workstream: "tacit-memory"));

        Assert.False(result.IsError, result.Text);
        // The first line is the reply; the embedding note that a test without a model gets follows it.
        Assert.StartsWith($"Added activity [{ShortOf("[tacit-memory] 一件事")}].", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_rewrite_replies_with_the_short_code_of_the_rewritten_activity()
    {
        var added = await _service.WriteRecentAsync(Add("先说的一件事"));

        var result = await _service.WriteRecentAsync(new WriteRecentRequest { Text = "改写后的一件事", Replaces = CodeOf(added) });

        Assert.False(result.IsError, result.Text);
        Assert.StartsWith($"Replaced activity [{ShortOf("改写后的一件事")}].", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Adding_an_activity_leaves_the_workstream_file_alone()
    {
        File.WriteAllText(Path.Combine(Memory, "workstreams.md"), "## 主线\n### alpha\n背景：一号主线。\n");

        await _service.WriteRecentAsync(Add("一件事"));

        Assert.Equal("## 主线\n### alpha\n背景：一号主线。\n", File.ReadAllText(Path.Combine(Memory, "workstreams.md")));
    }

    [Fact]
    public async Task The_removed_parameters_are_refused_by_name_and_the_next_step_points_to_the_consolidation()
    {
        var result = await _service.WriteRecentAsync(new WriteRecentRequest
        {
            Text = "一件事",
            UnknownParameters = ["op", "items", "hash"],
        });

        Assert.True(result.IsError);
        Assert.StartsWith(
            "Refused:\n- Unknown parameter op.\n- Unknown parameter items.\n- Unknown parameter hash.\n"
            + "Next step: Call write_recent again with only these parameters: text, workstream, source, replaces, confirm_no_secret. "
            + "The workstream list is maintained by the tacit-consolidate skill.",
            result.Text);
        Assert.False(File.Exists(MonthFile));
    }

    [Fact]
    public async Task An_unknown_parameter_is_refused_by_name_and_nothing_is_written()
    {
        var result = await _service.WriteRecentAsync(Add("一件事") with { UnknownParameters = ["content"] });

        Assert.True(result.IsError);
        Assert.StartsWith(
            "Refused: Unknown parameter content.\nNext step: Call write_recent again with only these parameters: text, workstream, source, replaces, confirm_no_secret.",
            result.Text);
        Assert.False(File.Exists(MonthFile));
        Assert.Empty(Git("status", "--porcelain").Trim());
    }

    [Fact]
    public async Task A_project_parameter_is_refused_by_name_and_the_next_step_names_workstream()
    {
        var result = await _service.WriteRecentAsync(Add("一件事") with { UnknownParameters = ["project"] });

        Assert.StartsWith(
            "Refused: Unknown parameter project.\nNext step: Call write_recent again with only these parameters: text, workstream, source, replaces, confirm_no_secret.",
            result.Text);
        Assert.False(File.Exists(MonthFile));
    }

    [Fact]
    public async Task An_unknown_parameter_is_refused_before_a_missing_text_is_looked_at()
    {
        var result = await _service.WriteRecentAsync(new WriteRecentRequest { UnknownParameters = ["content"] });

        Assert.StartsWith("Refused: Unknown parameter content.\nNext step: ", result.Text);
        Assert.DoesNotContain("Missing parameter text", result.Text);
    }

    [Fact]
    public async Task A_user_edit_in_the_month_file_is_committed_with_the_write_and_noted()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MonthFile)!);
        File.WriteAllText(MonthFile, "## 2026-11-02\n- 早上的事\n");
        Git("add", "recent/2026-11.md");
        Git("commit", "-m", "chore: seed month file");
        File.AppendAllText(MonthFile, "- 手动补的一条\n");

        var result = await _service.WriteRecentAsync(Add("下午的事"));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(
            $"recent: add {ShortOf("下午的事")} (with user edits)",
            Git("log", "-1", "--format=%s").Trim());
        Assert.Contains("- 手动补的一条", Git("show", "HEAD:recent/2026-11.md"));
        Assert.Equal(string.Empty, Git("status", "--porcelain").Trim());
    }

    [Fact]
    public async Task Concurrent_writes_are_serialized_and_both_land()
    {
        var first = _service.WriteRecentAsync(Add("甲"));
        var second = _service.WriteRecentAsync(Add("乙"));

        var results = await Task.WhenAll(first, second);

        Assert.All(results, result => Assert.False(result.IsError, result.Text));
        Assert.Equal("## 2026-11-02\n- 甲\n- 乙\n", File.ReadAllText(MonthFile));
    }

    [Fact]
    public async Task A_successful_write_schedules_the_automatic_backup_for_the_day()
    {
        await _service.WriteRecentAsync(Add("第一条"));
        await _service.SettledAsync();

        Assert.True(File.Exists(Path.Combine(_home.TacitHome, "backups", "auto-2026-11-02.zip")));
    }

    [Fact]
    public async Task A_month_file_that_is_not_utf8_is_refused_and_left_alone()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MonthFile)!);
        var bytes = new byte[] { 0x23, 0x23, 0xFF, 0xFE, 0x0A };
        File.WriteAllBytes(MonthFile, bytes);

        var result = await _service.WriteRecentAsync(Add("一件事"));

        Assert.True(result.IsError);
        Assert.Equal(bytes, File.ReadAllBytes(MonthFile));
    }
}
