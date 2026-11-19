using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace TacitMemory.Core.Tests;

public sealed class PromptDocumentTests : IDisposable
{
    private const int FixedLimit = 3000;

    private readonly TestHome _home = TestHome.WithManualSources();
    private readonly ConcurrentQueue<string> _notices = new();

    public PromptDocumentTests() => _home.InitReady();

    public void Dispose() => _home.Dispose();

    private string Prompts => Path.Combine(_home.TacitHome, "prompts");

    private string InstructionsMd => Path.Combine(Prompts, "instructions.md");

    private MemoryService Open() => _home.Tacit().OpenService(_notices.Enqueue, NullLoggerFactory.Instance);

    [Fact]
    public async Task Init_writes_no_prompt_document()
    {
        using var home = new TestHome();

        await home.Tacit().InitAsync(new InitRequest { Language = MemoryLanguage.Zh });

        Assert.True(Directory.Exists(Path.Combine(home.TacitHome, "prompts")));
        Assert.False(File.Exists(Path.Combine(home.TacitHome, "prompts", "rules.md")));
        Assert.False(File.Exists(Path.Combine(home.TacitHome, "rules.md")));
        Assert.False(File.Exists(Path.Combine(home.TacitHome, "prompts", "instructions.md")));
    }

    [Theory]
    [InlineData("zh")]
    [InlineData("en")]
    public async Task Without_rules_md_the_instructions_carry_the_english_built_in_criteria_in_either_language(string language)
    {
        File.WriteAllText(Path.Combine(_home.TacitHome, "config.yaml"), $"{TestHome.Models}language: {language}\n");

        var instructions = await Open().Tools.ServerInstructionsAsync();

        Assert.Contains("## Worth recording\n- The user corrects or interrupts the agent\n", instructions);
        Assert.Contains("## Not recorded\n- Routine coding, typo fixes and log reading\n", instructions);
        Assert.False(File.Exists(Path.Combine(Prompts, "rules.md")));
    }

    [Fact]
    public async Task Deleting_rules_md_while_running_brings_back_the_built_in_criteria()
    {
        Directory.CreateDirectory(Prompts);
        File.WriteAllText(Path.Combine(Prompts, "rules.md"), "## Worth recording\n- 自定义的标准\n");
        using var service = Open();
        Assert.Contains("- 自定义的标准", await service.Tools.ServerInstructionsAsync());

        File.Delete(Path.Combine(Prompts, "rules.md"));
        _home.Sources!.PromptsChanged();

        await service.SettledAsync();
        Assert.Contains("- The user corrects or interrupts the agent", (await service.Tools.ServerInstructionsAsync()));
        Assert.False(File.Exists(Path.Combine(Prompts, "rules.md")));
    }

    [Fact]
    public void The_old_rules_location_is_not_read()
    {
        File.WriteAllText(Path.Combine(_home.TacitHome, "rules.md"), "## Worth recording\n- 旧位置的条目\n");

        var rules = _home.Tacit().LoadConfig().Prompts.Rules;

        Assert.Null(rules);
    }

    [Fact]
    public async Task Instructions_md_replaces_the_policy_paragraphs_and_keeps_the_fixed_frame()
    {
        Directory.CreateDirectory(Prompts);
        File.WriteAllText(InstructionsMd, "## 核心规则\n- 自定义的检索规则一条\n");

        var instructions = await Open().Tools.ServerInstructionsAsync();

        Assert.StartsWith("TacitMemory is the user's personal memory across sessions.", instructions);
        Assert.Contains("## 核心规则\n- 自定义的检索规则一条\n", instructions);
        Assert.DoesNotContain("## Recall", instructions);
        Assert.DoesNotContain("## Write\n", instructions);
        Assert.Contains("## Language", instructions);
        Assert.Contains("## Worth recording", instructions);
    }

    [Fact]
    public async Task The_general_rules_follow_the_core_rules_and_point_at_the_skill_once()
    {
        var instructions = await Open().Tools.ServerInstructionsAsync();

        var core = instructions.IndexOf("## Write\n", StringComparison.Ordinal);
        var general = instructions.IndexOf("read the `tacit-memory` skill", StringComparison.Ordinal);
        var language = instructions.IndexOf("## Language", StringComparison.Ordinal);
        Assert.True(core >= 0 && core < general && general < language, instructions);
        Assert.Equal(1, Count(instructions, "tacit-memory"));
    }

