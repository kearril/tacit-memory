namespace TacitMemory.Core.Tests;

/// <summary>
/// Checking a prompt document without saving it, as the prompts page does while the user types: the measure of the text
/// against its limit, counted as it is sent, and the same refusals a save of it gives.
/// </summary>
public sealed class PromptCheckTests : IDisposable
{
    private readonly TestHome _home = new();
    private readonly MemoryService _service;

    public PromptCheckTests()
    {
        _home.InitReady();
        _service = _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);
    }

    public void Dispose()
    {
        _service.Dispose();
        _home.Dispose();
    }

    private string Prompts => Path.Combine(_home.TacitHome, "prompts");

    private PromptDocumentView Document(PromptName name) => _service.Settings.Prompts().Documents.Single(document => document.Name == name);

    [Fact]
    public void Without_files_the_core_rules_and_criteria_measure_the_same_fixed_part_and_the_room_left_for_activity()
    {
        var instructions = Document(PromptName.Instructions).Measure;
        var rules = Document(PromptName.Rules).Measure;

        Assert.Equal(instructions.Length, rules.Length);
        Assert.Equal(3000, instructions.Limit);
        Assert.Equal(4000 - instructions.Length, instructions.ActivityRoom);
        Assert.Null(instructions.Items);
        Assert.Equal(new PromptItems(5, 3, 10), rules.Items);
    }

    [Fact]
    public void The_resident_document_and_the_writing_conventions_measure_their_own_text()
    {
        var resident = Document(PromptName.Resident);
        var skill = Document(PromptName.Skill).Measure;

        Assert.Equal(new PromptMeasure(resident.Text.Length, 1500, null, null), resident.Measure);
        Assert.Equal(new PromptMeasure(0, 3000, null, null), skill);
        Assert.Equal(new PromptMeasure(6, 3000, null, null), _service.Settings.CheckPrompt(PromptName.Skill, "第一行\r\n第二").Measure);
    }

    [Fact]
    public void Comments_that_fill_their_lines_do_not_count_toward_the_core_rules()
    {
        var plain = _service.Settings.CheckPrompt(PromptName.Instructions, "## 核心\n- 规则\n").Measure;
        var commented = _service.Settings.CheckPrompt(PromptName.Instructions, "## 核心\n<!-- 给编辑者的说明 -->\n- 规则\n").Measure;

        Assert.Equal(plain.Length, commented.Length);
    }

    [Fact]
    public void The_criteria_count_as_they_are_laid_out_when_sent()
    {
        var plain = _service.Settings.CheckPrompt(PromptName.Rules, "## Worth recording\n- 一条\n").Measure;
        var loose = _service.Settings.CheckPrompt(PromptName.Rules, "前言\n## Worth recording\n- 一条   \n  续行\n## 其他\n- 不算\n").Measure;

        Assert.Equal(plain.Length, loose.Length);
        Assert.Equal(new PromptItems(1, 0, 10), loose.Items);
    }

    [Fact]
    public async Task The_core_rules_are_measured_beside_the_criteria_the_service_runs()
    {
        var before = _service.Settings.CheckPrompt(PromptName.Instructions, "## 核心\n- 规则\n").Measure.Length;

        await _service.Settings.SavePromptAsync(PromptName.Rules, null, "## Worth recording\n- 短\n");
        var after = _service.Settings.CheckPrompt(PromptName.Instructions, "## 核心\n- 规则\n").Measure.Length;

        Assert.True(after < before);
        Assert.Equal(Document(PromptName.Instructions).Measure.Length, Document(PromptName.Rules).Measure.Length);
    }

    [Fact]
    public void Core_rules_over_the_fixed_limit_leave_less_than_no_room_for_activity_and_are_refused()
    {
        var check = _service.Settings.CheckPrompt(PromptName.Instructions, "## 核心\n" + new string('核', 3000));

        Assert.True(check.Measure.Length > 3000);
        Assert.Equal(4000 - check.Measure.Length, check.Measure.ActivityRoom);
        Assert.True(check.Measure.ActivityRoom < 1000);
        Assert.Matches(@"超出 \d+ 字符", Assert.Single(check.Problems));
    }

    [Fact]
    public void Both_overfull_sections_of_the_criteria_are_reported_with_their_counts()
    {
        var items = string.Concat(Enumerable.Range(1, 11).Select(i => $"- 条目 {i}\n"));

        var check = _service.Settings.CheckPrompt(PromptName.Rules, "## Worth recording\n" + items + "## Not recorded\n" + items + "- 第十二条\n");

        Assert.Equal(new PromptItems(11, 12, 10), check.Measure.Items);
        Assert.Equal(2, check.Problems.Count);
        Assert.Contains("多出 1 条", check.Problems[0]);
        Assert.Contains("多出 2 条", check.Problems[1]);
    }

    public static TheoryData<PromptName, string> Broken => new()
    {
        { PromptName.Instructions, "## 核心\n" + new string('核', 2600) },
        { PromptName.Rules, "## Worth recording\n" + string.Concat(Enumerable.Range(1, 11).Select(i => $"- 条目 {i}\n")) },
        { PromptName.Resident, "外壳\n\n## mainline\n旧节名\n" },
        { PromptName.Resident, new string('长', 1501) },
        { PromptName.Skill, "---\nname: mine\n---\n" },
        { PromptName.Skill, new string('写', 3001) },
    };

    [Theory]
    [MemberData(nameof(Broken))]
    public async Task Every_refusal_of_a_save_is_reported_by_the_check_in_the_same_words(PromptName name, string text)
    {
        var check = _service.Settings.CheckPrompt(name, text);

        var error = await Assert.ThrowsAsync<TacitException>(() => _service.Settings.SavePromptAsync(name, null, text));
        Assert.NotEmpty(check.Problems);
        Assert.Equal(error.Problems!.Select(problem => problem.Message), check.Problems);
    }

    [Fact]
    public void A_text_that_keeps_the_rules_has_no_problems_and_the_check_writes_no_file()
    {
        var check = _service.Settings.CheckPrompt(PromptName.Skill, "页面标题用中文。\n");

        Assert.Empty(check.Problems);
        Assert.False(File.Exists(Path.Combine(Prompts, "skill.md")));
    }
}
