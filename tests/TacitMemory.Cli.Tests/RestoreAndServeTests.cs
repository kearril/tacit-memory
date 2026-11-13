namespace TacitMemory.Cli.Tests;

/// <summary>
/// The <c>restore</c> confirmation (a non-terminal without <c>--yes</c> is a usage error that restores nothing, an answer
/// other than <c>y</c> cancels with 1, <c>--yes</c> restores with 0), and the exit codes of <c>serve</c>.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class RestoreConfirmationTests : IAsyncLifetime
{
    private readonly CliHarness _cli = new();
    private string _zip = string.Empty;

    private string Persona => Path.Combine(_cli.Home.DefaultMemory, "persona.md");

    /// <summary>A running service and a backup written while the persona said "before"; the persona then says "after".</summary>
    public async Task InitializeAsync()
    {
        _cli.InitReady();
        await _cli.StartServiceAsync();
        File.WriteAllText(Persona, "before");

        var directory = _cli.Home.PathFor("backups-out");
        Assert.Equal(0, (await _cli.RunAsync(["backup", "--to", directory])).ExitCode);
        _zip = Directory.GetFiles(directory, "*.zip").Single();

        File.WriteAllText(Persona, "after");
    }

    public Task DisposeAsync()
    {
        _cli.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Without_yes_a_non_terminal_stdin_is_a_usage_error_and_restores_nothing()
    {
        var result = await _cli.RunAsync(["restore", "--from", _zip], stdinIsTerminal: false);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("--yes", result.Stderr);
        Assert.Equal("after", File.ReadAllText(Persona));
    }

    [Fact]
    public async Task At_a_terminal_an_answer_other_than_y_cancels_with_exit_1()
    {
        var result = await _cli.RunAsync(["restore", "--from", _zip], stdin: "n\n", stdinIsTerminal: true);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("after", File.ReadAllText(Persona));
    }

    [Fact]
    public async Task With_yes_the_backup_is_restored_and_exits_0()
    {
        var result = await _cli.RunAsync(["restore", "--from", _zip, "--yes"], stdinIsTerminal: false);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("before", File.ReadAllText(Persona));
    }

    [Fact]
    public async Task The_short_y_restores_without_asking_and_exits_0()
    {
        var result = await _cli.RunAsync(["restore", "--from", _zip, "-y"], stdinIsTerminal: false);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("before", File.ReadAllText(Persona));
    }

    [Fact]
    public async Task The_old_config_flag_is_refused_and_include_config_is_accepted()
    {
        Assert.Equal(2, (await _cli.RunAsync(["restore", "--from", _zip, "--config", "--yes"])).ExitCode);
        Assert.Equal(0, (await _cli.RunAsync(["restore", "--from", _zip, "--include-config", "--yes"])).ExitCode);
    }
}

/// <summary>
/// <c>serve</c>: 1 when the port is taken, 3 when the config does not load, 0 when stopped with Ctrl+C. The stop is
/// asked for through the cancellation token the framework gives the command, which is what Ctrl+C triggers.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class ServeTests : IDisposable
{
    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    [Fact]
    public async Task Serve_exits_1_when_the_port_is_taken()
    {
        _cli.InitReady();
        using var taken = CliHarness.Occupy(_cli.Port);

        var result = await _cli.RunAsync(["serve"]);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(_cli.Port.ToString(), result.Stderr);
    }

    [Fact]
    public async Task Serve_exits_3_when_the_config_does_not_load()
    {
        _cli.InitReady();
        _cli.BreakConfig();

        var result = await _cli.RunAsync(["serve"]);

        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public async Task Serve_prints_the_extra_listener_address_after_the_loopback_one()
    {
        _cli.Home.InitReady(extraConfig: $"server:\n  port: {_cli.Port}\n  host: 127.0.0.2\n");
        using var stop = new CancellationTokenSource();
        var run = _cli.RunAsync(["serve"], cancellationToken: stop.Token);

        for (var waited = 0; waited < 300 && !run.IsCompleted && !_cli.Stdout.Contains("额外监听", StringComparison.Ordinal); waited++)
            await Task.Delay(100);
        Assert.Contains($"额外监听：http://127.0.0.2:{_cli.Port}/", _cli.Stdout);

        stop.Cancel();
        var result = await run;

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("127.0.0.1", result.Stdout);
    }

    [Fact]
    public async Task Serve_warns_and_keeps_running_on_loopback_when_the_extra_address_cannot_bind()
    {
        // 192.0.2.1 is a documentation address this machine does not have.
        _cli.Home.InitReady(extraConfig: $"server:\n  port: {_cli.Port}\n  host: 192.0.2.1\n");
        using var stop = new CancellationTokenSource();
        var run = _cli.RunAsync(["serve"], cancellationToken: stop.Token);

        for (var waited = 0; waited < 300 && !run.IsCompleted && !_cli.Stdout.Contains("127.0.0.1", StringComparison.Ordinal); waited++)
            await Task.Delay(100);
        Assert.Contains("127.0.0.1", _cli.Stdout);

        stop.Cancel();
        var result = await run;

        Assert.Equal(0, result.ExitCode);
        Assert.Contains($"额外监听 http://192.0.2.1:{_cli.Port}/ 绑定失败", result.Stdout);
        Assert.DoesNotContain("额外监听：", result.Stdout);
    }

    [Fact]
    public async Task Serve_exits_0_when_stopped_with_ctrl_c()
    {
        _cli.InitReady();
        using var stop = new CancellationTokenSource();
        var run = _cli.RunAsync(["serve"], cancellationToken: stop.Token);

        for (var waited = 0; waited < 300 && !run.IsCompleted && !_cli.Stdout.Contains("127.0.0.1"); waited++)
            await Task.Delay(100);
        Assert.Contains("127.0.0.1", _cli.Stdout);

        stop.Cancel();
        var result = await run;

        Assert.Equal(0, result.ExitCode);
    }
}
