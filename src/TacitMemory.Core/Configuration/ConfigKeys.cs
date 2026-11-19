using System.Globalization;
using TacitMemory.Core.Decisions;
using TacitMemory.Core.Retrieval;

namespace TacitMemory.Core.Configuration;

/// <summary>
/// One key of <c>config.yaml</c>, declared once: its path, what it accepts, and how the service treats a change to it. The
/// loader, the writer, the settings page and the recall trace all take it from here.
/// </summary>
internal class ConfigEntry(ConfigKeyPath id, string path)
{
    /// <summary>The key as the contracts name it.</summary>
    public ConfigKeyPath Id { get; } = id;

    /// <summary>The key path, such as <c>retrieval.topK</c>; the wire name of <see cref="Id"/>.</summary>
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

    /// <summary>
    /// The key's value in a <see cref="ConfigFile"/> as <c>config.yaml</c> would write it, or as the service takes it when the
    /// file leaves it out. Every key that waits for a restart, is read-only or is a secret has it.
    /// </summary>
    public Func<ConfigFile, string?>? Value { get; init; }

    /// <summary>
    /// A secret's change: the <see cref="ConfigFile"/> with the secret set to a value, or removed with <c>null</c>. A secret
    /// whose model or cloud backup the file does not configure is left out with it.
    /// </summary>
    public Func<ConfigFile, string?, ConfigFile>? Rewrite { get; init; }

    /// <summary>The key as the settings page describes it.</summary>
    public ConfigKey Contract => new(Id, Min, Max, Choices, Restart, ReadOnly, SecretVariable is not null);
}

/// <summary>A whole number within a range, and the value it takes when the file leaves it out.</summary>
internal sealed class IntegerEntry : ConfigEntry
{
    public IntegerEntry(ConfigKeyPath id, string path, int fallback, int min, int max) : base(id, path)
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
    public ProbabilityEntry(ConfigKeyPath id, string path, double fallback) : base(id, path)
    {
        Default = fallback;
        Min = 0;
        Max = 1;
    }

    public double Default { get; }
}

/// <summary>The keys of one auxiliary model under <c>models</c>.</summary>
internal sealed class ModelKeys(
    string name,
    (ConfigKeyPath Provider, ConfigKeyPath Url, ConfigKeyPath Model, ConfigKeyPath ApiKey) ids,
    string secretVariable,
    Func<ModelsFile, ModelFile?> model,
    Func<ModelsFile, ModelFile, ModelsFile> replace)
{
    /// <summary>The model's name under <c>models</c>, such as <c>embedding</c>.</summary>
    public string Name { get; } = name;

    public string Path { get; } = $"models.{name}";

    public ConfigEntry Provider { get; } = new(ids.Provider, $"models.{name}.provider") { Choices = ["cloudflare"] };

    public ConfigEntry Url { get; } = new(ids.Url, $"models.{name}.url");

    public ConfigEntry Model { get; } = new(ids.Model, $"models.{name}.model");

    public ConfigEntry ApiKey { get; } = new(ids.ApiKey, $"models.{name}.apiKey")
    {
        SecretVariable = secretVariable,
        Value = file => model(file.Models)?.ApiKey,
        Rewrite = (file, value) => model(file.Models) is { } configured
            ? file with { Models = replace(file.Models, configured with { ApiKey = value }) }
            : file,
    };

    public IEnumerable<ConfigEntry> All => [Provider, Url, Model, ApiKey];
}

/// <summary>The pair of thresholds of one judgment under <c>decision</c>, with its built-in pair as their defaults.</summary>
internal sealed class ThresholdKeys(string name, (ConfigKeyPath Certain, ConfigKeyPath Unlikely) ids, Threshold builtIn)
{
    /// <summary>The judgment's name under <c>decision</c>, such as <c>samePage</c>.</summary>
    public string Name { get; } = name;

    public string Path { get; } = $"decision.{name}";

    public ProbabilityEntry Certain { get; } = new(ids.Certain, $"decision.{name}.certain", builtIn.Certain);

    public ProbabilityEntry Unlikely { get; } = new(ids.Unlikely, $"decision.{name}.unlikely", builtIn.Unlikely);

