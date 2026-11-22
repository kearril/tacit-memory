using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TacitMemory.Core;
using TacitMemory.Core.Index;

namespace TacitMemory.Benchmarks;

internal sealed record PerfRunOptions(
    IReadOnlyList<int> Scales,
    int Iterations,
    int Warmup,
    int Dimension,
    string? SavePath);

internal sealed record PerfBenchmarkReport(
    DateTimeOffset GeneratedAt,
    int Dimension,
    int Iterations,
    int Warmup,
    IReadOnlyList<ScaleBenchmarkResult> Scales);

internal sealed record ScaleBenchmarkResult(
    int TargetUnits,
    int ActualUnits,
    int VectorCount,
    double SyncColdMs,
    double SyncIncrementalMs,
    double FtsP50Ms,
    double FtsP95Ms,
    long FtsAllocBytes,
    double VectorP50Ms,
    double VectorP95Ms,
    long VectorAllocBytes,
    double EndToEndP50Ms,
    double EndToEndP95Ms,
    long EndToEndAllocBytes);

internal static class PerfRunner
{
    private const string StubEmbeddingModel = "perf-stub-emb";
    private const string StubRerankerModel = "perf-stub-rerank";

    private static readonly string[] Workstreams = ["my-app", "ops-infra", "data-pipeline", "desktop-tool"];
    private static readonly string[] PageTypes = ["incident", "decision", "entity", "work"];

    private static readonly string[] Topics =
    [
        "数据库连接池耗尽", "边缘代理证书续期", "增量水位线回溯", "桌面热键冲突",
        "构建缓存失效", "容器网络超时", "分布式锁续约", "日志轮转磁盘打满",
        "查询计划退化", "索引重建阻塞", "异步队列重试", "配置热重载竞态",
    ];

    private static readonly string[] CodeTerms =
    [
        "pg_stat_statements", "WAL_CHECKPOINT_TRUNCATE", "SyncScheduler", "env.ps1",
        "ERR_CERT_AUTHORITY_INVALID", "max-old-space-size", "tokio::sync::RwLock", "frozen-lockfile",
        "ConnectionPoolTimeout", "BackoffPolicy", "ParquetPartitioner", "IndexPipeline",
    ];

    private static readonly string[] SampleQueries =
    [
        "数据库连接池耗尽 SyncScheduler",
        "边缘代理证书续期 ERR_CERT_AUTHORITY_INVALID",
        "增量水位线回溯 ParquetPartitioner",
        "桌面热键冲突 tokio::sync::RwLock",
        "构建缓存失效 env.ps1",
        "查询计划退化 pg_stat_statements",
        "索引重建阻塞 WAL_CHECKPOINT_TRUNCATE",
        "配置热重载竞态 BackoffPolicy",
    ];

    public static async Task<int> RunAsync(
        PerfRunOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (options.Scales.Count == 0 || options.Scales.Any(scale => scale <= 0))
        {
            error.WriteLine("Scales must contain positive integers.");
            return 1;
        }

        await using var stub = await ZeroLatencyModelStub.StartAsync(options.Dimension, cancellationToken);
        var results = new List<ScaleBenchmarkResult>(options.Scales.Count);
        foreach (var scale in options.Scales)
        {
            var result = await RunScaleAsync(scale, options, stub, cancellationToken);
            results.Add(result);
        }

        var report = new PerfBenchmarkReport(
            DateTimeOffset.UtcNow,
            options.Dimension,
            options.Iterations,
            options.Warmup,
            results);

        PrintReport(output, report);

        var savePath = options.SavePath ?? Path.Combine(BenchmarkPaths.DefaultResultsDir(), "perf-last.json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(savePath))!);
        await File.WriteAllTextAsync(
            savePath,
            JsonSerializer.Serialize(report, RecallEvaluation.JsonOptions),
            new UTF8Encoding(false),
            cancellationToken);
        return 0;
    }

    private static async Task<ScaleBenchmarkResult> RunScaleAsync(
        int targetUnits,
        PerfRunOptions options,
        ZeroLatencyModelStub stub,
        CancellationToken cancellationToken)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "tacit-bench-perf-" + Guid.NewGuid().ToString("N")[..8]);
        var userHome = Path.Combine(tempRoot, "home");
        var tacitHome = Path.Combine(userHome, ".tacit");
        var memoryDir = Path.Combine(tacitHome, "memory");
        Directory.CreateDirectory(userHome);

