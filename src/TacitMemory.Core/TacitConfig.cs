namespace TacitMemory.Core;

/// <summary>The validated <c>config.yaml</c> together with <c>rules.md</c>.</summary>
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

    /// <summary>The contents of <c>rules.md</c>.</summary>
    public required RecordingCriteria Rules { get; init; }
}

public sealed record ModelsConfig(ModelEndpoint Embedding, ModelEndpoint Reranker, DecisionModelEndpoint Decision);

public sealed record ModelEndpoint(Uri Endpoint, string Model);

/// <summary>The decision model; exactly one of <see cref="ApiKey"/> and <see cref="ApiKeyEnv"/> is set.</summary>
public sealed record DecisionModelEndpoint(Uri Endpoint, string Model, string? ApiKey, string? ApiKeyEnv);

public sealed record LimitsConfig(PersonaLimits Persona, int ItemLength, int Mainline, PageLimits Page);

public sealed record PersonaLimits(int RedLines, int WorkingRules, int Preferences, int Profile);

public sealed record PageLimits(int TitleLength, int AliasLength, int MinAliases);

public sealed record CheckupConfig(int RemindAfterDays, int MainlineStaleDays);

/// <summary>Configured thresholds per judgment; <c>null</c> means the built-in default.</summary>
public sealed record DecisionThresholds(
    Threshold? SamePreference, Threshold? SamePage, Threshold? SameActivity, Threshold? RuleConflict, Threshold? Credential);

/// <summary>At or above <see cref="Certain"/> a judgment is taken as yes, at or below <see cref="Unlikely"/> as no.</summary>
public sealed record Threshold(double Certain, double Unlikely);

/// <param name="MinRerankScore"><c>null</c> means the built-in default.</param>
public sealed record RetrievalConfig(int Candidates, int TopK, double? MinRerankScore);

/// <param name="Keep">Automatic backups kept; 0 turns them off.</param>
public sealed record BackupConfig(int Keep, string? Remote);

/// <summary>The recording criteria from <c>rules.md</c>: what is worth recording and what is not.</summary>
public sealed record RecordingCriteria(IReadOnlyList<string> WorthRecording, IReadOnlyList<string> NotRecorded);