    public IEnumerable<ConfigEntry> All => [Certain, Unlikely];
}

/// <summary>Every key of <c>config.yaml</c>, in the order of the configuration reference.</summary>
internal static class ConfigKeys
{
    /// <summary>The largest count a count key accepts.</summary>
    private const int CountMax = 100_000;

    /// <summary>The address <c>server.host</c> may name that means no extra listener, and the one it means when absent.</summary>
    public const string LoopbackHost = "127.0.0.1";

    public static readonly IntegerEntry Port = new(ConfigKeyPath.ServerPort, "server.port", 18888, 1, 65535)
    {
        Restart = true,
        Value = file => Number(file.Server.Port ?? Port!.Default),
    };

    public static readonly ConfigEntry Host = new(ConfigKeyPath.ServerHost, "server.host") { Restart = true, Value = file => file.Server.Host ?? LoopbackHost };
    public static readonly ConfigEntry MemoryPath = new(ConfigKeyPath.MemoryPath, "memory.path") { Restart = true, ReadOnly = true, Value = file => file.Memory.Path };

    public static readonly ConfigEntry Language = new(ConfigKeyPath.Language, "language")
    {
        Choices = ["zh", "en"],
        Restart = true,
        ReadOnly = true,
        Value = file => file.Language,
    };

    public static readonly ModelKeys Embedding = new(
        "embedding",
        (ConfigKeyPath.ModelsEmbeddingProvider, ConfigKeyPath.ModelsEmbeddingUrl, ConfigKeyPath.ModelsEmbeddingModel, ConfigKeyPath.ModelsEmbeddingApiKey),
        SecretVariables.EmbeddingApiKey, models => models.Embedding, (models, model) => models with { Embedding = model });

    public static readonly ModelKeys Reranker = new(
        "reranker",
        (ConfigKeyPath.ModelsRerankerProvider, ConfigKeyPath.ModelsRerankerUrl, ConfigKeyPath.ModelsRerankerModel, ConfigKeyPath.ModelsRerankerApiKey),
        SecretVariables.RerankerApiKey, models => models.Reranker, (models, model) => models with { Reranker = model });

    public static readonly ModelKeys Decision = new(
        "decision",
        (ConfigKeyPath.ModelsDecisionProvider, ConfigKeyPath.ModelsDecisionUrl, ConfigKeyPath.ModelsDecisionModel, ConfigKeyPath.ModelsDecisionApiKey),
        SecretVariables.DecisionApiKey, models => models.Decision, (models, model) => models with { Decision = model });

    public static readonly IntegerEntry Brief = Count(ConfigKeyPath.LimitsBrief, "limits.brief", 3000);

    public static readonly IntegerEntry MinAliases = new(ConfigKeyPath.LimitsPageMinAliases, "limits.page.minAliases", 2, 1, CountMax)
    {
        Restart = true,
        Value = file => Number(file.Limits.Page?.MinAliases ?? MinAliases!.Default),
    };

    public static readonly IntegerEntry RemindAfterDays = Count(ConfigKeyPath.ConsolidationRemindAfterDays, "consolidation.remindAfterDays", 3);
    public static readonly IntegerEntry RemindCandidates = Count(ConfigKeyPath.ConsolidationRemindCandidates, "consolidation.remindCandidates", 2);
    public static readonly IntegerEntry BudgetSeconds = Count(ConfigKeyPath.ConsolidationBudgetSeconds, "consolidation.budgetSeconds", 20);
    public static readonly IntegerEntry MaxConcurrency = Count(ConfigKeyPath.ConsolidationMaxConcurrency, "consolidation.maxConcurrency", 5);
    public static readonly IntegerEntry HintEntryLength = Count(ConfigKeyPath.ConsolidationHintEntryLength, "consolidation.hintEntryLength", 200);
    public static readonly IntegerEntry HintResidentLength = Count(ConfigKeyPath.ConsolidationHintResidentLength, "consolidation.hintResidentLength", 6000);

