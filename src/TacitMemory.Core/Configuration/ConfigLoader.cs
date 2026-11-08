using System.Globalization;
using System.Text;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Localization;
using YamlDotNet.RepresentationModel;

namespace TacitMemory.Core.Configuration;

/// <summary>Loads <c>config.yaml</c> (schema and defaults as documented) and the prompt documents from <c>TACIT_HOME</c>.</summary>
internal static class ConfigLoader
{
    /// <summary>The port the service listens on when <c>server.port</c> is not given.</summary>
    internal const int DefaultPort = 18888;

    public static TacitConfig Load(TacitOptions options)
    {
        var config = Read(options);
        if (!Directory.Exists(Path.Combine(config.MemoryPath, ".git")) && !File.Exists(Path.Combine(config.MemoryPath, ".git")))
            throw new TacitException(TacitError.NotInitialized, Messages.NotInitializedMemory.Format(config.Language, config.MemoryPath));
        return config;
    }

    /// <summary>
    /// Reads and validates <c>config.yaml</c> and the prompt documents, without checking that the memory repository exists:
    /// a restored config may name a repository that is not here yet, and the restore reports that rather than failing.
    /// </summary>
    internal static TacitConfig Read(TacitOptions options) =>
        Read(options, Path.Combine(options.TacitHome, "config.yaml"));

