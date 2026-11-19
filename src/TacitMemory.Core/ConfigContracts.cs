using System.Globalization;
using System.Text.Json.Serialization;
using TacitMemory.Core.Configuration;
using TacitMemory.Core.Localization;

namespace TacitMemory.Core;

/// <summary>
/// <c>config.yaml</c> for the settings page: the settings as the file holds them, as the service runs them and at their
/// defaults, each in the file's shape, with what each key accepts. Secrets are left out of the three and given apart in
/// <see cref="Secrets"/>. <see cref="Hash"/> names this content of the file; a save based on it is refused once the file
/// has changed.
/// </summary>
/// <param name="File">The settings as <c>config.yaml</c> holds them now.</param>
/// <param name="Running">The settings the service runs with: they differ from <see cref="File"/> where a setting waits for a restart, or where the file was edited by hand since the service last loaded it.</param>
/// <param name="Defaults">The value each key takes when the file leaves it out; a key with no default is <c>null</c>.</param>
/// <param name="Secrets">Where each secret's value comes from, as the file and the environment give it now.</param>
public sealed record ConfigView(
    string Hash, ConfigFile File, ConfigFile Running, ConfigFile Defaults, IReadOnlyList<ConfigKey> Keys, IReadOnlyList<SecretState> Secrets);

/// <summary>
/// Where one secret's value comes from, by its key path: its environment variable, which wins over the file, or
/// <c>config.yaml</c>; <c>null</c> when neither holds it.
/// </summary>
/// <param name="Value">The value <c>config.yaml</c> holds, given only when the secret comes from the file; a value from the environment never leaves the service.</param>
public sealed record SecretState(ConfigKeyPath Path, SecretSource? Source, string Variable, string? Value);

/// <summary>The path of a key of <c>config.yaml</c>, written as the file nests it, such as <c>retrieval.topK</c>.</summary>
public enum ConfigKeyPath
{
    [JsonStringEnumMemberName("server.port")]
    ServerPort,

    [JsonStringEnumMemberName("server.host")]
    ServerHost,

    [JsonStringEnumMemberName("memory.path")]
    MemoryPath,

    [JsonStringEnumMemberName("language")]
    Language,

    [JsonStringEnumMemberName("models.embedding.provider")]
    ModelsEmbeddingProvider,

    [JsonStringEnumMemberName("models.embedding.url")]
    ModelsEmbeddingUrl,

    [JsonStringEnumMemberName("models.embedding.model")]
    ModelsEmbeddingModel,

    [JsonStringEnumMemberName("models.embedding.apiKey")]
    ModelsEmbeddingApiKey,

    [JsonStringEnumMemberName("models.reranker.provider")]
    ModelsRerankerProvider,

    [JsonStringEnumMemberName("models.reranker.url")]
    ModelsRerankerUrl,

    [JsonStringEnumMemberName("models.reranker.model")]
    ModelsRerankerModel,

    [JsonStringEnumMemberName("models.reranker.apiKey")]
    ModelsRerankerApiKey,

    [JsonStringEnumMemberName("models.decision.provider")]
    ModelsDecisionProvider,

    [JsonStringEnumMemberName("models.decision.url")]
    ModelsDecisionUrl,

    [JsonStringEnumMemberName("models.decision.model")]
    ModelsDecisionModel,

    [JsonStringEnumMemberName("models.decision.apiKey")]
    ModelsDecisionApiKey,

    [JsonStringEnumMemberName("limits.brief")]
    LimitsBrief,

    [JsonStringEnumMemberName("limits.page.minAliases")]
    LimitsPageMinAliases,

    [JsonStringEnumMemberName("consolidation.remindAfterDays")]
    ConsolidationRemindAfterDays,

    [JsonStringEnumMemberName("consolidation.remindCandidates")]
    ConsolidationRemindCandidates,

    [JsonStringEnumMemberName("consolidation.budgetSeconds")]
    ConsolidationBudgetSeconds,

    [JsonStringEnumMemberName("consolidation.maxConcurrency")]
    ConsolidationMaxConcurrency,

