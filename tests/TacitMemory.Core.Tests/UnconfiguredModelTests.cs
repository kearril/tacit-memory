using Microsoft.Extensions.Logging;
using TacitMemory.Core.Index;

namespace TacitMemory.Core.Tests;

/// <summary>
/// A model left out of <c>config.yaml</c> is unconfigured: the service starts and works without it, degrades as it does
/// for an unavailable model, but says nothing about it in the results, the instructions or the consolidation's "not checked"
/// notes of unavailability, never calls it, and never puts it in a cooldown. Through the facade.
/// </summary>
public sealed class UnconfiguredModelTests : IDisposable
{
    /// <summary>A bound on each await: a wait that never ends fails here instead of hanging.</summary>
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);

    private readonly TestHome _home = new();
    private readonly ModelDouble _models = new();
    private readonly CapturingLoggerFactory _logs = new();
    private MemoryService? _service;

    public UnconfiguredModelTests() => _home.InitReady(models: _models);

    public void Dispose()
    {
        _models.ReleaseDecisions();
        _models.ReleaseEmbeddings();
        _service?.Dispose();
        _models.Dispose();
        _home.Dispose();
    }

    private MemoryService Service => _service ??= _home.Tacit().OpenService(_ => { }, _logs);

    private string MemoryPath => _home.DefaultMemory;

    private string EmbeddingLine => $"embedding: {{ url: '{_models.EmbeddingUrl}', model: double-embedding }}";

    private string RerankerLine => $"reranker: {{ url: '{_models.RerankUrl}', model: double-reranker }}";

    private string DecisionLine => $"decision: {{ url: '{_models.DecisionUrl}', model: double-decision, apiKey: test-key }}";

    /// <summary>Replaces <c>config.yaml</c> with a language and the given <c>models</c> lines, and nothing else.</summary>
    private void WriteModels(params string[] lines)
    {
        var models = lines.Length == 0 ? "" : "models:\n" + string.Concat(lines.Select(line => "  " + line + "\n"));
        File.WriteAllText(Path.Combine(_home.TacitHome, "config.yaml"), "language: zh\n" + models);
    }

    private void StopService()
    {
        _service?.Dispose();
        _service = null;
    }

    /// <summary>The text of one consolidation category: from its heading to the next heading, or to the end.</summary>
    private static string Section(string text, string title)
    {
        var start = text.IndexOf($"### {title}\n", StringComparison.Ordinal);
        Assert.True(start >= 0, $"No category \"{title}\" in the report.");
        var end = text.IndexOf("\n### ", start + 1, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..end];
    }

    private Task<ToolResult> WriteEntry(string text) =>
        Service.WriteRecentAsync(new WriteRecentRequest { Text = text }).WaitAsync(Guard);

    private Task<ToolResult> AddPreference(string text) =>
        Service.WritePersonaAsync(new WritePersonaRequest { Op = "add", Section = "preferences", Text = text, Source = text }).WaitAsync(Guard);

    [Fact]
    public async Task Without_any_model_the_service_starts_and_writes_and_recalls_as_usual_with_no_model_notes()
    {
        WriteModels();

        var page = await Service.WritePageAsync(new WritePageRequest
        {
            Op = "create",
            Type = ToolValues.PageTypes.Work,
            Page = "tacit-memory/backup",
            Title = "备份",
            Aliases = ["备份方式", "备份问题"],
            Sections = [new("要点", "每日自动备份到 zip")],
        }).WaitAsync(Guard);
        var proposed = await Service.WritePersonaAsync(new WritePersonaRequest
        {
            Op = "propose",
            Section = "preferences",
            Text = "沟通：先要结论",
            Source = "先要结论",
        }).WaitAsync(Guard);
        var first = await WriteEntry("整理了备份方案");
        var second = await WriteEntry("备份方案又改了一版");
        var recall = await Service.RecallAsync(new RecallRequest { Query = "备份" }).WaitAsync(Guard);

        foreach (var result in new[] { page, proposed, first, second, recall })
        {
            Assert.False(result.IsError, result.Text);
            Assert.DoesNotContain("Note:", result.Text);
        }
        Assert.Contains("备份", recall.Text);
    }

    [Fact]
    public async Task Without_any_model_no_model_is_ever_called_and_none_is_listed_as_unavailable()
    {
        WriteModels();
        await WriteEntry("整理了备份方案");
        await WriteEntry("备份方案又改了一版");
        await Service.RecallAsync(new RecallRequest { Query = "备份" }).WaitAsync(Guard);
        await AddPreference("沟通：先要结论");

        Assert.Equal(0, _models.EmbeddingCalls);
        Assert.Equal(0, _models.RerankCalls);
        Assert.Equal(0, _models.DecisionCalls);
        Assert.DoesNotContain("Unavailable auxiliary models", await Service.ServerInstructionsAsync());
    }

    [Fact]
    public async Task With_the_models_unavailable_the_same_writes_say_so()
    {
        WriteModels(EmbeddingLine, DecisionLine);
        _models.EmbeddingDown = true;
        _models.DecisionDown = true;

        var page = await Service.WritePageAsync(new WritePageRequest
        {
            Op = "create",
            Type = ToolValues.PageTypes.Work,
            Page = "tacit-memory/backup",
            Title = "备份",
            Aliases = ["备份方式", "备份问题"],
            Sections = [new("要点", "每日自动备份到 zip")],
        }).WaitAsync(Guard);
        await WriteEntry("整理了备份方案");
        var second = await WriteEntry("备份方案又改了一版");

        Assert.Contains("embedding model is unavailable", page.Text);
        Assert.Contains("decision model is unavailable", second.Text);
    }

    [Fact]
    public async Task With_only_some_models_configured_the_others_are_never_called_and_leave_no_note()
    {
        WriteModels(EmbeddingLine);

        await WriteEntry("整理了备份方案");
        var second = await WriteEntry("备份方案又改了一版");
        var recall = await Service.RecallAsync(new RecallRequest { Query = "备份" }).WaitAsync(Guard);

        Assert.DoesNotContain("Note:", second.Text);
        Assert.DoesNotContain("reranker", recall.Text);
        Assert.Equal(0, _models.DecisionCalls);
        Assert.Equal(0, _models.RerankCalls);
    }

    [Fact]
    public async Task The_instructions_list_a_cooling_model_and_never_an_unconfigured_one()
    {
        WriteModels(EmbeddingLine, DecisionLine);
        _models.DecisionDown = true;
        await WriteEntry("整理了备份方案");
        await WriteEntry("备份方案又改了一版");

        var instructions = await Service.ServerInstructionsAsync();

        Assert.Contains("- Unavailable auxiliary models: decision. ", instructions);
        Assert.DoesNotContain("reranker", instructions);
    }

    [Fact]
    public async Task The_status_report_names_an_unconfigured_model_as_unconfigured_and_a_configured_one_as_available()
    {
        WriteModels(EmbeddingLine);

        var report = await Service.StatusAsync(new Uri("http://127.0.0.1:18888/mcp")).WaitAsync(Guard);

        var items = report.Models!.Items;
        Assert.Equal(ModelState.Available, items.Single(item => item.Role == "embedding").State);
        Assert.Equal(ModelState.Unconfigured, items.Single(item => item.Role == "reranker").State);
        Assert.Equal(ModelState.Unconfigured, items.Single(item => item.Role == "decision").State);
    }

    [Fact]
    public async Task The_consolidation_marks_the_categories_an_unconfigured_model_leaves_unchecked_and_asks_nothing()
    {
        WriteModels();
        await AddPreference("沟通：先要结论");
        await AddPreference("回复别太长");

        var report = await Service.ConsolidationReportAsync().WaitAsync(Guard);

        Assert.Contains("Not checked (not configured)", Section(report, "Conflicting persona entries"));
        Assert.DoesNotContain("unavailable", Section(report, "Conflicting persona entries"));
        Assert.Equal(0, _models.EmbeddingCalls);
        Assert.Equal(0, _models.DecisionCalls);
    }

    [Fact]
    public async Task With_the_decision_model_unconfigured_the_consolidation_asks_nothing_and_says_so_while_embedding_still_runs()
    {
        WriteModels(EmbeddingLine);
        await AddPreference("沟通：先要结论");
        await AddPreference("回复别太长");

        var report = await Service.ConsolidationReportAsync().WaitAsync(Guard);

        Assert.Contains("Not checked (not configured)", Section(report, "Conflicting persona entries"));
        Assert.True(_models.EmbeddingCalls > 0);
        Assert.Equal(0, _models.DecisionCalls);
    }

    [Fact]
    public async Task The_startup_log_names_the_unconfigured_models_and_no_url_or_key()
    {
        WriteModels(EmbeddingLine);

        _ = Service;

        var line = Assert.Single(_logs.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("reranker", StringComparison.Ordinal));
        Assert.Contains("decision", line.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(_models.DecisionUrl.Host, line.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("test-key", line.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("embedding", line.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Leaving_the_embedding_model_out_keeps_the_stored_vectors_and_adding_it_back_needs_no_new_embedding()
    {
        WriteModels(EmbeddingLine);
        await Service.WritePageAsync(new WritePageRequest
        {
            Op = "create",
            Type = ToolValues.PageTypes.Work,
            Page = "tacit-memory/backup",
            Title = "备份",
            Aliases = ["备份方式", "备份问题"],
            Sections = [new("要点", "每日自动备份到 zip")],
        }).WaitAsync(Guard);
        await Service.VectorCompletion.WaitAsync(Guard);
        StopService();
        var stored = StoredVectors("double-embedding");
        Assert.True(stored > 0);

        WriteModels();
        _ = Service;
        await Service.VectorCompletion.WaitAsync(Guard);
        StopService();
        Assert.Equal(stored, StoredVectors(null));

        var callsBefore = _models.EmbeddingCalls;
        WriteModels(EmbeddingLine);
        _ = Service;
        await Service.VectorCompletion.WaitAsync(Guard);

        Assert.Equal(callsBefore, _models.EmbeddingCalls);
        Assert.Equal(stored, StoredVectors("double-embedding"));
    }

    [Fact]
    public async Task Changing_the_embedding_model_name_drops_the_vectors_and_fills_them_again()
    {
        WriteModels(EmbeddingLine);
        await Service.WritePageAsync(new WritePageRequest
        {
            Op = "create",
            Type = ToolValues.PageTypes.Work,
            Page = "tacit-memory/backup",
            Title = "备份",
            Aliases = ["备份方式", "备份问题"],
            Sections = [new("要点", "每日自动备份到 zip")],
        }).WaitAsync(Guard);
        await Service.VectorCompletion.WaitAsync(Guard);
        StopService();
        var callsBefore = _models.EmbeddingCalls;

        WriteModels(EmbeddingLine.Replace("double-embedding", "another-embedding", StringComparison.Ordinal));
        _ = Service;
        await Service.VectorCompletion.WaitAsync(Guard);

        Assert.True(_models.EmbeddingCalls > callsBefore);
        Assert.True(StoredVectors("another-embedding") > 0);
    }

    [Fact]
    public async Task Reindex_without_an_embedding_model_rebuilds_the_full_text_and_says_it_is_not_configured()
    {
        WriteModels();
        await Service.WritePageAsync(new WritePageRequest
        {
            Op = "create",
            Type = ToolValues.PageTypes.Work,
            Page = "tacit-memory/backup",
            Title = "备份",
            Aliases = ["备份方式", "备份问题"],
            Sections = [new("要点", "每日自动备份到 zip")],
        }).WaitAsync(Guard);

        var result = await Service.ReindexAsync().WaitAsync(Guard);

        Assert.Equal(0, result.Vectors);
        Assert.False(result.VectorsPending);
        Assert.True(result.Units > 0);
        Assert.Contains("嵌入模型未配置", result.Report);
        Assert.DoesNotContain("向量将在模型恢复后补齐", result.Report);
        Assert.Contains("备份", (await Service.RecallAsync(new RecallRequest { Query = "备份" }).WaitAsync(Guard)).Text);
    }

    [Fact]
    public async Task A_cached_answer_is_not_used_once_the_decision_model_is_removed()
    {
        WriteModels(EmbeddingLine, DecisionLine);
        await AddPreference("沟通：先要结论");
        await AddPreference("回复别太长");
        await Service.ConsolidationReportAsync().WaitAsync(Guard);
        await Service.JudgmentCompletion.WaitAsync(Guard);
        var asked = _models.DecisionCalls;
        Assert.True(asked > 0, "The first consolidation should have asked the decision model.");
        StopService();

        WriteModels(EmbeddingLine);
        var report = await Service.ConsolidationReportAsync().WaitAsync(Guard);

        Assert.Contains("Not checked (not configured)", Section(report, "Conflicting persona entries"));
        Assert.Equal(asked, _models.DecisionCalls);
    }

    /// <summary>The vectors stored in the index, read with the model name the index was made for; <c>null</c> reads it without one.</summary>
    private int StoredVectors(string? model)
    {
        StopService();
        using var index = new MemoryIndex(MemoryPath, model, MemoryLanguage.Zh, () => TestHome.Today, () => new Dictionary<string, DateOnly>());
        return index.Counts().Vectors;
    }
}
