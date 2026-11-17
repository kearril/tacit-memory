using TacitMemory.Core.Localization;

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
            ConfigKeys.MaxConcurrency.Default, ConfigKeys.HintItemLength.Default, ConfigKeys.HintResidentLength.Default),
        Decision = new DecisionFile(
            File(ConfigKeys.SamePreference), File(ConfigKeys.SamePage), File(ConfigKeys.SameTopic), File(ConfigKeys.RuleConflict)),
        Retrieval = new RetrievalFile(ConfigKeys.Candidates.Default, ConfigKeys.TopK.Default, ConfigKeys.MinRerankScore.Default),
        Backup = new BackupFile(ConfigKeys.Keep.Default),
    };

    private static ThresholdFile File(ThresholdKeys keys) => new(keys.Certain.Default, keys.Unlikely.Default);

    /// <summary>
    /// Where each secret of <paramref name="config"/> comes from: its variable when set, else the file; a secret whose model
    /// or cloud backup is not configured follows its variable alone.
    /// </summary>
    public static IReadOnlyList<SecretState> SecretStates(TacitConfig config, TacitOptions options)
    {
        var file = ConfigFile.From(config);
        return
        [
            .. ConfigKeys.Secrets.Select(secret => new SecretState(
                secret.Path,
                FromEnvironment(options, secret.SecretVariable!) is not null ? SecretSource.Environment
                    : file.ValueAt(secret) is not null ? SecretSource.ConfigFile
                    : null,
                secret.SecretVariable!)),
        ];
    }

    /// <summary>The value a secret takes: its variable's, else the file's; <c>null</c> when neither holds it.</summary>
    /// <exception cref="TacitException"><paramref name="path"/> is not a secret (<see cref="TacitError.SecretUnknown"/>).</exception>
    public static string? SecretValue(TacitConfig config, TacitOptions options, string path)
    {
        var secret = ConfigKeys.Secrets.FirstOrDefault(secret => secret.Path == path)
            ?? throw new TacitException(TacitError.SecretUnknown, ManagementApiMessages.SecretUnknown.Format(config.Language, path));
        return FromEnvironment(options, secret.SecretVariable!) ?? ConfigFile.From(config).ValueAt(secret);
    }

    private static string? FromEnvironment(TacitOptions options, string variable) =>
        options.Secrets.TryGetValue(variable, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
}
