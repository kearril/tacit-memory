using TacitMemory.Core.Show;

namespace TacitMemory.Cli.Tests;

/// <summary>
/// Help and version exit 0. Results go to stdout alone: <c>show --default</c> prints only the built-in text, and
/// <c>connect</c> binds into the throwaway user home, never into a real client's folder.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class HelpAndOutputTests : IDisposable
{
    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    public static TheoryData<string[]> HelpRequests() => new()
    {
        { ["-h"] },
        { ["--help"] },
        { ["connect", "--help"] },
    };

    [Theory]
    [MemberData(nameof(HelpRequests))]
    public async Task Help_exits_0_and_prints_to_stdout(string[] args)
    {
        var result = await _cli.RunAsync(args);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Usage:", result.Stdout);
        Assert.Empty(result.Stderr);
    }

    [Fact]
    public async Task A_bare_command_line_prints_the_overview_and_exits_0()
    {
        var result = await _cli.RunAsync([]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Usage:", result.Stdout);
        Assert.Contains("connect", result.Stdout);
        Assert.Empty(result.Stderr);
    }

    [Fact]
    public async Task Version_exits_0_and_prints_the_version_to_stdout()
    {
        var result = await _cli.RunAsync(["--version"]);

        Assert.Equal(0, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.Stdout));
        Assert.Empty(result.Stderr);
    }

    [Fact]
    public async Task Connect_binds_the_client_inside_the_throwaway_user_home()
    {
        _cli.InitReady();

        var result = await _cli.RunAsync(["connect", "--client", "OMP"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Stderr);
        Assert.True(File.Exists(Path.Combine(_cli.Home.UserHome, ".omp", "agent", "AGENTS.md")));
    }

    [Theory]
    [InlineData("instructions", ShowTarget.Instructions)]
    [InlineData("resident", ShowTarget.Resident)]
    [InlineData("skill", ShowTarget.Skill)]
    public async Task Show_with_default_writes_only_the_built_in_text_to_stdout(string target, ShowTarget showTarget)
    {
        _cli.InitReady();
        var expected = (await _cli.Home.Tacit().ShowAsync(showTarget, useDefault: true)).Report;

        var result = await _cli.RunAsync(["show", target, "--default"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected + Environment.NewLine, result.Stdout);
        Assert.Empty(result.Stderr);
    }
}
