using System.Text.RegularExpressions;

namespace TacitMemory.Core.Tests;

public sealed class RecallActivityTests : IDisposable
{
    private static readonly float[] Travel = Axis(0);
    private static readonly float[] Elsewhere = Axis(1);

    private readonly TestHome _home = new();
    private readonly ModelDouble _models = new();
    private MemoryService? _service;

    public RecallActivityTests()
    {
        // Anything about travel expenses, however it is worded, lands on one vector.
        _models.Embed = text => text.Contains("差旅", StringComparison.Ordinal) || text.Contains("出差", StringComparison.Ordinal) ? Travel : Elsewhere;
        _home.InitReady(models: _models);
    }

    public void Dispose()
    {
        _service?.Dispose();
        _models.Dispose();
        _home.Dispose();
    }

    private MemoryService Service => _service ??= _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

    private string Memory => _home.DefaultMemory;

    private string MonthFile(string month = "2026-11") => Path.Combine(Memory, "recent", month + ".md");

    /// <summary>Writes a month file by hand, as the user would.</summary>
    private void Month(string text, string month = "2026-11")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MonthFile(month))!);
        File.WriteAllText(MonthFile(month), text.ReplaceLineEndings("\n"));
    }

    private static WriteRecentRequest Activity(string text, string? workstream = null) => new()
    {
        Text = text,
        Workstream = workstream,
    };

    private Task<ToolResult> Write(string text, string? workstream = null) => Service.WriteRecentAsync(Activity(text, workstream));

    private Task<ToolResult> Recall(string query, string? kind = null, string? workstream = null, string? from = null, string? to = null) =>
        Service.RecallAsync(new RecallRequest { Query = query, Kind = kind, Workstream = workstream, From = from, To = to });

    private Task WritePage(string page, string title, string body) => Service.WritePageAsync(new WritePageRequest
    {
        Op = "create",
        Type = ToolValues.PageTypes.Work,
        Page = page,
        Title = title,
        Aliases = ["别名甲", "别名乙"],
        Sections = [new("根因", body)],
    });

    private static string CodeOf(ToolResult result) => Regex.Match(result.Text, @"\[([0-9a-f]{6}(?:-\d+)?)\]").Groups[1].Value;

    private static float[] Axis(int index)
    {
        var vector = new float[8];
        vector[index] = 1f;
        return vector;
    }

    private static async Task Eventually(Func<Task<bool>> condition)
    {
        for (var waited = TimeSpan.Zero; waited < TimeSpan.FromSeconds(10); waited += TimeSpan.FromMilliseconds(100))
        {
            if (await condition())
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        Assert.Fail("The condition did not hold within 10 seconds.");
    }

    [Fact]
    public async Task Activities_are_recalled_by_kind_with_their_date_workstream_body_and_short_code()
    {
        var added = await Write("想清楚了画像里缺的是人的倾向", workstream: "tacit-memory");

        var result = await Recall("画像", kind: "activities");

        Assert.False(result.IsError, result.Text);
        Assert.Contains($"[1] activity 2026-11-02 [{CodeOf(added)}], workstream tacit-memory", result.Text);
        Assert.Contains("想清楚了画像里缺的是人的倾向", result.Text);
    }

    [Fact]
    public async Task Without_a_kind_pages_and_activities_are_recalled_together()
    {
        await WritePage("tacit-memory/db-upgrade", "数据库升级后扩展丢失", "PGroonga 扩展没有随升级重装。");
        await Write("数据库升级后中文搜索又没有结果", workstream: "tacit-memory");

        var result = await Recall("数据库升级");

        Assert.Contains("2 result(s).", result.Text);
        Assert.Contains("tacit-memory/db-upgrade", result.Text);
        Assert.Contains("activity 2026-11-02", result.Text);
        Assert.Contains("数据库升级后中文搜索又没有结果", result.Text);
    }

    [Fact]
    public async Task Kind_pages_leaves_out_activities_and_kind_activities_leaves_out_pages()
    {
        await WritePage("tacit-memory/db-upgrade", "数据库升级后扩展丢失", "PGroonga 扩展没有随升级重装。");
        await Write("数据库升级后中文搜索又没有结果", workstream: "tacit-memory");

        var pages = await Recall("数据库升级", kind: "pages");
        var activities = await Recall("数据库升级", kind: "activities");

        Assert.Contains("tacit-memory/db-upgrade", pages.Text);
        Assert.DoesNotContain("activity 2026-11-02", pages.Text);
        Assert.Contains("activity 2026-11-02", activities.Text);
        Assert.DoesNotContain("tacit-memory/db-upgrade", activities.Text);
    }

    [Fact]
    public async Task A_date_range_keeps_the_activities_of_the_days_in_it()
    {
        Month("## 2026-10-03\n- 部署失败在十月初\n## 2026-10-25\n- 部署失败在十月底\n", "2026-10");
        Month("## 2026-11-02\n- 部署失败在十一月\n");

        var ranged = await Recall("部署失败", kind: "activities", from: "2026-10-20", to: "2026-10-31");

        Assert.Contains("部署失败在十月底", ranged.Text);
        Assert.DoesNotContain("部署失败在十月初", ranged.Text);
        Assert.DoesNotContain("部署失败在十一月", ranged.Text);
    }

    [Fact]
    public async Task Workstream_keeps_only_the_activities_labelled_with_that_workstream()
    {
        Month("## 2026-11-02\n- [tacit-memory] 部署失败的根因在缓存\n- 部署失败另一个没有标签的活动\n");

        var result = await Recall("部署失败", kind: "activities", workstream: "tacit-memory");

        Assert.Contains("部署失败的根因在缓存", result.Text);
        Assert.DoesNotContain("没有标签的活动", result.Text);
    }

    [Fact]
    public async Task The_mainline_is_not_an_activity()
    {
        File.WriteAllText(Path.Combine(Memory, "workstreams.md"), "- 部署失败 主线内容\n");

        var result = await Recall("部署失败", kind: "activities");

        Assert.StartsWith("No relevant memory.", result.Text);
    }

    [Fact]
    public async Task Lines_without_a_date_and_headings_that_are_not_dates_are_skipped_without_blocking_the_others()
    {
        Month("- 没有日期的部署失败\n## 不是日期的标题\n- 不该被索引的部署失败\n## 2026-11-03\n- 有效的部署失败\n");

        var result = await Recall("部署失败", kind: "activities");

        Assert.Contains("有效的部署失败", result.Text);
        Assert.Contains("activity 2026-11-03", result.Text);
        Assert.DoesNotContain("没有日期的部署失败", result.Text);
        Assert.DoesNotContain("不该被索引的部署失败", result.Text);
    }

    [Fact]
    public async Task An_activity_written_through_the_service_is_listed_once()
    {
        await Write("只写一次的活动关于部署");

        var result = await Recall("部署", kind: "activities");

        Assert.Contains("[1] activity 2026-11-02", result.Text);
        Assert.DoesNotContain("[2] ", result.Text);
    }

    [Fact]
    public async Task Activities_edited_by_hand_while_running_are_searched_with_their_new_content()
    {
        Month("## 2026-11-02\n- 旧的活动内容\n");
        Assert.Contains("旧的活动内容", (await Recall("旧的活动", kind: "activities")).Text);

        Month("## 2026-11-02\n- 新的活动关于证书过期\n");
        await Eventually(async () => (await Recall("证书过期", kind: "activities")).Text.Contains("新的活动关于证书过期", StringComparison.Ordinal));

        File.Delete(MonthFile());
        await Eventually(async () => (await Recall("证书过期", kind: "activities")).Text.StartsWith("No relevant memory.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Activities_changed_while_the_service_was_stopped_are_synced_at_start()
    {
        await Write("旧的活动会被删掉");
        Assert.Contains("旧的活动会被删掉", (await Recall("旧的活动", kind: "activities")).Text);
        Service.Dispose();
        _service = null;

        Month("## 2026-11-02\n- 停服期间写入的活动关于证书\n");

        Assert.DoesNotContain("旧的活动会被删掉", (await Recall("旧的活动", kind: "activities")).Text);
        Assert.Contains("停服期间写入的活动", (await Recall("证书", kind: "activities")).Text);
    }

    [Fact]
    public async Task Without_the_embedding_model_activities_are_found_by_full_text_until_it_returns()
    {
        _models.EmbeddingDown = true;

        var written = await Write("差旅票据丢了，补办要找财务");
        var byWords = await Recall("票据丢了", kind: "activities");
        var bySense = await Recall("出差的钱怎么拿回来", kind: "activities");

        Assert.False(written.IsError, written.Text);
        Assert.Contains("embedding model is unavailable", written.Text);
        Assert.Contains("差旅票据丢了", byWords.Text);
        Assert.DoesNotContain("差旅票据丢了", bySense.Text);

        _models.EmbeddingDown = false;
        _home.Clock.Advance(TimeSpan.FromMinutes(1));
        await Recall("随便查一下");
        await Service.VectorCompletion;

        Assert.Contains("差旅票据丢了", (await Recall("出差的钱怎么拿回来", kind: "activities")).Text);
    }

    [Fact]
    public async Task An_activity_is_found_by_its_workstream_label_alone()
    {
        await Write("差旅报销单补交了", workstream: "tacit-memory");

        var result = await Recall("tacit-memory", kind: "activities");

        Assert.False(result.IsError, result.Text);
        Assert.Contains("差旅报销单补交了", result.Text);
    }
}
