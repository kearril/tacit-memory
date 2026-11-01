using TacitMemory.Core.Show;

namespace TacitMemory.Core.Tests;

/// <summary>The show operation: what the agent receives, with sources and lengths. No service is opened in these tests.</summary>
public sealed class ShowTests : IDisposable
{
    private readonly TestHome _home = new();

    public ShowTests() => _home.InitReady();

    public void Dispose() => _home.Dispose();

    private string Prompts => Path.Combine(_home.TacitHome, "prompts");

    private static ShowPart Part(ShowResult result, string key) => result.Parts.Single(part => part.Key == key);

    private static bool IsCjk(char character) => character is >= '\u4e00' and <= '\u9fff';

    [Fact]
    public async Task Instructions_name_each_part_with_its_source_and_the_fixed_and_total_lengths()
    {
        var result = await _home.Tacit().ShowAsync(ShowTarget.Instructions);

        Assert.Equal(ShowSource.Code, Part(result, "opening").Source);
        Assert.Equal(ShowSource.BuiltInDefault, Part(result, "instructions").Source);
        Assert.Equal(ShowSource.Code, Part(result, "general").Source);
        Assert.Equal(ShowSource.Code, Part(result, "language").Source);
        var criteria = Part(result, "criteria");
        Assert.Equal(ShowSource.UserFile, criteria.Source);
        Assert.Equal(Path.Combine(Prompts, "rules.md"), criteria.Path);

        Assert.Equal(2500, result.Fixed!.Limit);
        Assert.Equal(4000, result.Whole!.Limit);
        Assert.True(result.Fixed.Length <= result.Fixed.Limit);
        Assert.True(result.Whole.Length >= result.Fixed.Length && result.Whole.Length <= result.Whole.Limit);
        var heading = result.Report.Split('\n')[0];
        Assert.Contains($"{result.Fixed!.Length}/2500", heading);
        Assert.Contains($"{result.Whole!.Length}/4000", heading);
    }

    [Fact]
    public async Task Instructions_leave_out_the_runtime_status()
    {
        var result = await _home.Tacit().ShowAsync(ShowTarget.Instructions);

        Assert.DoesNotContain("## Status", result.Report);
        Assert.DoesNotContain("Unavailable auxiliary models", result.Report);
    }

    [Fact]
    public async Task The_fixed_part_of_the_instructions_has_the_length_the_service_checks()
    {
        var home = _home.Tacit();
        var shown = (await home.ShowAsync(ShowTarget.Instructions)).Fixed!.Length;

        // Adding the same text to the core rules must grow the fixed part by exactly that much.
        Directory.CreateDirectory(Prompts);
        File.WriteAllText(Path.Combine(Prompts, "instructions.md"), "## Extra\n- One more rule.\n");
        var extended = await home.ShowAsync(ShowTarget.Instructions);

        Assert.Equal(ShowSource.UserFile, Part(extended, "instructions").Source);
        Assert.NotEqual(shown, extended.Fixed!.Length);
        Assert.Equal(extended.Fixed.Length, Part(extended, "instructions").Text.Length
            + Part(extended, "opening").Text.Length + Part(extended, "general").Text.Length
            + Part(extended, "language").Text.Length + Part(extended, "criteria").Text.Length + 4 * 2);
    }

    [Fact]
    public async Task The_user_instructions_are_shown_without_their_html_comments()
    {
        Directory.CreateDirectory(Prompts);
        File.WriteAllText(Path.Combine(Prompts, "instructions.md"), "<!-- editor note -->\n## Custom\n- A custom rule.\n");

        var result = await _home.Tacit().ShowAsync(ShowTarget.Instructions);

        var part = Part(result, "instructions");
        Assert.Equal(ShowSource.UserFile, part.Source);
        Assert.Equal(Path.Combine(Prompts, "instructions.md"), part.Path);
        Assert.Contains("## Custom", part.Text);
        Assert.DoesNotContain("editor note", result.Report);
    }

    [Fact]
    public async Task The_default_instructions_keep_their_comments_and_are_the_built_in_text_alone()
    {
        var defaults = await _home.Tacit().ShowAsync(ShowTarget.Instructions, useDefault: true);

        Assert.Equal(defaults.Report, Assert.Single(defaults.Parts).Text);
        Assert.Contains("<!--", defaults.Report);
        Assert.StartsWith("## Using memory", defaults.Report);
        Assert.Null(defaults.Fixed);
    }

