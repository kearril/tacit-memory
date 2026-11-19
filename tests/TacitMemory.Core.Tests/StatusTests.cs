namespace TacitMemory.Core.Tests;

/// <summary>
/// The status report through the facade: the columns of a running service, the offline report, the report of an invalid
/// config, and the auxiliary models' states, cooldown included. Nothing here calls a model or the network.
/// </summary>
public sealed class StatusTests : IDisposable
{
    private static readonly Uri Endpoint = new("http://127.0.0.1:18888/mcp");

    private readonly TestHome _home = new();
    private readonly ModelDouble _models = new();
    private MemoryService? _service;

    public StatusTests() => _home.InitReady(models: _models);

    public void Dispose()
    {
        _service?.Dispose();
        _models.Dispose();
        _home.Dispose();
    }

    private MemoryService Service => _service ??= _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

    private string ConfigPath => Path.Combine(_home.TacitHome, "config.yaml");

    [Fact]
    public async Task A_running_service_reports_its_endpoint_version_uptime_and_the_three_models()
    {
        // The service's start is the moment it opens, so it opens before the clock moves on.
        var running = Service;
        _home.Clock.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(7));

        var report = await running.Reports.StatusAsync(Endpoint);

        var service = Assert.IsType<ServiceStatus>(report.Service);
        Assert.True(service.Running);
        Assert.Equal(Endpoint, service.Endpoint);
        Assert.False(string.IsNullOrWhiteSpace(service.Version));
        Assert.Equal(307, service.UptimeSeconds);
        Assert.False(service.OtherProgramAnswered);
        Assert.Equal(
            new[] { "运行中", "地址：http://127.0.0.1:18888/mcp", $"版本：{service.Version}", "已运行：5 分钟 7 秒", "Web 界面：http://127.0.0.1:18888/ui/" },
            service.Lines);

        var models = Assert.IsType<ModelsStatus>(report.Models);
        Assert.Equal(new[] { "embedding", "reranker", "decision" }, models.Items.Select(item => item.Role));
        Assert.All(models.Items, item => Assert.Equal(ModelState.Available, item.State));
        Assert.Equal(["辅助模型"], new[] { models.Title });
        Assert.Equal(3, models.Lines.Count);

