using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace TacitMemory.Core.Tests;

public sealed class MemorySkillTests : IDisposable
{
    private const int PromptLimit = 3000;

    private readonly TestHome _home = new();
    private readonly ConcurrentQueue<string> _notices = new();

    public MemorySkillTests() => _home.InitReady();

    public void Dispose() => _home.Dispose();

    private string Prompts => Path.Combine(_home.TacitHome, "prompts");

    private string SkillPrompt => Path.Combine(Prompts, "skill.md");

    private string SkillFile => Path.Combine(_home.TacitHome, "skills", "tacit-memory", "SKILL.md");

    private MemoryService Open() => _home.Tacit().OpenService(_notices.Enqueue);

    [Fact]
    public void Startup_writes_the_skill_with_frontmatter_and_a_generated_note_after_it()
    {
        using var service = Open();

        var text = File.ReadAllText(SkillFile);
        Assert.StartsWith("---\nname: tacit-memory\n", text, StringComparison.Ordinal);
        var frontmatterEnd = text.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        Assert.True(frontmatterEnd > 0, "the frontmatter is closed");
        Assert.Contains("<!--", text[(frontmatterEnd + 5)..], StringComparison.Ordinal);
    }

    [Fact]
    public void The_description_stays_within_the_compression_limit_and_names_the_write_tools_early()
    {
        using var service = Open();

        var description = Description(File.ReadAllText(SkillFile));

        Assert.True(description.Length <= 160, $"description is {description.Length} characters");
        Assert.Contains("write_", description[..Math.Min(100, description.Length)], StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_skill_prompt_the_skill_has_no_user_conventions_section()
    {
        using var service = Open();

        Assert.DoesNotContain("## User conventions", File.ReadAllText(SkillFile), StringComparison.Ordinal);
    }

    [Fact]
    public void A_blank_skill_prompt_adds_no_user_conventions_section()
    {
        Directory.CreateDirectory(Prompts);
        File.WriteAllText(SkillPrompt, "  \n\n");

        _home.Tacit().GenerateSkill();

        Assert.DoesNotContain("## User conventions", File.ReadAllText(SkillFile), StringComparison.Ordinal);
    }

    [Fact]
    public void The_skill_prompt_follows_the_system_part_under_user_conventions()
    {
        Directory.CreateDirectory(Prompts);
        File.WriteAllText(SkillPrompt, "- 页面标题统一用中文\n");

        var result = _home.Tacit().GenerateSkill();

        var text = File.ReadAllText(SkillFile);
        Assert.True(result.Written);
        Assert.Equal(1, Count(text, "## User conventions"));
        Assert.True(text.IndexOf("## User conventions", StringComparison.Ordinal) < text.IndexOf("- 页面标题统一用中文", StringComparison.Ordinal));
        Assert.EndsWith("- 页面标题统一用中文\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_skill_prompt_with_frontmatter_refuses_startup_and_names_the_file()
    {
        Directory.CreateDirectory(Prompts);
        File.WriteAllText(SkillPrompt, "---\nname: 别的名字\ndescription: 别的描述\n---\n- 规则\n");

        var error = Assert.Throws<TacitException>(() => _home.Tacit().LoadConfig());

        Assert.Equal(TacitError.PromptInvalid, error.Error);
        Assert.Contains("skill.md", error.Message, StringComparison.Ordinal);
        Assert.Throws<TacitException>(() => _home.Tacit().OpenService(_notices.Enqueue));
    }

    [Fact]
    public void A_skill_prompt_over_the_limit_refuses_startup_and_names_the_file()
    {
        Directory.CreateDirectory(Prompts);
        File.WriteAllText(SkillPrompt, new string('长', PromptLimit + 1));

        var error = Assert.Throws<TacitException>(() => _home.Tacit().LoadConfig());

        Assert.Equal(TacitError.PromptInvalid, error.Error);
        Assert.Contains("skill.md", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_skill_prompt_at_the_limit_is_accepted()
    {
        Directory.CreateDirectory(Prompts);
        File.WriteAllText(SkillPrompt, new string('长', PromptLimit));

        var result = _home.Tacit().GenerateSkill();

        Assert.True(result.Written);
    }

    [Fact]
    public void Generating_without_a_running_service_writes_the_skill_and_writes_it_again_after_deletion()
    {
        var tacit = _home.Tacit();

        var first = tacit.GenerateSkill();
        Assert.True(first.Written);
        Assert.True(File.Exists(SkillFile));

        File.Delete(SkillFile);
        var second = tacit.GenerateSkill();

        Assert.True(second.Written);
        Assert.True(File.Exists(SkillFile));
    }

    [Fact]
    public void Unchanged_content_is_not_written_again()
    {
        var tacit = _home.Tacit();
        tacit.GenerateSkill();
        var old = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(SkillFile, old);

        var again = tacit.GenerateSkill();

        Assert.False(again.Written);
        Assert.Equal(old, File.GetLastWriteTimeUtc(SkillFile));
    }

    [Fact]
    public async Task Editing_the_skill_prompt_while_running_regenerates_the_skill()
    {
        using var service = Open();
        Directory.CreateDirectory(Prompts);

        File.WriteAllText(SkillPrompt, "- 新的写法约定\n");

        await Eventually(() => File.ReadAllText(SkillFile).Contains("- 新的写法约定", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_skill_prompt_that_breaks_its_rules_while_running_is_reported_and_the_previous_skill_kept()
    {
        Directory.CreateDirectory(Prompts);
        File.WriteAllText(SkillPrompt, "- 可用的约定\n");
        using var service = Open();

        File.WriteAllText(SkillPrompt, "---\nname: 别的名字\n---\n- 不可用\n");

        await Eventually(() => _notices.Any(notice => notice.Contains("skill.md", StringComparison.Ordinal)));
        var text = File.ReadAllText(SkillFile);
        Assert.Contains("- 可用的约定", text, StringComparison.Ordinal);
        Assert.DoesNotContain("- 不可用", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Starting_the_service_rewrites_a_stale_skill()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SkillFile)!);
        File.WriteAllText(SkillFile, "stale content\n");

        using var service = Open();

        var text = File.ReadAllText(SkillFile);
        Assert.DoesNotContain("stale content", text, StringComparison.Ordinal);
        Assert.StartsWith("---\nname: tacit-memory\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_backup_zip_does_not_hold_the_generated_skill()
    {
        using var service = Open();
        Assert.True(File.Exists(SkillFile));

        await service.WritePageAsync(new WritePageRequest
        {
            Op = "create",
            Page = "tacit-memory/skill-backup",
            Title = "技能备份",
            Aliases = ["技能备份别名一", "技能备份别名二"],
            Sections = [new("现象", "用于检查备份内容。")],
        });
        await service.BackupCompletion;

        var backup = Path.Combine(_home.TacitHome, "backups", $"auto-{TestHome.Today:yyyy-MM-dd}.zip");
        using var archive = ZipFile.OpenRead(backup);
        var names = archive.Entries.Select(entry => entry.FullName).ToArray();
        Assert.DoesNotContain(names, name => name.StartsWith("skills/", StringComparison.Ordinal));
    }

    private static string Description(string text) =>
        Regex.Match(text, "^description: \"(.*)\"$", RegexOptions.Multiline).Groups[1].Value;

    private static int Count(string text, string value) =>
        Regex.Matches(text, Regex.Escape(value)).Count;

    private static async Task Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "condition not met within 10 s");
            await Task.Delay(50);
        }
    }
}
