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
        { ["connect", "--client", "omp"], "--context" },
        { ["init", "--language", "fr"], "zh, en" },
        { ["status", "--bogus"], "--bogus" },
        { ["connect", "--client", "omp", "--bogus"], "--bogus" },
        { ["connect", "--client", "omp", "--context", "x"], "--skills" },
        { ["connect", "--client", "omp", "--skills", "x"], "--context" },
        { ["connect", "--client", "a_b", "--context", "x", "--skills", "y"], "a_b" },
        { ["disconnect"], "--client" },
        { ["init", "extra"], "extra" },
        { ["backup", "--to", "x", "--remote"], "--remote" },
        { ["restore"], "--from" },
        { ["restore", "x.zip"], "x.zip" },
        { ["restore", "--from", "x.zip", "--remote"], "--remote" },
        { ["show", "tools", "--default"], "--default" },
        { ["show", "remember", "--default"], "--default" },
        { ["show", "checkup", "--default"], "--default" },
        { ["show", "--default"], "--default" },
        { ["probe", "bogus"], "bogus" },
        { ["PROBE", "BOGUS"], "BOGUS" },
        { ["probe", "embedding", "extra"], "extra" },
        { ["probe", "decision", "--default"], "--default" },
        { ["CONNECT"], "--client" },
        { ["Show", "BOGUS"], "BOGUS" },
        { ["restore", "--from", ""], "--from" },
        { ["backup", "--to", "  "], "--to" },
        { ["init", "--memory", ""], "--memory" },
        { ["connect", "--client", "omp", "--context", "", "--skills", "x"], "--context" },
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

    [Theory]
    [InlineData("restore", "--from", "")]
    [InlineData("backup", "--to", "   ")]
    [InlineData("init", "--memory", "")]
    public async Task A_blank_path_option_is_a_usage_error_with_one_line_on_stderr(string command, string option, string value)
    {
        var result = await _cli.RunAsync([command, option, value]);

        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Single(result.Stderr.Trim().Split('\n'));
        Assert.DoesNotContain("   at ", result.Stderr, StringComparison.Ordinal);
    }
}