    [Fact]
    public async Task Instructions_md_replacement_keeps_the_general_rules()
    {
        Directory.CreateDirectory(Prompts);
        File.WriteAllText(InstructionsMd, "## 核心规则\n- 自定义的检索规则一条\n");

        var instructions = await Open().Tools.ServerInstructionsAsync();

        Assert.Contains("- 自定义的检索规则一条", instructions);
        Assert.Contains("tacit-memory", instructions);
    }

    [Fact]
    public async Task Html_comments_that_fill_their_lines_in_instructions_md_are_not_sent_to_the_agent()
    {
        Directory.CreateDirectory(Prompts);
        File.WriteAllText(InstructionsMd, "## 核心规则\n<!-- 给改写者看的注释 -->\n- 可见的规则一条\n<!--\n多行的注释\n-->\n- 可见的规则二条\n");

        var instructions = await Open().Tools.ServerInstructionsAsync();

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

        var instructions = await Open().Tools.ServerInstructionsAsync();

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

        Assert.DoesNotContain("<!--", await service.Tools.ServerInstructionsAsync());
        Assert.Contains("tacit-memory", await service.Tools.ServerInstructionsAsync());
    }

    [Fact]
    public async Task Instructions_md_is_not_checked_for_language()
    {
        using var home = new TestHome();
        home.InitReady(language: MemoryLanguage.En);
        Directory.CreateDirectory(Path.Combine(home.TacitHome, "prompts"));
        File.WriteAllText(Path.Combine(home.TacitHome, "prompts", "instructions.md"), "## 核心\n- 用中文写的规则\n");

        using var service = home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

        Assert.Contains("- 用中文写的规则", await service.Tools.ServerInstructionsAsync());
    }

    [Fact]
    public void Instructions_md_that_breaks_the_fixed_limit_refuses_startup_and_names_only_the_files_that_exist()
    {
        Directory.CreateDirectory(Prompts);
        File.WriteAllText(InstructionsMd, "## 核心\n" + new string('长', 2600) + "\n");

        var error = Assert.Throws<TacitException>(() => Open());

        Assert.Equal(TacitError.PromptInvalid, error.Error);
        Assert.Contains(InstructionsMd, error.Message);
        Assert.DoesNotContain("rules.md", error.Message);
        var match = Regex.Match(error.Message, @"共 (\d+) 字符.*超出 (\d+) 字符");
        Assert.True(match.Success, error.Message);
        Assert.Equal(int.Parse(match.Groups[1].Value) - FixedLimit, int.Parse(match.Groups[2].Value));
    }

    [Fact]
    public void Without_instructions_md_only_rules_md_is_named_when_the_limit_is_broken()
    {
        Directory.CreateDirectory(Prompts);
        File.WriteAllText(Path.Combine(Prompts, "rules.md"), "## Worth recording\n" + string.Concat(Enumerable.Repeat($"- {new string('长', 200)}\n", 10)));

        var error = Assert.Throws<TacitException>(() => Open());

        Assert.Contains(Path.Combine(Prompts, "rules.md"), error.Message);
        Assert.DoesNotContain("instructions.md", error.Message);
    }

    [Fact]
    public async Task Editing_instructions_md_while_running_changes_the_next_instructions()
    {
        using var service = Open();
        Assert.Contains("## Recall", await service.Tools.ServerInstructionsAsync());

        Directory.CreateDirectory(Prompts);
        File.WriteAllText(InstructionsMd, "## 核心\n- 新写的核心规则\n");
        _home.Sources!.PromptsChanged();

        await service.SettledAsync();
        Assert.Contains("- 新写的核心规则", (await service.Tools.ServerInstructionsAsync()));
    }

    [Fact]
    public async Task Deleting_instructions_md_while_running_restores_the_default()
    {
        Directory.CreateDirectory(Prompts);
        File.WriteAllText(InstructionsMd, "## 核心\n- 临时规则\n");
        using var service = Open();
        Assert.Contains("- 临时规则", await service.Tools.ServerInstructionsAsync());

        File.Delete(InstructionsMd);
        _home.Sources!.PromptsChanged();

        await service.SettledAsync();
        Assert.Contains("## Recall", (await service.Tools.ServerInstructionsAsync()));
    }

