using System.Globalization;
using TacitMemory.Core;

namespace TacitMemory.Benchmarks;

public static class Program
{
    public static Task<int> Main(string[] args) =>
        BenchmarkApp.RunAsync(args, Console.Out, Console.Error);
}

/// <summary>In-process entry point for the benchmark CLI, shared by <see cref="Program"/> and tests.</summary>
public static class BenchmarkApp
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintUsage(output);
            return 0;
        }

        var command = args[0];
        var options = ParseFlags(args[1..]);
        return command switch
        {
            "recall" => await RunRecallAsync(options, output, error, cancellationToken),
            "perf" => await RunPerfAsync(options, output, error, cancellationToken),
            _ => Fail(error, $"Unknown command: {command}"),
        };
    }

    private static async Task<int> RunPerfAsync(
        Dictionary<string, string?> flags,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var scalesText = flags.GetValueOrDefault("--scales") ?? "500,2000,10000";
        var scales = new List<int>();
        foreach (var part in scalesText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
                return Fail(error, $"Invalid scale value: {part}");
            scales.Add(parsed);
        }

        var iterations = ParseInt(flags.GetValueOrDefault("--iterations")) ?? 30;
        var warmup = ParseInt(flags.GetValueOrDefault("--warmup")) ?? 5;
        var dimension = ParseInt(flags.GetValueOrDefault("--dimension")) ?? 1024;
        var savePath = flags.GetValueOrDefault("--save");

        var runOptions = new PerfRunOptions(scales, iterations, warmup, dimension, savePath);
        return await PerfRunner.RunAsync(runOptions, output, error, cancellationToken);
    }

    private static async Task<int> RunRecallAsync(
        Dictionary<string, string?> flags,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var corpusDir = flags.GetValueOrDefault("--corpus") ?? BenchmarkPaths.DefaultCorpusDir();
        var cacheDir = flags.GetValueOrDefault("--cache-dir") ?? BenchmarkPaths.DefaultCacheDir();
        var noCache = flags.ContainsKey("--no-cache");
        var savePath = flags.GetValueOrDefault("--save");
        var baselinePath = flags.GetValueOrDefault("--baseline");
        if (baselinePath is null)
        {
            var defaultBaseline = Path.Combine(BenchmarkPaths.DefaultResultsDir(), "baseline.json");
            if (File.Exists(defaultBaseline) && !string.Equals(Path.GetFullPath(defaultBaseline), savePath is null ? null : Path.GetFullPath(savePath), StringComparison.OrdinalIgnoreCase))
                baselinePath = defaultBaseline;
        }

        ModelEndpoint? embedding = null;
        ModelEndpoint? reranker = null;
        if (flags.GetValueOrDefault("--from-config") is { Length: > 0 } fromConfig)
        {
            var loaded = TryLoadModelsFromConfig(fromConfig, error);
            if (loaded is null)
                return 1;
            (embedding, reranker) = loaded.Value;
        }

        embedding = ApplyExplicitOverrides(embedding, flags, "embedding");
        reranker = ApplyExplicitOverrides(reranker, flags, "reranker");

        var topK = ParseInt(flags.GetValueOrDefault("--top-k"));
        var candidates = ParseInt(flags.GetValueOrDefault("--candidates"));
        var minRerankScore = ParseDouble(flags.GetValueOrDefault("--min-rerank-score"));
        var strictCorpus = flags.ContainsKey("--strict-corpus");

        var runOptions = new RecallRunOptions(
            corpusDir, embedding, reranker, cacheDir, noCache, savePath, baselinePath, topK, candidates, minRerankScore, strictCorpus);
        return await RecallRunner.RunAsync(runOptions, output, error, cancellationToken);
    }

    /// <summary>
    /// Reads only the embedding and reranker model endpoints from an existing <c>config.yaml</c> without modifying the
    /// source directory or opening the memory repository configured in it.
    /// </summary>
    private static (ModelEndpoint? Embedding, ModelEndpoint? Reranker)? TryLoadModelsFromConfig(string path, TextWriter error)
    {
        var expanded = ExpandHome(path);
        var configFile = Directory.Exists(expanded) ? Path.Combine(expanded, "config.yaml") : expanded;
        if (!File.Exists(configFile))
        {
            error.WriteLine($"Config file not found: {configFile}");
            return null;
        }

        var tempHome = Path.Combine(Path.GetTempPath(), "tacit-bench-cfg-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempHome);
        try
        {
            File.Copy(configFile, Path.Combine(tempHome, "config.yaml"));
            var env = TacitOptions.FromEnvironment();
            var options = env with { TacitHome = tempHome };
            var config = new Tacit(options).LoadConfigFile();
            return (ToEndpoint(config.Models.Embedding), ToEndpoint(config.Models.Reranker));
        }
        catch (Exception ex) when (ex is TacitException or IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"Failed to read models from {configFile}: {ex.Message}");
            return null;
        }
        finally
        {
            if (Directory.Exists(tempHome))
                Directory.Delete(tempHome, recursive: true);
        }
    }

    private static ModelEndpoint? ToEndpoint(ModelConfig? model) =>
        model is null ? null : new ModelEndpoint(model.Url, model.Model, model.ApiKey?.Value, model.Provider);

    private static string ExpandHome(string path)
    {
        if (path == "~")
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);
        return Path.GetFullPath(path);
    }

    private static ModelEndpoint? ApplyExplicitOverrides(ModelEndpoint? baseEndpoint, Dictionary<string, string?> flags, string role)
    {
        var urlText = flags.GetValueOrDefault($"--{role}-url");
        var model = flags.GetValueOrDefault($"--{role}-model");
        var apiKey = flags.GetValueOrDefault($"--{role}-key");
        var providerText = flags.GetValueOrDefault($"--{role}-provider");

        var url = !string.IsNullOrWhiteSpace(urlText) ? new Uri(urlText) : baseEndpoint?.Url;
        if (url is null)
            return null;

        var provider = !string.IsNullOrWhiteSpace(providerText)
            ? (string.Equals(providerText, "cloudflare", StringComparison.OrdinalIgnoreCase) ? ModelProvider.Cloudflare : ModelProvider.Generic)
            : baseEndpoint?.Provider ?? ModelProvider.Generic;

        return new ModelEndpoint(
            url,
            !string.IsNullOrWhiteSpace(model) ? model : baseEndpoint?.Model ?? $"benchmark-{role}",
            !string.IsNullOrWhiteSpace(apiKey) ? apiKey : baseEndpoint?.ApiKey,
            provider);
    }

    private static Dictionary<string, string?> ParseFlags(string[] args)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
                continue;
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                result[arg] = args[i + 1];
                i++;
            }
            else
            {
                result[arg] = null;
            }
        }
        return result;
    }

    private static int? ParseInt(string? value) =>
        value is not null && int.TryParse(value, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static double? ParseDouble(string? value) =>
        value is not null && double.TryParse(value, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static int Fail(TextWriter error, string message)
    {
        error.WriteLine(message);
        return 1;
    }

    private static void PrintUsage(TextWriter output)
    {
        output.WriteLine("Usage: TacitMemory.Benchmarks <recall|perf> [options]");
        output.WriteLine("  recall  Run retrieval recall evaluation via TraceRecallAsync");
        output.WriteLine("          --corpus <dir> --from-config <path> --baseline <json> --save <json>");
        output.WriteLine("          --embedding-url <url> --embedding-model <name> --embedding-key <key>");
        output.WriteLine("          --reranker-url <url> --reranker-model <name> --reranker-key <key> --reranker-provider <generic|cloudflare>");
        output.WriteLine("          --top-k <n> --candidates <n> --min-rerank-score <f> --cache-dir <dir> --no-cache --strict-corpus");
        output.WriteLine("  perf    Run scale and latency benchmarks across synthetic corpora");
        output.WriteLine("          --scales <500,2000,10000> --iterations <30> --warmup <5> --dimension <1024> --save <json>");
    }
}