    [Fact]
    public async Task Saving_the_default_instructions_as_the_file_gives_the_same_core_rules()
    {
        var home = _home.Tacit();
        var before = Part(await home.ShowAsync(ShowTarget.Instructions), "instructions");

        Directory.CreateDirectory(Prompts);
        File.WriteAllText(Path.Combine(Prompts, "instructions.md"), (await home.ShowAsync(ShowTarget.Instructions, useDefault: true)).Report);
        var after = Part(await home.ShowAsync(ShowTarget.Instructions), "instructions");

        Assert.Equal(ShowSource.BuiltInDefault, before.Source);
        Assert.Equal(ShowSource.UserFile, after.Source);
        Assert.Equal(before.Text, after.Text);
    }

    [Fact]
    public async Task The_resident_block_shows_the_built_in_shell_and_four_sections_with_their_lengths()
    {
        var result = await _home.Tacit().ShowAsync(ShowTarget.Resident);

        Assert.Equal(ShowSource.BuiltInDefault, Part(result, "shell").Source);
        Assert.False(string.IsNullOrWhiteSpace(Part(result, "red_lines").Text));
        Assert.False(string.IsNullOrWhiteSpace(Part(result, "profile").Text));
        Assert.Equal(1500, result.Whole!.Limit);
        Assert.Equal(result.Whole.Length, result.Report.Split("\n\n", 2)[1].Length);
        Assert.Contains($"{result.Whole!.Length}/1500", result.Report.Split('\n')[0]);
    }

    [Fact]
    public async Task The_default_resident_text_saved_as_the_file_gives_the_same_parts()
    {
        var home = _home.Tacit();
        var before = await home.ShowAsync(ShowTarget.Resident);

        Directory.CreateDirectory(Prompts);
        File.WriteAllText(Path.Combine(Prompts, "resident.md"), (await home.ShowAsync(ShowTarget.Resident, useDefault: true)).Report);
        var after = await home.ShowAsync(ShowTarget.Resident);

        Assert.Equal(ShowSource.UserFile, Part(after, "shell").Source);
        foreach (var key in new[] { "shell", "red_lines", "working_rules", "preferences", "profile" })
            Assert.Equal(Part(before, key).Text, Part(after, key).Text);
        Assert.Equal(before.Whole!.Length, after.Whole!.Length);
    }

    [Fact]
    public async Task A_resident_file_without_a_section_shows_no_guidance_for_it()
    {
        Directory.CreateDirectory(Prompts);
        File.WriteAllText(Path.Combine(Prompts, "resident.md"), "Shell text.\n\n## profile\nFacts only.\n");

        var result = await _home.Tacit().ShowAsync(ShowTarget.Resident);

        Assert.Equal("", Part(result, "red_lines").Text);
        Assert.Equal("Facts only.", Part(result, "profile").Text);
        Assert.DoesNotContain("## red_lines", result.Report);
    }

    [Fact]
    public async Task The_resident_length_shown_is_the_length_of_the_file_the_limit_checks()
    {
        // Trailing blank lines are not part of the composed block, but the length limit counts them.
        var raw = "Shell text.\n\n## profile\nFacts only.\n" + new string('\n', 300);
        Directory.CreateDirectory(Prompts);
        File.WriteAllText(Path.Combine(Prompts, "resident.md"), raw);

        var result = await _home.Tacit().ShowAsync(ShowTarget.Resident);

        Assert.Equal(raw.Length, result.Whole!.Length);
        Assert.Contains($"{raw.Length}/", result.Report);
    }

    [Fact]
    public async Task The_default_skill_is_the_system_part_alone()
    {
        var home = _home.Tacit();

        var defaults = await home.ShowAsync(ShowTarget.Skill, useDefault: true);
        var full = await home.ShowAsync(ShowTarget.Skill);

        Assert.Equal(Part(full, "system").Text, defaults.Report);
        Assert.DoesNotContain("name: tacit-memory", defaults.Report);
    }

    [Fact]
    public async Task The_skill_shows_its_whole_content_and_has_no_conventions_section_without_skill_md()
    {
        var result = await _home.Tacit().ShowAsync(ShowTarget.Skill);

        Assert.Equal(["system"], result.Parts.Select(part => part.Key));
        Assert.Contains("\n\n---\nname: tacit-memory\n", result.Report);
        Assert.Matches(@"(?m)^# \S", result.Report);
        Assert.DoesNotContain("## User conventions", result.Report);
    }

