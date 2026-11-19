namespace TacitMemory.Core.Tests;

/// <summary>
/// Reloading reopens the service's runtime with <c>config.yaml</c> as it is now, as a maintenance operation. The service
/// object itself stays: callers keep using it, and what outlives a reload (the start time, the extra listener's outcome)
/// is kept.
/// </summary>
public sealed class ReloadTests : IDisposable
{
    private readonly TestHome _home = new();
    private MemoryService? _service;

    public ReloadTests() => _home.InitReady("retrieval: { topK: 3 }\n");

    public void Dispose()
    {
        _service?.Dispose();
        _home.Dispose();
    }

    private MemoryService Service => _service ??= _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

    private void Hand(string slug, string title, string body)
    {
        var path = Path.Combine(_home.DefaultMemory, "pages", "p", slug + ".md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $"---\ntitle: {title}\naliases: [别名一, 别名二]\ndates: {{ first: 2026-10-01, last: 2026-10-05 }}\n---\n\n{body}\n");
    }

    private Task<ToolResult> Recall(string query) => Service.Tools.RecallAsync(new RecallRequest { Query = query });

    private void Save(Func<ConfigFile, ConfigFile> change) => _home.Tacit().SaveConfig(change(_home.Tacit().ReadConfigFile()));

    [Fact]
    public async Task A_setting_saved_after_the_start_takes_effect_once_the_service_reloads()
    {
        foreach (var slug in (string[])["a", "b", "c"])
            Hand(slug, $"部署问题 {slug}", "## 经过\n部署失败");
        Assert.Contains("3 result(s).", (await Recall("部署失败")).Text);

        Save(file => file with { Retrieval = file.Retrieval with { TopK = 1 } });
        await Service.ReloadAsync();

        Assert.Contains("1 result(s).", (await Recall("部署失败")).Text);
        Assert.Equal(1, Service.Config.Retrieval.TopK);
    }

    [Fact]
    public async Task The_settings_that_wait_for_a_restart_keep_their_running_values()
    {
        var running = Service.Config;
        Directory.CreateDirectory(Path.Combine(_home.UserHome, "elsewhere", ".git"));
        Save(file => file with
        {
            Server = new ServerFile(Port: 19999, Host: "192.168.1.10"),
            Memory = new MemoryFile("~/elsewhere"),
            Language = "en",
            Limits = new LimitsFile(Brief: 1200, Page: new PageLimitsFile(MinAliases: 4)),
        });

        await Service.ReloadAsync();

        var config = Service.Config;
        Assert.Equal(running.Port, config.Port);
        Assert.Equal(running.ExtraAddress, config.ExtraAddress);
        Assert.Equal(running.MemoryPath, config.MemoryPath);
        Assert.Equal(running.Language, config.Language);
        Assert.Equal(running.Limits.Page, config.Limits.Page);
        Assert.Equal(1200, config.Limits.Brief);
    }

    [Fact]
    public async Task When_the_new_config_cannot_be_opened_the_service_runs_on_with_the_old_one()
    {
        Hand("a", "部署问题 a", "## 经过\n部署失败");
        var running = Service.Config;
        Save(file => file with { Retrieval = file.Retrieval with { TopK = 1 } });
        Service.OnOpening = config => { if (config.Retrieval.TopK == 1) throw new IOException("index is locked"); };

        var error = await Assert.ThrowsAsync<TacitException>(() => Service.ReloadAsync());

        Assert.Equal(TacitError.ReloadFailed, error.Error);
        Assert.Contains("index is locked", error.Message);
        Assert.Equal(running, Service.Config);
        Assert.Contains("1 result(s).", (await Recall("部署失败")).Text);
        Assert.False(Service.Failed.IsCompleted);
    }

    [Fact]
    public async Task A_saved_config_that_cannot_be_opened_puts_the_file_back_as_it_was()
    {
        var path = Path.Combine(_home.TacitHome, "config.yaml");
        var before = File.ReadAllText(path);
        var view = Service.Settings.ConfigView();
        Service.OnOpening = config => { if (config.Retrieval.TopK == 1) throw new IOException("index is locked"); };

        var error = await Assert.ThrowsAsync<TacitException>(
            () => Service.Settings.SaveConfigAsync(view.File with { Retrieval = view.File.Retrieval with { TopK = 1 } }, view.Hash));

        Assert.Equal(TacitError.ReloadFailed, error.Error);
        Assert.Equal(before, File.ReadAllText(path));
        Assert.Equal(3, Service.Config.Retrieval.TopK);
    }

    [Fact]
    public async Task Saving_keeps_the_secrets_the_file_holds()
    {
        var view = Service.Settings.ConfigView();
        Assert.Null(view.File.Models.Decision!.ApiKey);

        await Service.Settings.SaveConfigAsync(view.File with { Retrieval = view.File.Retrieval with { TopK = 4 } }, view.Hash);

        Assert.Equal("test-key", _home.Tacit().ReadConfigFile().Models.Decision!.ApiKey);
    }

    [Fact]
    public async Task A_write_that_arrives_during_a_reload_waits_for_it_then_runs()
    {
        using var opening = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Save(file => file with { Retrieval = file.Retrieval with { TopK = 1 } });
        Service.OnOpening = _ => { opening.Set(); release.Wait(TimeSpan.FromSeconds(10)); };

        var reload = Task.Run(() => Service.ReloadAsync());
        Assert.True(opening.Wait(TimeSpan.FromSeconds(10)));
        var write = Service.Tools.WriteRecentAsync(new WriteRecentRequest { Text = "重新加载期间写下的活动" });
        await Task.Delay(300);
        Assert.False(write.IsCompleted);

        release.Set();
        await reload.WaitAsync(TimeSpan.FromSeconds(10));
        var result = await write.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(result.IsError, result.Text);
        Assert.Contains("重新加载期间写下的活动", (await Recall("重新加载期间")).Text);
    }

    [Fact]
    public async Task The_start_time_and_the_extra_listener_outlive_a_reload()
    {
        Service.RecordExtraListener(System.Net.IPAddress.Parse("192.168.1.10"), 18888, failureReason: null);
        var listener = Service.ExtraListener;
        _home.Clock.Advance(TimeSpan.FromMinutes(5));
        var before = await Service.Reports.StatusAsync(new Uri("http://127.0.0.1:18888/mcp"));

        await Service.ReloadAsync();
        var after = await Service.Reports.StatusAsync(new Uri("http://127.0.0.1:18888/mcp"));

        Assert.Same(listener, Service.ExtraListener);
        Assert.Equal(300, before.Service!.UptimeSeconds);
        Assert.Equal(before.Service.UptimeSeconds, after.Service!.UptimeSeconds);
    }

    [Fact]
    public async Task When_neither_config_can_be_opened_the_service_reports_that_it_cannot_go_on()
    {
        Save(file => file with { Retrieval = file.Retrieval with { TopK = 1 } });
        Service.OnOpening = _ => throw new IOException("disk is full");

        var error = await Assert.ThrowsAsync<TacitException>(() => Service.ReloadAsync());

        Assert.Equal(TacitError.ReloadFailed, error.Error);
        Assert.Contains("disk is full", (await Service.Failed.WaitAsync(TimeSpan.FromSeconds(5))).Message);
    }

    [Fact]
    public async Task The_memory_browser_taken_before_a_reload_reads_the_reopened_memory()
    {
        var browse = Service.Browse;
        Hand("a", "部署问题", "## 经过\n部署失败");
        Assert.Single((await browse.PagesAsync()).Pages);

        Save(file => file with { Retrieval = file.Retrieval with { TopK = 1 } });
        await Service.ReloadAsync();
        Hand("b", "回滚问题", "## 经过\n回滚失败");

        Assert.Equal(["p/a", "p/b"], (await browse.PagesAsync()).Pages.Select(page => page.Id).Order());
    }

    [Fact]
    public async Task Saving_machine_identity_updates_the_resident_block_without_a_restart()
    {
        _home.Bind();
        var agents = Path.Combine(_home.UserHome, ".omp", "agent", "AGENTS.md");
        Assert.Contains("Machine: test-machine\n", File.ReadAllText(agents));

        var saved = await Service.Settings.SaveConfigAsync(
            Service.Settings.ConfigView().File with { Machine = new MachineFile("mac-mini", "家里服务器") },
            Service.Settings.ConfigView().Hash);

        Assert.Empty(saved.Restart);
        Assert.Equal(new MachineConfig("mac-mini", "家里服务器"), Service.Config.Machine);
        Assert.Contains("Machine: mac-mini \u2014 家里服务器\n", File.ReadAllText(agents));
    }
}
