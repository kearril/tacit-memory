using System.Text;

namespace TacitMemory.Core.Tests;

/// <summary>
/// The service log read back: the days that have a file, newest first, and one day's events. A line that starts with a time
/// and a level begins an event and every other line belongs to the one before it; a last line the writer has not finished
/// is left for the next read; the file is read while the service still writes to it.
/// </summary>
public sealed class ServiceLogTests : IDisposable
{
    private static readonly DateOnly Today = TestHome.Today;
    private static readonly DateOnly Yesterday = Today.AddDays(-1);

    private readonly TestHome _home = new();
    private MemoryService? _service;

    public ServiceLogTests() => _home.InitReady();

    public void Dispose()
    {
        _service?.Dispose();
        _home.Dispose();
    }

    private MemoryService Service => _service ??= _home.Tacit().OpenService(_ => { }, new CapturingLoggerFactory());

    private string Logs => Path.Combine(_home.TacitHome, "logs");

    private string FileOf(DateOnly day) => Path.Combine(Logs, $"tacit-{day:yyyyMMdd}.log");

    private void Write(DateOnly day, string text)
    {
        Directory.CreateDirectory(Logs);
        File.WriteAllText(FileOf(day), text, new UTF8Encoding(false));
    }

    [Fact]
    public void The_days_are_the_log_files_newest_first_and_other_files_are_ignored()
    {
        Write(Yesterday, "");
        Write(Today, "");
        Write(Today.AddDays(-5), "");
        File.WriteAllText(Path.Combine(Logs, "tacit-2026110.log"), "");
        File.WriteAllText(Path.Combine(Logs, "notes.txt"), "");

        var days = Service.LogDays();

        Assert.Equal(Today, days.Today);
        Assert.Equal([Today, Yesterday, Today.AddDays(-5)], days.Dates);
    }

    [Fact]
    public void Without_a_logs_directory_there_are_no_days()
    {
        var days = Service.LogDays();

        Assert.Equal(Today, days.Today);
        Assert.Empty(days.Dates);
    }

    [Fact]
    public void Each_event_is_read_with_its_time_level_and_message()
    {
        Write(Yesterday, """
            2026-11-01 09:15:02.031 +08:00 INF 常驻服务启动：版本 1.0.0
            2026-11-01 09:15:03.500 +08:00 WRN 额外监听绑定失败
            2026-11-01 09:16:00.000 +08:00 ERR 备份失败

            """.ReplaceLineEndings("\n"));

        var day = Service.ServiceLog(Yesterday.ToString("yyyy-MM-dd"));

        Assert.Equal(Yesterday, day.Date);
        Assert.Equal(Today, day.Today);
        Assert.Equal(3, day.Next);
        Assert.Equal(
            [
                new ServiceLogEntry(new DateTimeOffset(2026, 11, 1, 9, 15, 2, 31, TimeSpan.FromHours(8)), ServiceLogLevel.Information, "常驻服务启动：版本 1.0.0"),
                new ServiceLogEntry(new DateTimeOffset(2026, 11, 1, 9, 15, 3, 500, TimeSpan.FromHours(8)), ServiceLogLevel.Warning, "额外监听绑定失败"),
                new ServiceLogEntry(new DateTimeOffset(2026, 11, 1, 9, 16, 0, 0, TimeSpan.FromHours(8)), ServiceLogLevel.Error, "备份失败"),
            ],
            day.Entries);
    }

    [Fact]
    public void Lines_without_a_time_and_level_belong_to_the_event_before_them()
    {
        Write(Yesterday, """
            2026-11-01 09:15:02.031 +08:00 ERR config.yaml 无法加载：
            - models.decision.apiKey：未设置
            - backup.webdav.password：未设置
            2026-11-01 09:20:00.000 +08:00 WRN 请求失败
            System.Threading.Tasks.TaskCanceledException: The operation was canceled.
               at Somewhere.Run()

            """.ReplaceLineEndings("\r\n"));

        var entries = Service.ServiceLog(Yesterday.ToString("yyyy-MM-dd")).Entries;

        Assert.Equal(
            [
                "config.yaml 无法加载：\n- models.decision.apiKey：未设置\n- backup.webdav.password：未设置",
                "请求失败\nSystem.Threading.Tasks.TaskCanceledException: The operation was canceled.\n   at Somewhere.Run()",
            ],
            entries.Select(entry => entry.Message));
    }

