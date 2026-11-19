namespace TacitMemory.Core.Configuration;

/// <summary>
/// What each key of <c>config.yaml</c> accepts and defaults to, as the settings page shows it, and where each secret comes
/// from; all taken from the key table the loader also reads.
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
            ConfigKeys.MaxConcurrency.Default, ConfigKeys.HintEntryLength.Default, ConfigKeys.HintResidentLength.Default),
        Decision = new DecisionFile(
            File(ConfigKeys.SamePreference), File(ConfigKeys.SamePage), File(ConfigKeys.SameTopic), File(ConfigKeys.RuleConflict)),
        Retrieval = new RetrievalFile(ConfigKeys.Candidates.Default, ConfigKeys.TopK.Default, ConfigKeys.MinRerankScore.Default),
        Backup = new BackupFile(ConfigKeys.Keep.Default),
    };

    private static ThresholdFile File(ThresholdKeys keys) => new(keys.Certain.Default, keys.Unlikely.Default);

    /// <summary>
    /// Where each secret of <paramref name="config"/> comes from: its variable when set, else the file; a secret whose model
    /// or cloud backup is not configured follows its variable alone. The file's value goes with a secret that comes from it.
    /// </summary>
    public static IReadOnlyList<SecretState> SecretStates(TacitConfig config, TacitOptions options)
    {
        var file = ConfigFile.From(config);
        return
        [
            .. ConfigKeys.Secrets.Select(secret =>
            {
                if (FromEnvironment(options, secret.SecretVariable!) is not null)
                    return new SecretState(secret.Id, SecretSource.Environment, secret.SecretVariable!, null);
                var value = file.ValueAt(secret);
                return new SecretState(secret.Id, value is null ? null : SecretSource.ConfigFile, secret.SecretVariable!, value);
            }),
        ];
    }

    private static string? FromEnvironment(TacitOptions options, string variable) =>
        options.Secrets.TryGetValue(variable, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
}
