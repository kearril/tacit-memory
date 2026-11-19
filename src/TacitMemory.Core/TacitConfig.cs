using System.Net;

namespace TacitMemory.Core;

/// <summary>The validated <c>config.yaml</c> together with the prompt documents in <c>prompts/</c>.</summary>
public sealed record TacitConfig
{
    public required int Port { get; init; }

    /// <summary>
    /// The address <c>server.host</c> names, which the service listens on in addition to <c>127.0.0.1</c>; <c>null</c> when
    /// none is named, or when it names <c>127.0.0.1</c> itself, which means the same.
    /// </summary>
    public IPAddress? ExtraAddress { get; init; }

    /// <summary>Full path of the memory repository.</summary>
    public required string MemoryPath { get; init; }

    public required MemoryLanguage Language { get; init; }

    public required MachineConfig Machine { get; init; }

    public required ModelsConfig Models { get; init; }

    public required LimitsConfig Limits { get; init; }

    public required ConsolidationConfig Consolidation { get; init; }

    public required DecisionThresholds Decision { get; init; }

    public required RetrievalConfig Retrieval { get; init; }

    public required BackupConfig Backup { get; init; }

    public required SyncConfig Sync { get; init; }

    /// <summary>The prompt documents in <c>prompts/</c>.</summary>
    public required PromptDocuments Prompts { get; init; }

    /// <summary>Whether <c>config.yaml</c> holds a plaintext secret: a model's <c>apiKey</c> or <c>backup.webdav.password</c>.</summary>
    internal bool HoldsPlaintextSecret =>
        Models.Embedding?.ApiKey?.WrittenInConfig == true || Models.Reranker?.ApiKey?.WrittenInConfig == true
        || Models.Decision?.ApiKey?.WrittenInConfig == true || Backup.WebDav?.Password?.WrittenInConfig == true;
}

/// <summary>
/// The three auxiliary models: embedding, reranker and decision. A <c>null</c> member is an unconfigured model: the service
/// runs without it, never calls it, and never lists it as unavailable. Each configured model is set up the same way.
/// </summary>
public sealed record ModelsConfig(ModelConfig? Embedding, ModelConfig? Reranker, ModelConfig? Decision);

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

/// <param name="Brief">Characters a workstream brief may have; only its page index is shortened to fit.</param>
/// <param name="Page">The limits of detail pages.</param>
public sealed record LimitsConfig(int Brief, PageLimits Page);

/// <param name="MinAliases">Fewest aliases a detail page must have.</param>
public sealed record PageLimits(int MinAliases);

/// <param name="RemindCandidates">Candidates at which the consolidation reminder is due.</param>
/// <param name="HintEntryLength">Characters above which a persona entry or an activity gets a length hint in the consolidation report. Only a hint, never a refusal.</param>
/// <param name="HintResidentLength">Characters above which the resident block gets a length hint in the consolidation report. Only a hint, never a refusal.</param>
public sealed record ConsolidationConfig(int RemindAfterDays, int BudgetSeconds, int MaxConcurrency, int RemindCandidates, int HintEntryLength, int HintResidentLength);

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
/// <param name="File">
/// The name of the backup file in the remote directory, which the backup writes and the restore reads; <c>tacit-backup.zip</c>
/// when not configured.
/// </param>
public sealed record WebDavConfig(Uri Url, string? Username, SecretValue? Password, string File);

/// <summary>This machine's identity: its normalized name and optional single-line description.</summary>
/// <param name="Name">Configured machine name, or normalized from the host name when omitted.</param>
/// <param name="Description">Optional single-line description shown beside the name in the resident block.</param>
public sealed record MachineConfig(string Name, string? Description);

/// <summary>The sync of the memory repository with a git remote.</summary>
/// <param name="Remote">The git address of the remote, taken as git takes it; <c>null</c> when the sync is off.</param>
/// <param name="PullMinutes">Minutes between the timed pulls; 0 turns them off, leaving the syncs around writes.</param>
public sealed record SyncConfig(string? Remote, int PullMinutes);

/// <summary>Where a secret's value came from: its environment variable, or <c>config.yaml</c>.</summary>
public enum SecretSource
{
    Environment,
    ConfigFile,
}

/// <summary>
/// A secret as loaded. <see cref="Source"/> says where <see cref="Value"/> came from; <see cref="InConfig"/> is the
/// plain text <c>config.yaml</c> holds for it, even when the environment variable overrides it, or <c>null</c> when the
/// file holds none.
/// </summary>
public sealed record SecretValue(string Value, SecretSource Source, string? InConfig)
{
    /// <summary>Whether <c>config.yaml</c> holds the secret in plain text.</summary>
    public bool WrittenInConfig => InConfig is not null;

    /// <summary>Never includes the value: configuration records are printed and logged.</summary>
    public override string ToString() => $"secret from {Source}";
}

/// <summary>The recording criteria from <c>rules.md</c>: what is worth recording and what is not.</summary>
public sealed record RecordingCriteria(IReadOnlyList<string> WorthRecording, IReadOnlyList<string> NotRecorded);

/// <summary>
/// The prompt documents in <c>prompts/</c>: the recording criteria from <c>rules.md</c>, the text of
/// <c>instructions.md</c>, <c>resident.md</c> and <c>skill.md</c>. Each is <c>null</c> when its file is absent and the
/// built-in default applies; <c>skill.md</c> is also <c>null</c> when it is blank.
/// </summary>
public sealed record PromptDocuments(RecordingCriteria? Rules, string? Instructions, ResidentDocument? Resident, string? Skill);

/// <summary>
/// <c>resident.md</c>: the shell paragraph of the resident block and the guidance under each of its sections, in the
/// file's own words. An empty <see cref="Shell"/> leaves the paragraph out; a <c>null</c> guidance leaves the guidance
/// line out under that section.
/// </summary>
public sealed record ResidentDocument(
    string Shell,
    string? RedLines,
    string? WorkingRules,
    string? Preferences,
    string? Profile,
    string? SituationalRules,
    string? Workstreams);
