namespace TacitMemory.Cli.Tests;

/// <summary>
/// <c>tacit init</c> takes the memory language from <c>--language</c> or the existing config; with neither, it asks at a
/// terminal and refuses elsewhere, so an Agent or a script never waits on a question.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class InitCommandTests : IDisposable
{
    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    private string ConfigPath => Path.Combine(_cli.Home.TacitHome, "config.yaml");

    [Fact]
    public async Task Without_a_language_or_a_terminal_init_is_a_usage_error_and_writes_nothing()
    {
        var result = await _cli.RunAsync(["init"]);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("--language", result.Stderr, StringComparison.Ordinal);
        Assert.False(File.Exists(ConfigPath));
    }

    [Fact]
    public async Task At_a_terminal_init_asks_for_the_language_until_it_gets_one()
    {
        var result = await _cli.RunAsync(["init"], stdin: "fr\nzh\n", stdinIsTerminal: true);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(2, result.Stderr.Split("zh or en", StringSplitOptions.None).Length - 1);
        Assert.Equal("language: zh\n", File.ReadAllText(ConfigPath));
    }

    [Fact]
    public async Task At_a_terminal_init_stops_when_the_input_ends_without_an_answer()
    {
        var result = await _cli.RunAsync(["init"], stdin: "", stdinIsTerminal: true);

        Assert.Equal(1, result.ExitCode);
        Assert.False(File.Exists(ConfigPath));
    }

    [Fact]
    public async Task With_a_language_in_the_existing_config_init_does_not_ask()
    {
        Directory.CreateDirectory(_cli.Home.TacitHome);
        File.WriteAllText(ConfigPath, "language: en\n");

        var result = await _cli.RunAsync(["init"]);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("zh or en", result.Stderr, StringComparison.Ordinal);
        Assert.Equal("language: en\n", File.ReadAllText(ConfigPath));
    }
}
