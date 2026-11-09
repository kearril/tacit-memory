using System.Text.RegularExpressions;
using TacitMemory.Core.Show;

namespace TacitMemory.Core.Tests;

/// <summary>The two memory commands, generated as skills only the user can run, beside the memory skill.</summary>
public sealed class CommandSkillTests : IDisposable
{
    private readonly TestHome _home = new();

    public CommandSkillTests() => _home.InitReady();

    public void Dispose() => _home.Dispose();

    private string SkillFile(string name) => Path.Combine(_home.TacitHome, "skills", name, "SKILL.md");

    private static string Frontmatter(string text)
    {
        Assert.StartsWith("---\n", text, StringComparison.Ordinal);
        return text[4..text.IndexOf("\n---\n", 4, StringComparison.Ordinal)];
    }

    [Theory]
    [InlineData("tacit-remember")]
    [InlineData("tacit-consolidate")]
    public void Generation_writes_a_memory_command_the_model_may_not_invoke_on_its_own(string name)
    {
        var result = _home.Tacit().GenerateSkills();

        Assert.True(Assert.Single(result.Skills, skill => skill.Name == name).Written);
        var frontmatter = Frontmatter(File.ReadAllText(SkillFile(name)));
        Assert.Contains($"name: {name}\n", frontmatter + "\n", StringComparison.Ordinal);
        Assert.Contains("disable-model-invocation: true", frontmatter, StringComparison.Ordinal);
        var description = Regex.Match(frontmatter, "^description: \"(.*)\"$", RegexOptions.Multiline).Groups[1].Value;
        Assert.InRange(description.Length, 1, 160);
    }

    [Fact]
    public void Starting_the_service_writes_every_skill()
    {
        using var service = _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

        Assert.True(File.Exists(SkillFile("tacit-memory")));
        Assert.True(File.Exists(SkillFile("tacit-remember")));
        Assert.True(File.Exists(SkillFile("tacit-consolidate")));
    }

    [Fact]
    public void Generating_again_rewrites_none_of_the_unchanged_skills()
    {
        var tacit = _home.Tacit();
        var first = tacit.GenerateSkills();
        var old = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        foreach (var skill in first.Skills)
            File.SetLastWriteTimeUtc(skill.Path, old);

        var again = tacit.GenerateSkills();

        Assert.All(again.Skills, skill =>
        {
            Assert.False(skill.Written);
            Assert.Equal(old, File.GetLastWriteTimeUtc(skill.Path));
        });
    }

    [Fact]
    public async Task The_remember_skill_does_not_depend_on_the_memory()
    {
        var tacit = _home.Tacit();
        tacit.GenerateSkills();
        var before = File.ReadAllText(SkillFile("tacit-remember"));
        using (var service = tacit.OpenService(_ => { }, NullLoggerFactory.Instance))
            await service.WriteRecentAsync(new WriteRecentRequest { Op = "add_activity", Text = "写了一条今天的活动" });

        tacit.GenerateSkills();

        Assert.Equal(before, File.ReadAllText(SkillFile("tacit-remember")));
        Assert.DoesNotContain("写了一条今天的活动", before, StringComparison.Ordinal);
    }

    [Fact]
    public void The_consolidation_skill_reads_the_report_resource_first_and_ends_with_a_consolidation_commit()
    {
        _home.Tacit().GenerateSkills();

        var text = File.ReadAllText(SkillFile("tacit-consolidate"));

        Assert.Contains("tacit://consolidation-report", text, StringComparison.Ordinal);
        Assert.Contains("consolidate:", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_consolidation_skill_writes_the_structure_words_and_examples_in_the_memory_language()
    {
        _home.Tacit().GenerateSkills();
        var chinese = File.ReadAllText(SkillFile("tacit-consolidate"));
        Assert.Contains("拒绝列表", chinese, StringComparison.Ordinal);
        Assert.Contains("改为", chinese, StringComparison.Ordinal);
        Assert.Contains("归属：偏好", chinese, StringComparison.Ordinal);
        Assert.Contains("拒绝 原话「", chinese, StringComparison.Ordinal);

        using var home = new TestHome();
        // InitReady rewrites config.yaml without the language key, so the memory language is set again here.
        home.InitReady(extraConfig: "language: en\n", language: MemoryLanguage.En);
        home.Tacit().GenerateSkills();
        var english = File.ReadAllText(Path.Combine(home.TacitHome, "skills", "tacit-consolidate", "SKILL.md"));
        Assert.Contains("Rejection list", english, StringComparison.Ordinal);
        Assert.Contains("instead", english, StringComparison.Ordinal);
        Assert.Contains("Belongs to: Preferences", english, StringComparison.Ordinal);
        Assert.Contains("rejected quote「", english, StringComparison.Ordinal);
        Assert.DoesNotContain("拒绝列表", english, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ShowTarget.Remember, "tacit-remember")]
    [InlineData(ShowTarget.Consolidation, "tacit-consolidate")]
    public async Task Show_prints_a_memory_command_as_written_and_has_no_built_in_default(ShowTarget target, string name)
    {
        var tacit = _home.Tacit();
        tacit.GenerateSkills();

        var result = await tacit.ShowAsync(target);

        var part = Assert.Single(result.Parts);
        Assert.Equal(ShowSource.Code, part.Source);
        Assert.Equal(File.ReadAllText(SkillFile(name)).TrimEnd('\n'), part.Text);
        var refused = await Assert.ThrowsAsync<TacitException>(() => tacit.ShowAsync(target, useDefault: true));
        Assert.Equal(TacitError.NoBuiltInText, refused.Error);
    }
}
