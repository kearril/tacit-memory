using System.Collections.Concurrent;

namespace TacitMemory.Core.Tests;

/// <summary>
/// Saving a prompt document and returning it to its built-in default through the service, as the prompts page does: the
/// text is checked before any file changes, and what is saved applies at once, without waiting for the watcher.
/// </summary>
public sealed class PromptSaveTests : IDisposable
{
    private readonly TestHome _home = new();
    private readonly ConcurrentQueue<string> _notices = new();
    private readonly MemoryService _service;

    public PromptSaveTests()
    {
        _home.InitReady();
        _service = _home.Tacit().OpenService(_notices.Enqueue, NullLoggerFactory.Instance);
    }

    public void Dispose()
    {
        _service.Dispose();
        _home.Dispose();
    }

    private string Prompts => Path.Combine(_home.TacitHome, "prompts");

    private string FileOf(string name) => Path.Combine(Prompts, name);

    private string SkillMd => Path.Combine(_home.TacitHome, "skills", "tacit-memory", "SKILL.md");

    private string Hash(PromptName name) => _service.Prompts().Documents.Single(document => document.Name == name).Hash!;

    [Fact]
    public async Task A_first_save_writes_the_file_with_lf_line_breaks_and_the_instructions_take_it_at_once()
    {
        var saved = await _service.SavePromptAsync(PromptName.Instructions, null, "## 核心\r\n- 新写的核心规则\r\n");

        Assert.Equal("## 核心\n- 新写的核心规则\n", File.ReadAllText(FileOf("instructions.md")));
        Assert.False(saved.Document.BuiltIn);
        Assert.Equal("## 核心\n- 新写的核心规则\n", saved.Document.Text);
        Assert.Equal(Hash(PromptName.Instructions), saved.Document.Hash);
        Assert.Empty(saved.Notices);
        Assert.Contains("- 新写的核心规则", await _service.ServerInstructionsAsync());
    }

    [Fact]
    public async Task Saving_the_writing_conventions_writes_the_memory_skill_again_at_once()
    {
        await _service.SavePromptAsync(PromptName.Skill, null, "页面标题用中文。\n");

        Assert.Contains("页面标题用中文。", File.ReadAllText(SkillMd));
    }

    [Fact]
    public async Task Saving_the_resident_document_renders_the_resident_blocks_at_once()
    {
        _home.Bind();
        var context = Path.Combine(_home.UserHome, ".omp", "agent", "AGENTS.md");

        var saved = await _service.SavePromptAsync(PromptName.Resident, null, "外壳的第一段，自定义。\n\n## red_lines\n红线的引导语。\n");

        Assert.Empty(saved.Notices);
        Assert.Contains("外壳的第一段，自定义。", File.ReadAllText(context));
    }

    [Fact]
    public async Task Saving_the_resident_document_renders_once_and_not_again_after_the_debounce()
    {
        _home.Bind();

        await _service.SavePromptAsync(PromptName.Resident, null, "外壳，自定义。\n");
        await Task.Delay(TimeSpan.FromSeconds(1));

        Assert.Empty(_notices);
    }

    [Fact]
    public async Task A_text_that_breaks_its_rules_is_refused_with_the_file_named_and_no_file_is_written()
    {
        var text = "## 值得记\n" + string.Concat(Enumerable.Range(1, 11).Select(i => $"- 规则 {i}\n"));

        var error = await Assert.ThrowsAsync<TacitException>(() => _service.SavePromptAsync(PromptName.Rules, null, text));

        Assert.Equal(TacitError.PromptRejected, error.Error);
        var problem = Assert.Single(error.Problems!);
        Assert.Equal("rules.md", problem.Path);
        Assert.Contains("多出 1 条", problem.Message);
        Assert.False(File.Exists(FileOf("rules.md")));
    }

    [Fact]
    public async Task A_resident_text_with_an_unknown_section_is_refused_with_its_line()
    {
        var error = await Assert.ThrowsAsync<TacitException>(() =>
            _service.SavePromptAsync(PromptName.Resident, null, "外壳\n\n## mainline\n旧节名\n"));

        Assert.Equal(TacitError.PromptRejected, error.Error);
        Assert.Contains("mainline", error.Problems![0].Message);
        Assert.False(File.Exists(FileOf("resident.md")));
    }

