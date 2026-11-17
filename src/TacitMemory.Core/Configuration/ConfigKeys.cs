using TacitMemory.Core.Decisions;
using TacitMemory.Core.Retrieval;

namespace TacitMemory.Core.Configuration;

/// <summary>
/// One key of <c>config.yaml</c>, declared once: its path, what it accepts, and how the service treats a change to it. The
/// loader, the writer, the settings page and the recall trace all take it from here.
/// </summary>
internal class ConfigEntry(string path)
{
    /// <summary>The key path, such as <c>retrieval.topK</c>.</summary>
    public string Path { get; } = path;

    /// <summary>The section path the key sits in, such as <c>retrieval</c>; empty for a key at the top.</summary>
    public string Section => Path.Contains('.') ? Path[..Path.LastIndexOf('.')] : "";

    /// <summary>The key's own name within its section, such as <c>topK</c>.</summary>
    public string Key => Path[(Path.LastIndexOf('.') + 1)..];

    /// <summary>The smallest number the key accepts; <c>null</c> for a key that is not a number.</summary>
    public double? Min { get; init; }

    /// <summary>The largest number the key accepts; <c>null</c> for a key that is not a number.</summary>
    public double? Max { get; init; }

    /// <summary>The values the key accepts when they are fixed; <c>null</c> otherwise.</summary>
    public IReadOnlyList<string>? Choices { get; init; }

    /// <summary>Whether a saved change waits for the service to restart instead of taking effect at once.</summary>
    public bool Restart { get; init; }

    /// <summary>Whether the settings page shows the key without letting it change.</summary>
    public bool ReadOnly { get; init; }

    /// <summary>The environment variable whose value wins over the file's; <c>null</c> for a key that is not a secret.</summary>
    public string? SecretVariable { get; init; }

    /// <summary>The key as the settings page describes it.</summary>
    public ConfigKey Contract => new(Path, Min, Max, Choices, Restart, ReadOnly);
}

/// <summary>A whole number within a range, and the value it takes when the file leaves it out.</summary>
internal sealed class IntegerEntry : ConfigEntry
{
    public IntegerEntry(string path, int fallback, int min, int max) : base(path)
    {
        Default = fallback;
        Min = min;
        Max = max;
    }

    public int Default { get; }

    public int Lowest => (int)Min!.Value;

    public int Highest => (int)Max!.Value;
}

/// <summary>A number from 0 to 1, and the value it takes when the file leaves it out.</summary>
internal sealed class ProbabilityEntry : ConfigEntry
{
    public ProbabilityEntry(string path, double fallback) : base(path)
    {
        Default = fallback;
        Min = 0;
        Max = 1;
    }

    public double Default { get; }
}

/// <summary>The keys of one auxiliary model under <c>models</c>.</summary>
internal sealed class ModelKeys(string name, string secretVariable)
{
    /// <summary>The model's name under <c>models</c>, such as <c>embedding</c>.</summary>
    public string Name { get; } = name;

    public string Path { get; } = $"models.{name}";

    public ConfigEntry Provider { get; } = new($"models.{name}.provider") { Choices = ["cloudflare"] };

    public ConfigEntry Url { get; } = new($"models.{name}.url");

    public ConfigEntry Model { get; } = new($"models.{name}.model");

    public ConfigEntry ApiKey { get; } = new($"models.{name}.apiKey") { SecretVariable = secretVariable };

    public IEnumerable<ConfigEntry> All => [Provider, Url, Model, ApiKey];
}

/// <summary>The pair of thresholds of one judgment under <c>decision</c>, with its built-in pair as their defaults.</summary>
internal sealed class ThresholdKeys(string name, Threshold builtIn)
{
    /// <summary>The judgment's name under <c>decision</c>, such as <c>samePage</c>.</summary>
    public string Name { get; } = name;

    public string Path { get; } = $"decision.{name}";

    public ProbabilityEntry Certain { get; } = new($"decision.{name}.certain", builtIn.Certain);

    public ProbabilityEntry Unlikely { get; } = new($"decision.{name}.unlikely", builtIn.Unlikely);

