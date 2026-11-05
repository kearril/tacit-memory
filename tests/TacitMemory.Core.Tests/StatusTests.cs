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

        var report = await running.StatusAsync(Endpoint);

        var service = Assert.IsType<ServiceStatus>(report.Service);
        Assert.True(service.Running);
        Assert.Equal(Endpoint, service.Endpoint);
        Assert.False(string.IsNullOrWhiteSpace(service.Version));
        Assert.Equal(307, service.UptimeSeconds);
        Assert.False(service.OtherProgramAnswered);
        Assert.Equal(
            new[] { "运行中", "地址：http://127.0.0.1:18888/mcp", $"版本：{service.Version}", "已运行：5 分钟 7 秒" },
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

        var report = await service.StatusAsync(Endpoint);

        Assert.Contains("已运行：1 天 2 小时 3 分钟 4 秒", Assert.IsType<ServiceStatus>(report.Service).Lines);
    }

    [Fact]
    public async Task A_model_whose_request_failed_is_cooling_down_with_the_seconds_left_and_recovers_after_the_cooldown()
    {
        _models.EmbeddingDown = true;
        await Service.RecallAsync(new RecallRequest { Query = "部署" });
        _home.Clock.Advance(TimeSpan.FromSeconds(10));

        var cooling = Assert.IsType<ModelsStatus>((await Service.StatusAsync(Endpoint)).Models);
        var embedding = Assert.Single(cooling.Items, item => item.Role == "embedding");
        Assert.Equal(ModelState.CoolingDown, embedding.State);
        Assert.Equal(20, embedding.CoolingSeconds);
        Assert.Contains("embedding：冷却中，剩余 20 秒", cooling.Lines);
        Assert.Equal(ModelState.Available, Assert.Single(cooling.Items, item => item.Role == "reranker").State);

        _home.Clock.Advance(TimeSpan.FromSeconds(20));

        var recovered = Assert.IsType<ModelsStatus>((await Service.StatusAsync(Endpoint)).Models);
        var after = Assert.Single(recovered.Items, item => item.Role == "embedding");
        Assert.Equal(ModelState.Available, after.State);
        Assert.Null(after.CoolingSeconds);
    }

    [Fact]
    public async Task The_offline_report_says_the_service_is_not_running_and_leaves_out_the_columns_that_need_it()
    {
        var tacit = _home.Tacit();
        var config = tacit.LoadConfig();

        var report = await tacit.OfflineStatus(config);

        var service = Assert.IsType<ServiceStatus>(report.Service);
        Assert.False(service.Running);
        Assert.Equal(["服务未运行"], service.Lines);
        Assert.Null(service.Endpoint);
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
        Assert.Equal([$"服务未运行：端口 {config.Port} 有其他程序在回答"], service.Lines);
    }

    [Fact]
    public void The_hint_for_commands_that_need_the_service_names_the_command_to_run()
    {
        var tacit = _home.Tacit();
        var config = tacit.LoadConfig();

        Assert.Equal("服务未运行，先执行 tacit serve。", tacit.ServiceNotRunningMessage(config, portAnsweredByOther: false));
        Assert.Equal(
            $"端口 {config.Port} 有其他程序在回答，常驻服务未运行。请先执行 tacit serve，或在 config.yaml 中修改 server.port。",
            tacit.ServiceNotRunningMessage(config, portAnsweredByOther: true));
    }

    [Fact]
    public void An_invalid_config_is_reported_with_every_wrong_key_path_and_the_logs_location()
    {
        File.WriteAllText(ConfigPath, "models:\n  embedding: { endpoint: 'nope' }\n");
        var tacit = _home.Tacit();
        var error = Assert.Throws<TacitException>(() => tacit.LoadConfig());

        var report = tacit.InvalidConfigStatus(error);

        Assert.Null(report.Service);
        Assert.Null(report.Models);
        var config = Assert.IsType<ConfigStatus>(report.Config);
        Assert.Contains(config.Lines, line => line.Contains("models.embedding.endpoint", StringComparison.Ordinal));
        Assert.Contains(config.Lines, line => line.Contains("models.reranker", StringComparison.Ordinal));
        Assert.Equal(Path.Combine(_home.TacitHome, "logs"), report.Logs!.Directory);
    }
}
