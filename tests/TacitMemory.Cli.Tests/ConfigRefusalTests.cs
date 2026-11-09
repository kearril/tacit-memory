namespace TacitMemory.Cli.Tests;

/// <summary>
/// A config that does not load exits 3 for the commands that read it, and the commands that do not need it still
/// work: <c>disconnect</c>, and <c>show</c> with <c>--default</c>.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class ConfigRefusalTests : IDisposable
{
    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    public static TheoryData<string[]> CommandsThatLoadTheConfig() => new()
    {
        { ["status"] },
        { ["backup"] },
        { ["show", "instructions"] },
        { ["show", "tools"] },
        { ["reindex"] },
    };

    [Theory]
    [MemberData(nameof(CommandsThatLoadTheConfig))]
    public async Task A_config_that_does_not_load_exits_3(string[] args)
    {
        _cli.InitReady();
        _cli.BreakConfig();

        var result = await _cli.RunAsync(args);

        Assert.Equal(3, result.ExitCode);
    }

    [Theory]
    [MemberData(nameof(CommandsThatLoadTheConfig))]
    public async Task A_config_still_holding_the_removed_candidates_section_exits_3_and_names_the_key(string[] args)
    {
        _cli.InitReady();
        File.AppendAllText(_cli.ConfigPath, "candidates: { minDays: 2 }\n");

        var result = await _cli.RunAsync(args);

        Assert.Equal(3, result.ExitCode);
        // status reports config errors on stdout as its report; the other commands write them to stderr.
        Assert.Contains("candidates", result.Stdout + result.Stderr);
    }

    [Fact]
    public async Task Connect_exits_3_when_the_config_does_not_load()
    {
        _cli.InitReady();
        _cli.BreakConfig();

        var result = await _cli.RunAsync(_cli.OmpConnectArgs());

        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public async Task A_missing_decision_key_no_longer_refuses_the_config()
    {
        _cli.InitReady();
        _cli.RemoveDecisionKey();

        var result = await _cli.RunAsync(_cli.OmpConnectArgs());

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task A_config_without_any_model_is_not_refused_and_connect_exits_0()
    {
        _cli.InitReady();
        _cli.RemoveModels();

        var result = await _cli.RunAsync(_cli.OmpConnectArgs());

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("models", result.Stderr);
    }

    [Fact]
    public async Task An_endpoint_setting_exits_3_and_names_its_key_path()
    {
        _cli.InitReady();
        _cli.BreakConfig();

        var result = await _cli.RunAsync(_cli.OmpConnectArgs());

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("models.embedding.endpoint", result.Stderr);
    }

    [Fact]
    public async Task Disconnect_works_while_the_config_does_not_load()
    {
        _cli.InitReady();
        Assert.Equal(0, (await _cli.RunAsync(_cli.OmpConnectArgs())).ExitCode);
        _cli.BreakConfig();

        var result = await _cli.RunAsync(["disconnect", "--client", "omp"]);

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task Show_with_default_works_while_the_config_does_not_load()
    {
        _cli.InitReady();
        _cli.BreakConfig();

        var result = await _cli.RunAsync(["show", "instructions", "--default"]);

        Assert.Equal(0, result.ExitCode);
        Assert.NotEmpty(result.Stdout);
    }

    [Fact]
    public async Task Status_with_an_invalid_config_exits_3_and_shows_the_config_errors_with_the_running_service()
    {
        _cli.InitReady();
        await _cli.StartServiceAsync();
        _cli.BreakConfig();

        var result = await _cli.RunAsync(["status"]);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("models.embedding.endpoint", result.Stdout);
        Assert.Contains("运行中", result.Stdout);
        Assert.Empty(result.Stderr);
    }

    [Fact]
    public async Task Status_with_an_invalid_config_exits_3_and_gives_the_endpoint_when_the_service_is_not_running()
    {
        _cli.InitReady();
        _cli.BreakConfig();

        var result = await _cli.RunAsync(["status"]);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("models.embedding.endpoint", result.Stdout);
        Assert.Contains("服务未运行", result.Stdout);
        Assert.Contains($"http://127.0.0.1:{_cli.Port}/mcp", result.Stdout);
    }

    [Fact]
    public async Task Status_with_an_invalid_config_exits_3_and_names_another_program_on_the_port()
    {
        _cli.InitReady();
        _cli.BreakConfig();
        await _cli.StartForeignListenerAsync();

        var result = await _cli.RunAsync(["status"]);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains($"端口 {_cli.Port} 有其他程序在回答", result.Stdout);
    }
}
