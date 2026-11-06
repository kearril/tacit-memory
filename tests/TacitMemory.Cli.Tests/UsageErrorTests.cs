namespace TacitMemory.Cli.Tests;

/// <summary>
/// Usage errors exit 2 with the reason on stderr and nothing on stdout, and they are found before the config is read.
/// Only the reason's key fact is asserted, not the whole sentence.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class UsageErrorTests : IDisposable
{
    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    public static TheoryData<string[], string> UsageErrors() => new()
    {
        { ["foo"], "foo" },
        { ["show"], "instructions" },
        { ["show", "bogus"], "bogus" },
        { ["connect"], "--client" },
        { ["connect", "--client", "fr"], "omp" },
        { ["init", "--language", "fr"], "zh, en" },
        { ["status", "--bogus"], "--bogus" },
        { ["connect", "--client", "omp", "--bogus"], "--bogus" },
        { ["init", "extra"], "extra" },
        { ["backup", "--to", "x", "--remote"], "--remote" },
        { ["restore"], "zip" },
        { ["restore", "x.zip", "--remote"], "--remote" },
        { ["show", "tools", "--default"], "--default" },
    };

    [Theory]
    [MemberData(nameof(UsageErrors))]
    public async Task A_usage_error_exits_2_and_says_why_on_stderr_only(string[] args, string reason)
    {
        var result = await _cli.RunAsync(args);

        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains(reason, result.Stderr);
    }

    [Fact]
    public async Task A_usage_error_is_found_before_the_config_is_read()
    {
        _cli.InitReady();
        _cli.BreakConfig();

        var result = await _cli.RunAsync(["backup", "--to", "x", "--remote"]);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("--remote", result.Stderr);
    }

    [Fact]
    public async Task Enum_values_are_read_without_regard_to_case()
    {
        var result = await _cli.RunAsync(["init", "--language", "ZH"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Stderr);
    }
}
