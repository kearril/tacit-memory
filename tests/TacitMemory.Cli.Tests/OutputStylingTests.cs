using System.Text.RegularExpressions;

namespace TacitMemory.Cli.Tests;

/// <summary>
/// Output styling without a running service: <c>status</c> and <c>connect</c>. A stream that is not a terminal, and any
/// stream while NO_COLOR is set, gets plain text with no control characters. A terminal without NO_COLOR gets the styled
/// layout, which shows the same words. The appearance itself is checked by eye, not here.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class OutputStylingTests : IDisposable
{
    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    [Fact]
    public async Task Status_to_a_pipe_is_plain_text_without_control_characters()
    {
        _cli.InitReady();

        var result = await _cli.RunAsync(["status"]);

        Assert.Equal(4, result.ExitCode);
        Assert.DoesNotContain(OutputWords.Escape, result.Stdout);
    }

    [Fact]
    public async Task Status_with_an_unconfigured_model_shows_the_same_words_at_a_terminal_and_in_a_pipe()
    {
        _cli.InitReady();
        _cli.RemoveModels();

        var pipe = await _cli.RunAsync(["status"]);

        var styled = await _cli.RunAsync(["status"], stdoutIsTerminal: true);

        Assert.Equal(4, pipe.ExitCode);
        Assert.Equal(pipe.ExitCode, styled.ExitCode);
        Assert.Contains("辅助模型", pipe.Stdout, StringComparison.Ordinal);
        Assert.Contains("embedding：未配置", pipe.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain(OutputWords.Escape, pipe.Stdout);
        Assert.Contains(OutputWords.Escape, styled.Stdout);
        Assert.Equal(OutputWords.Of(pipe.Stdout), OutputWords.Of(styled.Stdout));
    }

    [Fact]
    public async Task Status_counts_todays_warnings_and_errors_at_a_terminal_and_in_a_pipe()
    {
        _cli.InitReady();
        var logs = Path.Combine(_cli.Home.TacitHome, "logs");
        Directory.CreateDirectory(logs);
        File.WriteAllText(
            Path.Combine(logs, $"tacit-{TacitMemory.Core.Tests.TestHome.Today:yyyyMMdd}.log"),
            "2026-11-02 09:00:00.000 +00:00 WRN 额外监听绑定失败\n");

        var pipe = await _cli.RunAsync(["status"]);
        var styled = await _cli.RunAsync(["status"], stdoutIsTerminal: true);

        Assert.Contains("今天：1 条警告，0 条错误", pipe.Stdout, StringComparison.Ordinal);
        Assert.Equal(OutputWords.Of(pipe.Stdout), OutputWords.Of(styled.Stdout));
    }

    [Fact]
    public async Task Status_names_the_configured_extra_listener_while_the_service_is_not_running_at_a_terminal_and_in_a_pipe()
    {
        _cli.InitReady();
        // The server section is the last in the config, so the host goes under it.
        File.AppendAllText(_cli.ConfigPath, "  host: 192.168.1.10\n");

        var pipe = await _cli.RunAsync(["status"]);
        var styled = await _cli.RunAsync(["status"], stdoutIsTerminal: true);

        Assert.Equal(4, pipe.ExitCode);
        Assert.Contains($"额外监听：http://192.168.1.10:{_cli.Port}/（服务未运行）", pipe.Stdout, StringComparison.Ordinal);
        Assert.Equal(OutputWords.Of(pipe.Stdout), OutputWords.Of(styled.Stdout));
    }

    [Fact]
    public async Task Status_at_a_terminal_with_NO_COLOR_is_the_same_plain_text()
    {
        _cli.InitReady();
        var pipe = await _cli.RunAsync(["status"]);

        var refused = await _cli.RunAsync(["status"], stdoutIsTerminal: true, noColor: true);

        Assert.Equal(pipe.ExitCode, refused.ExitCode);
        Assert.Equal(pipe.Stdout, refused.Stdout);
        Assert.DoesNotContain(OutputWords.Escape, refused.Stdout);
    }

    [Fact]
    public async Task Status_at_a_terminal_is_styled_and_shows_the_same_words()
    {
        _cli.InitReady();
        var pipe = await _cli.RunAsync(["status"]);

        var styled = await _cli.RunAsync(["status"], stdoutIsTerminal: true);

        Assert.Equal(pipe.ExitCode, styled.ExitCode);
        Assert.Contains(OutputWords.Escape, styled.Stdout);
        Assert.Equal(OutputWords.Of(pipe.Stdout), OutputWords.Of(styled.Stdout));
    }

    [Fact]
    public async Task Connect_to_a_pipe_is_plain_text_that_prints_the_endpoint()
    {
        _cli.InitReady();

        var result = await _cli.RunAsync(_cli.OmpConnectArgs());

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain(OutputWords.Escape, result.Stdout);
        Assert.Contains($"http://127.0.0.1:{_cli.Port}/mcp", result.Stdout);
    }

    [Fact]
    public async Task Connect_at_a_terminal_with_NO_COLOR_is_plain_text()
    {
        _cli.InitReady();

        var result = await _cli.RunAsync(_cli.OmpConnectArgs(), stdoutIsTerminal: true, noColor: true);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain(OutputWords.Escape, result.Stdout);
        Assert.Contains($"http://127.0.0.1:{_cli.Port}/mcp", result.Stdout);
    }

    [Fact]
    public async Task Connect_at_a_terminal_is_styled_and_keeps_the_endpoint()
    {
        _cli.InitReady();

        var result = await _cli.RunAsync(_cli.OmpConnectArgs(), stdoutIsTerminal: true);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(OutputWords.Escape, result.Stdout);
        Assert.Contains($"http://127.0.0.1:{_cli.Port}/mcp", OutputWords.Plain(result.Stdout));
    }

    [Fact]
    public async Task A_refused_wait_at_a_terminal_stderr_still_exits_4_with_its_message()
    {
        _cli.InitReady();

        var result = await _cli.RunAsync(["reindex"], stderrIsTerminal: true);

        Assert.Equal(4, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains("tacit serve", OutputWords.Plain(result.Stderr));
    }
}

/// <summary>
/// Output styling with a running service: the <c>restore</c> preview on stdout, and the wait indicator on stderr for
/// <c>reindex</c> and the execution of <c>restore</c>. Each stream is judged on its own: stdout and stderr may differ.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class StyledRestoreAndWaitTests : IAsyncLifetime
{
    private readonly CliHarness _cli = new();
    private string _zip = string.Empty;

    /// <summary>A running service and a backup of the current memory, which the restore tests restore.</summary>
    public async Task InitializeAsync()
    {
        _cli.InitReady();
        await _cli.StartServiceAsync();

        var directory = _cli.Home.PathFor("backups-out");
        Assert.Equal(0, (await _cli.RunAsync(["backup", "--to", directory])).ExitCode);
        _zip = Directory.GetFiles(directory, "*.zip").Single();
    }

    public Task DisposeAsync()
    {
        _cli.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task The_restore_preview_to_a_pipe_is_plain_text_without_control_characters()
    {
        var result = await _cli.RunAsync(["restore", "--from", _zip]);

        Assert.Equal(2, result.ExitCode);
        Assert.DoesNotContain(OutputWords.Escape, result.Stdout);
        Assert.Contains("--yes", result.Stderr);
    }

    [Fact]
    public async Task The_restore_preview_at_a_terminal_with_NO_COLOR_is_the_same_plain_text()
    {
        var pipe = await _cli.RunAsync(["restore", "--from", _zip]);

        var refused = await _cli.RunAsync(["restore", "--from", _zip], stdoutIsTerminal: true, noColor: true);

        Assert.Equal(pipe.Stdout, refused.Stdout);
        Assert.DoesNotContain(OutputWords.Escape, refused.Stdout);
    }

    [Fact]
    public async Task The_restore_preview_at_a_terminal_is_styled_and_shows_the_same_words()
    {
        var pipe = await _cli.RunAsync(["restore", "--from", _zip]);

        var styled = await _cli.RunAsync(["restore", "--from", _zip], stdoutIsTerminal: true);

        Assert.Contains(OutputWords.Escape, styled.Stdout);
        Assert.Equal(OutputWords.Of(pipe.Stdout), OutputWords.Of(styled.Stdout));
    }

    [Fact]
    public async Task Reindex_at_a_terminal_stderr_shows_a_spinner_and_stdout_stays_plain()
    {
        var result = await _cli.RunAsync(["reindex"], stderrIsTerminal: true);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(OutputWords.Escape, result.Stderr);
        Assert.DoesNotContain(OutputWords.Escape, result.Stdout);
    }

    [Fact]
    public async Task Reindex_at_a_terminal_stderr_with_NO_COLOR_writes_no_control_characters()
    {
        var result = await _cli.RunAsync(["reindex"], stderrIsTerminal: true, noColor: true);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain(OutputWords.Escape, result.Stderr);
        Assert.DoesNotContain(OutputWords.Escape, result.Stdout);
    }

    [Fact]
    public async Task Restore_with_yes_shows_the_spinner_on_stderr_while_the_stdout_preview_stays_plain()
    {
        var result = await _cli.RunAsync(["restore", "--from", _zip, "--yes"], stderrIsTerminal: true);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(OutputWords.Escape, result.Stderr);
        Assert.DoesNotContain(OutputWords.Escape, result.Stdout);
    }

    [Fact]
    public async Task Stdout_at_a_terminal_is_styled_while_a_redirected_stderr_stays_plain()
    {
        var result = await _cli.RunAsync(["restore", "--from", _zip, "--yes"], stdoutIsTerminal: true);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(OutputWords.Escape, result.Stdout);
        Assert.DoesNotContain(OutputWords.Escape, result.Stderr);
    }
}

/// <summary>The clients column of <c>status</c>: its words with the service stopped, and while it runs.</summary>
[Collection(CliCollection.Name)]
public sealed class ClientsColumnCliTests : IDisposable
{
    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    [Fact]
    public async Task The_clients_column_is_shown_while_the_service_is_stopped_and_names_each_recorded_client_without_control_characters()
    {
        _cli.InitReady();
        // A file that a client's own text has put control characters into: the report must still print plain words.
        File.WriteAllText(
            Path.Combine(_cli.Home.TacitHome, "clients.json"),
            "{\"clients\":[{\"name\":\"probe\\u001b[31m-client\",\"lastSeen\":\"2026-11-02\",\"protocolVersion\":\"2026-07-28\",\"capabilities\":[],\"extensions\":[\"io.example/tasks\"]}]}");

        var result = await _cli.RunAsync(["status"]);

        Assert.Equal(4, result.ExitCode);
        Assert.Contains("客户端" + Environment.NewLine, result.Stdout);
        Assert.True(result.Stdout.Contains("  probe[31m-client：最近连接 2026-11-02，协议 2026-07-28，能力：无，扩展：io.example/tasks", StringComparison.Ordinal), result.Stdout);
        Assert.DoesNotContain(OutputWords.Escape, result.Stdout);
    }

    [Fact]
    public async Task The_clients_column_is_shown_while_the_service_runs()
    {
        _cli.InitReady();
        await _cli.StartServiceAsync();

        var result = await _cli.RunAsync(["status"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("客户端" + Environment.NewLine, result.Stdout);
        Assert.Contains("  尚无客户端连接过服务。", result.Stdout);
        Assert.DoesNotContain(OutputWords.Escape, result.Stdout);
    }
}

/// <summary>Compares outputs by their words: the escape sequences, whitespace and box-drawing characters are left out.</summary>
internal static class OutputWords
{
    /// <summary>The escape character that starts every control sequence the styled output writes. A char, not a string: culture-sensitive string matching ignores it.</summary>
    public const char Escape = '\u001b';

    private static readonly Regex Sequence = new("\u001b\\[[0-9;?]*[A-Za-z]", RegexOptions.CultureInvariant);

    /// <summary>The text with its control sequences removed.</summary>
    public static string Plain(string text) => Sequence.Replace(text, string.Empty);

    /// <summary>The characters that carry words: the plain text without whitespace and without the box-drawing block U+2500 to U+257F.</summary>
    public static string Of(string text) =>
        new(Plain(text).Where(c => !char.IsWhiteSpace(c) && (c < '\u2500' || c > '\u257F')).ToArray());
}
