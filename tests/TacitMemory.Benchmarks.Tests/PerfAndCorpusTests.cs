using System.Text.Json;
using TacitMemory.Core.Tests;

namespace TacitMemory.Benchmarks.Tests;

public sealed class PerfAndCorpusTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tacit-perf-test-" + Guid.NewGuid().ToString("N")[..8]);

    public PerfAndCorpusTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task PerfModeMeasuresSyncFtsVectorAndEndToEndAtConfiguredScales()
    {
        var savePath = Path.Combine(_root, "perf.json");
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = await BenchmarkApp.RunAsync(
            ["perf", "--scales", "20,40", "--iterations", "2", "--warmup", "1", "--save", savePath],
            stdout,
            stderr);

        Assert.True(string.IsNullOrWhiteSpace(stderr.ToString()), stderr.ToString());
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(savePath));

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(savePath));
        var scales = doc.RootElement.GetProperty("scales");
        Assert.Equal(2, scales.GetArrayLength());

        var first = scales[0];
        var second = scales[1];
        Assert.True(first.GetProperty("actualUnits").GetInt32() >= 20);
        Assert.True(second.GetProperty("actualUnits").GetInt32() >= 40);
        Assert.True(first.GetProperty("syncColdMs").GetDouble() >= 0);
        Assert.True(first.GetProperty("syncIncrementalMs").GetDouble() >= 0);
        Assert.True(first.GetProperty("ftsP50Ms").GetDouble() >= 0);
        Assert.True(first.GetProperty("ftsP95Ms").GetDouble() >= 0);
        Assert.True(first.GetProperty("vectorP50Ms").GetDouble() >= 0);
        Assert.True(first.GetProperty("vectorP95Ms").GetDouble() >= 0);
        Assert.True(first.GetProperty("vectorAllocBytes").GetInt64() > 0);
        Assert.True(first.GetProperty("endToEndP50Ms").GetDouble() >= 0);
        Assert.True(first.GetProperty("endToEndAllocBytes").GetInt64() > 0);

        var text = stdout.ToString();
        Assert.Contains("FTS5", text, StringComparison.Ordinal);
        Assert.Contains("Vector", text, StringComparison.Ordinal);
        Assert.Contains("EndToEnd", text, StringComparison.Ordinal);
        Assert.Contains("E2E Alloc", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuiltInCorpusPassesStrictValidationAndEndToEndRecallSmoke()
    {
        var savePath = Path.Combine(_root, "builtin-recall.json");
        var cacheDir = Path.Combine(_root, "builtin-cache");
        using var upstream = new ModelDouble
        {
            Embed = ModelDouble.BagOfPairs,
            Score = (_, _) => 0.5,
        };

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exitCode = await BenchmarkApp.RunAsync(
            [
                "recall",
                "--strict-corpus",
                "--embedding-url", upstream.EmbeddingUrl.ToString(),
                "--embedding-model", "double-emb",
                "--reranker-url", upstream.RerankUrl.ToString(),
                "--reranker-model", "double-rerank",
                "--cache-dir", cacheDir,
                "--save", savePath,
            ],
            stdout,
            stderr);

        Assert.True(string.IsNullOrWhiteSpace(stderr.ToString()), stderr.ToString());
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(savePath));

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(savePath));
        var summary = doc.RootElement.GetProperty("summary");
        Assert.True(summary.GetProperty("positiveCount").GetInt32() >= 25);
        Assert.True(summary.GetProperty("negativeCount").GetInt32() >= 6);
    }
}
