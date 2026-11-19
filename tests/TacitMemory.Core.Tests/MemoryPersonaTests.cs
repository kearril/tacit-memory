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
        ## Red lines
        - 不要删库；改为先备份
          <!-- 2026-10-25 quote「别删库」 -->

        ## Working rules
        - 需求不清时 → 先连续追问
          先列出不清楚的点
          <!-- 2026-10-20 quote「先问清楚再动手」 -->
          <!-- 2026-10-28 quote「还是要先问」 -->

        ## Preferences
        - 沟通：先要结论

        ## Profile

        ## Workstream: tacit-memory
        ### Working rules
        - 发版 → 先跑全量测试

        ## Domain: db-migration
        Trigger: 执行数据库迁移时
        ### Preferences
        - 验证：先在测试库演练再上线

        """;

    private const string Candidates = """
        ## Candidates

        ### 回答先给结论
        Belongs to: Preferences
        Scope: workstream:tacit-memory
        - 2026-11-01 quote「先说结论」
        - 2026-11-03 quote「结论放前面」

        ## Rejection list

        ### 每次都跑全量测试
        Belongs to: Working rules
        - 2026-10-30 quote「全量跑一下」
        - 2026-11-02 rejected quote「不用每次全量」

        """;

    [Fact]
    public async Task An_entry_s_sources_are_read_in_either_language_with_the_day_or_the_words_left_out_and_other_comments_dropped()
    {
        Write(MemoryLayout.Persona, """
            ## Preferences
            - 沟通：先要结论
              正文的一行
              <!-- 2026-10-01 -->
              <!-- QUOTE「conclusion first」 -->
              <!-- 2026-10-02 quote「先说结论」 -->
              <!-- 随手写的注释 -->
              <!-- 2026-10-03 随手写的注释 -->

            """);

        var report = await Service.Browse.PersonaAsync();

        var entry = Assert.Single(report.Global[2].Entries);
        Assert.Equal(["正文的一行"], entry.Lines);
        Assert.Equal(
            [new SourceView("2026-10-01", null), new SourceView(null, "conclusion first"), new SourceView("2026-10-02", "先说结论")],
            entry.Sources);
    }

    [Fact]
    public async Task Every_block_and_entry_is_read_with_its_continuation_code_and_sources()
    {
        Write(MemoryLayout.Persona, Persona);

        var report = await Service.Browse.PersonaAsync();

        Assert.Equal(
            [PersonaSection.RedLines, PersonaSection.WorkingRules, PersonaSection.Preferences, PersonaSection.Profile],
            report.Global.Select(section => section.Section));
        Assert.Empty(report.Global[3].Entries);
        var rule = Assert.Single(report.Global[1].Entries);
        Assert.Equal("需求不清时 → 先连续追问", rule.Text);
        Assert.Equal(["先列出不清楚的点"], rule.Lines);
        Assert.Equal([new SourceView("2026-10-20", "先问清楚再动手"), new SourceView("2026-10-28", "还是要先问")], rule.Sources);
        Assert.Matches("^[0-9a-f]{6}$", rule.Code);
        Assert.Equal(6, rule.Line);

        var recalled = await Service.Tools.RecallAsync(new RecallRequest { Kind = "persona" });
        Assert.Contains($"[{rule.Code}] 需求不清时", recalled.Text);

        var domain = Assert.Single(report.Domains);
        Assert.Equal(("db-migration", "执行数据库迁移时"), (domain.Name, domain.Trigger));
        Assert.Equal("验证：先在测试库演练再上线", Assert.Single(Assert.Single(domain.Sections).Entries).Text);
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

        var report = await Service.Browse.PersonaAsync();

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

        var report = await Service.Browse.PersonaAsync();

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
            ## Red lines
            - 不要删库
            - 不要强推；改为开分支

            ## Preferences
            随手写的一行
            - 沟通：先要结论

            """);
        Write(MemoryLayout.Candidates, """
            ## Candidates

            ### 没有归属的候选
            - 2026-11-01 quote「随口一说」

            """);

        var report = await Service.Browse.PersonaAsync();

        Assert.DoesNotContain(report.PersonaFile.Problems, problem => problem.Line == 2);
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

        var report = await Service.Browse.PersonaAsync();

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

        await Service.Browse.PersonaAsync();

        Assert.Equal(pages, await Service.PageCountsAsync());
        Assert.Equal(activities, await Service.ActivityCountsAsync(TestHome.Today));
    }
}
