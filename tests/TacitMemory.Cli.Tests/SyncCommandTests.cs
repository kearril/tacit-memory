using TacitMemory.Core.Tests;

namespace TacitMemory.Cli.Tests;

/// <summary>
/// <c>tacit sync</c> through the running service: a plain sync exits 0 once the remote matches, choosing a side asks for
/// confirmation like <c>restore</c>, and with the sync off it says so and exits 0.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class SyncCommandTests : IDisposable
{
    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    private string Remote => Path.Combine(_cli.Home.Root, "remote.git");

    private async Task StartAsync(bool synced)
    {
        _cli.InitReady();
        if (synced)
        {
            TestHome.Git(_cli.Home.Root, "init", "--quiet", "--bare", Remote);
            File.AppendAllText(_cli.ConfigPath, $"sync:\n  remote: '{Remote}'\n");
        }
        await _cli.StartServiceAsync();
    }

    private string Head(string repository) => TestHome.Git(repository, "rev-parse", "HEAD").Trim();

    [Fact]
    public async Task A_plain_sync_pushes_the_memory_and_exits_0()
    {
        await StartAsync(synced: true);

        var result = await _cli.RunAsync(["sync"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(Head(_cli.Home.DefaultMemory), Head(Remote));
    }

    [Fact]
    public async Task With_the_sync_off_a_plain_sync_says_so_and_exits_0()
    {
        await StartAsync(synced: false);

        var result = await _cli.RunAsync(["sync"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("sync.remote", result.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Choosing_a_side_without_yes_at_a_non_terminal_is_a_usage_error_and_changes_nothing()
    {
        await StartAsync(synced: true);

        var result = await _cli.RunAsync(["sync", "--use-local"], stdinIsTerminal: false);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("--yes", result.Stderr, StringComparison.Ordinal);
        Assert.Equal("", TestHome.Git(Remote, "for-each-ref"));
    }

    [Fact]
    public async Task Choosing_this_machine_with_yes_pushes_it_and_exits_0()
    {
        await StartAsync(synced: true);

        var result = await _cli.RunAsync(["sync", "--use-local", "-y"], stdinIsTerminal: false);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(Head(_cli.Home.DefaultMemory), Head(Remote));
    }

    [Fact]
    public async Task At_a_terminal_y_chooses_the_side_after_the_preview()
    {
        await StartAsync(synced: true);

        var result = await _cli.RunAsync(["sync", "--use-local"], stdin: "y\n", stdinIsTerminal: true);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(Head(_cli.Home.DefaultMemory), Head(Remote));
    }

    [Fact]
    public async Task At_a_terminal_any_other_answer_cancels_with_exit_1_and_changes_nothing()
    {
        await StartAsync(synced: true);

        var result = await _cli.RunAsync(["sync", "--use-local"], stdin: "n\n", stdinIsTerminal: true);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("", TestHome.Git(Remote, "for-each-ref"));
    }

    [Fact]
    public async Task Choosing_the_remote_while_it_is_empty_is_refused_with_exit_1()
    {
        await StartAsync(synced: true);

        var result = await _cli.RunAsync(["sync", "--use-remote", "--yes"], stdinIsTerminal: false);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("--use-local", result.Stderr, StringComparison.Ordinal);
    }
}