    public static readonly ThresholdKeys SamePreference = new(
        "samePreference", (ConfigKeyPath.DecisionSamePreferenceCertain, ConfigKeyPath.DecisionSamePreferenceUnlikely), Judgment.SamePreference);
    public static readonly ThresholdKeys SamePage = new(
        "samePage", (ConfigKeyPath.DecisionSamePageCertain, ConfigKeyPath.DecisionSamePageUnlikely), Judgment.SamePage);
    public static readonly ThresholdKeys SameTopic = new(
        "sameTopic", (ConfigKeyPath.DecisionSameTopicCertain, ConfigKeyPath.DecisionSameTopicUnlikely), Judgment.SameTopic);
    public static readonly ThresholdKeys RuleConflict = new(
        "ruleConflict", (ConfigKeyPath.DecisionRuleConflictCertain, ConfigKeyPath.DecisionRuleConflictUnlikely), Judgment.RuleConflict);

    public static readonly IntegerEntry Candidates = Count(ConfigKeyPath.RetrievalCandidates, "retrieval.candidates", 30);
    public static readonly IntegerEntry TopK = Count(ConfigKeyPath.RetrievalTopK, "retrieval.topK", 5);
    public static readonly ProbabilityEntry MinRerankScore = new(ConfigKeyPath.RetrievalMinRerankScore, "retrieval.minRerankScore", RecallPipeline.DefaultMinRerankScore);

    public static readonly IntegerEntry Keep = new(ConfigKeyPath.BackupKeep, "backup.keep", 7, 0, 1000);
    public static readonly ConfigEntry WebDavUrl = new(ConfigKeyPath.BackupWebDavUrl, "backup.webdav.url");
    public static readonly ConfigEntry WebDavUsername = new(ConfigKeyPath.BackupWebDavUsername, "backup.webdav.username");
    public static readonly ConfigEntry WebDavPassword = new(ConfigKeyPath.BackupWebDavPassword, "backup.webdav.password")
    {
        SecretVariable = SecretVariables.WebDavPassword,
        Value = file => file.Backup.WebDav?.Password,
        Rewrite = (file, value) => file.Backup.WebDav is { } webdav
            ? file with { Backup = file.Backup with { WebDav = webdav with { Password = value } } }
            : file,
    };

    /// <summary>Every key, in the order of the configuration reference.</summary>
    public static readonly IReadOnlyList<ConfigEntry> All =
    [
        Port, Host, MemoryPath, Language,
        .. Embedding.All, .. Reranker.All, .. Decision.All,
        Brief, MinAliases,
        RemindAfterDays, RemindCandidates, BudgetSeconds, MaxConcurrency, HintEntryLength, HintResidentLength,
        .. SamePreference.All, .. SamePage.All, .. SameTopic.All, .. RuleConflict.All,
        Candidates, TopK, MinRerankScore,
        Keep, WebDavUrl, WebDavUsername, WebDavPassword,
    ];

    private static readonly Dictionary<ConfigKeyPath, ConfigEntry> ById = All.ToDictionary(entry => entry.Id);

    /// <summary>The key a contract names.</summary>
    public static ConfigEntry Of(ConfigKeyPath id) => ById[id];

    /// <summary>The secrets, in the order of the configuration reference.</summary>
    public static readonly IReadOnlyList<ConfigEntry> Secrets = [.. All.Where(entry => entry.SecretVariable is not null)];

    /// <summary>The keys that wait for a restart, in the order of the configuration reference.</summary>
    public static readonly IReadOnlyList<ConfigEntry> Restarting = [.. All.Where(entry => entry.Restart)];

    /// <summary>The keys the settings page does not let change, in the order of the configuration reference.</summary>
    public static readonly IReadOnlyList<ConfigEntry> ReadOnly = [.. All.Where(entry => entry.ReadOnly)];

    /// <summary>Where the memory repository is when <c>memory.path</c> is not given: <c>memory</c> under <c>TACIT_HOME</c>.</summary>
    public static string DefaultMemoryPath(string tacitHome) => System.IO.Path.Combine(tacitHome, "memory");

    private static IntegerEntry Count(ConfigKeyPath id, string path, int fallback) => new(id, path, fallback, 1, CountMax);

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
