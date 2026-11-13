using TacitMemory.Core.Decisions;
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
        Retrieval = new RetrievalFile(ConfigDefaults.Candidates, ConfigDefaults.TopK, RecallTool.DefaultMinRerankScore),
        Backup = new BackupFile(ConfigDefaults.Keep),
    };

    public static ThresholdFile File(Threshold threshold) => new(threshold.Certain, threshold.Unlikely);

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