    [Fact]
    public void Lines_before_the_first_event_are_an_event_of_their_own_without_a_time()
    {
        Write(Yesterday, "- models.decision.apiKey：未设置\n2026-11-01 09:15:02.031 +08:00 INF 常驻服务启动\n");

        var entries = Service.ServiceLog(Yesterday.ToString("yyyy-MM-dd")).Entries;

        Assert.Equal(2, entries.Count);
        Assert.Null(entries[0].Time);
        Assert.Equal(ServiceLogLevel.Information, entries[0].Level);
        Assert.Equal("- models.decision.apiKey：未设置", entries[0].Message);
    }

    [Theory]
    [InlineData("DBG", ServiceLogLevel.Information)]
    [InlineData("VRB", ServiceLogLevel.Information)]
    [InlineData("FTL", ServiceLogLevel.Error)]
    public void The_levels_the_service_never_writes_are_read_as_the_nearest_of_the_three(string code, ServiceLogLevel level)
    {
        Write(Yesterday, $"2026-11-01 09:15:02.031 +08:00 {code} 消息\n");

        Assert.Equal(level, Assert.Single(Service.ServiceLog(Yesterday.ToString("yyyy-MM-dd")).Entries).Level);
    }

    [Fact]
    public void A_last_line_without_its_line_break_is_left_for_the_next_read()
    {
        Write(Today, "2026-11-02 10:00:00.000 +00:00 INF 第一条\n2026-11-02 10:00:01.000 +00:00 INF 第二");

        var before = Service.ServiceLog(Today.ToString("yyyy-MM-dd"));
        File.AppendAllText(FileOf(Today), "条\n");
        var after = Service.ServiceLog(Today.ToString("yyyy-MM-dd"));

        Assert.Equal(["第一条"], before.Entries.Select(entry => entry.Message));
        Assert.Equal(1, before.Next);
        Assert.Equal(["第一条", "第二条"], after.Entries.Select(entry => entry.Message));
    }

    [Fact]
    public void A_file_the_service_holds_open_for_writing_is_still_read()
    {
        Write(Today, "");
        using (var writer = new FileStream(FileOf(Today), FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            writer.Write(Encoding.UTF8.GetBytes("2026-11-02 10:00:00.000 +00:00 INF 写入中\n"));
            writer.Flush();

            Assert.Equal(["写入中"], Service.ServiceLog(Today.ToString("yyyy-MM-dd")).Entries.Select(entry => entry.Message));
        }
    }

    [Fact]
    public void Today_without_a_file_has_no_events()
    {
        var day = Service.ServiceLog(Today.ToString("yyyy-MM-dd"));

        Assert.Empty(day.Entries);
        Assert.Equal(0, day.Next);
    }

    [Fact]
    public void Another_day_without_a_file_is_not_found()
    {
        var refusal = Assert.Throws<TacitException>(() => Service.ServiceLog(Yesterday.ToString("yyyy-MM-dd")));

        Assert.Equal(TacitError.LogDateNotFound, refusal.Error);
        Assert.Contains("2026-11-01", refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("20261101")]
    [InlineData("2026-13-01")]
    [InlineData("yesterday")]
    public void A_date_that_is_not_a_calendar_date_is_refused(string date)
    {
        var refusal = Assert.Throws<TacitException>(() => Service.ServiceLog(date));

        Assert.Equal(TacitError.RequestInvalid, refusal.Error);
    }
}
