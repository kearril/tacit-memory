using System.Collections.Concurrent;

namespace TacitMemory.Core.Tests;

/// <summary>
/// A prompt document broken by hand while the service runs: the service keeps its previous text and remembers why, so the
/// prompts page and the status report can show it; a fixed file, a save or a return to the default clears it.
/// </summary>
public sealed class PromptProblemTests : IDisposable
{
    private static readonly Uri Endpoint = new("http://127.0.0.1:18888/mcp");

    private readonly TestHome _home = new();
    private readonly ConcurrentQueue<string> _notices = new();
    private readonly MemoryService _service;

    public PromptProblemTests()
    {
        _home.InitReady();
        _service = _home.Tacit().OpenService(_notices.Enqueue, NullLoggerFactory.Instance);
    }

    public void Dispose()
    {
        _service.Dispose();
        _home.Dispose();
    }

    private string FileOf(string name) => Path.Combine(_home.TacitHome, "prompts", name);

    private string? ProblemOf(PromptName name) => _service.Prompts().Documents.Single(document => document.Name == name).Problem;

    [Fact]
    public async Task A_file_broken_while_the_service_runs_shows_why_and_the_service_keeps_the_previous_text()
    {
        File.WriteAllText(FileOf("resident.md"), "外壳\n\n## mainline\n旧节名\n");

        await Eventually(() => ProblemOf(PromptName.Resident) is not null);

        var resident = _service.Prompts().Documents.Single(document => document.Name == PromptName.Resident);
        Assert.Contains("mainline", resident.Problem);
        Assert.Equal("外壳\n\n## mainline\n旧节名\n", resident.Text);
        Assert.Null(ProblemOf(PromptName.Skill));
    }

    [Fact]
    public async Task Core_rules_broken_while_the_service_runs_leave_the_server_instructions_as_they_were()
    {
        File.WriteAllText(FileOf("instructions.md"), "## 核心\n" + new string('核', 3000));

        await Eventually(() => ProblemOf(PromptName.Instructions) is not null);

        Assert.Contains("## Recall", await _service.ServerInstructionsAsync());
    }

    [Fact]
    public async Task Fixed_part_over_its_limit_marks_both_the_core_rules_and_the_criteria()
    {
        File.WriteAllText(FileOf("instructions.md"), "## 核心\n" + new string('核', 3000));

        await Eventually(() => ProblemOf(PromptName.Rules) is not null);

        Assert.Equal(ProblemOf(PromptName.Instructions), ProblemOf(PromptName.Rules));
        Assert.Matches(@"超出 \d+ 字符", ProblemOf(PromptName.Rules));
    }

    [Fact]
    public async Task Fixing_the_file_clears_the_problem()
    {
        File.WriteAllText(FileOf("skill.md"), "---\nname: mine\n---\n");
        await Eventually(() => ProblemOf(PromptName.Skill) is not null);

        File.WriteAllText(FileOf("skill.md"), "页面标题用中文。\n");

        await Eventually(() => ProblemOf(PromptName.Skill) is null);
    }

    [Fact]
    public async Task Saving_from_the_page_clears_the_problem()
    {
        File.WriteAllText(FileOf("skill.md"), "---\nname: mine\n---\n");
        await Eventually(() => ProblemOf(PromptName.Skill) is not null);
        var hash = _service.Prompts().Documents.Single(document => document.Name == PromptName.Skill).Hash;

        var saved = await _service.SavePromptAsync(PromptName.Skill, hash, "页面标题用中文。\n");

        Assert.Null(saved.Document.Problem);
        Assert.Null(ProblemOf(PromptName.Skill));
        Assert.Null(saved.Document.Problem);
    }

    [Fact]
    public async Task Restoring_the_default_clears_the_problem()
    {
        File.WriteAllText(FileOf("resident.md"), "外壳\n\n## mainline\n旧节名\n");
        await Eventually(() => ProblemOf(PromptName.Resident) is not null);

        var restored = await _service.RestorePromptDefaultAsync(PromptName.Resident);

        Assert.Null(restored.Document.Problem);
        Assert.Null(ProblemOf(PromptName.Resident));
    }

    [Fact]
    public async Task The_status_report_lists_each_broken_document_while_one_is_broken()
    {
        Assert.Null((await _service.StatusAsync(Endpoint)).Prompts);

        File.WriteAllText(FileOf("resident.md"), "外壳\n\n## mainline\n旧节名\n");
        await Eventually(() => ProblemOf(PromptName.Resident) is not null);

        var prompts = Assert.IsType<PromptsStatus>((await _service.StatusAsync(Endpoint)).Prompts);
        Assert.Equal("提示词文档有误", prompts.Title);
        var item = Assert.Single(prompts.Items);
        Assert.Equal(PromptName.Resident, item.Name);
        Assert.StartsWith("prompts/resident.md 有误，服务正在使用上一版：", Assert.Single(prompts.Lines));
        Assert.Equal([StatusLevel.Attention], ((IStatusColumn)prompts).LineLevels());
    }

    /// <summary>Waits until the condition holds, letting the service run the refreshes it has scheduled on the test clock.</summary>
    private async Task Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (true)
        {
            await _service.SettledAsync();
            if (condition())
                return;
            Assert.True(DateTime.UtcNow < deadline, "condition not met within 10 s");
            await Task.Delay(50);
        }
    }
}
