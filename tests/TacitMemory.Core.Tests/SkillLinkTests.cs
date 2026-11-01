using System.Diagnostics;
using System.Text.Json.Nodes;

namespace TacitMemory.Core.Tests;

/// <summary>The skill link <c>connect</c> and <c>disconnect</c> make under the omp skill folder. Real links are created.</summary>
public sealed class SkillLinkTests : IDisposable
{
    private readonly TestHome _home = new();

    public SkillLinkTests() => _home.InitReady();

    public void Dispose()
    {
        // Unlink first, so the recursive delete of the test home never walks into a link.
        if (new DirectoryInfo(Link).LinkTarget is not null)
        {
            if (OperatingSystem.IsWindows())
                Directory.Delete(Link, recursive: false);
            else
                File.Delete(Link);
        }
        _home.Dispose();
    }

    private string Link => Path.Combine(_home.UserHome, ".omp", "agent", "skills", "tacit-memory");

    private string SkillFolder => Path.Combine(_home.TacitHome, "skills", "tacit-memory");

    private string SkillFile => Path.Combine(SkillFolder, "SKILL.md");

    private string AgentsMd => Path.Combine(_home.UserHome, ".omp", "agent", "AGENTS.md");

    private string McpJson => Path.Combine(_home.UserHome, ".omp", "agent", "mcp.json");

    private string BindingsJson => Path.Combine(_home.TacitHome, "bindings.json");

    private static bool IsLinkEntry(string path) => new DirectoryInfo(path).LinkTarget is not null;

    /// <summary>Where the link at <paramref name="path"/> leads, or <c>null</c> when it is not a link.</summary>
    private static string? LeadsTo(string path) => IsLinkEntry(path)
        ? new DirectoryInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName
        : null;

