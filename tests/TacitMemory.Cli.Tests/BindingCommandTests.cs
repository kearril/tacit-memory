namespace TacitMemory.Cli.Tests;

/// <summary>
/// <c>connect</c> and <c>disconnect</c> through the CLI: every option of <c>connect</c> is required, a bad name is a usage
/// error, the endpoint reaches stdout, and <c>disconnect</c> needs only the name.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class BindingCommandTests : IDisposable
{
    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    private string AgentsMd => Path.Combine(_cli.Home.UserHome, ".omp", "agent", "AGENTS.md");

    private string BindingsJson => Path.Combine(_cli.Home.TacitHome, "bindings.json");

    [Fact]
    public async Task Connect_without_skills_exits_2_and_writes_nothing()
    {
        _cli.InitReady();
        Directory.CreateDirectory(Path.GetDirectoryName(AgentsMd)!);

        var result = await _cli.RunAsync(["connect", "--client", "omp", "--context", "~/.omp/agent/AGENTS.md"]);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("--skills", result.Stderr);
        Assert.Empty(result.Stdout);
        Assert.False(File.Exists(AgentsMd));
        Assert.False(File.Exists(BindingsJson));
    }

    [Fact]
    public async Task Connect_prints_the_mcp_endpoint_on_stdout()
    {
        _cli.InitReady();

        var result = await _cli.RunAsync(_cli.OmpConnectArgs());

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Stderr);
        Assert.Contains($"http://127.0.0.1:{_cli.Port}/mcp", result.Stdout);
        Assert.Contains("Streamable HTTP", result.Stdout);
    }

    [Fact]
    public async Task Connect_with_an_invalid_client_name_exits_2()
    {
        _cli.InitReady();

        var result = await _cli.RunAsync(["connect", "--client", "Open AI", "--context", "~/.omp/agent/AGENTS.md", "--skills", "~/.omp/agent/skills"]);

        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains("Open AI", result.Stderr);
    }

    [Fact]
    public async Task Disconnect_takes_only_the_name_and_reads_the_paths_from_the_record()
    {
        _cli.InitReady();
        Assert.Equal(0, (await _cli.RunAsync(_cli.OmpConnectArgs())).ExitCode);

        var result = await _cli.RunAsync(["disconnect", "--client", "OMP"]);

        Assert.Equal(0, result.ExitCode);
        Assert.False(File.Exists(AgentsMd));
    }

    [Fact]
    public async Task Disconnect_of_a_name_that_is_not_bound_exits_1()
    {
        _cli.InitReady();

        var result = await _cli.RunAsync(["disconnect", "--client", "nobody"]);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains("nobody", result.Stderr);
    }
}