    [Fact]
    public async Task An_instructions_md_that_breaks_the_limit_while_running_is_reported_and_the_previous_one_kept()
    {
        Directory.CreateDirectory(Prompts);
        File.WriteAllText(InstructionsMd, "## 核心\n- 可用的规则\n");
        using var service = Open();
        var before = await service.Tools.ServerInstructionsAsync();

        File.WriteAllText(InstructionsMd, "## 核心\n" + new string('长', 2600) + "\n");
        _home.Sources!.PromptsChanged();

        await service.SettledAsync();
        Assert.Contains(_notices, notice => notice.Contains("instructions.md", StringComparison.Ordinal));
        Assert.Equal(before, await service.Tools.ServerInstructionsAsync());
    }

    [Fact]
    public async Task Files_outside_prompts_do_not_rebuild_the_cached_instructions()
    {
        using var service = Open();
        var before = await service.Tools.ServerInstructionsAsync();

        File.WriteAllText(Path.Combine(_home.TacitHome, "unrelated.txt"), "not a prompt document");
        await Task.Delay(700);

        Assert.Same(before, await service.Tools.ServerInstructionsAsync());
    }

    [Fact]
    public async Task The_backup_zip_holds_the_whole_prompts_folder()
    {
        Directory.CreateDirectory(Prompts);
        File.WriteAllText(InstructionsMd, "## 核心\n- 备份用的规则\n");
        using var service = Open();

        await service.Tools.WritePageAsync(new WritePageRequest
        {
            Op = "create",
            Type = ToolValues.PageTypes.Work,
            Page = "tacit-memory/prompt-backup",
            Title = "提示词备份",
            Aliases = ["提示词备份别名一", "提示词备份别名二"],
            Sections = [new("现象", "用于检查备份内容。")],
        });
        await service.SettledAsync();

        var backup = Path.Combine(_home.TacitHome, "backups", $"auto-{TestHome.Today:yyyy-MM-dd}.zip");
        using var archive = ZipFile.OpenRead(backup);
        var names = archive.Entries.Select(entry => entry.FullName).ToArray();
        Assert.Contains("prompts/instructions.md", names);
    }

    [Fact]
    public async Task The_prompts_page_reads_each_file_or_the_built_in_text_and_the_hash_follows_the_file()
    {
        using var service = Open();
        var builtIn = service.Settings.Prompts().Documents.Single(document => document.Name == PromptName.Rules);

        File.WriteAllText(Path.Combine(Prompts, "rules.md"), "## Worth recording\n- 自定义的标准\n");
        var first = service.Settings.Prompts().Documents.Single(document => document.Name == PromptName.Rules);
        File.WriteAllText(Path.Combine(Prompts, "rules.md"), "## Worth recording\n- 改过的标准\n");
        var second = service.Settings.Prompts().Documents.Single(document => document.Name == PromptName.Rules);

        Assert.True(builtIn.BuiltIn);
        Assert.Null(builtIn.Hash);
        Assert.Equal((await _home.Tacit().ShowAsync(Show.ShowTarget.Rules, useDefault: true)).Report, builtIn.Text);
        Assert.False(first.BuiltIn);
        Assert.Equal("## Worth recording\n- 自定义的标准\n", first.Text);
        Assert.NotNull(first.Hash);
        Assert.NotEqual(first.Hash, second.Hash);
    }

    [Fact]
    public async Task The_built_in_texts_on_the_prompts_page_are_the_ones_show_default_prints()
    {
        using var service = Open();
        var documents = service.Settings.Prompts().Documents;

        Assert.Equal((await _home.Tacit().ShowAsync(Show.ShowTarget.Instructions, useDefault: true)).Report, documents[0].Text);
        Assert.Equal((await _home.Tacit().ShowAsync(Show.ShowTarget.Resident, useDefault: true)).Report, documents[2].Text);
        Assert.Equal("", documents[3].Text);
    }

    private static int Count(string text, string value) =>
        Regex.Matches(text, Regex.Escape(value)).Count;

}
