namespace TacitMemory.Core.Tests;

public sealed class VectorTests : IDisposable
{
    private static readonly float[] Travel = Unit(0);
    private static readonly float[] Elsewhere = Unit(1);
    private static readonly float[] Middle = Normalize([0.6f, 0.8f]);

    private readonly TestHome _home = new();
    private readonly ModelDouble _models = new();
    private MemoryService? _service;

    public VectorTests()
    {
        // Anything about travel expenses, however it is worded, lands on one vector.
        _models.Embed = text => text.Contains("差旅", StringComparison.Ordinal) || text.Contains("出差", StringComparison.Ordinal) ? Travel : Elsewhere;
        _home.InitReady(models: _models);
    }

    public void Dispose()
    {
        _service?.Dispose();
        _models.Dispose();
        _home.Dispose();
    }

    private MemoryService Service => _service ??= _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

    private Task<ToolResult> Create(string page, string title, string body) => Service.WritePageAsync(new WritePageRequest
    {
        Op = "create",
        Type = ToolValues.PageTypes.Work,
        Page = page,
        Title = title,
        Aliases = [title + "别名", title + "又名"],
        Sections = [new("内容", body)],
    });

    private Task<ToolResult> Recall(string query) => Service.RecallAsync(new RecallRequest { Query = query });

    [Fact]
    public async Task A_differently_worded_query_finds_the_page_through_its_vector()
    {
        await Create("company/reimburse", "差旅报销流程", "票据贴好后交给财务，月底统一打款。");

        var result = await Recall("出差的钱怎么拿回来");

        Assert.Contains("company/reimburse", result.Text);
        Assert.DoesNotContain("embedding model is unavailable", result.Text);
    }

    [Fact]
    public async Task A_unit_found_by_both_full_text_and_vector_ranks_above_one_found_by_full_text_alone()
    {
        _models.Embed = text => text.Contains("近邻", StringComparison.Ordinal) ? Travel
            : text.Contains("填充", StringComparison.Ordinal) ? Middle
            : Elsewhere;
        await Create("p/words-only", "全文页", "部署失败 部署失败 部署失败，日志里全是部署失败。");
        await Create("p/both", "近邻页", "部署失败一次，近邻。");
        foreach (var n in Enumerable.Range(1, 4))
            await Create($"p/filler-{n}", $"填充页{n}", $"无关的填充内容{n}。");
        var stored = _models.Embed;
        _models.Embed = text => text == "部署失败" ? Travel : stored(text);

        var result = await Recall("部署失败");

        Assert.True(result.Text.IndexOf("p/both", StringComparison.Ordinal) < result.Text.IndexOf("p/words-only", StringComparison.Ordinal), result.Text);
    }

    [Fact]
    public async Task Without_the_embedding_model_writes_succeed_and_search_falls_back_to_full_text_until_it_returns()
    {
        _models.EmbeddingDown = true;

        var written = await Create("company/reimburse", "差旅报销流程", "票据贴好后交给财务。");
        var byWords = await Recall("报销流程");
        var bySense = await Recall("出差的钱怎么拿回来");

        Assert.False(written.IsError, written.Text);
        Assert.Contains("embedding model is unavailable", written.Text);
        Assert.Contains("company/reimburse", byWords.Text);
        Assert.Contains("embedding model is unavailable", byWords.Text);
        Assert.DoesNotContain("company/reimburse", bySense.Text);

        _models.EmbeddingDown = false;
        _home.Clock.Advance(TimeSpan.FromMinutes(1));
        await Recall("随便查一下");
        await Service.SettledAsync();

        Assert.Contains("company/reimburse", (await Recall("出差的钱怎么拿回来")).Text);
    }

    [Fact]
    public async Task Vectors_of_another_embedding_model_are_made_again()
    {
        await Create("company/reimburse", "差旅报销流程", "票据贴好后交给财务。");
        _service!.Dispose();
        _service = null;
        File.WriteAllText(Path.Combine(_home.TacitHome, "config.yaml"), _models.ModelsYaml("another-embedding").ReplaceLineEndings("\n"));
        var callsBefore = _models.EmbeddingCalls;

        _ = Service;
        await Service.SettledAsync();

        Assert.True(_models.EmbeddingCalls > callsBefore);
        Assert.Contains("company/reimburse", (await Recall("出差的钱怎么拿回来")).Text);
    }

    [Fact]
    public async Task Vectors_of_another_length_are_dropped_and_made_again()
    {
        await Create("company/reimburse", "差旅报销流程", "票据贴好后交给财务。");
        var original = _models.Embed;
        _models.Embed = text => [.. original(text), .. new float[8]];

        await Recall("出差");
        await Service.SettledAsync();

        Assert.Contains("company/reimburse", (await Recall("出差的钱怎么拿回来")).Text);
    }

    private static float[] Unit(int axis)
    {
        var vector = new float[2];
        vector[axis] = 1;
        return vector;
    }

    private static float[] Normalize(float[] vector)
    {
        var length = MathF.Sqrt(vector.Sum(value => value * value));
        return vector.Select(value => value / length).ToArray();
    }
}
