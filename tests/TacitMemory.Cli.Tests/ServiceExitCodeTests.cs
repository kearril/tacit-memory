using TacitMemory.Core;

namespace TacitMemory.Cli.Tests;

/// <summary>
/// Exit codes of the commands that talk to the service: 4 when nothing answers or another program answers the port, 0
/// for <c>status</c> when a service answers with its report, and 1 when the service refuses a request.
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
        { ["restore", "--from", "missing.zip"] },
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
    public async Task Status_names_a_prompt_document_the_service_keeps_at_its_previous_version_and_exits_3()
    {
        await _cli.StartServiceAsync();
        File.WriteAllText(Path.Combine(_cli.Home.TacitHome, "prompts", "resident.md"), "外壳\n\n## mainline\n旧节名\n");

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        var result = await _cli.RunAsync(["status"]);
        while (!result.Stdout.Contains("提示词文档有误", StringComparison.Ordinal))
        {
            Assert.True(DateTime.UtcNow < deadline, "the status did not name the document within 10 s");
            // The reload waits for its debounce on the test clock, so each round lets that time pass.
            _cli.Home.Clock.Elapse(TimeSpan.FromMilliseconds(300));
            await Task.Delay(100);
            result = await _cli.RunAsync(["status"]);
        }

        // The file would also refuse a restart, so the config column names it and the code is the one for a refused config.
        Assert.Equal(3, result.ExitCode);
        Assert.Contains("prompts/resident.md 有误，服务正在使用上一版：", result.Stdout);
        Assert.Contains("mainline", result.Stdout);
    }

    [Fact]
    public async Task Status_leaves_out_the_prompt_documents_while_none_is_broken()
    {
        await _cli.StartServiceAsync();

        var result = await _cli.RunAsync(["status"]);

        Assert.DoesNotContain("提示词文档有误", result.Stdout);
    }

    [Fact]
    public async Task Status_names_the_web_address_when_the_service_is_not_running()
    {
        var result = await _cli.RunAsync(["status"]);

        Assert.Contains($"Web 界面：http://127.0.0.1:{_cli.Port}/ui/", result.Stdout);
    }

    [Fact]
    public async Task Status_names_the_web_address_and_the_extra_listener_while_the_service_runs()
    {
        _cli.Home.InitReady(extraConfig: $"server:\n  port: {_cli.Port}\n  host: 127.0.0.2\n");
        _cli.SetAccessPassword();
        await _cli.StartServiceAsync();

        var result = await _cli.RunAsync(["status"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains($"Web 界面：http://127.0.0.1:{_cli.Port}/ui/", result.Stdout);
        Assert.Contains($"额外监听：http://127.0.0.2:{_cli.Port}/", result.Stdout);
    }

    [Fact]
    public async Task Status_names_the_reason_when_the_extra_listener_did_not_bind_and_the_service_still_runs()
    {
        // 192.0.2.1 is a documentation address this machine does not have.
        _cli.Home.InitReady(extraConfig: $"server:\n  port: {_cli.Port}\n  host: 192.0.2.1\n");
        _cli.SetAccessPassword();
        await _cli.StartServiceAsync();

        var result = await _cli.RunAsync(["status"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains($"额外监听失败：http://192.0.2.1:{_cli.Port}/，192.0.2.1 不是本机的地址", result.Stdout);
    }

    [Fact]
    public async Task Status_exits_1_when_the_service_refuses_the_report_and_names_the_refusal_on_stderr()
    {
        await _cli.StartRefusingServiceAsync(TacitError.InternalFailure, "the service is under maintenance");

        var result = await _cli.RunAsync(["status"]);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains("under maintenance", result.Stderr);
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
    public async Task A_backup_without_options_is_written_to_the_backups_folder_of_tacit_home()
    {
        await _cli.StartServiceAsync();

        var result = await _cli.RunAsync(["backup"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Single(Directory.GetFiles(Path.Combine(_cli.Home.TacitHome, "backups"), "manual-*.zip"));

    }

    [Fact]
    public async Task A_refusal_by_the_running_service_exits_1_with_its_message_on_stderr()
    {
        await _cli.StartServiceAsync();

        var result = await _cli.RunAsync(["restore", "--from", _cli.Home.PathFor("missing.zip"), "--yes"]);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.NotEmpty(result.Stderr);
    }
}
