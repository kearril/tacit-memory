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

    [Fact]
    public async Task Connect_exits_3_when_the_config_does_not_load()
    {
        _cli.InitReady();
        _cli.BreakConfig();

        var result = await _cli.RunAsync(_cli.OmpConnectArgs());

        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public async Task A_missing_decision_key_exits_3_and_names_the_variable()
    {
        _cli.InitReady();
        _cli.RemoveDecisionKey();

        var result = await _cli.RunAsync(_cli.OmpConnectArgs());

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("TACIT_DECISION_API_KEY", result.Stderr);
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
}
