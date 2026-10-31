using System.Diagnostics;
using System.Globalization;

namespace TacitMemory.Core.Tests;

/// <summary>A throwaway user home and <c>TACIT_HOME</c> under %TEMP%.</summary>
internal sealed class TestHome : IDisposable
{
    public TestHome()
    {
        Root = Path.Combine(Path.GetTempPath(), "tacit-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(UserHome);
    }

    public string Root { get; }

    public string UserHome => Path.Combine(Root, "home");

    public string TacitHome => Path.Combine(UserHome, ".tacit");

    /// <summary>Where <c>init</c> puts the memory repository unless told otherwise.</summary>
    public string DefaultMemory => Path.Combine(TacitHome, "memory");

    public string PathFor(string name) => Path.Combine(Root, name);

    /// <summary>The date the test clock shows.</summary>
    public static readonly DateOnly Today = new(2026, 11, 2);

    /// <summary>The clock of every <see cref="Tacit"/> made here; tests may move it forward.</summary>
    public FixedTime Clock { get; } = new(new DateTimeOffset(Today.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero));

    public Tacit Tacit(string uiCulture = "zh-CN") =>
        new(new TacitOptions
        {
            TacitHome = TacitHome,
            UserHome = UserHome,
            UiCulture = CultureInfo.GetCultureInfo(uiCulture),
            Time = Clock,
        });

    /// <summary>A <c>models</c> section that passes validation; nothing is listening at these addresses in tests.</summary>
    public const string Models = """
        models:
          embedding: { endpoint: 'http://127.0.0.1:38417/v1', model: qwen3-embedding-0.6b }
          reranker: { endpoint: 'http://127.0.0.1:38417/v1', model: qwen3-reranker-0.6b }
          decision: { endpoint: 'https://api.typesafe.ai/v1', model: jev-latest, apiKeyEnv: JEV_KEY }

        """;

    /// <summary>
    /// Runs <c>init</c> in Chinese and replaces the config with one that loads, with every model at
    /// <paramref name="models"/>, or down when none is given.
    /// </summary>
    public void InitReady(string extraConfig = "", ModelDouble? models = null)
    {
        Tacit().InitAsync(new InitRequest { Language = MemoryLanguage.Zh }).GetAwaiter().GetResult();
        File.WriteAllText(Path.Combine(TacitHome, "config.yaml"), ((models ?? ModelDouble.Down).ModelsYaml() + extraConfig).ReplaceLineEndings("\n"));
    }

    /// <summary>Runs <c>git</c> in <paramref name="directory"/> and returns its standard output.</summary>
    public static string Git(string directory, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in (string[])["-c", "core.quotepath=off", .. args])
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', args)} failed: {process.StandardError.ReadToEnd()}");
        return output;
    }

    public void Dispose()
    {
        try
        {
            // Git marks its object files read-only, which Directory.Delete refuses.
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Left for the system to clean up.
        }
    }
}

/// <summary>A clock that stands still unless moved, in UTC.</summary>
internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
