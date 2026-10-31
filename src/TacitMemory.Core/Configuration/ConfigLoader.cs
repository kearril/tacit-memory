using System.Text;
using TacitMemory.Core.Localization;
using YamlDotNet.RepresentationModel;

namespace TacitMemory.Core.Configuration;

/// <summary>Loads <c>config.yaml</c> (schema and defaults as documented) and <c>rules.md</c> from <c>TACIT_HOME</c>.</summary>
internal static class ConfigLoader
{
    public static TacitConfig Load(TacitOptions options)
    {
        var path = Path.Combine(options.TacitHome, "config.yaml");
        var fallbackLanguage = LanguageCodes.FromUiCulture(options.UiCulture);
        if (!File.Exists(path))
            throw new TacitException(TacitError.NotInitialized, Messages.NotInitializedConfig.Format(fallbackLanguage, path));

        var root = Parse(path, fallbackLanguage);
        var languageText = root.Children.TryGetValue(new YamlScalarNode("language"), out var languageNode)
            ? (languageNode as YamlScalarNode)?.Value
            : null;
        var language = LanguageCodes.Parse(languageText) ?? fallbackLanguage;

        var reader = new ConfigReader(language);
        reader.OnlyKnown(root, "", "server", "memory", "language", "models", "limits", "candidates", "checkup", "decision", "retrieval", "backup");
        if (reader.Text(root, "", "language") is { } code && LanguageCodes.Parse(code) is null)
            reader.Error("language", Messages.ConfigExpectedLanguage, code);

        var server = reader.Mapping(root, "", "server");
        reader.OnlyKnown(server, "server", "port");
        var port = reader.Integer(server, "server", "port", 18888, 1, 65535);

        var memory = reader.Mapping(root, "", "memory");
        reader.OnlyKnown(memory, "memory", "path");
        var memoryPath = reader.Text(memory, "memory", "path") is { } configured
            ? HomePaths.Expand(configured, options.UserHome)
            : Path.Combine(options.TacitHome, "memory");

        var models = ReadModels(reader, root);
        var limits = ReadLimits(reader, root);
        var candidateMinDays = ReadCandidates(reader, root);
        var checkup = ReadCheckup(reader, root);
        var decision = ReadDecision(reader, root);
        var retrieval = ReadRetrieval(reader, root);
        var backup = ReadBackup(reader, root);

        if (reader.Errors.Count > 0 || models is null)
        {
            var message = new StringBuilder(Messages.ConfigErrors.Format(language, path));
            foreach (var error in reader.Errors)
                message.Append('\n').Append(error);
            throw new TacitException(TacitError.ConfigInvalid, message.ToString());
        }

        if (!Directory.Exists(Path.Combine(memoryPath, ".git")) && !File.Exists(Path.Combine(memoryPath, ".git")))
            throw new TacitException(TacitError.NotInitialized, Messages.NotInitializedMemory.Format(language, memoryPath));

        return new TacitConfig
        {
            Port = port,
            MemoryPath = memoryPath,
            Language = language,
            Models = models,
            Limits = limits,
            CandidateMinDays = candidateMinDays,
            Checkup = checkup,
            Decision = decision,
            Retrieval = retrieval,
            Backup = backup,
            Rules = RulesFile.Load(options.TacitHome, language),
        };
    }

