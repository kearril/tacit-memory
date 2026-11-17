using System.Reflection;
using System.Text.RegularExpressions;
using TacitMemory.Core.Consolidation;
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
            await service.WriteRecentAsync(new WriteRecentRequest { Text = "写了一条今天的活动" });

        tacit.GenerateSkills();

        Assert.Equal(before, File.ReadAllText(SkillFile("tacit-remember")));
        Assert.DoesNotContain("写了一条今天的活动", before, StringComparison.Ordinal);
    }

    [Fact]
    public void The_remember_skill_has_five_steps_and_leaves_the_workstream_to_consolidation()
    {
        _home.Tacit().GenerateSkills();

        var text = File.ReadAllText(SkillFile("tacit-remember"));

        Assert.Contains("\n5. List what you wrote, one line each", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\n6. ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("workstreams.md", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_consolidation_skill_has_six_steps_in_order_and_ends_with_a_consolidation_commit()
    {
        _home.Tacit().GenerateSkills();

        var text = File.ReadAllText(SkillFile("tacit-consolidate"));

        Assert.Contains("## Steps\n", text, StringComparison.Ordinal);
        var position = 0;
        for (var step = 1; step <= 6; step++)
        {
            var found = text.IndexOf($"\n{step}. ", position, StringComparison.Ordinal);
            Assert.True(found >= 0, $"step {step} is missing or out of order");
            position = found;
        }
        Assert.DoesNotContain("\n7. ", text, StringComparison.Ordinal);
        Assert.Contains("consolidate:", text, StringComparison.Ordinal);
        Assert.Contains("--allow-empty", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_consolidation_skill_does_not_promise_every_deferred_item_again_at_the_next_consolidation()
    {
        // Only candidates stay in candidates.md; the other categories are reported only when they change again.
        _home.Tacit().GenerateSkills();

        var text = File.ReadAllText(SkillFile("tacit-consolidate"));

        Assert.DoesNotContain("reported again at the next consolidation", text, StringComparison.Ordinal);
        Assert.Contains("A candidate stays in candidates.md and comes back at the next consolidation", text, StringComparison.Ordinal);
        Assert.Contains("comes back only when it changes again", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_consolidation_skill_reads_the_report_resource_first_and_stops_without_it()
    {
        _home.Tacit().GenerateSkills();

        var text = File.ReadAllText(SkillFile("tacit-consolidate"));

        var resource = text.IndexOf("tacit://consolidation-report", StringComparison.Ordinal);
        Assert.True(resource >= 0);
        Assert.True(resource < text.IndexOf("## Steps", StringComparison.Ordinal));
        Assert.Contains("tacit serve", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_consolidation_skill_names_every_report_title_from_the_shared_definition()
    {
        _home.Tacit().GenerateSkills();

        var text = File.ReadAllText(SkillFile("tacit-consolidate"));
        var titles = typeof(ConsolidationTitles).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(string))
            .Select(field => (string)field.GetValue(null)!)
            .ToList();

        Assert.Equal(15, titles.Count);
        foreach (var title in titles)
            Assert.Contains(title, text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_consolidation_skill_rewrites_only_the_recent_line_of_a_workstream_and_shows_an_entry_in_each_language()
    {
        _home.Tacit().GenerateSkills();
        var chinese = File.ReadAllText(SkillFile("tacit-consolidate"));
        Assert.Contains("leave its background line unchanged", chinese, StringComparison.Ordinal);
        Assert.Contains("Background line of a workstream", chinese, StringComparison.Ordinal);
        Assert.Contains("Recent line of a workstream", chinese, StringComparison.Ordinal);
        Assert.Contains("## 主线\n### home-server\n背景：家里的常驻服务器，跑代理分流和自用服务。\n近况：", chinese, StringComparison.Ordinal);
        Assert.DoesNotContain("summarized weeks it lists", chinese, StringComparison.Ordinal);

        using var home = new TestHome();
        home.InitReady(language: MemoryLanguage.En);
        home.Tacit().GenerateSkills();
        var english = File.ReadAllText(Path.Combine(home.TacitHome, "skills", "tacit-consolidate", "SKILL.md"));
        Assert.Contains("leave its background line unchanged", english, StringComparison.Ordinal);
        Assert.Contains("## Workstream\n### home-server\nBackground: ", english, StringComparison.Ordinal);
        Assert.Contains("\nRecent: ", english, StringComparison.Ordinal);
        Assert.DoesNotContain("## 主线", english, StringComparison.Ordinal);
    }

    [Fact]
    public void The_consolidation_skill_gives_an_only_workstreams_example_for_the_user_s_added_text()
    {
        _home.Tacit().GenerateSkills();

        var text = File.ReadAllText(SkillFile("tacit-consolidate"));

        Assert.Contains("\"only workstreams\"", text, StringComparison.Ordinal);
        Assert.Contains("Workstream proposal material", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_only_workstreams_example_still_goes_on_to_the_consolidation_commit()
    {
        _home.Tacit().GenerateSkills();

        var text = File.ReadAllText(SkillFile("tacit-consolidate"));

        Assert.Contains("\"only workstreams\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("and nothing else", text, StringComparison.Ordinal);
        Assert.Contains("still ends with the `consolidate:` commit of step 6", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_consolidation_skill_keeps_changes_behind_the_user_s_confirmation_and_numbers_the_list()
    {
        _home.Tacit().GenerateSkills();

        var text = File.ReadAllText(SkillFile("tacit-consolidate"));

        Assert.Contains("only after the user confirms", text, StringComparison.Ordinal);
        Assert.Contains("one numbered list", text, StringComparison.Ordinal);
        Assert.Contains("1 2 4 confirm", text, StringComparison.Ordinal);
        Assert.Contains("change to workstream scope", text, StringComparison.Ordinal);
        Assert.Contains("generalization", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_consolidation_skill_has_no_checkup_terms_and_no_real_dates()
    {
        _home.Tacit().GenerateSkills();
        var chinese = File.ReadAllText(SkillFile("tacit-consolidate"));
        Assert.DoesNotContain("checkup", chinese, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("体检", chinese, StringComparison.Ordinal);
        Assert.DoesNotContain("可确认", chinese, StringComparison.Ordinal);
        Assert.Contains("YYYY-Www", chinese, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"\b\d{4}-\d{2}-\d{2}\b"), chinese);
        Assert.DoesNotMatch(new Regex(@"\b\d{4}-W\d{2}\b"), chinese);
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
        foreach (var word in new[] { "作用域：", "领域", "主线", "触发", "遗留", "推翻记录", "教训", "汇总" })
            Assert.Contains(word, chinese, StringComparison.Ordinal);
        Assert.Contains("## 领域：network-proxy", chinese, StringComparison.Ordinal);
        Assert.Contains("作用域：domain:network-proxy", chinese, StringComparison.Ordinal);
        Assert.Contains("- YYYY-Www：", chinese, StringComparison.Ordinal);

        using var home = new TestHome();
        home.InitReady(language: MemoryLanguage.En);
        home.Tacit().GenerateSkills();
        var english = File.ReadAllText(Path.Combine(home.TacitHome, "skills", "tacit-consolidate", "SKILL.md"));
        Assert.Contains("Rejection list", english, StringComparison.Ordinal);
        Assert.Contains("instead", english, StringComparison.Ordinal);
        Assert.Contains("Belongs to: Preferences", english, StringComparison.Ordinal);
        Assert.Contains("rejected quote「", english, StringComparison.Ordinal);
        foreach (var word in new[] { "Scope: ", "Domain", "Workstream", "Trigger", "Leftovers", "Overturned", "Lesson", "Summary" })
            Assert.Contains(word, english, StringComparison.Ordinal);
        Assert.Contains("## Domain: network-proxy", english, StringComparison.Ordinal);
        Assert.Contains("Scope: domain:network-proxy", english, StringComparison.Ordinal);
        Assert.Contains("- YYYY-Www：", english, StringComparison.Ordinal);
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
