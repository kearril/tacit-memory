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
        { ["probe", "--help"] },
        { ["probe", "reranker", "--help"] },
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

        Directory.CreateDirectory(Path.Combine(_cli.Home.UserHome, ".omp", "agent", "skills"));
        var result = await _cli.RunAsync(["connect", "--client", "OMP", "--context", "~/.omp/agent/AGENTS.md", "--skills", "~/.omp/agent/skills"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Stderr);
        Assert.True(File.Exists(Path.Combine(_cli.Home.UserHome, ".omp", "agent", "AGENTS.md")));
    }

    [Theory]
    [InlineData("instructions", ShowTarget.Instructions)]
    [InlineData("resident", ShowTarget.Resident)]
    [InlineData("skill", ShowTarget.Skill)]
    [InlineData("RESIDENT", ShowTarget.Resident)]
    public async Task Show_with_default_writes_only_the_built_in_text_to_stdout(string target, ShowTarget showTarget)
    {
        _cli.InitReady();
        var expected = (await _cli.Home.Tacit().ShowAsync(showTarget, useDefault: true)).Report;

        var result = await _cli.RunAsync(["show", target, "--default"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected + Environment.NewLine, result.Stdout);
        Assert.Empty(result.Stderr);
    }

    [Fact]
    public async Task Show_tools_prints_the_configured_minimum_of_aliases_and_no_removed_limit()
    {
        _cli.Home.InitReady(extraConfig: "limits: { page: { minAliases: 3 } }\n");

        var result = await _cli.RunAsync(["show", "tools"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("At least 3.", result.Stdout);
        Assert.DoesNotContain("characters", result.Stdout);
        Assert.Empty(result.Stderr);
    }

    [Fact]
    public async Task Command_words_match_without_regard_to_case()
    {
        var result = await _cli.RunAsync(["CONNECT", "--help"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Usage: connect", result.Stdout);
    }

    [Fact]
    public async Task The_overview_lists_the_exit_codes_once()
    {
        var result = await _cli.RunAsync(["--help"]);

        Assert.Equal(1, result.Stdout.Split("Exit codes").Length - 1);
    }

    [Fact]
    public async Task The_overview_lists_probe()
    {
        var result = await _cli.RunAsync(["--help"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("probe", result.Stdout);
    }

    [Theory]
    [InlineData("restore")]
    [InlineData("backup")]
    [InlineData("init")]
    [InlineData("show")]
    [InlineData("connect")]
    [InlineData("disconnect")]
    public async Task Command_help_shows_no_default_placeholder_or_internal_type(string command)
    {
        var result = await _cli.RunAsync([command, "--help"]);

        Assert.DoesNotContain("[Default", result.Stdout);
        Assert.DoesNotContain("ShowWhat", result.Stdout);
        Assert.DoesNotContain("?>", result.Stdout);
    }

    [Fact]
    public async Task Restore_help_gives_the_short_form_of_yes_and_the_long_form_of_the_rest()
    {
        var result = await _cli.RunAsync(["restore", "--help"]);

        Assert.Contains("-y, --yes", result.Stdout);
        Assert.Contains("--include-config", result.Stdout);
        Assert.Contains("--from", result.Stdout);
    }
}
