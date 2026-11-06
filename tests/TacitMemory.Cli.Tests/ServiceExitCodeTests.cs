namespace TacitMemory.Cli.Tests;

/// <summary>
/// Exit codes of the commands that talk to the service: 4 when nothing answers or another program answers the port, 0
/// for <c>status</c> when the service runs, and 1 when the service refuses a request.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class ServiceExitCodeTests : IDisposable
{
    private readonly CliHarness _cli = new();

    public ServiceExitCodeTests() => _cli.InitReady();

    public void Dispose() => _cli.Dispose();

    public static TheoryData<string[]> CommandsThatNeedTheService() => new()
    {
        { ["reindex"] },
        { ["backup"] },
        { ["restore", "missing.zip"] },
    };

    [Theory]
    [MemberData(nameof(CommandsThatNeedTheService))]
    public async Task A_command_that_needs_the_service_exits_4_when_it_is_not_running(string[] args)
    {
        var result = await _cli.RunAsync(args);

        Assert.Equal(4, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains("tacit serve", result.Stderr);
    }

    [Fact]
    public async Task Status_exits_4_and_reports_the_service_as_not_running()
    {
        var result = await _cli.RunAsync(["status"]);

        Assert.Equal(4, result.ExitCode);
        Assert.Contains("服务未运行", result.Stdout);
        Assert.Contains($"http://127.0.0.1:{_cli.Port}/mcp", result.Stdout);
        Assert.Empty(result.Stderr);
    }

    [Fact]
    public async Task Status_exits_4_when_another_program_answers_the_port()
    {
        await _cli.StartForeignListenerAsync();

        var result = await _cli.RunAsync(["status"]);

        Assert.Equal(4, result.ExitCode);
        Assert.Contains($"端口 {_cli.Port} 有其他程序在回答", result.Stdout);
    }

    [Fact]
    public async Task Status_exits_0_while_the_service_runs_and_writes_plain_text_when_not_a_terminal()
    {
        await _cli.StartServiceAsync();

        var result = await _cli.RunAsync(["status"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("运行中", result.Stdout);
        Assert.DoesNotContain('\u001b', result.Stdout);
        Assert.Empty(result.Stderr);
    }

    [Fact]
    public async Task A_backup_written_by_the_running_service_exits_0()
    {
        await _cli.StartServiceAsync();
        var directory = _cli.Home.PathFor("backups-out");

        var result = await _cli.RunAsync(["backup", "--to", directory]);

        Assert.Equal(0, result.ExitCode);
        Assert.NotEmpty(Directory.GetFiles(directory, "*.zip"));
    }

    [Fact]
    public async Task A_refusal_by_the_running_service_exits_1_with_its_message_on_stderr()
    {
        await _cli.StartServiceAsync();

        var result = await _cli.RunAsync(["restore", _cli.Home.PathFor("missing.zip"), "--yes"]);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.NotEmpty(result.Stderr);
    }
}
