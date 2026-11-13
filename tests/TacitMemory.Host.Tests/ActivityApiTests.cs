using System.Net;
using System.Text;
using System.Text.Json;
using TacitMemory.Core;
using TacitMemory.Core.Tests;
using TacitMemory.Host.Api;

namespace TacitMemory.Host.Tests;

/// <summary>
/// <c>GET /api/activity</c> over real HTTP: the activity log counted per day and workstream over the 53-week window, the
/// consolidation days, the month files that cannot be read, and the refusal of another device without a session. The test
/// clock shows Monday 2026-11-02, so the 53-week window runs from Monday 2025-11-03 to that day.
/// </summary>
public sealed class ActivityApiTests : IAsyncLifetime
{
    private readonly TestHome _home = new();
    private MemoryService _service = null!;
    private ServiceHost _host = null!;
    private HttpClient _local = null!;
    private HttpClient _remote = null!;

    public async Task InitializeAsync()
    {
        _home.InitReady();
        _service = _home.Tacit().OpenService(_ => { }, new CapturingLoggerFactory());
        _host = await ServiceHost.TryStartAsync(_service, port: 0, NullLoggerProvider.Instance, PeerClients.OnlyLoopbackIsLocal)
            ?? throw new InvalidOperationException("Port 0 is never in use.");
        _local = PeerClients.Local();
        _remote = PeerClients.Remote();
    }

    public async Task DisposeAsync()
    {
        _local.Dispose();
        _remote.Dispose();
        await _host.DisposeAsync();
        _service.Dispose();
        _home.Dispose();
    }

    private Uri Url => new($"http://127.0.0.1:{_host.Endpoint.Port}/api/activity");

    private string Recent => Path.Combine(_home.DefaultMemory, "recent");

    private void WriteMonth(string month, string text) =>
        File.WriteAllText(Path.Combine(Recent, month + ".md"), text.ReplaceLineEndings("\n"), new UTF8Encoding(false));

    private async Task<ActivityReport> ReadAsync()
    {
        using var response = await _local.GetAsync(Url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonSerializer.Deserialize<ActivityReport>(await response.Content.ReadAsStringAsync(), ManagementApiJson.Options)!;
    }

    /// <summary>Makes an empty commit in the memory repository with the given subject, authored on the given day.</summary>
    private void Commit(string subject, DateOnly day) =>
        TestHome.Git(
            _home.DefaultMemory,
            "-c", "user.name=test", "-c", "user.email=test@example.com",
            "commit", "--allow-empty", "-m", subject, $"--date={day:yyyy-MM-dd}T12:00:00");

    [Fact]
    public async Task Each_activity_counts_once_by_its_workstream_label_or_as_unlabeled_across_month_files()
    {
        Directory.CreateDirectory(Recent);
        WriteMonth("2026-10", """
            ## 2026-10-30
            - [tacit-memory] 第一条
              续行不计
              <!-- 来源「对话」 -->
            - [tacit-memory] 第二条
            - [mac-mini] 第三条
            - 没有标注的一条

            ## 汇总
            - 2026-W44：汇总不计
            """);
        WriteMonth("2026-11", """
            ## 2026-11-02
            - [mac-mini] 今天
            ## 2026-11-03
            - [mac-mini] 明天之后不返回
            """);

        var report = await ReadAsync();

        Assert.Equal(new DateOnly(2025, 11, 3), report.From);
        Assert.Equal(new DateOnly(2026, 11, 2), report.To);
        Assert.Equal([new DateOnly(2026, 10, 30), new DateOnly(2026, 11, 2)], report.Days.Select(day => day.Day));
        var october = report.Days[0];
        Assert.Equal([new WorkstreamCount("tacit-memory", 2), new WorkstreamCount("mac-mini", 1)], october.Workstreams);
        Assert.Equal(1, october.Unlabeled);
        Assert.Equal([new WorkstreamCount("mac-mini", 1)], report.Days[1].Workstreams);
        Assert.Empty(report.SkippedFiles);
    }

    [Fact]
    public async Task The_window_starts_on_the_monday_52_weeks_before_this_week_and_files_before_it_are_not_read()
    {
        Directory.CreateDirectory(Recent);
        WriteMonth("2025-11", """
            ## 2025-11-02
            - 窗口之前一天
            ## 2025-11-03
            - 窗口第一天
            """);
        // Not valid UTF-8, but entirely before the window: it is never opened, so it is not reported as skipped.
        File.WriteAllBytes(Path.Combine(Recent, "2025-10.md"), [0x23, 0x23, 0x20, 0xC3, 0x28]);

        var report = await ReadAsync();

        var day = Assert.Single(report.Days);
        Assert.Equal(new DateOnly(2025, 11, 3), day.Day);
        Assert.Equal(1, day.Unlabeled);
        Assert.Empty(report.SkippedFiles);
    }

    [Fact]
    public async Task A_month_file_that_is_not_utf8_is_skipped_and_named_and_the_rest_is_returned()
    {
        Directory.CreateDirectory(Recent);
        File.WriteAllBytes(Path.Combine(Recent, "2026-09.md"), [0x23, 0x23, 0x20, 0xC3, 0x28]);
        WriteMonth("2026-10", """
            ## 2026-10-01
            - [mac-mini] 照常计数
            """);

        var report = await ReadAsync();

        Assert.Equal(["recent/2026-09.md"], report.SkippedFiles);
        Assert.Equal(new DateOnly(2026, 10, 1), Assert.Single(report.Days).Day);
    }

    [Fact]
    public async Task The_consolidation_days_are_the_consolidate_commits_inside_the_window()
    {
        Commit("consolidate: 窗口之前", new DateOnly(2025, 10, 1));
        Commit("consolidate: 第一次", new DateOnly(2026, 10, 20));
        Commit("consolidate: 同一天第二次", new DateOnly(2026, 10, 20));
        Commit("record: 不是整理", new DateOnly(2026, 10, 21));
        Commit("consolidate: 第二次", new DateOnly(2026, 10, 27));

        var report = await ReadAsync();

        Assert.Equal([new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 27)], report.ConsolidationDays);
        Assert.False(report.HistoryUnreadable);
    }

    [Fact]
    public async Task A_history_that_cannot_be_read_leaves_the_consolidation_days_empty_and_still_counts_the_days()
    {
        Directory.CreateDirectory(Recent);
        WriteMonth("2026-10", """
            ## 2026-10-30
            - 照常计数
            """);
        Directory.Move(Path.Combine(_home.DefaultMemory, ".git"), Path.Combine(_home.DefaultMemory, ".git-moved"));

        var report = await ReadAsync();

        Assert.True(report.HistoryUnreadable);
        Assert.Empty(report.ConsolidationDays);
        Assert.Single(report.Days);
    }

    [Fact]
    public async Task No_activity_log_returns_an_empty_window()
    {
        var report = await ReadAsync();

        Assert.Empty(report.Days);
        Assert.Empty(report.SkippedFiles);
    }

    [Fact]
    public async Task Another_device_without_a_session_is_refused_with_401()
    {
        using var response = await _remote.GetAsync(Url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var error = JsonSerializer.Deserialize<ApiError>(await response.Content.ReadAsStringAsync(), ManagementApiJson.Options)!;
        Assert.Equal(nameof(TacitError.ApiUnauthorized), error.Error);
    }
}
