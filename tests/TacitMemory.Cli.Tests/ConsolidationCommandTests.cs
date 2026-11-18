namespace TacitMemory.Cli.Tests;

/// <summary>
/// <c>tacit consolidation</c> asks the running service whether a consolidation is due and prints the answer in one line,
/// in the memory language; the exit code is 0 either way.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class ConsolidationCommandTests : IDisposable
{
    private readonly CliHarness _cli = new();

    public ConsolidationCommandTests() => _cli.InitReady();

    public void Dispose() => _cli.Dispose();

    [Fact]
    public async Task An_empty_workstream_prints_that_a_consolidation_is_due_and_exits_0()
    {
        await _cli.StartServiceAsync();

        var result = await _cli.RunAsync(["consolidation"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("该整理了\n", result.Stdout.ReplaceLineEndings("\n"));
        Assert.Empty(result.Stderr);
    }

    [Fact]
    public async Task A_fresh_memory_with_a_workstream_prints_that_none_is_due_and_exits_0()
    {
        File.WriteAllText(Path.Combine(_cli.Home.DefaultMemory, "workstreams.md"), "## Workstream\n### alpha\nBackground: 有背景的主线。\n");
        await _cli.StartServiceAsync();

        var result = await _cli.RunAsync(["consolidation"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("暂不需要整理\n", result.Stdout.ReplaceLineEndings("\n"));
    }
}
