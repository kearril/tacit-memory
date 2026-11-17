using TacitMemory.Core.Persona;

namespace TacitMemory.Core.Tests;

/// <summary>The persona as the memory page reads it: every block and entry, the candidates and their files' problems.</summary>
public sealed class MemoryPersonaTests : IDisposable
{
    private readonly TestHome _home = new();
    private MemoryService? _service;

    public MemoryPersonaTests() => _home.InitReady();

    public void Dispose()
    {
        _service?.Dispose();
        _home.Dispose();
    }

    private MemoryService Service => _service ??= _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

    private void Write(string file, string text) =>
        File.WriteAllText(Path.Combine(_home.DefaultMemory, file), text.ReplaceLineEndings("\n"));

    private const string Persona = """
        ## 红线
        - 不要删库；改为先备份
          <!-- 2026-10-25 原话「别删库」 -->

        ## 协作规则
        - 需求不清时 → 先连续追问
          先列出不清楚的点
          <!-- 2026-10-20 原话「先问清楚再动手」 -->
          <!-- 2026-10-28 原话「还是要先问」 -->

        ## 偏好
        - 沟通：先要结论

        ## 档案

        ## 主线：tacit-memory
        ### 协作规则
        - 发版 → 先跑全量测试

        ## 领域：network-proxy
        触发：改代理、分流或测速时
        ### 偏好
        - 测速：取三次的中位数

        """;

    private const string Candidates = """
        ## 候选

        ### 回答先给结论
        归属：偏好
        作用域：workstream:tacit-memory
        - 2026-11-01 原话「先说结论」
        - 2026-11-03 原话「结论放前面」

        ## 拒绝列表

        ### 每次都跑全量测试
        归属：协作规则
        - 2026-10-30 原话「全量跑一下」
        - 2026-11-02 拒绝 原话「不用每次全量」

        """;

    [Fact]
    public async Task Every_block_and_entry_is_read_with_its_continuation_code_and_sources()
    {
        Write(MemoryLayout.Persona, Persona);

        var report = await Service.PersonaAsync();

        Assert.Equal(
            [PersonaSection.RedLines, PersonaSection.WorkingRules, PersonaSection.Preferences, PersonaSection.Profile],
            report.Global.Select(section => section.Section));
        Assert.Empty(report.Global[3].Entries);
        var rule = Assert.Single(report.Global[1].Entries);
        Assert.Equal("需求不清时 → 先连续追问", rule.Text);
        Assert.Equal(
            ["先列出不清楚的点", "<!-- 2026-10-20 原话「先问清楚再动手」 -->", "<!-- 2026-10-28 原话「还是要先问」 -->"],
            rule.Continuation);
        Assert.Matches("^[0-9a-f]{6}$", rule.Code);
        Assert.Equal(6, rule.Line);

        var recalled = await Service.RecallAsync(new RecallRequest { Kind = "persona" });
        Assert.Contains($"[{rule.Code}] 需求不清时", recalled.Text);

        var domain = Assert.Single(report.Domains);
        Assert.Equal(("network-proxy", "改代理、分流或测速时"), (domain.Name, domain.Trigger));
        Assert.Equal("测速：取三次的中位数", Assert.Single(Assert.Single(domain.Sections).Entries).Text);
        var workstream = Assert.Single(report.Workstreams);
        Assert.Equal("tacit-memory", workstream.Name);
        Assert.Equal(PersonaSection.WorkingRules, Assert.Single(workstream.Sections).Section);

        Assert.EndsWith("persona.md", report.PersonaFile.Path);
        Assert.Empty(report.PersonaFile.Problems);
    }

    [Fact]
    public async Task Candidates_and_the_rejection_list_are_read_with_their_occurrences()
    {
        Write(MemoryLayout.Candidates, Candidates);

        var report = await Service.PersonaAsync();

        var candidate = Assert.Single(report.Candidates);
        Assert.Equal(("回答先给结论", PersonaSection.Preferences, "workstream:tacit-memory"), (candidate.Title, candidate.Section, candidate.Scope));
        Assert.Equal(
            [new CandidateOccurrenceView("2026-11-01", "先说结论", false), new CandidateOccurrenceView("2026-11-03", "结论放前面", false)],
            candidate.Occurrences);
        var rejected = Assert.Single(report.RejectionList);
        Assert.Equal("global", rejected.Scope);
        Assert.Equal([false, true], rejected.Occurrences.Select(occurrence => occurrence.Rejected));
        Assert.EndsWith("candidates.md", report.CandidatesFile.Path);
    }

    [Fact]
    public async Task Missing_files_read_as_an_empty_persona_with_no_problem()
    {
        File.Delete(Path.Combine(_home.DefaultMemory, MemoryLayout.Persona));
        File.Delete(Path.Combine(_home.DefaultMemory, MemoryLayout.Candidates));

        var report = await Service.PersonaAsync();

        Assert.All(report.Global, section => Assert.Empty(section.Entries));
        Assert.Empty(report.Domains);
        Assert.Empty(report.Workstreams);
        Assert.Empty(report.Candidates);
        Assert.Empty(report.RejectionList);
        Assert.Empty(report.PersonaFile.Problems);
        Assert.Empty(report.CandidatesFile.Problems);
    }

    [Fact]
    public async Task Format_problems_are_listed_by_line_and_the_rest_is_read_as_usual()
    {
        Write(MemoryLayout.Persona, """
            ## 红线
            - 不要删库
            - 不要强推；改为开分支

            ## 偏好
            随手写的一行
            - 沟通：先要结论

            """);
        Write(MemoryLayout.Candidates, """
            ## 候选

            ### 没有归属的候选
            - 2026-11-01 原话「随口一说」

            """);

        var report = await Service.PersonaAsync();

        var problem = Assert.Single(report.PersonaFile.Problems, problem => problem.Line == 2);
        Assert.Equal("Red line without instead", problem.Kind);
        Assert.Contains(report.PersonaFile.Problems, problem => problem.Line == 6 && problem.Kind == "Unrecognized content");
        Assert.Equal(2, report.Global[0].Entries.Count);
        var preferences = report.Global[2].Entries;
        Assert.Equal([null, preferences[1].Code], preferences.Select(entry => entry.Code));
        Assert.Equal("随手写的一行", preferences[0].Text);

        Assert.Empty(report.Candidates);
        Assert.Contains(report.CandidatesFile.Problems, problem => problem.Line == 3 && problem.Kind == "Candidate without section");
    }

    [Fact]
    public async Task A_persona_that_is_not_utf8_is_one_problem_and_no_entry()
    {
        File.WriteAllBytes(Path.Combine(_home.DefaultMemory, MemoryLayout.Persona), [0xff, 0xfe, 0x41, 0x80]);

        var report = await Service.PersonaAsync();

        Assert.All(report.Global, section => Assert.Empty(section.Entries));
        var problem = Assert.Single(report.PersonaFile.Problems);
        Assert.Null(problem.Line);
        Assert.Equal("encoding", problem.Kind);
    }

    [Fact]
    public async Task Reading_the_persona_counts_nothing()
    {
        Write(MemoryLayout.Persona, Persona);
        var pages = await Service.PageCountsAsync();
        var activities = await Service.ActivityCountsAsync(TestHome.Today);

        await Service.PersonaAsync();

        Assert.Equal(pages, await Service.PageCountsAsync());
        Assert.Equal(activities, await Service.ActivityCountsAsync(TestHome.Today));
    }
}