    /// <summary>Reads the config file at <paramref name="path"/>; its prompt documents still come from <c>TACIT_HOME</c>.</summary>
    internal static TacitConfig Read(TacitOptions options, string path)
    {
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
        var port = reader.Integer(server, "server", "port", DefaultPort, 1, 65535);

        var memory = reader.Mapping(root, "", "memory");
        reader.OnlyKnown(memory, "memory", "path");
        var memoryPath = reader.Text(memory, "memory", "path") is { } configured
            ? HomePaths.Expand(configured, options.UserHome)
            : Path.Combine(options.TacitHome, "memory");

        var models = ReadModels(reader, root, options.Secrets);
        var limits = ReadLimits(reader, root);
        var candidateMinDays = ReadCandidates(reader, root);
        var checkup = ReadCheckup(reader, root);
        var decision = ReadDecision(reader, root);
        var retrieval = ReadRetrieval(reader, root);
        var backup = ReadBackup(reader, root, options.Secrets);

        if (reader.Errors.Count > 0 || models is null)
        {
            var message = new StringBuilder(Messages.ConfigErrors.Format(language, path));
            foreach (var error in reader.Errors)
                message.Append('\n').Append(error);
            throw new TacitException(TacitError.ConfigInvalid, message.ToString());
        }

        var prompts = PromptFiles.Load(options.TacitHome, language);
        ServerInstructionText.Check(language, prompts, options.TacitHome);

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
            Prompts = prompts,
        };
    }

    /// <summary>The memory language for messages: the config's language, or the system UI language when the config does not load.</summary>
    public static MemoryLanguage LanguageOf(TacitOptions options)
    {
        try
        {
            return Load(options).Language;
        }
        catch (TacitException)
        {
            return LanguageCodes.FromUiCulture(options.UiCulture);
        }
    }

    /// <summary>
    /// Reads the port and the language from <c>config.yaml</c> without validating anything else, for the status report of a
    /// config that does not load. Reads no secret. Each value falls back on its own: the default port, and the system UI
    /// language, when the file is missing or cannot be parsed, or when the key is absent or not a valid value.
    /// </summary>
    internal static LooseSettings ReadLoose(TacitOptions options)
    {
        var fallbackLanguage = LanguageCodes.FromUiCulture(options.UiCulture);
        YamlMappingNode root;
        try
        {
            var path = Path.Combine(options.TacitHome, "config.yaml");
            if (!File.Exists(path))
                return new LooseSettings(DefaultPort, fallbackLanguage);
            root = Parse(path, fallbackLanguage);
        }
        catch (Exception e) when (e is TacitException or IOException or UnauthorizedAccessException)
        {
            return new LooseSettings(DefaultPort, fallbackLanguage);
        }

        var language = LanguageCodes.Parse(Scalar(root, "language")) ?? fallbackLanguage;
        var server = root.Children.TryGetValue(new YamlScalarNode("server"), out var serverNode) ? serverNode as YamlMappingNode : null;
        var port = int.TryParse(Scalar(server, "port"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            && value is >= 1 and <= 65535
            ? value
            : DefaultPort;
        return new LooseSettings(port, language);
    }

    private static string? Scalar(YamlMappingNode? map, string key) =>
        map is not null && map.Children.TryGetValue(new YamlScalarNode(key), out var node) ? (node as YamlScalarNode)?.Value : null;

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

    /// <summary>
    /// The <c>models</c> section, or <c>null</c> when any part of it is missing or wrong (all reported). Each model takes
    /// <c>url</c>, <c>model</c> and an optional <c>apiKey</c>; no other key is known.
    /// </summary>
    private static ModelsConfig? ReadModels(ConfigReader reader, YamlMappingNode root, IReadOnlyDictionary<string, string> secrets)
    {
        var models = reader.Mapping(root, "", "models", required: true);
        reader.OnlyKnown(models, "models", "embedding", "reranker", "decision");

        ModelConfig? Model(string key, string variable)
        {
            var path = ConfigReader.Join("models", key);
            var map = reader.Mapping(models, "models", key, required: models is not null);
            reader.OnlyKnown(map, path, "url", "model", "apiKey");
            var url = reader.Url(map, path, "url");
            var model = reader.Text(map, path, "model", required: map is not null);
            var apiKey = ReadSecret(reader, map, path, "apiKey", variable, secrets, required: false);
            return url is null || model is null ? null : new ModelConfig(url, model, apiKey);
        }

        var embedding = Model("embedding", SecretVariables.EmbeddingApiKey);
        var reranker = Model("reranker", SecretVariables.RerankerApiKey);
        var decision = Model("decision", SecretVariables.DecisionApiKey);

        return embedding is null || reranker is null || decision is null
            ? null
            : new ModelsConfig(embedding, reranker, decision);
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
            Count(reader, limits, "limits", "itemLength", 200),
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
        reader.OnlyKnown(checkup, "checkup", "remindAfterDays", "mainlineStaleDays", "budgetSeconds", "maxConcurrency");
        return new CheckupConfig(
            Count(reader, checkup, "checkup", "remindAfterDays", 7),
            Count(reader, checkup, "checkup", "mainlineStaleDays", 14),
            Count(reader, checkup, "checkup", "budgetSeconds", 20),
            Count(reader, checkup, "checkup", "maxConcurrency", 5));
    }

    private static DecisionThresholds ReadDecision(ConfigReader reader, YamlMappingNode root)
    {
        var decision = reader.Mapping(root, "", "decision");
        reader.OnlyKnown(decision, "decision", "samePreference", "samePage", "sameTopic", "ruleConflict");

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

        return new DecisionThresholds(Read("samePreference"), Read("samePage"), Read("sameTopic"), Read("ruleConflict"));
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

    private static BackupConfig ReadBackup(ConfigReader reader, YamlMappingNode root, IReadOnlyDictionary<string, string> secrets)
    {
        var backup = reader.Mapping(root, "", "backup");
        reader.OnlyKnown(backup, "backup", "keep", "webdav");
        return new BackupConfig(reader.Integer(backup, "backup", "keep", 7, 0, 1000), ReadWebDav(reader, backup, secrets));
    }

    /// <summary>The <c>backup.webdav</c> section, or <c>null</c> when absent or wrong (all reported).</summary>
    private static WebDavConfig? ReadWebDav(ConfigReader reader, YamlMappingNode? backup, IReadOnlyDictionary<string, string> secrets)
    {
        const string path = "backup.webdav";
        var webdav = reader.Mapping(backup, "backup", "webdav");
        reader.OnlyKnown(webdav, path, "url", "username", "password");
        var url = reader.Url(webdav, path, "url");
        var username = reader.Text(webdav, path, "username");
        var password = ReadSecret(reader, webdav, path, "password", SecretVariables.WebDavPassword, secrets, required: username is not null);
        return url is null ? null : new WebDavConfig(AsDirectory(url), username, password);
    }

    /// <summary>The URL as a directory: a <c>/</c> is appended when its path does not end in one.</summary>
    private static Uri AsDirectory(Uri url) =>
        url.AbsolutePath.EndsWith('/') ? url : new UriBuilder(url) { Path = url.AbsolutePath + "/" }.Uri;

    /// <summary>
    /// A secret: the environment variable's value when it is set and not blank, otherwise the plain-text value under
    /// <paramref name="key"/>. A <paramref name="required"/> secret that has neither is reported with its key path and
    /// the variable's name.
    /// </summary>
    private static SecretValue? ReadSecret(
        ConfigReader reader, YamlMappingNode? map, string path, string key, string variable, IReadOnlyDictionary<string, string> secrets, bool required)
    {
        var configured = reader.Text(map, path, key);
        if (secrets.TryGetValue(variable, out var fromEnvironment) && !string.IsNullOrWhiteSpace(fromEnvironment))
            return new SecretValue(fromEnvironment.Trim(), SecretSource.Environment, WrittenInConfig: configured is not null);
        if (configured is not null)
            return new SecretValue(configured, SecretSource.ConfigFile, WrittenInConfig: true);
        if (required)
            reader.Error(ConfigReader.Join(path, key), Messages.ConfigSecretMissing, variable);
        return null;
    }

    /// <summary>A positive count with a default.</summary>
    private static int Count(ConfigReader reader, YamlMappingNode? map, string path, string key, int fallback) =>
        reader.Integer(map, path, key, fallback, 1, 100_000);
}
