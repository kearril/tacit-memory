using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace TacitMemory.Core.Tests;

public sealed class PromptDocumentTests : IDisposable
{
    private const int FixedLimit = 2500;

    private readonly TestHome _home = new();
    private readonly ConcurrentQueue<string> _notices = new();

    public PromptDocumentTests() => _home.InitReady();

    public void Dispose() => _home.Dispose();

    private string Prompts => Path.Combine(_home.TacitHome, "prompts");

    private string InstructionsMd => Path.Combine(Prompts, "instructions.md");

    private MemoryService Open() => _home.Tacit().OpenService(_notices.Enqueue, NullLoggerFactory.Instance);

    [Fact]
    public async Task Init_writes_rules_under_prompts_and_no_instructions_file()
    {
        using var home = new TestHome();

        await home.Tacit().InitAsync(new InitRequest { Language = MemoryLanguage.Zh });

        Assert.True(File.Exists(Path.Combine(home.TacitHome, "prompts", "rules.md")));
        Assert.False(File.Exists(Path.Combine(home.TacitHome, "rules.md")));
        Assert.False(File.Exists(Path.Combine(home.TacitHome, "prompts", "instructions.md")));
    }

    [Fact]
    public void The_old_rules_location_is_not_read()
    {
        File.WriteAllText(Path.Combine(_home.TacitHome, "rules.md"), "## 值得记\n- 旧位置的条目\n");

        var rules = _home.Tacit().LoadConfig().Prompts.Rules;

        Assert.DoesNotContain("旧位置的条目", rules.WorthRecording);
    }

    [Fact]
    public async Task Instructions_md_replaces_the_policy_paragraphs_and_keeps_the_fixed_frame()
    {
        Directory.CreateDirectory(Prompts);
        File.WriteAllText(InstructionsMd, "## 核心规则\n- 自定义的检索规则一条\n");

        var instructions = await Open().ServerInstructionsAsync();

        Assert.StartsWith("TacitMemory is the user's personal memory across sessions.", instructions);
        Assert.Contains("## 核心规则\n- 自定义的检索规则一条\n", instructions);
        Assert.DoesNotContain("## When to recall", instructions);
        Assert.DoesNotContain("## Writing persona", instructions);
        Assert.Contains("## Language", instructions);
        Assert.Contains("## Worth recording", instructions);
    }

    [Fact]
    public async Task The_general_rules_follow_the_core_rules_and_point_at_the_skill()
    {
        var instructions = await Open().ServerInstructionsAsync();

        var core = instructions.IndexOf("## Writing persona", StringComparison.Ordinal);
        var general = instructions.IndexOf("tacit-memory", StringComparison.Ordinal);
        var language = instructions.IndexOf("## Language", StringComparison.Ordinal);
        Assert.True(core >= 0 && core < general && general < language, instructions);
    }

    [Fact]
    public async Task Instructions_md_replacement_keeps_the_general_rules()
    {
        Directory.CreateDirectory(Prompts);
        File.WriteAllText(InstructionsMd, "## 核心规则\n- 自定义的检索规则一条\n");

        var instructions = await Open().ServerInstructionsAsync();

        Assert.Contains("- 自定义的检索规则一条", instructions);
        Assert.Contains("tacit-memory", instructions);
    }

    [Fact]
    public async Task Html_comments_that_fill_their_lines_in_instructions_md_are_not_sent_to_the_agent()
    {
        Directory.CreateDirectory(Prompts);
        File.WriteAllText(InstructionsMd, "## 核心规则\n<!-- 给改写者看的注释 -->\n- 可见的规则一条\n<!--\n多行的注释\n-->\n- 可见的规则二条\n");

        var instructions = await Open().ServerInstructionsAsync();

        Assert.Contains("## 核心规则\n- 可见的规则一条\n- 可见的规则二条\n", instructions);
        Assert.DoesNotContain("<!--", instructions);
        Assert.DoesNotContain("给改写者看的注释", instructions);
        Assert.DoesNotContain("多行的注释", instructions);
    }

    [Fact]
    public async Task A_comment_that_shares_its_line_with_text_takes_nothing_after_it()
    {
        Directory.CreateDirectory(Prompts);
        File.WriteAllText(InstructionsMd, "## 核心规则\n<!-- n --> - 规则甲\n- 规则乙\n<!-- x -->\n- 规则丙\n");

        var instructions = await Open().ServerInstructionsAsync();

        Assert.Contains("- 规则甲\n- 规则乙\n- 规则丙\n", instructions);
        Assert.DoesNotContain("<!-- x -->", instructions);
    }

    [Theory]
    [InlineData(MemoryLanguage.Zh)]
    [InlineData(MemoryLanguage.En)]
    public async Task The_built_in_instructions_send_no_html_comments_and_keep_the_general_rules(MemoryLanguage language)
    {
        using var home = new TestHome();
        home.InitReady(language: language);

        using var service = home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

        Assert.DoesNotContain("<!--", await service.ServerInstructionsAsync());
        Assert.Contains("tacit-memory", await service.ServerInstructionsAsync());
    }

