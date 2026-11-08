namespace TacitMemory.Core;

/// <summary>The validated <c>config.yaml</c> together with the prompt documents in <c>prompts/</c>.</summary>
public sealed record TacitConfig
{
    public required int Port { get; init; }

    /// <summary>Full path of the memory repository.</summary>
    public required string MemoryPath { get; init; }

    public required MemoryLanguage Language { get; init; }

    public required ModelsConfig Models { get; init; }

    public required LimitsConfig Limits { get; init; }

    /// <summary>Distinct days a persona candidate must appear on before it can be confirmed.</summary>
    public required int CandidateMinDays { get; init; }

    public required CheckupConfig Checkup { get; init; }

    public required DecisionThresholds Decision { get; init; }

    public required RetrievalConfig Retrieval { get; init; }

    public required BackupConfig Backup { get; init; }

    /// <summary>The prompt documents in <c>prompts/</c>.</summary>
    public required PromptDocuments Prompts { get; init; }

    /// <summary>Whether <c>config.yaml</c> holds a plaintext secret: a model's <c>apiKey</c> or <c>backup.webdav.password</c>.</summary>
    internal bool HoldsPlaintextSecret =>
        Models.Embedding.ApiKey?.WrittenInConfig == true || Models.Reranker.ApiKey?.WrittenInConfig == true
        || Models.Decision.ApiKey?.WrittenInConfig == true || Backup.WebDav?.Password?.WrittenInConfig == true;
}

/// <summary>The three auxiliary models: embedding, reranker and decision. Each is configured the same way.</summary>
public sealed record ModelsConfig(ModelConfig Embedding, ModelConfig Reranker, ModelConfig Decision);

/// <summary>
/// One auxiliary model. <see cref="Url"/> is the complete request address, sent as written; <see cref="ApiKey"/> is
/// <c>null</c> when neither the environment nor <c>config.yaml</c> holds a key, and then no authentication is sent.
/// <see cref="Provider"/> chooses the request format where a provider departs from the generic protocol.
/// </summary>
public sealed record ModelConfig(Uri Url, string Model, SecretValue? ApiKey, ModelProvider Provider);

/// <summary>The request format of an auxiliary model: the generic protocol of its kind, or the one of Cloudflare Workers AI.</summary>
public enum ModelProvider
{
    /// <summary>The generic protocol: OpenAI Embeddings, Cohere/Jina Rerank or System One, by model kind.</summary>
    Generic,

    /// <summary>Cloudflare Workers AI, where its format differs from the generic protocol.</summary>
    Cloudflare,
}

public sealed record LimitsConfig(PersonaLimits Persona, int ItemLength, int Mainline, PageLimits Page);

public sealed record PersonaLimits(int RedLines, int WorkingRules, int Preferences, int Profile);

public sealed record PageLimits(int TitleLength, int AliasLength, int MinAliases);

public sealed record CheckupConfig(int RemindAfterDays, int MainlineStaleDays, int BudgetSeconds, int MaxConcurrency);

/// <summary>Configured thresholds per judgment; <c>null</c> means the built-in default.</summary>
public sealed record DecisionThresholds(Threshold? SamePreference, Threshold? SamePage, Threshold? SameTopic, Threshold? RuleConflict);

/// <summary>At or above <see cref="Certain"/> a judgment is taken as yes, at or below <see cref="Unlikely"/> as no.</summary>
public sealed record Threshold(double Certain, double Unlikely);

/// <param name="MinRerankScore"><c>null</c> means the built-in default.</param>
public sealed record RetrievalConfig(int Candidates, int TopK, double? MinRerankScore);

/// <param name="Keep">Automatic backups kept; 0 turns them off.</param>
/// <param name="WebDav">The cloud backup target, or <c>null</c> when <c>backup.webdav</c> is not configured.</param>
public sealed record BackupConfig(int Keep, WebDavConfig? WebDav);

/// <summary>The WebDAV target of the cloud backup.</summary>
/// <param name="Url">The remote directory; always ends in <c>/</c>.</param>
/// <param name="Username"><c>null</c> when not configured: no authentication is sent.</param>
/// <param name="Password"><c>null</c> when neither the environment nor the config holds one.</param>
public sealed record WebDavConfig(Uri Url, string? Username, SecretValue? Password);

/// <summary>Where a secret's value came from: its environment variable, or <c>config.yaml</c>.</summary>
public enum SecretSource
{
    Environment,
    ConfigFile,
}

/// <summary>
/// A secret as loaded. <see cref="Source"/> says where <see cref="Value"/> came from; <see cref="WrittenInConfig"/> is
/// true when <c>config.yaml</c> also holds it in plain text, even when the environment variable overrides it.
/// </summary>
public sealed record SecretValue(string Value, SecretSource Source, bool WrittenInConfig)
{
    /// <summary>Never includes the value: configuration records are printed and logged.</summary>
    public override string ToString() => $"secret from {Source}";
}

/// <summary>The recording criteria from <c>rules.md</c>: what is worth recording and what is not.</summary>
public sealed record RecordingCriteria(IReadOnlyList<string> WorthRecording, IReadOnlyList<string> NotRecorded);

/// <summary>
/// The prompt documents in <c>prompts/</c>: the recording criteria from <c>rules.md</c>, the text of
/// <c>instructions.md</c>, <c>resident.md</c> and <c>skill.md</c>. The last three are <c>null</c> when their file is absent
/// and the built-in default applies; <c>skill.md</c> is also <c>null</c> when it is blank.
/// </summary>
public sealed record PromptDocuments(RecordingCriteria Rules, string? Instructions, ResidentDocument? Resident, string? Skill);

/// <summary>
/// <c>resident.md</c>: the shell paragraph of the resident block and the guidance under each of its four sections,
/// in the file's own words. An empty <see cref="Shell"/> leaves the paragraph out; a <c>null</c> guidance leaves
/// the guidance line out under that section.
/// </summary>
public sealed record ResidentDocument(string Shell, string? RedLines, string? WorkingRules, string? Preferences, string? Profile);