    /// <summary>Makes a link the way another program would: a junction on Windows, a symbolic link elsewhere.</summary>
    private static void MakeForeignLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }
        using var process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            Arguments = $"/d /c mklink /J \"{link}\" \"{target}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    private static string Same(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    [Fact]
    public void Connect_generates_the_skill_and_links_it_into_the_omp_skills_folder()
    {
        _home.Tacit().Connect(ClientKind.Omp);

        Assert.True(File.Exists(SkillFile));
        Assert.True(IsLinkEntry(Link));
        Assert.True(string.Equals(Same(SkillFolder), Same(LeadsTo(Link)!), Comparison));
        Assert.Equal(File.ReadAllBytes(SkillFile), File.ReadAllBytes(Path.Combine(Link, "SKILL.md")));
        Assert.Contains("name: tacit-memory", File.ReadAllText(Path.Combine(Link, "SKILL.md")));
    }

    [Fact]
    public void Connect_makes_a_real_directory_junction_on_windows()
    {
        if (!OperatingSystem.IsWindows())
            return;

        _home.Tacit().Connect(ClientKind.Omp);

        // cmd lists a junction as <JUNCTION>; a symbolic link would be listed as <SYMLINKD>.
        var listing = RunCmd($"dir /AL \"{Path.GetDirectoryName(Link)}\"");
        Assert.Contains("<JUNCTION>", listing);
        Assert.Contains("tacit-memory", listing);
    }

    [Fact]
    public void Connecting_again_keeps_the_existing_link()
    {
        _home.Tacit().Connect(ClientKind.Omp);
        var created = new DirectoryInfo(Link).CreationTimeUtc;
        Thread.Sleep(50);

        _home.Tacit().Connect(ClientKind.Omp);

        Assert.Equal(created, new DirectoryInfo(Link).CreationTimeUtc);
        Assert.Equal(Same(SkillFolder), Same(LeadsTo(Link)!));
        Assert.Single(JsonNode.Parse(File.ReadAllText(BindingsJson))!["bindings"]!.AsArray());
    }

    [Fact]
    public void The_binding_record_holds_the_skill_link_path()
    {
        _home.Tacit().Connect(ClientKind.Omp);

        var binding = JsonNode.Parse(File.ReadAllText(BindingsJson))!["bindings"]![0]!;
        Assert.Equal(Link, (string?)binding["skillLink"]);
    }

    [Fact]
    public void A_directory_with_the_same_name_that_is_not_the_skill_is_refused_and_left_alone()
    {
        Directory.CreateDirectory(Link);
        File.WriteAllText(Path.Combine(Link, "mine.md"), "my own skill");

        var error = Assert.Throws<TacitException>(() => _home.Tacit().Connect(ClientKind.Omp));

        Assert.Equal(TacitError.SkillLinkRefused, error.Error);
        Assert.Contains(Link, error.Message);
        Assert.Equal("my own skill", File.ReadAllText(Path.Combine(Link, "mine.md")));
        Assert.False(File.Exists(AgentsMd));
        Assert.False(File.Exists(McpJson));
        Assert.False(File.Exists(BindingsJson));
    }

    [Fact]
    public void A_file_with_the_same_name_is_refused_and_left_alone()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Link)!);
        File.WriteAllText(Link, "not a skill");

        var error = Assert.Throws<TacitException>(() => _home.Tacit().Connect(ClientKind.Omp));

        Assert.Equal(TacitError.SkillLinkRefused, error.Error);
        Assert.Equal("not a skill", File.ReadAllText(Link));
        Assert.False(File.Exists(AgentsMd));
    }

    [Fact]
    public void A_link_to_somewhere_else_is_refused_and_keeps_pointing_there()
    {
        var other = _home.PathFor("other-skill");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "SKILL.md"), "someone else's");
        Directory.CreateDirectory(Path.GetDirectoryName(Link)!);
        MakeForeignLink(Link, other);

        var error = Assert.Throws<TacitException>(() => _home.Tacit().Connect(ClientKind.Omp));

        Assert.Equal(TacitError.SkillLinkRefused, error.Error);
        Assert.Equal(Same(other), Same(LeadsTo(Link)!));
        Assert.Equal("someone else's", File.ReadAllText(Path.Combine(other, "SKILL.md")));
        Assert.False(File.Exists(AgentsMd));
    }

    [Fact]
    public void A_link_that_cannot_be_made_is_refused_and_changes_nothing_else()
    {
        // A file where the skills folder should be: the link can then be neither created nor placed.
        Directory.CreateDirectory(Path.Combine(_home.UserHome, ".omp", "agent"));
        var blocker = Path.Combine(_home.UserHome, ".omp", "agent", "skills");
        File.WriteAllText(blocker, "in the way");

        var error = Assert.Throws<TacitException>(() => _home.Tacit().Connect(ClientKind.Omp));

        Assert.Equal(TacitError.SkillLinkRefused, error.Error);
        Assert.Equal("in the way", File.ReadAllText(blocker));
        Assert.False(File.Exists(AgentsMd));
        Assert.False(File.Exists(McpJson));
    }

    [Fact]
    public void A_refusal_after_the_link_is_made_removes_that_link_again()
    {
        // Broken markers in AGENTS.md are refused after the link step, so connect must not leave the link behind.
        Directory.CreateDirectory(Path.GetDirectoryName(AgentsMd)!);
        File.WriteAllText(AgentsMd, "<!-- tacit:end -->\n<!-- tacit:begin -->\n");

        var error = Assert.Throws<TacitException>(() => _home.Tacit().Connect(ClientKind.Omp));

        Assert.Equal(TacitError.MarkersBroken, error.Error);
        Assert.False(IsLinkEntry(Link));
        Assert.False(Directory.Exists(Link));
        Assert.True(File.Exists(SkillFile));
    }

    [Fact]
    public void A_bindings_file_that_cannot_be_written_removes_the_link_it_made()
    {
        File.WriteAllText(BindingsJson, "{\n  \"bindings\": []\n}\n");
        File.SetAttributes(BindingsJson, FileAttributes.ReadOnly);
        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => _home.Tacit().Connect(ClientKind.Omp));

            Assert.False(IsLinkEntry(Link));
            Assert.False(Directory.Exists(Link));
        }
        finally
        {
            File.SetAttributes(BindingsJson, FileAttributes.Normal);
        }
    }

    [Fact]
    public void Disconnect_removes_only_the_link_and_keeps_the_skill()
    {
        _home.Tacit().Connect(ClientKind.Omp);
        var skill = File.ReadAllBytes(SkillFile);
        File.WriteAllText(Path.Combine(SkillFolder, "other.txt"), "stays");

        _home.Tacit().Disconnect(ClientKind.Omp);

        Assert.False(IsLinkEntry(Link));
        Assert.False(Directory.Exists(Link));
        Assert.Equal(skill, File.ReadAllBytes(SkillFile));
        Assert.Equal("stays", File.ReadAllText(Path.Combine(SkillFolder, "other.txt")));
        Assert.Empty(JsonNode.Parse(File.ReadAllText(BindingsJson))!["bindings"]!.AsArray());
    }

    [Fact]
    public void Disconnect_leaves_a_directory_that_is_not_the_link_alone()
    {
        Directory.CreateDirectory(Link);
        File.WriteAllText(Path.Combine(Link, "mine.md"), "my own skill");

        _home.Tacit().Disconnect(ClientKind.Omp);

        Assert.Equal("my own skill", File.ReadAllText(Path.Combine(Link, "mine.md")));
    }

    [Fact]
    public void Disconnect_leaves_a_link_to_somewhere_else_alone()
    {
        var other = _home.PathFor("other-skill");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "SKILL.md"), "someone else's");
        Directory.CreateDirectory(Path.GetDirectoryName(Link)!);
        MakeForeignLink(Link, other);

        _home.Tacit().Disconnect(ClientKind.Omp);

        Assert.Equal(Same(other), Same(LeadsTo(Link)!));
        Assert.Equal("someone else's", File.ReadAllText(Path.Combine(other, "SKILL.md")));
    }

    private static string RunCmd(string command)
    {
        using var process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), $"/d /c {command}")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        })!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output;
    }
}