        try
        {
            var tacitOptions = new TacitOptions
            {
                TacitHome = tacitHome,
                UserHome = userHome,
                HostName = "perf-box",
            };
            var tacit = new Tacit(tacitOptions);
            await tacit.InitAsync(new InitRequest { Language = MemoryLanguage.Zh }, cancellationToken);
            GenerateSyntheticCorpus(memoryDir, targetUnits);

            var rng = new Random(42);
            var queryVectors = Enumerable.Range(0, SampleQueries.Length)
                .Select(_ => RandomUnitVector(rng, options.Dimension))
                .ToArray();

            double syncColdMs;
            double syncIncrementalMs;
            int actualUnits;
            int vectorCount;
            (double P50, double P95, long Alloc) ftsStats;
            (double P50, double P95, long Alloc) vectorStats;

            using (var index = new MemoryIndex(
                memoryDir,
                StubEmbeddingModel,
                () => new DateOnly(2026, 10, 31),
                () => new Dictionary<string, DateOnly>()))
            {
                var sw = Stopwatch.StartNew();
                index.SyncAll();
                sw.Stop();
                syncColdMs = sw.Elapsed.TotalMilliseconds;

                sw.Restart();
                index.SyncAll();
                sw.Stop();
                syncIncrementalMs = sw.Elapsed.TotalMilliseconds;

                // Populate deterministic normalized float32 vectors in batches without HTTP overhead.
                while (true)
                {
                    var pending = index.PendingUnits(512);
                    if (pending.Count == 0)
                        break;
                    var pairs = pending.Select(unit => (unit, RandomUnitVector(rng, options.Dimension))).ToList();
                    index.StoreVectors(pairs);
                }

                var counts = index.Counts();
                actualUnits = counts.Units;
                vectorCount = counts.Vectors;

                var noFilter = new IndexFilter(null, null, null);
                ftsStats = MeasureSync(
                    options.Warmup,
                    options.Iterations,
                    i => index.Search(SampleQueries[i % SampleQueries.Length], noFilter, 20));

                vectorStats = MeasureSync(
                    options.Warmup,
                    options.Iterations,
                    i => index.SearchVector(queryVectors[i % queryVectors.Length], noFilter, 20));
            }

            // Open the public MemoryService facade against the already-populated index + zero-latency loopback stub.
            var configYaml = $$"""
                language: zh
                models:
                  embedding:
                    url: '{{stub.EmbeddingUrl}}'
                    model: '{{StubEmbeddingModel}}'
                  reranker:
                    url: '{{stub.RerankUrl}}'
                    model: '{{StubRerankerModel}}'
                """;
            await File.WriteAllTextAsync(Path.Combine(tacitHome, "config.yaml"), configYaml, new UTF8Encoding(false), cancellationToken);

            (double P50, double P95, long Alloc) endToEndStats;
            using (var service = tacit.OpenService(_ => { }, NullLoggerFactory.Instance))
            {
                endToEndStats = await MeasureAsync(
                    options.Warmup,
                    options.Iterations,
                    i => service.Browse.TraceRecallAsync(
                        new RecallTraceRequest(SampleQueries[i % SampleQueries.Length]),
                        cancellationToken));
            }

            return new ScaleBenchmarkResult(
                TargetUnits: targetUnits,
                ActualUnits: actualUnits,
                VectorCount: vectorCount,
                SyncColdMs: syncColdMs,
                SyncIncrementalMs: syncIncrementalMs,
                FtsP50Ms: ftsStats.P50,
                FtsP95Ms: ftsStats.P95,
                FtsAllocBytes: ftsStats.Alloc,
                VectorP50Ms: vectorStats.P50,
                VectorP95Ms: vectorStats.P95,
                VectorAllocBytes: vectorStats.Alloc,
                EndToEndP50Ms: endToEndStats.P50,
                EndToEndP95Ms: endToEndStats.P95,
                EndToEndAllocBytes: endToEndStats.Alloc);
        }
        finally
        {
            BenchmarkPaths.TryDeleteDirectory(tempRoot);
        }
    }

    private static (double P50, double P95, long MedianAlloc) MeasureSync(int warmup, int iterations, Action<int> action)
    {
        for (var i = 0; i < warmup; i++)
            action(i);

        var times = new List<double>(iterations);
        var allocs = new List<long>(iterations);
        var sw = new Stopwatch();
        for (var i = 0; i < iterations; i++)
        {
            var beforeAlloc = GC.GetAllocatedBytesForCurrentThread();
            sw.Restart();
            action(i);
            sw.Stop();
            var afterAlloc = GC.GetAllocatedBytesForCurrentThread();
            times.Add(sw.Elapsed.TotalMilliseconds);
            allocs.Add(Math.Max(0, afterAlloc - beforeAlloc));
        }

        times.Sort();
        allocs.Sort();
        return (
            RecallEvaluation.Percentile(times, 0.50),
            RecallEvaluation.Percentile(times, 0.95),
            allocs[allocs.Count / 2]);
    }

    private static async Task<(double P50, double P95, long MedianAlloc)> MeasureAsync(
        int warmup,
        int iterations,
        Func<int, Task> action)
    {
        for (var i = 0; i < warmup; i++)
            await action(i);

        var times = new List<double>(iterations);
        var allocs = new List<long>(iterations);
        var sw = new Stopwatch();
        for (var i = 0; i < iterations; i++)
        {
            var beforeAlloc = GC.GetTotalAllocatedBytes(precise: false);
            sw.Restart();
            await action(i);
            sw.Stop();
            var afterAlloc = GC.GetTotalAllocatedBytes(precise: false);
            times.Add(sw.Elapsed.TotalMilliseconds);
            allocs.Add(Math.Max(0, afterAlloc - beforeAlloc));
        }

        times.Sort();
        allocs.Sort();
        return (
            RecallEvaluation.Percentile(times, 0.50),
            RecallEvaluation.Percentile(times, 0.95),
            allocs[allocs.Count / 2]);
    }

    private static void GenerateSyntheticCorpus(string memoryDir, int targetUnits)
    {
        var utf8NoBom = new UTF8Encoding(false);
        var workstreamsSb = new StringBuilder("## Workstream\n");
        foreach (var ws in Workstreams)
            workstreamsSb.Append(CultureInfo.InvariantCulture, $"### {ws}\nBackground: Synthetic benchmark workstream {ws}.\n");
        File.WriteAllText(Path.Combine(memoryDir, "workstreams.md"), workstreamsSb.ToString(), utf8NoBom);

        // ~85% page units (4 units per page: 1 clue + 3 sections), ~15% activity units.
        var pageCount = Math.Max(4, (int)Math.Ceiling(targetUnits * 0.85 / 4.0));
        var activityCount = Math.Max(4, targetUnits - pageCount * 4);

        foreach (var ws in Workstreams)
            Directory.CreateDirectory(Path.Combine(memoryDir, "pages", ws));

        for (var i = 0; i < pageCount; i++)
        {
            var ws = Workstreams[i % Workstreams.Length];
            var type = PageTypes[i % PageTypes.Length];
            var topic = Topics[i % Topics.Length];
            var code = CodeTerms[i % CodeTerms.Length];
            var day = new DateOnly(2026, 8, 1).AddDays(i % 90);
            var slug = $"item-{i:D4}";
            var content = $$"""
                ---
                title: {{topic}}处理记录 #{{i}}
                aliases: [{{topic}}故障-{{i}}, {{code}} 排查-{{i}}]
                dates: { first: {{day:yyyy-MM-dd}}, last: {{day:yyyy-MM-dd}} }
                type: {{type}}
                ---

                ## 背景
                在 {{ws}} 主线运行期间观测到 {{topic}}，涉及组件 {{code}} 与批处理任务 #{{i}}。

                ## 排查过程
                通过核对慢查询与服务指标定位到 {{code}} 参数配置偏保守，调整并发上限并复测通过。

                ## 结论
                在 dev-box 与 build-node 统一落地新参数，并保留回滚检查清单（编号 {{i}}）。
                """;
            File.WriteAllText(Path.Combine(memoryDir, "pages", ws, slug + ".md"), content.ReplaceLineEndings("\n") + "\n", utf8NoBom);
        }

        Directory.CreateDirectory(Path.Combine(memoryDir, "recent"));
        var recentSb = new StringBuilder();
        DateOnly? currentDay = null;
        for (var i = 0; i < activityCount; i++)
        {
            var dayOffset = (int)((long)i * 28 / Math.Max(1, activityCount));
            var day = new DateOnly(2026, 10, 1).AddDays(dayOffset);
            if (day != currentDay)
            {
                recentSb.Append(CultureInfo.InvariantCulture, $"## {day:yyyy-MM-dd}\n");
                currentDay = day;
            }
            var ws = Workstreams[i % Workstreams.Length];
            var topic = Topics[i % Topics.Length];
            var code = CodeTerms[i % CodeTerms.Length];
            recentSb.Append(CultureInfo.InvariantCulture, $"- [{ws}] 完成第 {i} 轮 {topic} 验证并更新 {code} 配置\n  <!-- on dev-box -->\n");
        }
        File.WriteAllText(Path.Combine(memoryDir, "recent", "2026-10.md"), recentSb.ToString(), utf8NoBom);
    }

    private static float[] RandomUnitVector(Random rng, int dimension)
    {
        var vector = new float[dimension];
        double sumSq = 0;
        for (var i = 0; i < dimension; i++)
        {
            var value = (float)(rng.NextDouble() * 2.0 - 1.0);
            vector[i] = value;
            sumSq += (double)value * value;
        }
        var invNorm = (float)(1.0 / Math.Sqrt(sumSq));
        for (var i = 0; i < dimension; i++)
            vector[i] *= invNorm;
        return vector;
    }

    private static void PrintReport(TextWriter output, PerfBenchmarkReport report)
    {
        output.WriteLine($"=== Performance Benchmark (dim={report.Dimension}, warmup={report.Warmup}, iterations={report.Iterations}) ===");
        output.WriteLine($"{"Units",7} | {"SyncCold",9} | {"SyncInc",8} | {"FTS5 P50/P95",15} | {"FTS Alloc",10} | {"Vector P50/P95",16} | {"Vec Alloc",10} | {"EndToEnd P50/P95",17} | {"E2E Alloc",10}");
        output.WriteLine(new string('-', 127));
        foreach (var s in report.Scales)
        {
            output.WriteLine(
                $"{s.ActualUnits,7} | {s.SyncColdMs,7:F1}ms | {s.SyncIncrementalMs,6:F1}ms | "
                + $"{s.FtsP50Ms,6:F2}/{s.FtsP95Ms,6:F2}ms | {FormatBytes(s.FtsAllocBytes),10} | "
                + $"{s.VectorP50Ms,6:F2}/{s.VectorP95Ms,6:F2}ms | {FormatBytes(s.VectorAllocBytes),10} | "
                + $"{s.EndToEndP50Ms,7:F2}/{s.EndToEndP95Ms,7:F2}ms | {FormatBytes(s.EndToEndAllocBytes),10}");
        }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):F2} MB",
        >= 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes} B",
    };

    /// <summary>Zero-latency loopback HTTP model stub so end-to-end perf runs exclude real model inference time.</summary>
    private sealed class ZeroLatencyModelStub : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly string _singleEmbeddingResponseJson;

        private ZeroLatencyModelStub(int dimension)
        {
            var vector = RandomUnitVector(new Random(7), dimension);
            var embeddingPayload = new JsonObject
            {
                ["data"] = new JsonArray(new JsonObject
                {
                    ["index"] = 0,
                    ["embedding"] = new JsonArray(vector.Select(value => JsonValue.Create(value)).ToArray()),
                }),
            };
            _singleEmbeddingResponseJson = embeddingPayload.ToJsonString();

            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
            _app = builder.Build();
            _app.MapPost("/v1/embeddings", () => Results.Content(_singleEmbeddingResponseJson, "application/json"));
            _app.MapPost("/v1/rerank", async (HttpRequest request) =>
            {
                var body = (await JsonNode.ParseAsync(request.Body))!.AsObject();
                var count = body["documents"]!.AsArray().Count;
                var results = new JsonArray();
                for (var i = 0; i < count; i++)
                {
                    results.Add(new JsonObject
                    {
                        ["index"] = i,
                        ["relevance_score"] = 1.0 - i * 0.01,
                    });
                }
                return Results.Json(new JsonObject { ["results"] = results });
            });
        }

        public Uri EmbeddingUrl { get; private set; } = null!;
        public Uri RerankUrl { get; private set; } = null!;

        public static async Task<ZeroLatencyModelStub> StartAsync(int dimension, CancellationToken cancellationToken)
        {
            var stub = new ZeroLatencyModelStub(dimension);
            await stub._app.StartAsync(cancellationToken);
            var root = new Uri(stub._app.Urls.Single());
            stub.EmbeddingUrl = new Uri(root, "/v1/embeddings");
            stub.RerankUrl = new Uri(root, "/v1/rerank");
            return stub;
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
