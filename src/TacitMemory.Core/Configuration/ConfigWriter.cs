using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TacitMemory.Core.Decisions;
using TacitMemory.Core.Retrieval;

namespace TacitMemory.Core.Configuration;

/// <summary>
/// Writes <c>config.yaml</c> whole from a <see cref="ConfigFile"/>: keys in the order of the configuration reference,
/// no comments, and only the keys that differ from their default. A section with nothing left in it is left out.
/// </summary>
internal static partial class ConfigWriter
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static string Render(ConfigFile file, TacitOptions options)
    {
        var root = new Mapping();

        var server = root.Section("server");
        server.Add("port", Unless(file.Server.Port, ConfigDefaults.Port));
        server.Add("host", file.Server.Host is ConfigDefaults.Host ? null : Text(file.Server.Host));

        var memoryPath = file.Memory.Path;
        var defaultMemory = ConfigDefaults.MemoryPath(options.TacitHome);
        root.Section("memory").Add(
            "path", memoryPath is null || SamePath(HomePaths.Expand(memoryPath, options.UserHome), defaultMemory) ? null : Text(memoryPath));
        root.Add("language", Text(file.Language));

        var models = root.Section("models");
        AddModel(models, "embedding", file.Models.Embedding);
        AddModel(models, "reranker", file.Models.Reranker);
        AddModel(models, "decision", file.Models.Decision);

        var limits = root.Section("limits");
        limits.Add("brief", Unless(file.Limits.Brief, ConfigDefaults.Brief));
        limits.Section("page").Add("minAliases", Unless(file.Limits.Page?.MinAliases, ConfigDefaults.MinAliases));

        var consolidation = root.Section("consolidation");
        var c = file.Consolidation;
        consolidation.Add("remindAfterDays", Unless(c.RemindAfterDays, ConfigDefaults.RemindAfterDays));
        consolidation.Add("remindCandidates", Unless(c.RemindCandidates, ConfigDefaults.RemindCandidates));
        consolidation.Add("budgetSeconds", Unless(c.BudgetSeconds, ConfigDefaults.BudgetSeconds));
        consolidation.Add("maxConcurrency", Unless(c.MaxConcurrency, ConfigDefaults.MaxConcurrency));
        consolidation.Add("hintItemLength", Unless(c.HintItemLength, ConfigDefaults.HintItemLength));
        consolidation.Add("hintResidentLength", Unless(c.HintResidentLength, ConfigDefaults.HintResidentLength));

        var decision = root.Section("decision");
        AddThreshold(decision, "samePreference", file.Decision.SamePreference, Judgment.SamePreference);
        AddThreshold(decision, "samePage", file.Decision.SamePage, Judgment.SamePage);
        AddThreshold(decision, "sameTopic", file.Decision.SameTopic, Judgment.SameTopic);
        AddThreshold(decision, "ruleConflict", file.Decision.RuleConflict, Judgment.RuleConflict);

        var retrieval = root.Section("retrieval");
        retrieval.Add("candidates", Unless(file.Retrieval.Candidates, ConfigDefaults.Candidates));
        retrieval.Add("topK", Unless(file.Retrieval.TopK, ConfigDefaults.TopK));
        retrieval.Add(
            "minRerankScore",
            file.Retrieval.MinRerankScore == RecallTool.DefaultMinRerankScore ? null : Number(file.Retrieval.MinRerankScore));

        var backup = root.Section("backup");
        backup.Add("keep", Unless(file.Backup.Keep, ConfigDefaults.Keep));
        if (file.Backup.WebDav is { } webdav)
        {
            var target = backup.Section("webdav");
            target.Add("url", Required(webdav.Url));
            target.Add("username", Text(webdav.Username));
            target.Add("password", Text(webdav.Password));
        }

        var output = new StringBuilder();
        root.Render(output, 0);
        return output.ToString();
    }

    /// <summary>Replaces the file at <paramref name="path"/> with <paramref name="content"/>; a write that fails part way leaves the old file.</summary>
    public static void Replace(string path, string content)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, content, Utf8);
        File.Move(temporary, path, overwrite: true);
    }

    private static void AddModel(Mapping models, string key, ModelFile? model)
    {
        if (model is null)
            return;
        var section = models.Section(key);
        section.Add("provider", Text(model.Provider));
        section.Add("url", Required(model.Url));
        section.Add("model", Required(model.Model));
        section.Add("apiKey", Text(model.ApiKey));
    }

    /// <summary>
    /// A judgment's thresholds, unless they are its built-in ones; both are written whenever either is given, so a missing
    /// one is reported on load.
    /// </summary>
    private static void AddThreshold(Mapping decision, string key, ThresholdFile? threshold, Threshold builtIn)
    {
        if (threshold is null || threshold == ConfigSchema.File(builtIn))
            return;
        var section = decision.Section(key);
        section.Add("certain", Number(threshold.Certain) ?? "''");
        section.Add("unlikely", Number(threshold.Unlikely) ?? "''");
    }

    private static string? Unless(int? value, int fallback) =>
        value is null || value == fallback ? null : value.Value.ToString(CultureInfo.InvariantCulture);

    private static string? Number(double? value) => value?.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>A key that must appear once its section does: an absent value is written empty, so the load reports it.</summary>
    private static string Required(string? value) => Text(value) ?? "''";

    /// <summary>Text as a YAML scalar: plain when it cannot be read as anything else, single-quoted otherwise.</summary>
    private static string? Text(string? value)
    {
        if (value is null)
            return null;
        return PlainText().IsMatch(value) && !Reserved.Contains(value) ? value : "'" + value.Replace("'", "''") + "'";
    }

    /// <summary>Words YAML would read as a boolean or a null instead of text.</summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "true", "false", "yes", "no", "on", "off", "y", "n", "null",
    };

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_.\-/]*$|^https?://[A-Za-z0-9_.\-/:@%?&=+~]+$")]
    private static partial Regex PlainText();

    private static bool SamePath(string a, string b) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>A YAML mapping under construction: keys in the order added, each a rendered scalar or a nested section.</summary>
    private sealed class Mapping
    {
        private readonly List<(string Key, object Value)> _entries = [];

        private bool IsEmpty => _entries.All(entry => entry.Value is Mapping { IsEmpty: true });

        public Mapping Section(string key)
        {
            var section = new Mapping();
            _entries.Add((key, section));
            return section;
        }

        public void Add(string key, string? scalar)
        {
            if (scalar is not null)
                _entries.Add((key, scalar));
        }

        public void Render(StringBuilder output, int depth)
        {
            var indent = new string(' ', depth * 2);
            foreach (var (key, value) in _entries)
            {
                switch (value)
                {
                    case Mapping { IsEmpty: true }:
                        break;
                    case Mapping section:
                        output.Append(indent).Append(key).Append(":\n");
                        section.Render(output, depth + 1);
                        break;
                    default:
                        output.Append(indent).Append(key).Append(": ").Append(value).Append('\n');
                        break;
                }
            }
        }
    }
}
