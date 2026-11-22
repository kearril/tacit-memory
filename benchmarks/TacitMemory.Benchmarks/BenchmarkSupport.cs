using TacitMemory.Core;

namespace TacitMemory.Benchmarks;

/// <summary>Resolved HTTP endpoint configuration for an auxiliary model used during a benchmark run.</summary>
internal sealed record ModelEndpoint(Uri Url, string Model, string? ApiKey, ModelProvider Provider);

/// <summary>Default workspace paths for the benchmark corpus, model cache and output snapshots.</summary>
internal static class BenchmarkPaths
{
    public static string BenchmarksRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "benchmarks", "corpus");
            if (Directory.Exists(candidate))
                return Path.Combine(dir.FullName, "benchmarks");
            dir = dir.Parent;
        }
        return Path.Combine(Directory.GetCurrentDirectory(), "benchmarks");
    }

    public static string DefaultCorpusDir() => Path.Combine(BenchmarksRoot(), "corpus");

    public static string DefaultCacheDir() => Path.Combine(BenchmarksRoot(), ".cache");

    public static string DefaultResultsDir() => Path.Combine(BenchmarksRoot(), "results");

    public static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup of throwaway temp directories.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup of throwaway temp directories.
        }
    }
}