        Assert.Null(report.Config);
        Assert.Equal(Path.Combine(_home.TacitHome, "logs"), report.Logs!.Directory);
    }

    [Fact]
    public async Task The_uptime_names_each_unit_that_is_not_zero()
    {
        var service = Service;
        _home.Clock.Advance(TimeSpan.FromDays(1) + TimeSpan.FromHours(2) + TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(4));

        var report = await service.Reports.StatusAsync(Endpoint);

        Assert.Contains("已运行：1 天 2 小时 3 分钟 4 秒", Assert.IsType<ServiceStatus>(report.Service).Lines);
    }

    [Fact]
    public async Task A_model_whose_request_failed_is_cooling_down_with_the_seconds_left_and_recovers_after_the_cooldown()
    {
        _models.EmbeddingDown = true;
        await Service.Tools.RecallAsync(new RecallRequest { Query = "部署" });
        _home.Clock.Advance(TimeSpan.FromSeconds(10));

        var cooling = Assert.IsType<ModelsStatus>((await Service.Reports.StatusAsync(Endpoint)).Models);
        var embedding = Assert.Single(cooling.Items, item => item.Role == "embedding");
        Assert.Equal(ModelState.CoolingDown, embedding.State);
        Assert.Equal(20, embedding.CoolingSeconds);
        Assert.Contains("embedding：冷却中，剩余 20 秒", cooling.Lines);
        Assert.Equal(ModelState.Available, Assert.Single(cooling.Items, item => item.Role == "reranker").State);

        _home.Clock.Advance(TimeSpan.FromSeconds(20));

        var recovered = Assert.IsType<ModelsStatus>((await Service.Reports.StatusAsync(Endpoint)).Models);
        var after = Assert.Single(recovered.Items, item => item.Role == "embedding");
        Assert.Equal(ModelState.Available, after.State);
        Assert.Null(after.CoolingSeconds);
    }

    [Fact]
    public async Task An_unconfigured_model_is_listed_beside_an_available_and_a_cooling_one()
    {
        File.WriteAllText(ConfigPath, $"language: zh\nmodels:\n  embedding: {{ url: '{_models.EmbeddingUrl}', model: double-embedding }}\n  decision: {{ url: '{_models.DecisionUrl}', model: double-decision, apiKey: test-key }}\n");
        _models.EmbeddingDown = true;
        await Service.Tools.RecallAsync(new RecallRequest { Query = "部署" });
        _home.Clock.Advance(TimeSpan.FromSeconds(10));

        var models = Assert.IsType<ModelsStatus>((await Service.Reports.StatusAsync(Endpoint)).Models);

        Assert.Equal(new[] { "embedding", "reranker", "decision" }, models.Items.Select(item => item.Role));
        Assert.Equal(ModelState.CoolingDown, Assert.Single(models.Items, item => item.Role == "embedding").State);
        Assert.Equal(20, Assert.Single(models.Items, item => item.Role == "embedding").CoolingSeconds);
        Assert.Equal(ModelState.Unconfigured, Assert.Single(models.Items, item => item.Role == "reranker").State);
        Assert.Equal(ModelState.Available, Assert.Single(models.Items, item => item.Role == "decision").State);
        Assert.Equal(new[] { "embedding：冷却中，剩余 20 秒", "reranker：未配置", "decision：可用" }, models.Lines);
        Assert.Equal(new StatusLevel?[] { StatusLevel.Attention, null, StatusLevel.Ok }, ((IStatusColumn)models).LineLevels());
    }

    [Fact]
    public async Task The_offline_report_says_the_service_is_not_running_and_leaves_out_the_columns_that_need_it()
    {
        var tacit = _home.Tacit();
        var config = tacit.LoadConfig();

        var report = await tacit.OfflineStatus(config);

        var service = Assert.IsType<ServiceStatus>(report.Service);
        Assert.False(service.Running);
        Assert.Equal(["服务未运行", "地址：http://127.0.0.1:18888/mcp", "Web 界面：http://127.0.0.1:18888/ui/"], service.Lines);
        Assert.Equal(Endpoint, service.Endpoint);
        Assert.Null(service.Version);
        Assert.Null(service.UptimeSeconds);
        Assert.False(service.OtherProgramAnswered);
        Assert.Null(report.Models);
        Assert.Null(report.Config);
        Assert.Equal(Path.Combine(_home.TacitHome, "logs"), report.Logs!.Directory);
    }

    [Fact]
    public async Task When_another_program_answers_the_port_the_offline_report_names_it()
    {
        var tacit = _home.Tacit();
        var config = tacit.LoadConfig();

        var service = Assert.IsType<ServiceStatus>((await tacit.OfflineStatus(config, portAnsweredByOther: true)).Service);

        Assert.False(service.Running);
        Assert.True(service.OtherProgramAnswered);
        Assert.Equal(
            [$"服务未运行：端口 {config.Port} 有其他程序在回答", $"地址：http://127.0.0.1:{config.Port}/mcp", $"Web 界面：http://127.0.0.1:{config.Port}/ui/"],
            service.Lines);
    }

    [Fact]
    public void The_hint_for_commands_that_need_the_service_names_the_command_to_run()
    {
        var tacit = _home.Tacit();
        var config = tacit.LoadConfig();

        Assert.Equal("服务未运行，先执行 tacit serve。", tacit.ServiceNotRunningMessage(config.Language, config.Port, portAnsweredByOther: false));
        Assert.Equal(
            $"端口 {config.Port} 有其他程序在回答，常驻服务未运行。请先执行 tacit serve，或在 config.yaml 中修改 server.port。",
            tacit.ServiceNotRunningMessage(config.Language, config.Port, portAnsweredByOther: true));
    }

    [Fact]
    public void An_invalid_config_is_reported_with_every_wrong_key_path_and_the_logs_location()
    {
        File.WriteAllText(ConfigPath, "language: zh\nmodels:\n  embedding: { endpoint: 'nope' }\n");
        var tacit = _home.Tacit();
        var error = Assert.Throws<TacitException>(() => tacit.LoadConfig());

        var report = tacit.InvalidConfigStatus(error, tacit.ReadLooseSettings());

        Assert.False(Assert.IsType<ServiceStatus>(report.Service).Running);
        Assert.Null(report.Models);
        var config = Assert.IsType<ConfigStatus>(report.Config);
        Assert.Contains(config.Lines, line => line.Contains("models.embedding.endpoint", StringComparison.Ordinal));
        Assert.Contains(config.Lines, line => line.Contains("models.embedding.url", StringComparison.Ordinal));
        Assert.DoesNotContain(config.Lines, line => line.Contains("models.reranker", StringComparison.Ordinal));
        Assert.Equal(Path.Combine(_home.TacitHome, "logs"), report.Logs!.Directory);
    }

    [Fact]
    public async Task The_logs_column_counts_todays_warnings_and_errors_in_every_report()
    {
        var logs = Path.Combine(_home.TacitHome, "logs");
        Directory.CreateDirectory(logs);
        File.WriteAllText(Path.Combine(logs, $"tacit-{TestHome.Today:yyyyMMdd}.log"), """
            2026-11-02 09:00:00.000 +00:00 WRN 一
            2026-11-02 09:00:01.000 +00:00 INF 二
            2026-11-02 09:00:02.000 +00:00 WRN 三
            2026-11-02 09:00:03.000 +00:00 ERR 四

            """.ReplaceLineEndings("\n"));
        File.WriteAllText(Path.Combine(logs, $"tacit-{TestHome.Today.AddDays(-1):yyyyMMdd}.log"), "2026-11-01 09:00:00.000 +00:00 ERR 昨天\n");
        var tacit = _home.Tacit();

        var running = (await Service.Reports.StatusAsync(Endpoint)).Logs!;
        var offline = (await tacit.OfflineStatus(tacit.LoadConfig())).Logs!;
        File.WriteAllText(ConfigPath, "language: zh\nmodels:\n  embedding: { endpoint: 'nope' }\n");
        var invalid = tacit.InvalidConfigStatus(Assert.Throws<TacitException>(() => tacit.LoadConfig()), tacit.ReadLooseSettings()).Logs!;

        foreach (var column in new[] { running, offline, invalid })
        {
            Assert.Equal(2, column.Warnings);
            Assert.Equal(1, column.Errors);
            Assert.Contains("今天：2 条警告，1 条错误", column.Lines);
            Assert.Equal(StatusLevel.Problem, ((IStatusColumn)column).LineLevels()[^1]);
        }
    }

    [Fact]
    public async Task Without_a_log_today_the_logs_column_says_there_is_nothing_to_look_at()
    {
        var column = (await Service.Reports.StatusAsync(Endpoint)).Logs!;

        Assert.Equal(0, column.Warnings);
        Assert.Equal(0, column.Errors);
        Assert.Contains("今天没有警告与错误", column.Lines);
        Assert.Null(((IStatusColumn)column).LineLevels()[^1]);
    }

    [Fact]
    public async Task A_log_that_cannot_be_read_today_leaves_the_counts_unknown_and_says_why()
    {
        var logs = Path.Combine(_home.TacitHome, "logs");
        Directory.CreateDirectory(logs);
        var path = Path.Combine(logs, $"tacit-{TestHome.Today:yyyyMMdd}.log");
        File.WriteAllText(path, "2026-11-02 09:00:00.000 +00:00 ERR 一\n");

        LogsStatus column;
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            column = (await Service.Reports.StatusAsync(Endpoint)).Logs!;

        Assert.Null(column.Warnings);
        Assert.Null(column.Errors);
        Assert.StartsWith("无法读取今天的日志：", column.Lines[^1]);
        Assert.Equal(StatusLevel.Problem, ((IStatusColumn)column).LineLevels()[^1]);
    }

    [Fact]
    public void The_loose_port_is_server_port_even_when_other_keys_are_wrong()
    {
        File.WriteAllText(ConfigPath, "language: zh\nmodels:\n  embedding: { endpoint: 'nope' }\nserver:\n  port: 19001\n");

        Assert.Equal(19001, _home.Tacit().ReadLooseSettings().Port);
    }

    [Fact]
    public void The_loose_port_is_the_default_when_the_config_file_is_missing()
    {
        File.Delete(ConfigPath);

        Assert.Equal(18888, _home.Tacit().ReadLooseSettings().Port);
    }

    [Fact]
    public void The_loose_port_is_the_default_when_the_yaml_cannot_be_parsed()
    {
        File.WriteAllText(ConfigPath, "server: { port: 19001\n");

        Assert.Equal(18888, _home.Tacit().ReadLooseSettings().Port);
    }

    [Theory]
    [InlineData("server:\n  port: 70000\n")]
    [InlineData("server:\n  port: many\n")]
    [InlineData("models:\n  embedding: { endpoint: 'nope' }\n")]
    public void The_loose_port_is_the_default_when_server_port_is_missing_or_not_a_port(string yaml)
    {
        File.WriteAllText(ConfigPath, yaml);

        Assert.Equal(18888, _home.Tacit().ReadLooseSettings().Port);
    }

    [Fact]
    public void The_loose_language_is_read_from_the_config_even_when_it_does_not_load()
    {
        File.WriteAllText(ConfigPath, "language: en\nmodels:\n  embedding: { endpoint: 'nope' }\n");

        Assert.Equal(MemoryLanguage.En, _home.Tacit().ReadLooseSettings().Language);
    }

    [Fact]
    public void An_invalid_config_report_gives_the_service_column_the_loose_endpoint_and_says_it_is_not_running()
    {
        File.WriteAllText(ConfigPath, "language: zh\nmodels:\n  embedding: { endpoint: 'nope' }\nserver:\n  port: 19001\n");
        var tacit = _home.Tacit();
        var error = Assert.Throws<TacitException>(() => tacit.LoadConfig());

        var report = tacit.InvalidConfigStatus(error, tacit.ReadLooseSettings());

        var service = Assert.IsType<ServiceStatus>(report.Service);
        Assert.False(service.Running);
        Assert.False(service.OtherProgramAnswered);
        Assert.Equal(new Uri("http://127.0.0.1:19001/mcp"), service.Endpoint);
        Assert.Equal(["服务未运行", "地址：http://127.0.0.1:19001/mcp", "Web 界面：http://127.0.0.1:19001/ui/"], service.Lines);
        Assert.Null(report.Models);
        Assert.NotNull(report.Config);
    }

    [Fact]
    public void An_invalid_config_report_names_another_program_answering_the_loose_port()
    {
        File.WriteAllText(ConfigPath, "language: zh\nmodels:\n  embedding: { endpoint: 'nope' }\nserver:\n  port: 19001\n");
        var tacit = _home.Tacit();
        var error = Assert.Throws<TacitException>(() => tacit.LoadConfig());

        var service = Assert.IsType<ServiceStatus>(
            tacit.InvalidConfigStatus(error, tacit.ReadLooseSettings(), portAnsweredByOther: true).Service);

        Assert.False(service.Running);
        Assert.True(service.OtherProgramAnswered);
        Assert.Equal(["服务未运行：端口 19001 有其他程序在回答", "地址：http://127.0.0.1:19001/mcp", "Web 界面：http://127.0.0.1:19001/ui/"], service.Lines);
    }

    [Fact]
    public void An_invalid_config_report_speaks_the_language_read_from_the_config()
    {
        File.WriteAllText(ConfigPath, "language: en\nmodels:\n  embedding: { endpoint: 'nope' }\n");
        var tacit = _home.Tacit();
        var error = Assert.Throws<TacitException>(() => tacit.LoadConfig());

        var report = tacit.InvalidConfigStatus(error, tacit.ReadLooseSettings());

        Assert.Equal(
            ["The service is not running", "Address: http://127.0.0.1:18888/mcp", "Web interface: http://127.0.0.1:18888/ui/"],
            Assert.IsType<ServiceStatus>(report.Service).Lines);
        Assert.Equal("Config", report.Config!.Title);
    }

    [Fact]
    public async Task A_bound_extra_listener_is_shown_in_the_service_column_with_its_address()
    {
        var service = Service;
        service.RecordExtraListener(System.Net.IPAddress.Parse("192.168.1.10"), 18888, failureReason: null);

        var column = Assert.IsType<ServiceStatus>((await service.Reports.StatusAsync(Endpoint)).Service);

        Assert.Equal("额外监听：http://192.168.1.10:18888/", column.Lines[^1]);
        Assert.Equal(new ExtraListenerStatus(new Uri("http://192.168.1.10:18888/"), null), column.ExtraListener);
        Assert.Equal(StatusLevel.Ok, column.LineLevels()[^1]);
    }

    [Fact]
    public async Task An_extra_listener_that_did_not_bind_is_a_problem_with_the_reason_in_the_service_column()
    {
        var service = Service;
        service.RecordExtraListener(System.Net.IPAddress.Parse("192.0.2.1"), 18888, "192.0.2.1 不是本机的地址");

        var column = Assert.IsType<ServiceStatus>((await service.Reports.StatusAsync(Endpoint)).Service);

        Assert.True(column.Running);
        Assert.Equal("额外监听失败：http://192.0.2.1:18888/，192.0.2.1 不是本机的地址", column.Lines[^1]);
        Assert.NotNull(column.ExtraListener!.FailureReason);
        Assert.Equal(StatusLevel.Problem, column.LineLevels()[^1]);
        Assert.Equal(StatusLevel.Ok, column.LineLevels()[0]);
    }

    [Fact]
    public void A_failed_extra_listener_is_logged_as_a_warning_naming_the_address()
    {
        var logs = new CapturingLoggerFactory();
        using var service = _home.Tacit().OpenService(_ => { }, logs);

        service.RecordExtraListener(System.Net.IPAddress.Parse("192.0.2.1"), 18888, "192.0.2.1 不是本机的地址");

        var warning = Assert.Single(logs.Entries, entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
        Assert.Contains("http://192.0.2.1:18888/", warning.Message);
    }

    [Fact]
    public async Task The_offline_report_names_the_configured_extra_listener_as_not_running_on_a_plain_line()
    {
        File.AppendAllText(ConfigPath, "server:\n  host: 192.168.1.10\n");
        var tacit = _home.Tacit();

        var column = Assert.IsType<ServiceStatus>((await tacit.OfflineStatus(tacit.LoadConfig())).Service);

        Assert.False(column.Running);
        Assert.Equal("额外监听：http://192.168.1.10:18888/（服务未运行）", column.Lines[^1]);
        Assert.Equal(new ExtraListenerStatus(new Uri("http://192.168.1.10:18888/"), null), column.ExtraListener);
        Assert.Equal([StatusLevel.Attention, StatusLevel.Attention, null, null], column.LineLevels());
    }

    [Fact]
    public async Task Without_an_extra_listener_the_service_column_has_no_extra_line_and_names_the_web_address()
    {
        var column = Assert.IsType<ServiceStatus>((await Service.Reports.StatusAsync(Endpoint)).Service);

        Assert.Null(column.ExtraListener);
        Assert.Equal(new Uri("http://127.0.0.1:18888/ui/"), column.WebAddress);
        Assert.DoesNotContain(column.Lines, line => line.StartsWith("额外监听", StringComparison.Ordinal));
    }
}