    [Fact]
    public async Task Instructions_md_is_not_checked_for_language()
    {
        using var home = new TestHome();
        home.InitReady(language: MemoryLanguage.En);
        Directory.CreateDirectory(Path.Combine(home.TacitHome, "prompts"));
        File.WriteAllText(Path.Combine(home.TacitHome, "prompts", "instructions.md"), "## 核心\n- 用中文写的规则\n");

        using var service = home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

        Assert.Contains("- 用中文写的规则", await service.ServerInstructionsAsync());
    }

    [Fact]
    public void Instructions_md_that_breaks_the_fixed_limit_refuses_startup_and_names_both_files()
    {
        Directory.CreateDirectory(Prompts);
        File.WriteAllText(InstructionsMd, "## 核心\n" + new string('长', 2600) + "\n");

        var error = Assert.Throws<TacitException>(() => Open());

        Assert.Equal(TacitError.PromptInvalid, error.Error);
        Assert.Contains(InstructionsMd, error.Message);
        Assert.Contains(Path.Combine(Prompts, "rules.md"), error.Message);
        var match = Regex.Match(error.Message, @"共 (\d+) 字符.*超出 (\d+) 字符");
        Assert.True(match.Success, error.Message);
        Assert.Equal(int.Parse(match.Groups[1].Value) - FixedLimit, int.Parse(match.Groups[2].Value));
    }

    [Fact]
    public void Without_instructions_md_only_rules_md_is_named_when_the_limit_is_broken()
    {
        File.WriteAllText(Path.Combine(Prompts, "rules.md"), "## 值得记\n" + string.Concat(Enumerable.Repeat($"- {new string('长', 200)}\n", 10)));

        var error = Assert.Throws<TacitException>(() => Open());

        Assert.Contains(Path.Combine(Prompts, "rules.md"), error.Message);
        Assert.DoesNotContain("instructions.md", error.Message);
    }

    [Fact]
    public async Task Editing_instructions_md_while_running_changes_the_next_instructions()
    {
        using var service = Open();
        Assert.Contains("## When to recall", await service.ServerInstructionsAsync());

        Directory.CreateDirectory(Prompts);
        File.WriteAllText(InstructionsMd, "## 核心\n- 新写的核心规则\n");

        await Eventually(async () => (await service.ServerInstructionsAsync()).Contains("- 新写的核心规则", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Deleting_instructions_md_while_running_restores_the_default()
    {
        Directory.CreateDirectory(Prompts);
        File.WriteAllText(InstructionsMd, "## 核心\n- 临时规则\n");
        using var service = Open();
        Assert.Contains("- 临时规则", await service.ServerInstructionsAsync());

        File.Delete(InstructionsMd);

        await Eventually(async () => (await service.ServerInstructionsAsync()).Contains("## When to recall", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_instructions_md_that_breaks_the_limit_while_running_is_reported_and_the_previous_one_kept()
    {
        Directory.CreateDirectory(Prompts);
        File.WriteAllText(InstructionsMd, "## 核心\n- 可用的规则\n");
        using var service = Open();
        var before = await service.ServerInstructionsAsync();

        File.WriteAllText(InstructionsMd, "## 核心\n" + new string('长', 2600) + "\n");

        await Eventually(() => _notices.Any(notice => notice.Contains("instructions.md", StringComparison.Ordinal)));
        Assert.Equal(before, await service.ServerInstructionsAsync());
    }

    [Fact]
    public async Task Files_outside_prompts_do_not_rebuild_the_cached_instructions()
    {
        using var service = Open();
        var before = await service.ServerInstructionsAsync();

        File.WriteAllText(Path.Combine(_home.TacitHome, "unrelated.txt"), "not a prompt document");
        await Task.Delay(700);

        Assert.Same(before, await service.ServerInstructionsAsync());
    }

    [Fact]
    public async Task The_backup_zip_holds_the_whole_prompts_folder()
    {
        Directory.CreateDirectory(Prompts);
        File.WriteAllText(InstructionsMd, "## 核心\n- 备份用的规则\n");
        using var service = Open();

        await service.WritePageAsync(new WritePageRequest
        {
            Op = "create",
            Page = "tacit-memory/prompt-backup",
            Title = "提示词备份",
            Aliases = ["提示词备份别名一", "提示词备份别名二"],
            Sections = [new("现象", "用于检查备份内容。")],
        });
        await service.BackupCompletion;

        var backup = Path.Combine(_home.TacitHome, "backups", $"auto-{TestHome.Today:yyyy-MM-dd}.zip");
        using var archive = ZipFile.OpenRead(backup);
        var names = archive.Entries.Select(entry => entry.FullName).ToArray();
        Assert.Contains("prompts/rules.md", names);
        Assert.Contains("prompts/instructions.md", names);
    }

    private static async Task Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "condition not met within 10 s");
            await Task.Delay(50);
        }
    }

    private static async Task Eventually(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "condition not met within 10 s");
            await Task.Delay(50);
        }
    }
}