    private static YamlMappingNode Parse(string path, MemoryLanguage language)
    {
        var yaml = new YamlStream();
        try
        {
            using var file = new StreamReader(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            yaml.Load(file);
        }
        catch (YamlDotNet.Core.YamlException e)
        {
            var reason = (e.InnerException ?? e).Message;
            throw new TacitException(
                TacitError.ConfigInvalid, Messages.ConfigUnparsable.Format(language, path, e.Start.Line, e.Start.Column, reason), e);
        }

        return yaml.Documents.FirstOrDefault()?.RootNode switch
        {
            null => new YamlMappingNode(),
            YamlMappingNode mapping => mapping,
            YamlScalarNode { Value: null or "" } => new YamlMappingNode(),
            var other => throw new TacitException(
                TacitError.ConfigInvalid, Messages.ConfigUnparsable.Format(
                    language, path, other.Start.Line, other.Start.Column, Messages.ConfigExpectedMapping.In(language))),
        };
    }

    /// <summary>The <c>models</c> section, or <c>null</c> when any part of it is missing or wrong (all reported).</summary>
    private static ModelsConfig? ReadModels(ConfigReader reader, YamlMappingNode root)
    {
        var models = reader.Mapping(root, "", "models", required: true);
        reader.OnlyKnown(models, "models", "embedding", "reranker", "decision");

        ModelEndpoint? Endpoint(string key)
        {
            var path = ConfigReader.Join("models", key);
            var map = reader.Mapping(models, "models", key, required: models is not null);
            reader.OnlyKnown(map, path, "endpoint", "model");
            var url = reader.Url(map, path, "endpoint");
            var model = reader.Text(map, path, "model", required: map is not null);
            return url is null || model is null ? null : new ModelEndpoint(url, model);
        }

        var embedding = Endpoint("embedding");
        var reranker = Endpoint("reranker");

        const string decisionPath = "models.decision";
        var decision = reader.Mapping(models, "models", "decision", required: models is not null);
        reader.OnlyKnown(decision, decisionPath, "endpoint", "model", "apiKey", "apiKeyEnv");
        var decisionUrl = reader.Url(decision, decisionPath, "endpoint");
        var decisionModel = reader.Text(decision, decisionPath, "model", required: decision is not null);
        var apiKey = reader.Text(decision, decisionPath, "apiKey");
        var apiKeyEnv = reader.Text(decision, decisionPath, "apiKeyEnv");
        if (decision is not null && (apiKey is null) == (apiKeyEnv is null))
            reader.Error(decisionPath, Messages.ConfigExactlyOneKey);

        return embedding is null || reranker is null || decisionUrl is null || decisionModel is null
            ? null
            : new ModelsConfig(embedding, reranker, new DecisionModelEndpoint(decisionUrl, decisionModel, apiKey, apiKeyEnv));
    }

    private static LimitsConfig ReadLimits(ConfigReader reader, YamlMappingNode root)
    {
        var limits = reader.Mapping(root, "", "limits");
        reader.OnlyKnown(limits, "limits", "persona", "itemLength", "mainline", "page");

        var persona = reader.Mapping(limits, "limits", "persona");
        reader.OnlyKnown(persona, "limits.persona", "redLines", "workingRules", "preferences", "profile");
        var page = reader.Mapping(limits, "limits", "page");
        reader.OnlyKnown(page, "limits.page", "titleLength", "aliasLength", "minAliases");

        return new LimitsConfig(
            new PersonaLimits(
                Count(reader, persona, "limits.persona", "redLines", 5),
                Count(reader, persona, "limits.persona", "workingRules", 20),
                Count(reader, persona, "limits.persona", "preferences", 15),
                Count(reader, persona, "limits.persona", "profile", 10)),
            Count(reader, limits, "limits", "itemLength", 100),
            Count(reader, limits, "limits", "mainline", 5),
            new PageLimits(
                Count(reader, page, "limits.page", "titleLength", 50),
                Count(reader, page, "limits.page", "aliasLength", 30),
                Count(reader, page, "limits.page", "minAliases", 2)));
    }

    private static int ReadCandidates(ConfigReader reader, YamlMappingNode root)
    {
        var candidates = reader.Mapping(root, "", "candidates");
        reader.OnlyKnown(candidates, "candidates", "minDays");
        return Count(reader, candidates, "candidates", "minDays", 2);
    }

    private static CheckupConfig ReadCheckup(ConfigReader reader, YamlMappingNode root)
    {
        var checkup = reader.Mapping(root, "", "checkup");
        reader.OnlyKnown(checkup, "checkup", "remindAfterDays", "mainlineStaleDays");
        return new CheckupConfig(
            Count(reader, checkup, "checkup", "remindAfterDays", 7),
            Count(reader, checkup, "checkup", "mainlineStaleDays", 14));
    }

    private static DecisionThresholds ReadDecision(ConfigReader reader, YamlMappingNode root)
    {
        var decision = reader.Mapping(root, "", "decision");
        reader.OnlyKnown(decision, "decision", "samePreference", "samePage", "sameActivity", "ruleConflict", "credential");

        Threshold? Read(string key)
        {
            var path = ConfigReader.Join("decision", key);
            var map = reader.Mapping(decision, "decision", key);
            if (map is null)
                return null;
            reader.OnlyKnown(map, path, "certain", "unlikely");
            var certain = reader.Probability(map, path, "certain", required: true);
            var unlikely = reader.Probability(map, path, "unlikely", required: true);
            if (certain is null || unlikely is null)
                return null;
            if (unlikely >= certain)
                reader.Error(path, Messages.ConfigThresholdOrder, unlikely, certain);
            return new Threshold(certain.Value, unlikely.Value);
        }

        return new DecisionThresholds(Read("samePreference"), Read("samePage"), Read("sameActivity"), Read("ruleConflict"), Read("credential"));
    }

    private static RetrievalConfig ReadRetrieval(ConfigReader reader, YamlMappingNode root)
    {
        var retrieval = reader.Mapping(root, "", "retrieval");
        reader.OnlyKnown(retrieval, "retrieval", "candidates", "topK", "minRerankScore");
        return new RetrievalConfig(
            Count(reader, retrieval, "retrieval", "candidates", 30),
            Count(reader, retrieval, "retrieval", "topK", 5),
            reader.Probability(retrieval, "retrieval", "minRerankScore"));
    }

    private static BackupConfig ReadBackup(ConfigReader reader, YamlMappingNode root)
    {
        var backup = reader.Mapping(root, "", "backup");
        reader.OnlyKnown(backup, "backup", "keep", "remote");
        return new BackupConfig(reader.Integer(backup, "backup", "keep", 7, 0, 1000), reader.Text(backup, "backup", "remote"));
    }

    /// <summary>A positive count with a default.</summary>
    private static int Count(ConfigReader reader, YamlMappingNode? map, string path, string key, int fallback) =>
        reader.Integer(map, path, key, fallback, 1, 100_000);
}
