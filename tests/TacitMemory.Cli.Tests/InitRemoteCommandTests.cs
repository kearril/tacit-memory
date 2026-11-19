using TacitMemory.Core.Tests;

namespace TacitMemory.Cli.Tests;

/// <summary>
/// <c>tacit init --remote</c> joins a synced memory: it clones the remote and names it in the config, and refuses anything
/// it would have to overwrite or guess.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class InitRemoteCommandTests : IDisposable
{
    private readonly CliHarness _cli = new();
    private readonly TestHome _source = new();

    public void Dispose()
    {
        _cli.Dispose();
        _source.Dispose();
    }

    private string Memory => _cli.Home.DefaultMemory;

    /// <summary>A bare repository in the test folder; with <paramref name="filled"/>, holding the source machine's memory.</summary>
    private string Remote(bool filled = true)
    {
        var remote = Path.Combine(_cli.Home.Root, "remote.git");
        if (!filled)
        {
            TestHome.Git(_cli.Home.Root, "init", "--quiet", "--bare", remote);
            return remote;
        }

        _source.InitReady();
        var branch = TestHome.Git(_source.DefaultMemory, "symbolic-ref", "--short", "HEAD").Trim();
        TestHome.Git(_cli.Home.Root, "init", "--quiet", "--bare", $"--initial-branch={branch}", remote);
        TestHome.Git(_source.DefaultMemory, "push", "--quiet", remote, $"HEAD:refs/heads/{branch}");
        return remote;
    }

    [Fact]
    public async Task Init_with_a_remote_clones_it_and_names_it_in_the_config()
    {
        var remote = Remote();

        var result = await _cli.RunAsync(["init", "--language", "zh", "--remote", remote]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            File.ReadAllText(Path.Combine(_source.DefaultMemory, "persona.md")),
            File.ReadAllText(Path.Combine(Memory, "persona.md")));
        Assert.Contains(remote, File.ReadAllText(_cli.ConfigPath), StringComparison.Ordinal);
        Assert.Equal(remote, TestHome.Git(Memory, "remote", "get-url", "tacit").Trim());
        Assert.Equal("", TestHome.Git(Memory, "status", "--porcelain"));
    }

    [Fact]
    public async Task An_empty_remote_is_refused_before_anything_is_written()
    {
        var remote = Remote(filled: false);

        var result = await _cli.RunAsync(["init", "--language", "zh", "--remote", remote]);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("tacit sync", result.Stderr, StringComparison.Ordinal);
        Assert.False(File.Exists(_cli.ConfigPath));
        Assert.False(Directory.Exists(Memory));
    }

    [Fact]
    public async Task A_memory_directory_that_is_not_a_clone_of_the_remote_is_left_alone()
    {
        var remote = Remote();
        Directory.CreateDirectory(Memory);
        File.WriteAllText(Path.Combine(Memory, "notes.md"), "已有的笔记\n");

        var result = await _cli.RunAsync(["init", "--language", "zh", "--remote", remote]);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(["notes.md"], Directory.GetFileSystemEntries(Memory).Select(Path.GetFileName));
        Assert.False(File.Exists(_cli.ConfigPath));
    }

    [Fact]
    public async Task An_existing_config_without_that_remote_is_refused()
    {
        var remote = Remote();
        Directory.CreateDirectory(_cli.Home.TacitHome);
        File.WriteAllText(_cli.ConfigPath, "language: zh\n");

        var result = await _cli.RunAsync(["init", "--remote", remote]);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("sync.remote", result.Stderr, StringComparison.Ordinal);
        Assert.Equal("language: zh\n", File.ReadAllText(_cli.ConfigPath));
        Assert.False(Directory.Exists(Memory));
    }
}