    [JsonStringEnumMemberName("consolidation.hintEntryLength")]
    ConsolidationHintEntryLength,

    [JsonStringEnumMemberName("consolidation.hintResidentLength")]
    ConsolidationHintResidentLength,

    [JsonStringEnumMemberName("decision.samePreference.certain")]
    DecisionSamePreferenceCertain,

    [JsonStringEnumMemberName("decision.samePreference.unlikely")]
    DecisionSamePreferenceUnlikely,

    [JsonStringEnumMemberName("decision.samePage.certain")]
    DecisionSamePageCertain,

    [JsonStringEnumMemberName("decision.samePage.unlikely")]
    DecisionSamePageUnlikely,

    [JsonStringEnumMemberName("decision.sameTopic.certain")]
    DecisionSameTopicCertain,

    [JsonStringEnumMemberName("decision.sameTopic.unlikely")]
    DecisionSameTopicUnlikely,

    [JsonStringEnumMemberName("decision.ruleConflict.certain")]
    DecisionRuleConflictCertain,

    [JsonStringEnumMemberName("decision.ruleConflict.unlikely")]
    DecisionRuleConflictUnlikely,

    [JsonStringEnumMemberName("retrieval.candidates")]
    RetrievalCandidates,

    [JsonStringEnumMemberName("retrieval.topK")]
    RetrievalTopK,

    [JsonStringEnumMemberName("retrieval.minRerankScore")]
    RetrievalMinRerankScore,

    [JsonStringEnumMemberName("backup.keep")]
    BackupKeep,

    [JsonStringEnumMemberName("backup.webdav.url")]
    BackupWebDavUrl,

    [JsonStringEnumMemberName("backup.webdav.username")]
    BackupWebDavUsername,

    [JsonStringEnumMemberName("backup.webdav.password")]
    BackupWebDavPassword,
}

/// <summary>What one key of <c>config.yaml</c> accepts, by its key path, such as <c>retrieval.topK</c>.</summary>
/// <param name="Min">The smallest number the key accepts; <c>null</c> for a key that is not a number.</param>
/// <param name="Max">The largest number the key accepts; <c>null</c> for a key that is not a number.</param>
/// <param name="Choices">The values the key accepts when they are fixed; <c>null</c> otherwise.</param>
/// <param name="Restart">Whether a saved change waits for the service to restart instead of taking effect at once.</param>
/// <param name="ReadOnly">Whether the settings page shows the key without letting it change.</param>
/// <param name="Secret">Whether the key is a secret: it is left out of the settings, given apart with where it comes from, and changed apart from them.</param>
public sealed record ConfigKey(
    ConfigKeyPath Path, double? Min, double? Max, IReadOnlyList<string>? Choices, bool Restart, bool ReadOnly, bool Secret);

/// <summary>The result of a saved config: the hash of the file as it was written.</summary>
/// <param name="Restart">The settings saved with a value the service will take only when it restarts.</param>
public sealed record ConfigSaved(string Hash, IReadOnlyList<RestartChange> Restart);

/// <summary>
/// A setting that waits for a restart, by its key path, with the value the service runs and the value the file holds, both
/// as <c>config.yaml</c> would write them.
/// </summary>
public sealed record RestartChange(ConfigKeyPath Path, string Running, string Saved)
{
    /// <summary>The settings that wait for a restart and differ between <paramref name="running"/> and <paramref name="saved"/>, in a fixed order.</summary>
    internal static IReadOnlyList<RestartChange> Between(TacitConfig running, TacitConfig saved)
    {
        var (before, after) = (ConfigFile.From(running), ConfigFile.From(saved));
        return
        [
            .. ConfigKeys.Restarting
                .Select(key => (key.Id, Running: before.ValueAt(key) ?? "", Saved: after.ValueAt(key) ?? ""))
                .Where(item => item.Running != item.Saved)
                .Select(item => new RestartChange(item.Id, item.Running, item.Saved)),
        ];
    }
}

/// <summary>One reason a config does not validate: the key path at fault and the message, in the memory language.</summary>
public sealed record ConfigProblem(string Path, string Message);