    [Fact]
    public async Task The_skill_appends_the_user_conventions_with_their_source_and_limit()
    {
        Directory.CreateDirectory(Prompts);
        File.WriteAllText(Path.Combine(Prompts, "skill.md"), "Use Chinese page titles.");

        var result = await _home.Tacit().ShowAsync(ShowTarget.Skill);

        var conventions = Part(result, "conventions");
        Assert.Equal(ShowSource.UserFile, conventions.Source);
        Assert.Equal(Path.Combine(Prompts, "skill.md"), conventions.Path);
        Assert.Equal(3000, conventions.Limit);
        Assert.Equal("Use Chinese page titles.", conventions.Text);
        Assert.Contains("## User conventions", result.Report);
        Assert.EndsWith("Use Chinese page titles.", result.Report);
    }

    [Fact]
    public async Task The_remember_prompt_is_generated_now_from_the_repository_and_has_no_default()
    {
        var result = await _home.Tacit().ShowAsync(ShowTarget.Remember);

        var part = Assert.Single(result.Parts);
        Assert.Equal(ShowSource.Code, part.Source);
        Assert.Contains(TestHome.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), part.Text);
        Assert.Null(result.Fixed);
        var refused = await Assert.ThrowsAsync<TacitException>(() => _home.Tacit().ShowAsync(ShowTarget.Remember, useDefault: true));
        Assert.Equal(TacitError.NoBuiltInText, refused.Error);
    }

    [Fact]
    public async Task Show_is_refused_with_the_usual_error_before_init()
    {
        using var fresh = new TestHome();

        var error = await Assert.ThrowsAsync<TacitException>(() => fresh.Tacit().ShowAsync(ShowTarget.Instructions));

        Assert.Equal(TacitError.NotInitialized, error.Error);
    }

    [Fact]
    public async Task The_defaults_need_no_init()
    {
        using var fresh = new TestHome();

        Assert.False(string.IsNullOrEmpty((await fresh.Tacit().ShowAsync(ShowTarget.Instructions, useDefault: true)).Report));
        Assert.False(string.IsNullOrEmpty((await fresh.Tacit().ShowAsync(ShowTarget.Resident, useDefault: true)).Report));
    }

    [Fact]
    public async Task The_labels_follow_the_memory_language()
    {
        using var english = new TestHome();
        english.InitReady(language: MemoryLanguage.En);

        var result = await english.Tacit("en-US").ShowAsync(ShowTarget.Instructions);

        var heading = result.Report.Split('\n')[0];
        Assert.Contains($"{result.Fixed!.Length}/2500", heading);
        Assert.Contains($"{result.Whole!.Length}/4000", heading);
        Assert.DoesNotContain(result.Report, IsCjk);
        Assert.Contains((await _home.Tacit().ShowAsync(ShowTarget.Instructions)).Report, IsCjk);
    }

    [Fact]
    public void Tools_are_listed_with_each_parameter_and_the_count()
    {
        var tools = new[]
        {
            new ToolDescription("recall", "Search the memory.", [new ParameterDescription("query", "string", "What to look for.")]),
            new ToolDescription("checkup", "Report on the memory.", []),
        };

        var result = _home.Tacit().ShowTools(tools);

        Assert.Contains("2", result.Report.Split('\n')[0]);
        Assert.Contains("## recall\nSearch the memory.", result.Report);
        Assert.Contains("- query (string): What to look for.", result.Report);
        Assert.True(result.Report.IndexOf("## recall", StringComparison.Ordinal) < result.Report.IndexOf("## checkup", StringComparison.Ordinal));
    }

    [Fact]
    public void Tools_follow_the_memory_language_and_refuse_the_default()
    {
        using var english = new TestHome();
        english.InitReady(language: MemoryLanguage.En);
        var tools = new[] { new ToolDescription("recall", "Search the memory.", []) };

        var result = english.Tacit("en-US").ShowTools(tools);

        Assert.DoesNotContain(result.Report, IsCjk);
        var refused = Assert.Throws<TacitException>(() => _home.Tacit().ShowTools(tools, useDefault: true));
        Assert.Equal(TacitError.NoBuiltInText, refused.Error);
    }
}
