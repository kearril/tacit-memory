using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace TacitMemory.Core.Tests;

/// <summary>
/// <c>reindex</c> through the facade: the rebuilt index answers as before, the judgment cache is emptied, a missing
/// embedding model leaves the vectors for later, the files the index cannot read are listed, and the log records it.
/// </summary>
public sealed class ReindexTests : IDisposable
{
    /// <summary>A bound on each await: a wait that never ends fails here instead of hanging.</summary>
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);

    private readonly TestHome _home = new();
    private readonly ModelDouble _models = new();
    private readonly CapturingLoggerFactory _logs = new();
    private MemoryService? _service;

    public ReindexTests() => _home.InitReady(models: _models);

    public void Dispose()
    {
        _models.ReleaseDecisions();
        _models.ReleaseEmbeddings();
        _service?.Dispose();
        _models.Dispose();
        _home.Dispose();
    }

    private MemoryService Service => _service ??= _home.Tacit().OpenService(_ => { }, _logs);

    private async Task CreatePage(string page, string title, string body)
    {
        var result = await Service.Tools.WritePageAsync(new WritePageRequest
        {
            Op = "create",
            Type = ToolValues.PageTypes.Work,
            Page = page,
            Title = title,
            Aliases = [title + "的坑", title + "问题"],
            Sections = [new("要点", body)],
        });
        Assert.False(result.IsError, result.Text);
    }

    private Task<ToolResult> Recall(string query) => Service.Tools.RecallAsync(new RecallRequest { Query = query });

    /// <summary>The text without the section dates shown after headings.</summary>
    private static string WithoutDates(string text) => Regex.Replace(text, @" \(last changed \d{4}-\d{2}-\d{2}\)", "");

    [Fact]
    public async Task After_a_rebuild_the_same_query_returns_the_same_result_and_every_unit_has_a_vector()
    {
        await CreatePage("tacit-memory/backup", "备份", "每日自动备份到 zip");
        await CreatePage("tacit-memory/restore", "恢复", "从备份恢复记忆仓库");
        var before = await Recall("备份");

        var result = await Service.Maintenance.ReindexAsync().WaitAsync(Guard);

        // The rebuild dates each section by its file's commit, which the test clock does not control; the dates have their own test.
        Assert.Equal(WithoutDates(before.Text), WithoutDates((await Recall("备份")).Text));
        Assert.True(result.Files >= 2, $"Expected at least the two pages, got {result.Files} files.");
        Assert.True(result.Units > 0);
        Assert.Equal(result.Units, result.Vectors);
        Assert.False(result.VectorsPending);
        Assert.Empty(result.SkippedFiles);
    }

    [Fact]
    public async Task The_rebuild_empties_the_judgment_cache_so_the_next_consolidation_asks_the_decision_model_again()
    {
        Write("回答尽量简短");
        PickPersonaEntry();
        await Service.Tools.ConsolidationReportAsync().WaitAsync(Guard);
        await Service.SettledAsync().WaitAsync(Guard);
        var asked = _models.DecisionCalls;
        Assert.True(asked > 0, "The first consolidation should have asked the decision model.");

        await Service.Tools.ConsolidationReportAsync().WaitAsync(Guard);
        await Service.SettledAsync().WaitAsync(Guard);
        Assert.Equal(asked, _models.DecisionCalls);

        await Service.Maintenance.ReindexAsync().WaitAsync(Guard);
        await Service.Tools.ConsolidationReportAsync().WaitAsync(Guard);
        await Service.SettledAsync().WaitAsync(Guard);

        Assert.True(_models.DecisionCalls > asked);
    }

    [Fact]
    public async Task With_the_embedding_model_down_the_full_text_index_is_rebuilt_and_the_result_says_vectors_wait()
    {
        await CreatePage("tacit-memory/backup", "备份", "每日自动备份到 zip");
        _models.EmbeddingDown = true;

        var result = await Service.Maintenance.ReindexAsync().WaitAsync(Guard);

        Assert.True(result.VectorsPending);
        Assert.Equal(0, result.Vectors);
        Assert.Contains("向量将在模型恢复后补齐", result.Report);
        var found = await Recall("备份");
        Assert.False(found.IsError);
        Assert.Contains("tacit-memory/backup", found.Text);
    }

    [Fact]
    public async Task A_file_that_is_not_utf8_is_skipped_and_listed_in_the_result()
    {
        await CreatePage("tacit-memory/backup", "备份", "每日自动备份到 zip");
        File.WriteAllBytes(Path.Combine(_home.DefaultMemory, "pages", "tacit-memory", "gbk.md"), [0x2D, 0x0A, 0xFF, 0xFE, 0x0A]);

        var result = await Service.Maintenance.ReindexAsync().WaitAsync(Guard);

        Assert.Equal(["pages/tacit-memory/gbk.md"], result.SkippedFiles);
        Assert.Contains("pages/tacit-memory/gbk.md", result.Report);
    }

    [Fact]
    public async Task The_log_records_the_start_the_end_and_the_result_of_the_rebuild()
    {
        await Service.Maintenance.ReindexAsync().WaitAsync(Guard);

        Assert.Single(_logs.Entries, entry => entry.Message.Contains("维护操作开始：reindex", StringComparison.Ordinal));
        Assert.Single(_logs.Entries, entry => entry.Message.Contains("维护操作结束：reindex", StringComparison.Ordinal));
        Assert.Single(_logs.Entries, entry => entry.Message.Contains("索引已重建", StringComparison.Ordinal));
    }

    [Fact]
    public async Task After_a_rebuild_each_section_takes_the_date_of_its_files_last_commit_and_an_uncommitted_file_takes_today()
    {
        var committed = Path.Combine(_home.DefaultMemory, "pages", "tacit-memory", "old.md");
        Directory.CreateDirectory(Path.GetDirectoryName(committed)!);
        File.WriteAllText(committed, "---\ntitle: 旧页\naliases: [旧坑, 老问题]\n---\n\n## 现状\n每日备份到 zip\n");
        TestHome.Git(_home.DefaultMemory, "-c", "user.name=test", "-c", "user.email=test@localhost", "add", "--", "pages");
        TestHome.Git(
            _home.DefaultMemory, "-c", "user.name=test", "-c", "user.email=test@localhost",
            "commit", "-q", "--date=2026-10-20T09:00:00+00:00", "-m", "pages: old");
        var uncommitted = Path.Combine(_home.DefaultMemory, "pages", "tacit-memory", "new.md");
        File.WriteAllText(uncommitted, "---\ntitle: 新页\naliases: [新坑, 新问题]\n---\n\n## 现状\n每日备份到 zip\n");

        await Service.Maintenance.ReindexAsync().WaitAsync(Guard);

        var old = await Service.Tools.RecallAsync(new RecallRequest { Page = "tacit-memory/old" });
        var fresh = await Service.Tools.RecallAsync(new RecallRequest { Page = "tacit-memory/new" });
        Assert.Contains("(last changed 2026-10-20)", old.Text);
        Assert.Contains("(last changed 2026-11-02)", fresh.Text);
    }

    private void Write(params string[] candidateTitles)
    {
        File.WriteAllText(Path.Combine(_home.DefaultMemory, "persona.md"), PersonaWithPreference.ReplaceLineEndings("\n"));
        var blocks = string.Concat(candidateTitles.Select(title => $"### {title}\nBelongs to: Preferences\n- 2026-10-30 quote「{title}」\n- 2026-10-31 quote「{title}」\n\n"));
        File.WriteAllText(Path.Combine(_home.DefaultMemory, "candidates.md"), ("## Candidates\n\n" + blocks + "## Rejection list\n").ReplaceLineEndings("\n"));
    }

    private void PickPersonaEntry() => _models.Choose = (_, options) => options.ToDictionary(
        option => option.Key,
        option => option.Value.Contains("先要结论", StringComparison.Ordinal) ? 0.95
            : option.Key == options.Keys.Last() ? 0.05
            : 0.0);

    private const string PersonaWithPreference =
        "## Red lines\n\n## Working rules\n\n## Preferences\n- 沟通：先要结论\n  <!-- 2026-10-20 quote「先要结论」 -->\n\n## Profile\n";
}
