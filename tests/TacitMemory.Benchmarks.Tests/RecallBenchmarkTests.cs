using System.Text.Json;
using TacitMemory.Core.Tests;

namespace TacitMemory.Benchmarks.Tests;

public sealed class RecallBenchmarkTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tacit-bench-test-" + Guid.NewGuid().ToString("N")[..8]);

    public RecallBenchmarkTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task RecallComputesMetricsStageFunnelAllMissFatesAndNegativeOutcomes()
    {
        var corpusDir = CreateRichMiniCorpus();
        var cacheDir = Path.Combine(_root, "cache");
        var savePath = Path.Combine(_root, "result.json");
        using var upstream = new ModelDouble
        {
            Embed = text => text switch
            {
                var s when s.Contains("pgroonga", StringComparison.OrdinalIgnoreCase) => [1f, 0f, 0f, 0f],
                var s when s.Contains("证书过期", StringComparison.Ordinal) || s.Contains("TLS", StringComparison.OrdinalIgnoreCase) => [0f, 1f, 0f, 0f],
                var s when s.Contains("构建缓存", StringComparison.Ordinal) => [0f, 0f, 1f, 0f],
                var s when s.Contains("死信队列", StringComparison.Ordinal) => [0f, 0f, 0.2f, 0.98f],
                _ => [0f, 0f, 0f, 1f],
            },
            Score = (query, doc) =>
            {
                if (query.Contains("完全不存在的话题", StringComparison.Ordinal))
                    return 0.05; // Abstained negative query
                if (query.Contains("误触发负例", StringComparison.Ordinal))
                    return 0.4; // False positive negative query
                if (query.Contains("构建缓存", StringComparison.Ordinal))
                    return doc.Contains("构建缓存", StringComparison.Ordinal) ? 0.7
                        : doc.Contains("pgroonga", StringComparison.OrdinalIgnoreCase) ? 0.9 : 0.1; // Rank 2 (tests MRR=0.5, Hit@1=0)
                if (query.Contains("死信队列", StringComparison.Ordinal))
                    return doc.Contains("死信队列", StringComparison.Ordinal) ? 0.5 : 0.9; // BeyondTopK when topK=1
                if (query.Contains("pgroonga", StringComparison.OrdinalIgnoreCase))
                    return doc.Contains("pgroonga", StringComparison.OrdinalIgnoreCase) ? 0.95 : 0.1; // Rank 1
                if (query.Contains("TLS", StringComparison.OrdinalIgnoreCase))
                    return 0.05; // BelowThreshold
                return 0.05;
            },
        };

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exitCode = await BenchmarkApp.RunAsync(
            [
                "recall",
                "--corpus", corpusDir,
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
        var root = doc.RootElement;
        var summary = root.GetProperty("summary");
        Assert.Equal(5, summary.GetProperty("positiveCount").GetInt32());
        Assert.Equal(2, summary.GetProperty("negativeCount").GetInt32());
        // 2 hits out of 5 positive queries (q-hit rank 1, q-rank2 rank 2); q-killed=below_threshold, q-topk=beyond_top_k, q-cand=beyond_candidates.
        Assert.Equal(0.4, summary.GetProperty("recallAt5").GetDouble(), 4);
        Assert.Equal(0.2, summary.GetProperty("hitAt1Rate").GetDouble(), 4);
        Assert.Equal(0.3, summary.GetProperty("mrr").GetDouble(), 4);
        Assert.Equal(1, summary.GetProperty("negativeFalsePositives").GetInt32());
        Assert.Equal(0.5, summary.GetProperty("abstentionRate").GetDouble(), 4);

        var funnel = root.GetProperty("funnel");
        Assert.True(funnel.GetProperty("fullTextHits").GetInt32() >= 4);
        Assert.True(funnel.GetProperty("vectorHits").GetInt32() >= 4);
        Assert.Equal(4, funnel.GetProperty("fusedHits").GetInt32());
        Assert.Equal(2, funnel.GetProperty("rerankedHits").GetInt32());
        Assert.Equal(1, funnel.GetProperty("missedBelowThreshold").GetInt32());
        Assert.Equal(1, funnel.GetProperty("missedBeyondTopK").GetInt32());
        Assert.Equal(1, funnel.GetProperty("missedBeyondCandidates").GetInt32());

        var text = stdout.ToString();
        Assert.Contains("Recall@5", text, StringComparison.Ordinal);
        Assert.Contains("hit@1=", text, StringComparison.Ordinal);
        Assert.Contains("below_threshold", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("beyond_top_k", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("beyond_candidates", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CachingProxyReusesPerItemCacheForwardsOnlyMissesAndMatchesDirectOrder()
    {
        var corpusDir = CreateMiniCorpus();
        var cacheDir = Path.Combine(_root, "cache");
        var save1 = Path.Combine(_root, "run1.json");
        var save2 = Path.Combine(_root, "run2.json");
        var save3 = Path.Combine(_root, "run3.json");
        var save4 = Path.Combine(_root, "run4.json");

        using var upstream = new ModelDouble
        {
            Embed = ModelDouble.BagOfPairs,
            Score = (query, doc) => doc.Contains("pgroonga", StringComparison.OrdinalIgnoreCase) && query.Contains("pgroonga", StringComparison.OrdinalIgnoreCase) ? 0.9 : 0.2,
        };

        string[] BaseArgs(string savePath, bool noCache = false) =>
        [
            "recall",
            "--corpus", corpusDir,
            "--embedding-url", upstream.EmbeddingUrl.ToString(),
            "--embedding-model", "cache-emb",
            "--reranker-url", upstream.RerankUrl.ToString(),
            "--reranker-model", "cache-rerank",
            "--cache-dir", cacheDir,
            "--save", savePath,
            .. noCache ? new[] { "--no-cache" } : [],
        ];

        // Run 1: cold cache populates upstream calls.
        Assert.Equal(0, await BenchmarkApp.RunAsync(BaseArgs(save1), new StringWriter(), new StringWriter()));
        var embCallsAfterRun1 = upstream.EmbeddingCalls;
        var rerankCallsAfterRun1 = upstream.RerankCalls;
        Assert.True(embCallsAfterRun1 > 0);
        Assert.True(rerankCallsAfterRun1 > 0);

        // Run 2: warm cache hits zero upstream requests.
        Assert.Equal(0, await BenchmarkApp.RunAsync(BaseArgs(save2), new StringWriter(), new StringWriter()));
        Assert.Equal(embCallsAfterRun1, upstream.EmbeddingCalls);
        Assert.Equal(rerankCallsAfterRun1, upstream.RerankCalls);

        // Run 3: add one new query and one new page -> only uncached texts/pairs hit upstream.
        File.WriteAllText(Path.Combine(corpusDir, "memory", "pages", "my-app", "queue-retry.md"), """
            ---
            title: 异步队列退避重试策略
            aliases: [消息重试退避, queue backoff]
            dates: { first: 2026-10-18, last: 2026-10-18 }
            type: decision
            ---

            ## 方案
            采用指数退避并在 5 次后转入死信队列。
            """);
        File.AppendAllText(
            Path.Combine(corpusDir, "queries.yaml"),
            "\n  - id: q-new\n    category: exact-code\n    query: \"死信队列退避重试\"\n    targets:\n      - page: \"my-app/queue-retry\"\n");

        Assert.Equal(0, await BenchmarkApp.RunAsync(BaseArgs(save3), new StringWriter(), new StringWriter()));
        var run3EmbInputs = upstream.EmbeddingRequests
            .Skip(embCallsAfterRun1)
            .SelectMany(req => req.Body["input"]!.AsArray().Select(node => node!.GetValue<string>()))
            .ToList();
        Assert.Contains(run3EmbInputs, input => input.Contains("死信队列", StringComparison.Ordinal));
        Assert.DoesNotContain(run3EmbInputs, input => input.Contains("pgroonga", StringComparison.OrdinalIgnoreCase));

        var run3RerankForHitQuery = upstream.RerankRequests
            .Skip(rerankCallsAfterRun1)
            .Where(req => req.Body["query"]!.GetValue<string>().Contains("pgroonga", StringComparison.OrdinalIgnoreCase))
            .SelectMany(req => req.Body["documents"]!.AsArray().Select(node => node!.GetValue<string>()))
            .ToList();
        Assert.DoesNotContain(run3RerankForHitQuery, doc => doc.Contains("pgroonga", StringComparison.OrdinalIgnoreCase));

        var embCallsAfterRun3 = upstream.EmbeddingCalls;
        var rerankCallsAfterRun3 = upstream.RerankCalls;

        // Run 4: --no-cache bypasses cache and hits upstream again, producing identical ordering and outcomes as Run 3.
        Assert.Equal(0, await BenchmarkApp.RunAsync(BaseArgs(save4, noCache: true), new StringWriter(), new StringWriter()));
        Assert.True(upstream.EmbeddingCalls > embCallsAfterRun3);
        Assert.True(upstream.RerankCalls > rerankCallsAfterRun3);

        using var cachedDoc = JsonDocument.Parse(await File.ReadAllTextAsync(save3));
        using var directDoc = JsonDocument.Parse(await File.ReadAllTextAsync(save4));
        var cachedQueries = cachedDoc.RootElement.GetProperty("queries").EnumerateArray().ToList();
        var directQueries = directDoc.RootElement.GetProperty("queries").EnumerateArray().ToList();
        Assert.Equal(cachedQueries.Count, directQueries.Count);
        for (var i = 0; i < cachedQueries.Count; i++)
        {
            Assert.Equal(cachedQueries[i].GetProperty("id").GetString(), directQueries[i].GetProperty("id").GetString());
            Assert.Equal(cachedQueries[i].GetProperty("recallAt5").GetDouble(), directQueries[i].GetProperty("recallAt5").GetDouble(), 6);
            Assert.Equal(
                cachedQueries[i].GetProperty("returnedTop").GetRawText(),
                directQueries[i].GetProperty("returnedTop").GetRawText());
        }
    }

    [Fact]
    public async Task FromConfigSupportsPartialOverridesReadOnlyAndPrintsBaselineDiff()
    {
        var corpusDir = CreateMiniCorpus();
        var cacheDir = Path.Combine(_root, "cache");
        var baselinePath = Path.Combine(_root, "baseline.json");
        var latestPath = Path.Combine(_root, "latest.json");
        var untouchedMemory = Path.Combine(_root, "do-not-touch-memory");

        var tlsScore = 0.0001;
        using var upstream = new ModelDouble
        {
            Embed = ModelDouble.BagOfPairs,
            Score = (query, doc) =>
            {
                if (query.Contains("完全不存在的话题", StringComparison.Ordinal))
                    return 0.0001;
                if (query.Contains("pgroonga", StringComparison.OrdinalIgnoreCase))
                    return doc.Contains("pgroonga", StringComparison.OrdinalIgnoreCase) ? 0.95 : 0.1;
                if (query.Contains("TLS", StringComparison.OrdinalIgnoreCase))
                    return doc.Contains("证书过期", StringComparison.Ordinal) ? tlsScore : 0.05;
                return 0.05;
            },
        };

        var sourceConfigDir = Path.Combine(_root, "source-home");
        Directory.CreateDirectory(sourceConfigDir);
        var sourceConfigPath = Path.Combine(sourceConfigDir, "config.yaml");
        await File.WriteAllTextAsync(sourceConfigPath, $"""
            language: zh
            memory:
              path: '{untouchedMemory.Replace("'", "''", StringComparison.Ordinal)}'
            {upstream.ModelsYaml(embeddingModel: "cfg-emb")}
            """);

        var out1 = new StringWriter();
        var err1 = new StringWriter();
        var code1 = await BenchmarkApp.RunAsync(
            [
                "recall",
                "--corpus", corpusDir,
                "--from-config", sourceConfigPath,
                "--embedding-model", "override-emb",
                "--cache-dir", cacheDir,
                "--save", baselinePath,
            ],
            out1,
            err1);

        Assert.True(string.IsNullOrWhiteSpace(err1.ToString()), err1.ToString());
        Assert.Equal(0, code1);
        Assert.False(Directory.Exists(untouchedMemory));
        Assert.Contains("override-emb", out1.ToString(), StringComparison.Ordinal);

        // Improve TLS score and run with --baseline to verify inline diff.
        tlsScore = 0.92;
        var out2 = new StringWriter();
        var err2 = new StringWriter();
        var code2 = await BenchmarkApp.RunAsync(
            ["recall", "--corpus", corpusDir, "--from-config", sourceConfigPath, "--baseline", baselinePath, "--save", latestPath, "--no-cache"],
            out2,
            err2);

        Assert.True(string.IsNullOrWhiteSpace(err2.ToString()), err2.ToString());
        Assert.Equal(0, code2);
        Assert.False(Directory.Exists(untouchedMemory));
        var diffText = out2.ToString();
        Assert.Contains("Baseline Diff", diffText, StringComparison.Ordinal);
        Assert.Contains("IMPROVED", diffText, StringComparison.Ordinal);
        Assert.Contains("[q-killed]", diffText, StringComparison.Ordinal);

        // Without models configured, recall runs in degraded mode and emits an explicit notice.
        var out3 = new StringWriter();
        var err3 = new StringWriter();
        var code3 = await BenchmarkApp.RunAsync(
            ["recall", "--corpus", corpusDir, "--save", Path.Combine(_root, "degraded.json")],
            out3,
            err3);
        Assert.True(string.IsNullOrWhiteSpace(err3.ToString()), err3.ToString());
        Assert.Equal(0, code3);
        Assert.Contains("degraded mode", out3.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private string CreateMiniCorpus()
    {
        var corpusDir = Path.Combine(_root, "corpus");
        var memoryDir = Path.Combine(corpusDir, "memory");
        Directory.CreateDirectory(Path.Combine(memoryDir, "pages", "my-app"));
        Directory.CreateDirectory(Path.Combine(memoryDir, "pages", "ops-infra"));
        Directory.CreateDirectory(Path.Combine(memoryDir, "recent"));

        File.WriteAllText(Path.Combine(memoryDir, "persona.md"), """
            ## Red lines

            ## Working rules

            ## Preferences

            ## Profile
            """);
        File.WriteAllText(Path.Combine(memoryDir, "candidates.md"), """
            ## Candidates

            ## Rejection list
            """);
        File.WriteAllText(Path.Combine(memoryDir, "workstreams.md"), """
            ## Workstream
            ### my-app
            Background: Internal workflow web application.
            ### ops-infra
            Background: Reverse proxy and TLS certificate maintenance.
            """);
        File.WriteAllText(Path.Combine(memoryDir, "pages", "my-app", "pgroonga-upgrade.md"), """
            ---
            title: 数据库升级后 pgroonga 插件失效
            aliases: [全文检索扩展丢失, pgroonga 故障]
            dates: { first: 2026-10-10, last: 2026-10-12 }
            type: incident
            ---

            ## 现象
            升级主库版本后 pgroonga 扩展无法加载导致全文检索失败。

            ## Lesson
            大版本升级前先确认扩展包已匹配目标版本。
            """);
        File.WriteAllText(Path.Combine(memoryDir, "pages", "ops-infra", "tls-renew.md"), """
            ---
            title: 边缘代理证书续期流程
            aliases: [TLS 证书过期处理, ACME 自动续期]
            dates: { first: 2026-10-15, last: 2026-10-15 }
            type: work
            ---

            ## 处理方式
            证书过期后需要重启边缘网关重新加载密钥。
            """);
        File.WriteAllText(Path.Combine(memoryDir, "recent", "2026-10.md"), """
            ## 2026-10-16
            - [my-app] 清理构建缓存并验证增量编译耗时
              <!-- on dev-box -->
            """);
        File.WriteAllText(Path.Combine(corpusDir, "queries.yaml"), """
            queries:
              - id: q-hit
                category: exact-code
                query: "pgroonga 扩展无法加载"
                targets:
                  - page: "my-app/pgroonga-upgrade"
                    section: "现象"
              - id: q-killed
                category: bilingual
                query: "TLS 证书过期怎么处理"
                targets:
                  - page: "ops-infra/tls-renew"
              - id: q-neg
                category: negative
                query: "完全不存在的话题量子计算"
                negative: true
                targets: []
            """);
        return corpusDir;
    }

    private string CreateRichMiniCorpus()
    {
        var corpusDir = CreateMiniCorpus();
        var memoryDir = Path.Combine(corpusDir, "memory");
        File.WriteAllText(Path.Combine(memoryDir, "pages", "my-app", "queue-retry.md"), """
            ---
            title: 异步队列退避重试策略
            aliases: [消息重试退避, 死信队列配置]
            dates: { first: 2026-10-18, last: 2026-10-18 }
            type: decision
            ---

            ## 方案
            采用指数退避并在 5 次后转入死信队列。
            """);
        File.WriteAllText(Path.Combine(corpusDir, "queries.yaml"), """
            queries:
              - id: q-hit
                category: exact-code
                query: "pgroonga 扩展无法加载"
                targets:
                  - page: "my-app/pgroonga-upgrade"
                    section: "现象"
              - id: q-rank2
                category: paraphrase-zh
                query: "清理构建缓存 增量编译 pgroonga"
                targets:
                  - activity: "2026-10-16"
                    contains: "构建缓存"
              - id: q-killed
                category: bilingual
                query: "TLS 证书过期怎么处理"
                targets:
                  - page: "ops-infra/tls-renew"
              - id: q-topk
                category: filtered
                query: "死信队列退避 pgroonga"
                topK: 1
                targets:
                  - page: "my-app/queue-retry"
              - id: q-cand
                category: cross-field
                query: "pgroonga 扩展无法加载 死信队列"
                candidates: 2
                targets:
                  - page: "my-app/queue-retry"
              - id: q-neg-ok
                category: negative
                query: "完全不存在的话题量子计算"
                negative: true
                targets: []
              - id: q-neg-fp
                category: negative
                query: "误触发负例 pgroonga"
                negative: true
                targets: []
            """);
        return corpusDir;
    }
}