    public IEnumerable<ConfigEntry> All => [Certain, Unlikely];
}

/// <summary>Every key of <c>config.yaml</c>, in the order of the configuration reference.</summary>
internal static class ConfigKeys
{
    /// <summary>The largest count a count key accepts.</summary>
    private const int CountMax = 100_000;

    /// <summary>The address <c>server.host</c> may name that means no extra listener, and the one it means when absent.</summary>
    public const string LoopbackHost = "127.0.0.1";

    public static readonly IntegerEntry Port = new("server.port", 18888, 1, 65535) { Restart = true };
    public static readonly ConfigEntry Host = new("server.host") { Restart = true };
    public static readonly ConfigEntry MemoryPath = new("memory.path") { Restart = true, ReadOnly = true };
    public static readonly ConfigEntry Language = new("language") { Choices = ["zh", "en"], Restart = true, ReadOnly = true };

    public static readonly ModelKeys Embedding = new("embedding", SecretVariables.EmbeddingApiKey);
    public static readonly ModelKeys Reranker = new("reranker", SecretVariables.RerankerApiKey);
    public static readonly ModelKeys Decision = new("decision", SecretVariables.DecisionApiKey);

    public static readonly IntegerEntry Brief = Count("limits.brief", 3000);
    public static readonly IntegerEntry MinAliases = new("limits.page.minAliases", 2, 1, CountMax) { Restart = true };

    public static readonly IntegerEntry RemindAfterDays = Count("consolidation.remindAfterDays", 7);
    public static readonly IntegerEntry RemindCandidates = Count("consolidation.remindCandidates", 5);
    public static readonly IntegerEntry BudgetSeconds = Count("consolidation.budgetSeconds", 20);
    public static readonly IntegerEntry MaxConcurrency = Count("consolidation.maxConcurrency", 5);
    public static readonly IntegerEntry HintItemLength = Count("consolidation.hintItemLength", 200);
    public static readonly IntegerEntry HintResidentLength = Count("consolidation.hintResidentLength", 6000);

    public static readonly ThresholdKeys SamePreference = new("samePreference", Judgment.SamePreference);
    public static readonly ThresholdKeys SamePage = new("samePage", Judgment.SamePage);
    public static readonly ThresholdKeys SameTopic = new("sameTopic", Judgment.SameTopic);
    public static readonly ThresholdKeys RuleConflict = new("ruleConflict", Judgment.RuleConflict);

    public static readonly IntegerEntry Candidates = Count("retrieval.candidates", 30);
    public static readonly IntegerEntry TopK = Count("retrieval.topK", 5);
    public static readonly ProbabilityEntry MinRerankScore = new("retrieval.minRerankScore", RecallPipeline.DefaultMinRerankScore);

    public static readonly IntegerEntry Keep = new("backup.keep", 7, 0, 1000);
    public static readonly ConfigEntry WebDavUrl = new("backup.webdav.url");
    public static readonly ConfigEntry WebDavUsername = new("backup.webdav.username");
    public static readonly ConfigEntry WebDavPassword = new("backup.webdav.password") { SecretVariable = SecretVariables.WebDavPassword };

    /// <summary>Every key, in the order of the configuration reference.</summary>
    public static readonly IReadOnlyList<ConfigEntry> All =
    [
        Port, Host, MemoryPath, Language,
        .. Embedding.All, .. Reranker.All, .. Decision.All,
        Brief, MinAliases,
        RemindAfterDays, RemindCandidates, BudgetSeconds, MaxConcurrency, HintItemLength, HintResidentLength,
        .. SamePreference.All, .. SamePage.All, .. SameTopic.All, .. RuleConflict.All,
        Candidates, TopK, MinRerankScore,
        Keep, WebDavUrl, WebDavUsername, WebDavPassword,
    ];

    /// <summary>Where the memory repository is when <c>memory.path</c> is not given: <c>memory</c> under <c>TACIT_HOME</c>.</summary>
    public static string DefaultMemoryPath(string tacitHome) => System.IO.Path.Combine(tacitHome, "memory");

    private static IntegerEntry Count(string path, int fallback) => new(path, fallback, 1, CountMax);
}