    [Fact]
    public async Task Core_rules_that_push_the_fixed_part_past_its_limit_beside_the_running_criteria_are_refused()
    {
        var error = await Assert.ThrowsAsync<TacitException>(() =>
            _service.SavePromptAsync(PromptName.Instructions, null, "## 核心\n" + new string('核', 2600) + "\n"));

        Assert.Equal(TacitError.PromptRejected, error.Error);
        Assert.Matches(@"超出 \d+ 字符", error.Problems![0].Message);
        Assert.False(File.Exists(FileOf("instructions.md")));
    }

    [Fact]
    public async Task A_file_changed_since_it_was_read_is_not_overwritten()
    {
        File.WriteAllText(FileOf("skill.md"), "手写的约定。\n");

        var unseen = await Assert.ThrowsAsync<TacitException>(() => _service.SavePromptAsync(PromptName.Skill, null, "页面上的约定。\n"));
        var hash = Hash(PromptName.Skill);
        File.WriteAllText(FileOf("skill.md"), "又改了一次。\n");
        var stale = await Assert.ThrowsAsync<TacitException>(() => _service.SavePromptAsync(PromptName.Skill, hash, "页面上的约定。\n"));

        Assert.Equal(TacitError.PromptChanged, unseen.Error);
        Assert.Equal(TacitError.PromptChanged, stale.Error);
        Assert.Contains("skill.md", stale.Message);
        Assert.Equal("又改了一次。\n", File.ReadAllText(FileOf("skill.md")));
    }

    [Fact]
    public async Task A_save_based_on_the_current_hash_replaces_the_file()
    {
        File.WriteAllText(FileOf("skill.md"), "手写的约定。\n");

        var saved = await _service.SavePromptAsync(PromptName.Skill, Hash(PromptName.Skill), "页面上的约定。\n");

        Assert.Equal("页面上的约定。\n", File.ReadAllText(FileOf("skill.md")));
        Assert.Equal(Hash(PromptName.Skill), saved.Document.Hash);
    }

    [Fact]
    public async Task Restoring_the_default_deletes_the_file_and_the_built_in_text_applies_at_once()
    {
        await _service.SavePromptAsync(PromptName.Instructions, null, "## 核心\n- 自定义的规则\n");

        var restored = await _service.RestorePromptDefaultAsync(PromptName.Instructions);

        Assert.False(File.Exists(FileOf("instructions.md")));
        Assert.True(restored.Document.BuiltIn);
        Assert.Null(restored.Document.Hash);
        var instructions = await _service.ServerInstructionsAsync();
        Assert.DoesNotContain("- 自定义的规则", instructions);
        Assert.Contains("## Recall", instructions);
    }

    [Fact]
    public async Task Restoring_the_default_of_a_document_without_a_file_changes_nothing()
    {
        var restored = await _service.RestorePromptDefaultAsync(PromptName.Rules);

        Assert.True(restored.Document.BuiltIn);
        Assert.Empty(restored.Notices);
        Assert.False(File.Exists(FileOf("rules.md")));
    }

    [Fact]
    public async Task Restoring_built_in_criteria_that_do_not_fit_beside_long_core_rules_is_refused_and_keeps_the_file()
    {
        // Short criteria leave room for long core rules; the longer built-in criteria would not fit beside them.
        await _service.SavePromptAsync(PromptName.Rules, null, "## 值得记\n- 短\n");
        await _service.SavePromptAsync(PromptName.Instructions, null, "## 核心\n" + new string('核', 2394));

        var error = await Assert.ThrowsAsync<TacitException>(() => _service.RestorePromptDefaultAsync(PromptName.Rules));

        Assert.Equal(TacitError.PromptRejected, error.Error);
        Assert.True(File.Exists(FileOf("rules.md")));
    }

    [Fact]
    public void An_unknown_name_is_refused()
    {
        var error = Assert.Throws<TacitException>(() => _service.PromptNamed("tools"));

        Assert.Equal(TacitError.PromptUnknown, error.Error);
        Assert.Equal(PromptName.Resident, _service.PromptNamed("resident"));
        Assert.Throws<TacitException>(() => _service.PromptNamed("Resident"));
    }
}
