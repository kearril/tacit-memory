using TacitMemory.Core.Localization;

namespace TacitMemory.Core;

/// <summary>
/// <c>config.yaml</c> as the file holds it, before validation: one member per key, in the file's own shape, and
/// <c>null</c> for a key the file leaves out, which takes its default. Secrets are their plain text in the file, not the
/// value an environment variable may override them with. <see cref="Tacit.SaveConfig"/> writes it; reading it back with
/// <see cref="Tacit.LoadConfig"/> is what validates it.
/// </summary>
public sealed record ConfigFile
{
    public ServerFile Server { get; init; } = new();

    public MemoryFile Memory { get; init; } = new();

    /// <summary>The language code, such as <c>zh</c>; always written, since its default depends on the machine.</summary>
    public string? Language { get; init; }

    public ModelsFile Models { get; init; } = new();

    public LimitsFile Limits { get; init; } = new();

    public ConsolidationFile Consolidation { get; init; } = new();

    public DecisionFile Decision { get; init; } = new();

    public RetrievalFile Retrieval { get; init; } = new();

    public BackupFile Backup { get; init; } = new();

    /// <summary>
    /// The file form of a loaded config: every key at its loaded value, and each secret at the plain text the file holds,
    /// not its value from the environment. Written out, it loads to the same settings.
    /// </summary>
    internal static ConfigFile From(TacitConfig config)
    {
        static ModelFile? Model(ModelConfig? model) => model is null
            ? null
            : new ModelFile(model.Provider == ModelProvider.Cloudflare ? "cloudflare" : null, model.Url.OriginalString, model.Model, model.ApiKey?.InConfig);

        static ThresholdFile? Judgment(Threshold? threshold) =>
            threshold is null ? null : new ThresholdFile(threshold.Certain, threshold.Unlikely);

        var webdav = config.Backup.WebDav;
        return new ConfigFile
        {
            Server = new ServerFile(config.Port, config.ExtraAddress?.ToString()),
            Memory = new MemoryFile(config.MemoryPath),
            Language = config.Language.Code(),
            Models = new ModelsFile(Model(config.Models.Embedding), Model(config.Models.Reranker), Model(config.Models.Decision)),
            Limits = new LimitsFile(config.Limits.Brief, new PageLimitsFile(config.Limits.Page.MinAliases)),
            Consolidation = new ConsolidationFile(
                config.Consolidation.RemindAfterDays,
                config.Consolidation.RemindCandidates,
                config.Consolidation.BudgetSeconds,
                config.Consolidation.MaxConcurrency,
                config.Consolidation.HintItemLength,
                config.Consolidation.HintResidentLength),
            Decision = new DecisionFile(
                Judgment(config.Decision.SamePreference),
                Judgment(config.Decision.SamePage),
                Judgment(config.Decision.SameTopic),
                Judgment(config.Decision.RuleConflict)),
            Retrieval = new RetrievalFile(config.Retrieval.Candidates, config.Retrieval.TopK, config.Retrieval.MinRerankScore),
            Backup = new BackupFile(
                config.Backup.Keep,
                webdav is null ? null : new WebDavFile(webdav.Url.OriginalString, webdav.Username, webdav.Password?.InConfig)),
        };
    }
}

public sealed record ServerFile(int? Port = null, string? Host = null);

public sealed record MemoryFile(string? Path = null);

/// <summary>A <c>null</c> model is unconfigured and is not written.</summary>
public sealed record ModelsFile(ModelFile? Embedding = null, ModelFile? Reranker = null, ModelFile? Decision = null);

/// <param name="Provider"><c>null</c> for the generic protocol.</param>
public sealed record ModelFile(string? Provider, string? Url, string? Model, string? ApiKey);

public sealed record LimitsFile(int? Brief = null, PageLimitsFile? Page = null);

public sealed record PageLimitsFile(int? MinAliases = null);

public sealed record ConsolidationFile(
    int? RemindAfterDays = null,
    int? RemindCandidates = null,
    int? BudgetSeconds = null,
    int? MaxConcurrency = null,
    int? HintItemLength = null,
    int? HintResidentLength = null);

/// <summary>A <c>null</c> judgment takes its built-in thresholds.</summary>
public sealed record DecisionFile(
    ThresholdFile? SamePreference = null, ThresholdFile? SamePage = null, ThresholdFile? SameTopic = null, ThresholdFile? RuleConflict = null);

public sealed record ThresholdFile(double? Certain, double? Unlikely);

public sealed record RetrievalFile(int? Candidates = null, int? TopK = null, double? MinRerankScore = null);

/// <param name="WebDav"><c>null</c> when there is no cloud backup.</param>
public sealed record BackupFile(int? Keep = null, WebDavFile? WebDav = null);

public sealed record WebDavFile(string? Url, string? Username, string? Password);
