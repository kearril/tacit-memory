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
        foreach (var name in Names)
        {
            if (IsLinkEntry(LinkOf(name)))
                Unlink(LinkOf(name));
        }
        _home.Dispose();
    }

    /// <summary>Removes the link entry itself, never what it points to.</summary>
    private static void Unlink(string link)
    {
        if (OperatingSystem.IsWindows())
            Directory.Delete(link, recursive: false);
        else
            File.Delete(link);
    }

    /// <summary>Every skill connect links into the client's skill folder.</summary>
    private static readonly string[] Names = ["tacit-memory", "tacit-remember", "tacit-checkup"];

    public static TheoryData<string> SkillNames => new(Names);

    private string LinkOf(string name) => Path.Combine(_home.UserHome, ".omp", "agent", "skills", name);

    private string FolderOf(string name) => Path.Combine(_home.TacitHome, "skills", name);

    private string Link => LinkOf("tacit-memory");

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

    [Theory]
    [MemberData(nameof(SkillNames))]
    public void Connect_generates_each_skill_and_links_it_into_the_omp_skills_folder(string name)
    {
        _home.Tacit().Connect(ClientKind.Omp);

        var link = LinkOf(name);
        Assert.True(File.Exists(Path.Combine(FolderOf(name), "SKILL.md")));
        Assert.True(IsLinkEntry(link));
        Assert.True(string.Equals(Same(FolderOf(name)), Same(LeadsTo(link)!), Comparison));
        Assert.Contains($"name: {name}", File.ReadAllText(Path.Combine(link, "SKILL.md")));
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

    [Theory]
    [MemberData(nameof(SkillNames))]
    public void A_directory_with_the_name_of_any_skill_refuses_connect_and_writes_nothing(string name)
    {
        var taken = LinkOf(name);
        Directory.CreateDirectory(taken);
        File.WriteAllText(Path.Combine(taken, "mine.md"), "my own skill");

        var error = Assert.Throws<TacitException>(() => _home.Tacit().Connect(ClientKind.Omp));

        Assert.Equal(TacitError.SkillLinkRefused, error.Error);
        Assert.Contains(taken, error.Message);
        Assert.Equal("my own skill", File.ReadAllText(Path.Combine(taken, "mine.md")));
        Assert.All(Names, other => Assert.False(IsLinkEntry(LinkOf(other))));
        Assert.False(File.Exists(AgentsMd));
        Assert.False(File.Exists(McpJson));
        Assert.False(File.Exists(BindingsJson));
        Assert.False(Directory.Exists(Path.Combine(_home.TacitHome, "skills")));
    }

    [Theory]
    [MemberData(nameof(SkillNames))]
    public void A_file_with_the_name_of_any_skill_refuses_connect_and_no_link_is_made(string name)
    {
        var taken = LinkOf(name);
        Directory.CreateDirectory(Path.GetDirectoryName(taken)!);
        File.WriteAllText(taken, "not a skill");

        var error = Assert.Throws<TacitException>(() => _home.Tacit().Connect(ClientKind.Omp));

        Assert.Equal(TacitError.SkillLinkRefused, error.Error);
        Assert.Equal("not a skill", File.ReadAllText(taken));
        Assert.All(Names.Where(other => other != name), other => Assert.False(IsLinkEntry(LinkOf(other))));
        Assert.False(File.Exists(AgentsMd));
    }

    [Theory]
    [MemberData(nameof(SkillNames))]
    public void A_link_elsewhere_with_the_name_of_any_skill_refuses_connect_and_keeps_pointing_there(string name)
    {
        var other = _home.PathFor("other-skill");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "SKILL.md"), "someone else's");
        var taken = LinkOf(name);
        Directory.CreateDirectory(Path.GetDirectoryName(taken)!);
        MakeForeignLink(taken, other);

        var error = Assert.Throws<TacitException>(() => _home.Tacit().Connect(ClientKind.Omp));

        Assert.Equal(TacitError.SkillLinkRefused, error.Error);
        Assert.Equal(Same(other), Same(LeadsTo(taken)!));
        Assert.Equal("someone else's", File.ReadAllText(Path.Combine(other, "SKILL.md")));
        Assert.All(Names.Where(n => n != name), n => Assert.False(IsLinkEntry(LinkOf(n))));
        Assert.False(File.Exists(AgentsMd));
    }

    [Fact]
    public void Connecting_again_adds_a_missing_link_and_keeps_the_others()
    {
        _home.Tacit().Connect(ClientKind.Omp);
        var created = new DirectoryInfo(Link).CreationTimeUtc;
        // As after an upgrade that brought a new skill: its link is not there yet.
        Unlink(LinkOf("tacit-remember"));
        Thread.Sleep(50);

        _home.Tacit().Connect(ClientKind.Omp);

        Assert.Equal(created, new DirectoryInfo(Link).CreationTimeUtc);
        Assert.All(Names, name => Assert.Equal(Same(FolderOf(name)), Same(LeadsTo(LinkOf(name))!)));
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
    public void A_refusal_after_the_links_are_made_removes_them_again()
    {
        // Broken markers in AGENTS.md are refused after the link step, so connect must not leave the links behind.
        Directory.CreateDirectory(Path.GetDirectoryName(AgentsMd)!);
        File.WriteAllText(AgentsMd, "<!-- tacit:end -->\n<!-- tacit:begin -->\n");

        var error = Assert.Throws<TacitException>(() => _home.Tacit().Connect(ClientKind.Omp));

        Assert.Equal(TacitError.MarkersBroken, error.Error);
        Assert.All(Names, name =>
        {
            Assert.False(IsLinkEntry(LinkOf(name)));
            Assert.False(Directory.Exists(LinkOf(name)));
        });
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

            Assert.All(Names, name => Assert.False(IsLinkEntry(LinkOf(name))));
        }
        finally
        {
            File.SetAttributes(BindingsJson, FileAttributes.Normal);
        }
    }

    [Fact]
    public void Disconnect_removes_only_the_links_and_keeps_the_skills()
    {
        _home.Tacit().Connect(ClientKind.Omp);
        var skill = File.ReadAllBytes(SkillFile);
        File.WriteAllText(Path.Combine(SkillFolder, "other.txt"), "stays");

        _home.Tacit().Disconnect(ClientKind.Omp);

        Assert.All(Names, name =>
        {
            Assert.False(IsLinkEntry(LinkOf(name)));
            Assert.False(Directory.Exists(LinkOf(name)));
            Assert.True(File.Exists(Path.Combine(FolderOf(name), "SKILL.md")));
        });
        Assert.Equal(skill, File.ReadAllBytes(SkillFile));
        Assert.Equal("stays", File.ReadAllText(Path.Combine(SkillFolder, "other.txt")));
        Assert.Empty(JsonNode.Parse(File.ReadAllText(BindingsJson))!["bindings"]!.AsArray());
    }

    [Fact]
    public void Disconnect_of_a_binding_made_before_the_memory_commands_removes_the_memory_skill_link()
    {
        _home.Tacit().Connect(ClientKind.Omp);
        // An older connect made only the memory skill's link.
        Unlink(LinkOf("tacit-remember"));

        _home.Tacit().Disconnect(ClientKind.Omp);

        Assert.All(Names, name => Assert.False(Directory.Exists(LinkOf(name))));
    }

    [Theory]
    [MemberData(nameof(SkillNames))]
    public void Disconnect_leaves_a_directory_with_the_name_of_a_skill_alone(string name)
    {
        _home.Tacit().Connect(ClientKind.Omp);
        var taken = LinkOf(name);
        Unlink(taken);
        Directory.CreateDirectory(taken);
        File.WriteAllText(Path.Combine(taken, "mine.md"), "my own skill");

        _home.Tacit().Disconnect(ClientKind.Omp);

        Assert.Equal("my own skill", File.ReadAllText(Path.Combine(taken, "mine.md")));
        Assert.All(Names.Where(other => other != name), other => Assert.False(Directory.Exists(LinkOf(other))));
    }

    [Theory]
    [MemberData(nameof(SkillNames))]
    public void Disconnect_leaves_a_link_elsewhere_with_the_name_of_a_skill_alone(string name)
    {
        var other = _home.PathFor("other-skill");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "SKILL.md"), "someone else's");
        var taken = LinkOf(name);
        Directory.CreateDirectory(Path.GetDirectoryName(taken)!);
        MakeForeignLink(taken, other);

        _home.Tacit().Disconnect(ClientKind.Omp);

        Assert.Equal(Same(other), Same(LeadsTo(taken)!));
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
