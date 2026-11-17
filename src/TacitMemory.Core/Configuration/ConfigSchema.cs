using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Configuration;

/// <summary>
/// What each key of <c>config.yaml</c> accepts and defaults to, as the settings page shows it. The limits are the ones the
/// loader checks; the loader stays the judge, and this only describes it.
/// </summary>
internal static class ConfigSchema
{
    public static IReadOnlyList<ConfigKey> Keys { get; } = [.. ConfigKeys.All.Select(entry => entry.Contract)];

    /// <summary>The defaults in the file's shape: built-in thresholds included, models and the cloud backup absent.</summary>
    public static ConfigFile Defaults(TacitOptions options) => new()
    {
        Server = new ServerFile(ConfigKeys.Port.Default),
        Memory = new MemoryFile(ConfigKeys.DefaultMemoryPath(options.TacitHome)),
        Limits = new LimitsFile(ConfigKeys.Brief.Default, new PageLimitsFile(ConfigKeys.MinAliases.Default)),
        Consolidation = new ConsolidationFile(
            ConfigKeys.RemindAfterDays.Default, ConfigKeys.RemindCandidates.Default, ConfigKeys.BudgetSeconds.Default,
            ConfigKeys.MaxConcurrency.Default, ConfigKeys.HintItemLength.Default, ConfigKeys.HintResidentLength.Default),
        Decision = new DecisionFile(
            File(ConfigKeys.SamePreference), File(ConfigKeys.SamePage), File(ConfigKeys.SameTopic), File(ConfigKeys.RuleConflict)),
        Retrieval = new RetrievalFile(ConfigKeys.Candidates.Default, ConfigKeys.TopK.Default, ConfigKeys.MinRerankScore.Default),
        Backup = new BackupFile(ConfigKeys.Keep.Default),
    };

    private static ThresholdFile File(ThresholdKeys keys) => new(keys.Certain.Default, keys.Unlikely.Default);

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
}
