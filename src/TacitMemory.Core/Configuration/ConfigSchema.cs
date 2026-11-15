using TacitMemory.Core.Decisions;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Retrieval;

namespace TacitMemory.Core.Configuration;

/// <summary>
/// What each key of <c>config.yaml</c> accepts and defaults to, as the settings page shows it. The limits are the ones the
/// loader checks; the loader stays the judge, and this only describes it.
/// </summary>
internal static class ConfigSchema
{
    /// <summary>The bounds of a count, as the loader reads one.</summary>
    private const int CountMax = 100_000;

    public static IReadOnlyList<ConfigKey> Keys { get; } = Build();

    /// <summary>The defaults in the file's shape: built-in thresholds included, models and the cloud backup absent.</summary>
    public static ConfigFile Defaults(TacitOptions options) => new()
    {
        Server = new ServerFile(ConfigDefaults.Port),
        Memory = new MemoryFile(ConfigDefaults.MemoryPath(options.TacitHome)),
        Limits = new LimitsFile(ConfigDefaults.Brief, new PageLimitsFile(ConfigDefaults.MinAliases)),
        Consolidation = new ConsolidationFile(
            ConfigDefaults.RemindAfterDays, ConfigDefaults.RemindCandidates, ConfigDefaults.BudgetSeconds,
            ConfigDefaults.MaxConcurrency, ConfigDefaults.HintItemLength, ConfigDefaults.HintResidentLength),
        Decision = new DecisionFile(
            File(Judgment.SamePreference), File(Judgment.SamePage), File(Judgment.SameTopic), File(Judgment.RuleConflict)),
        Retrieval = new RetrievalFile(ConfigDefaults.Candidates, ConfigDefaults.TopK, RecallPipeline.DefaultMinRerankScore),
        Backup = new BackupFile(ConfigDefaults.Keep),
    };

    public static ThresholdFile File(Threshold threshold) => new(threshold.Certain, threshold.Unlikely);

    /// <summary>The secrets, by key path, each with the environment variable that wins over the file.</summary>
    public static readonly IReadOnlyList<(string Path, string Variable)> Secrets =
    [
        ("models.embedding.apiKey", SecretVariables.EmbeddingApiKey),
        ("models.reranker.apiKey", SecretVariables.RerankerApiKey),
        ("models.decision.apiKey", SecretVariables.DecisionApiKey),
        ("backup.webdav.password", SecretVariables.WebDavPassword),
    ];

    /// <summary>Where each secret of <paramref name="config"/> comes from; a secret whose model or cloud backup is not configured follows its variable alone.</summary>
    public static IReadOnlyList<SecretState> SecretStates(TacitConfig config, TacitOptions options) =>
    [
        .. Secrets.Select(secret => new SecretState(
            secret.Path,
            SecretOf(config, secret.Path)?.Source ?? (FromEnvironment(options, secret.Variable) is null ? null : SecretSource.Environment),
            secret.Variable)),
    ];

    /// <summary>The value a secret takes: its variable's, else the file's; <c>null</c> when neither holds it.</summary>
    /// <exception cref="TacitException"><paramref name="path"/> is not a secret (<see cref="TacitError.SecretUnknown"/>).</exception>
    public static string? SecretValue(TacitConfig config, TacitOptions options, string path)
    {
        var secret = Secrets.FirstOrDefault(secret => secret.Path == path);
        if (secret.Path is null)
            throw new TacitException(TacitError.SecretUnknown, ManagementApiMessages.SecretUnknown.Format(config.Language, path));
        return SecretOf(config, path)?.Value ?? FromEnvironment(options, secret.Variable);
    }

    private static SecretValue? SecretOf(TacitConfig config, string path) => path switch
    {
        "models.embedding.apiKey" => config.Models.Embedding?.ApiKey,
        "models.reranker.apiKey" => config.Models.Reranker?.ApiKey,
        "models.decision.apiKey" => config.Models.Decision?.ApiKey,
        "backup.webdav.password" => config.Backup.WebDav?.Password,
        _ => null,
    };

    private static string? FromEnvironment(TacitOptions options, string variable) =>
        options.Secrets.TryGetValue(variable, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    private static List<ConfigKey> Build()
    {
        var keys = new List<ConfigKey>
        {
            new("server.port", 1, 65535, null, Restart: true, ReadOnly: false),
            new("server.host", null, null, null, Restart: true, ReadOnly: false),
            new("memory.path", null, null, null, Restart: true, ReadOnly: true),
            new("language", null, null, ["zh", "en"], Restart: true, ReadOnly: true),
        };
        foreach (var model in (string[])["embedding", "reranker", "decision"])
        {
            keys.Add(new($"models.{model}.provider", null, null, ["cloudflare"], false, false));
            keys.Add(Text($"models.{model}.url"));
            keys.Add(Text($"models.{model}.model"));
            keys.Add(Text($"models.{model}.apiKey"));
        }

        keys.Add(Count("limits.brief"));
        keys.Add(new("limits.page.minAliases", 1, CountMax, null, Restart: true, ReadOnly: false));
        foreach (var key in (string[])["remindAfterDays", "remindCandidates", "budgetSeconds", "maxConcurrency", "hintItemLength", "hintResidentLength"])
            keys.Add(Count($"consolidation.{key}"));
        foreach (var judgment in (string[])["samePreference", "samePage", "sameTopic", "ruleConflict"])
        {
            keys.Add(Probability($"decision.{judgment}.certain"));
            keys.Add(Probability($"decision.{judgment}.unlikely"));
        }

        keys.Add(Count("retrieval.candidates"));
        keys.Add(Count("retrieval.topK"));
        keys.Add(Probability("retrieval.minRerankScore"));
        keys.Add(new("backup.keep", 0, 1000, null, false, false));
        keys.Add(Text("backup.webdav.url"));
        keys.Add(Text("backup.webdav.username"));
        keys.Add(Text("backup.webdav.password"));
        return keys;
    }

    private static ConfigKey Text(string path) => new(path, null, null, null, false, false);

    private static ConfigKey Count(string path) => new(path, 1, CountMax, null, false, false);

    private static ConfigKey Probability(string path) => new(path, 0, 1, null, false, false);
}
