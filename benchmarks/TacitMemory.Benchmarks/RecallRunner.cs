using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TacitMemory.Core;

namespace TacitMemory.Benchmarks;

internal sealed record RecallRunOptions(
    string CorpusDir,
    ModelEndpoint? Embedding,
    ModelEndpoint? Reranker,
    string CacheDir,
    bool NoCache,
    string? SavePath,
    string? BaselinePath,
    int? TopK,
    int? Candidates,
    double? MinRerankScore,
    bool StrictCorpus = false);

internal static class RecallRunner
{
    public static async Task<int> RunAsync(
        RecallRunOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var memorySource = Path.Combine(options.CorpusDir, "memory");
        var queriesPath = Path.Combine(options.CorpusDir, "queries.yaml");
        if (!Directory.Exists(memorySource) || !File.Exists(queriesPath))
        {
            error.WriteLine($"Corpus directory must contain memory/ and queries.yaml: {options.CorpusDir}");
            return 1;
        }

        var manifest = QueryManifest.Load(queriesPath);
        if (manifest.Queries.Count == 0)
        {
            error.WriteLine($"No queries found in {queriesPath}");
            return 1;
        }

        var unconfigured = new List<string>();
        if (options.Embedding is null)
            unconfigured.Add("embedding");
        if (options.Reranker is null)
            unconfigured.Add("reranker");
        if (unconfigured.Count > 0)
            output.WriteLine($"Notice: running in degraded mode (unconfigured: {string.Join(", ", unconfigured)}).");

        var tempRoot = Path.Combine(Path.GetTempPath(), "tacit-bench-recall-" + Guid.NewGuid().ToString("N")[..8]);
        var userHome = Path.Combine(tempRoot, "home");
        var tacitHome = Path.Combine(userHome, ".tacit");
        var memoryDir = Path.Combine(tacitHome, "memory");
        Directory.CreateDirectory(userHome);

        try
        {
            await using var proxy = options.Embedding is not null || options.Reranker is not null
                ? await CachingModelProxy.StartAsync(options.Embedding, options.Reranker, options.CacheDir, options.NoCache, cancellationToken)
                : null;

            var tacitOptions = new TacitOptions
            {
                TacitHome = tacitHome,
                UserHome = userHome,
                HostName = "bench-box",
            };
            var tacit = new Tacit(tacitOptions);
            await tacit.InitAsync(new InitRequest { Language = MemoryLanguage.Zh }, cancellationToken);

            // Overlay the corpus files on top of the initialized memory repository without touching source files.
            CopyMemoryFiles(memorySource, memoryDir);

            // Write an isolated config pointing only to the local caching proxy and isolated memory repository.
            var configYaml = BuildConfigYaml(proxy?.EmbeddingProxyEndpoint, proxy?.RerankerProxyEndpoint);
            await File.WriteAllTextAsync(Path.Combine(tacitHome, "config.yaml"), configYaml, new UTF8Encoding(false), cancellationToken);

            using var service = tacit.OpenService(_ => { }, NullLoggerFactory.Instance);
            var validationErrors = await ValidateCorpusAsync(service, manifest, options.StrictCorpus, cancellationToken);
            if (validationErrors.Count > 0)
            {
                foreach (var problem in validationErrors)
                    error.WriteLine(problem);
                return 1;
            }

            // Ensure the derived index and all vectors are ready before measuring query latency.
            await service.Maintenance.ReindexAsync(cancellationToken);

            var outcomes = new List<QueryOutcome>(manifest.Queries.Count);
            foreach (var query in manifest.Queries)
            {
                var request = new RecallTraceRequest(
                    Query: query.Query,
                    Workstream: query.Workstream,
                    From: query.From,
                    To: query.To,
                    Kind: query.Kind,
                    MinRerankScore: options.MinRerankScore ?? query.MinRerankScore,
                    TopK: options.TopK ?? query.TopK,
                    Candidates: options.Candidates ?? query.Candidates);
                var stopwatch = Stopwatch.StartNew();
                var trace = await service.Browse.TraceRecallAsync(request, cancellationToken);
                stopwatch.Stop();
                outcomes.Add(RecallEvaluation.EvaluateQuery(query, trace, stopwatch.Elapsed.TotalMilliseconds));
            }

            var report = RecallEvaluation.Summarize(
                options.CorpusDir,
                options.Embedding?.Model,
                options.Reranker?.Model,
                outcomes);

            RecallBenchmarkReport? baseline = null;
            if (options.BaselinePath is not null && File.Exists(options.BaselinePath))
            {
                baseline = JsonSerializer.Deserialize<RecallBenchmarkReport>(
                    await File.ReadAllTextAsync(options.BaselinePath, cancellationToken),
                    RecallEvaluation.JsonOptions);
            }

            RecallEvaluation.PrintReport(output, report, baseline);
            if (proxy is not null)
            {
                var stats = proxy.Stats;
                output.WriteLine($"Cache:     embedding={stats.EmbeddingHits} hit / {stats.EmbeddingMisses} miss | rerank={stats.RerankHits} hit / {stats.RerankMisses} miss{(options.NoCache ? " (bypassed)" : "")}");
            }

            var savePath = options.SavePath ?? Path.Combine(BenchmarkPaths.DefaultResultsDir(), "last.json");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(savePath))!);
            await File.WriteAllTextAsync(
                savePath,
                JsonSerializer.Serialize(report, RecallEvaluation.JsonOptions),
                new UTF8Encoding(false),
                cancellationToken);
            return 0;
        }
        finally
        {
            BenchmarkPaths.TryDeleteDirectory(tempRoot);
        }
    }

    private static async Task<List<string>> ValidateCorpusAsync(
        MemoryService service,
        QueryManifest manifest,
        bool strictFiles,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        if (strictFiles)
        {
            var persona = await service.Browse.PersonaAsync(cancellationToken);
            foreach (var problem in persona.PersonaFile.Problems)
                errors.Add($"persona.md: {problem.Description}");
            foreach (var problem in persona.CandidatesFile.Problems)
                errors.Add($"candidates.md: {problem.Description}");
        }

        var pageList = await service.Browse.PagesAsync(cancellationToken);
        var pageSections = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var page in pageList.Pages)
        {
            var parts = page.Id.Split('/', 2);
            var view = await service.Browse.PageAsync(parts[0], parts[1], cancellationToken);
            if (strictFiles)
            {
                foreach (var problem in view.File.Problems)
                    errors.Add($"{page.Id}: {problem.Description}");
                foreach (var related in view.Related.Where(item => !item.Exists))
                    errors.Add($"{page.Id}: broken related link '{related.Id}'");
            }
            pageSections[page.Id] = view.Sections.Select(section => section.Name).ToHashSet(StringComparer.Ordinal);
        }

        var months = await service.Browse.ActivityMonthsAsync(cancellationToken);
        var activities = new List<(string Date, string Code, string Text)>();
        var summaries = new List<(string Date, string Text)>();
        foreach (var month in months.Months)
        {
            var view = await service.Browse.ActivityMonthAsync(month, cancellationToken);
            if (strictFiles)
            {
                foreach (var problem in view.File.Problems)
                    errors.Add($"recent/{month}.md: {problem.Description}");
            }

            foreach (var day in view.Days)
            {
                foreach (var activity in day.Activities)
                {
                    var labeled = activity.Workstream is { Length: > 0 } ws ? $"[{ws}] {activity.Text}" : activity.Text;
                    activities.Add((day.Date, activity.Code, labeled));
                }
            }

            foreach (var summary in view.Summaries)
                summaries.Add((summary.First, $"{summary.Label}：{summary.Text}"));
        }

        var inventory = new CorpusInventory(pageSections, activities, summaries);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var query in manifest.Queries)
        {
            if (string.IsNullOrWhiteSpace(query.Id))
                errors.Add("Query entry has empty id.");
            else if (!seenIds.Add(query.Id))
                errors.Add($"Duplicate query id: {query.Id}");

            if (string.IsNullOrWhiteSpace(query.Query))
                errors.Add($"Query '{query.Id}' has empty query text.");

            if (query.Negative)
            {
                if (query.Targets.Count > 0)
                    errors.Add($"Negative query '{query.Id}' must not list targets.");
                continue;
            }

            if (query.Targets.Count == 0)
            {
                errors.Add($"Positive query '{query.Id}' has no targets.");
                continue;
            }

            foreach (var target in query.Targets)
            {
                if (target.ValidateAgainst(query.Id, inventory) is { } targetError)
                    errors.Add(targetError);
            }
        }

        return errors;
    }

    private static string BuildConfigYaml(ModelEndpoint? embedding, ModelEndpoint? reranker)
    {
        var sb = new StringBuilder();
        sb.AppendLine("language: zh");
        if (embedding is not null || reranker is not null)
        {
            sb.AppendLine("models:");
            if (embedding is not null)
                AppendModel(sb, "embedding", embedding);
            if (reranker is not null)
                AppendModel(sb, "reranker", reranker);
        }
        return sb.ToString();
    }

    private static void AppendModel(StringBuilder sb, string role, ModelEndpoint endpoint)
    {
        sb.Append(CultureInfo.InvariantCulture, $"  {role}:\n");
        sb.Append(CultureInfo.InvariantCulture, $"    url: '{endpoint.Url}'\n");
        sb.Append(CultureInfo.InvariantCulture, $"    model: '{endpoint.Model.Replace("'", "''", StringComparison.Ordinal)}'\n");
        if (endpoint.Provider == ModelProvider.Cloudflare)
            sb.Append("    provider: cloudflare\n");
        if (!string.IsNullOrEmpty(endpoint.ApiKey))
            sb.Append(CultureInfo.InvariantCulture, $"    apiKey: '{endpoint.ApiKey.Replace("'", "''", StringComparison.Ordinal)}'\n");
    }

    private static void CopyMemoryFiles(string source, string target)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var firstSegment = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            if (firstSegment is ".git" or ".cache")
                continue;
            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }
}
