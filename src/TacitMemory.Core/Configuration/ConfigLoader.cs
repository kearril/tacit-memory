using System.Globalization;
using System.Net;
using System.Text;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Localization;
using YamlDotNet.RepresentationModel;

namespace TacitMemory.Core.Configuration;

/// <summary>Loads <c>config.yaml</c> (schema and defaults as documented) and the prompt documents from <c>TACIT_HOME</c>.</summary>
internal static class ConfigLoader
{
    /// <summary>The port the service listens on when <c>server.port</c> is not given.</summary>
    internal const int DefaultPort = ConfigDefaults.Port;

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

        string text;
        using (var file = new StreamReader(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            text = file.ReadToEnd();
        return Read(options, path, text);
    }

    /// <summary>
    /// Validates <paramref name="text"/> as the content of <c>config.yaml</c>, naming the file <paramref name="path"/> in
    /// messages; its prompt documents come from <c>TACIT_HOME</c>. Lets a config be checked before it is written.
    /// </summary>
    internal static TacitConfig Read(TacitOptions options, string path, string text)
    {
        var fallbackLanguage = LanguageCodes.FromUiCulture(options.UiCulture);
        var root = Parse(new StringReader(text), path, fallbackLanguage);
        var languageText = root.Children.TryGetValue(new YamlScalarNode("language"), out var languageNode)
            ? (languageNode as YamlScalarNode)?.Value
            : null;
        var language = LanguageCodes.Parse(languageText) ?? fallbackLanguage;

        var reader = new ConfigReader(language);
        reader.OnlyKnown(root, "", "server", "memory", "language", "models", "limits", "consolidation", "decision", "retrieval", "backup");
        if (reader.Text(root, "", "language") is { } code && LanguageCodes.Parse(code) is null)
            reader.Error("language", Messages.ConfigExpectedLanguage, code);

        var server = reader.Mapping(root, "", "server");
        reader.OnlyKnown(server, "server", "port", "host");
        var port = reader.Integer(server, "server", "port", DefaultPort, 1, 65535);
        var extraAddress = ReadExtraAddress(reader, server);

        var memory = reader.Mapping(root, "", "memory");
        reader.OnlyKnown(memory, "memory", "path");
        var memoryPath = reader.Text(memory, "memory", "path") is { } configured
            ? HomePaths.Expand(configured, options.UserHome)
            : ConfigDefaults.MemoryPath(options.TacitHome);

        var models = ReadModels(reader, root, options.Secrets);
        var limits = ReadLimits(reader, root);
        var consolidation = ReadConsolidation(reader, root);
        var decision = ReadDecision(reader, root);
        var retrieval = ReadRetrieval(reader, root);
        var backup = ReadBackup(reader, root, options.Secrets);

        if (reader.Errors.Count > 0)
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
            ExtraAddress = extraAddress,
            MemoryPath = memoryPath,
            Language = language,
            Models = models,
            Limits = limits,
            Consolidation = consolidation,
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
            using var file = new StreamReader(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            root = Parse(file, path, fallbackLanguage);
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

    private static YamlMappingNode Parse(TextReader text, string path, MemoryLanguage language)
    {
        var yaml = new YamlStream();
        try
        {
            yaml.Load(text);
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
    /// <c>server.host</c>: one IP address literal, or none. <c>127.0.0.1</c>, the address the service always listens on, means
    /// none; every other address is an extra listener, loopback ones included. Anything else, host names included, is reported
    /// with its key path.
    /// </summary>
    private static IPAddress? ReadExtraAddress(ConfigReader reader, YamlMappingNode? server)
    {
        var text = reader.Text(server, "server", "host");
        if (text is null)
            return null;
        if (IsIpAddressLiteral(text, out var address))
            return address.Equals(IPAddress.Loopback) ? null : address;
        reader.Error(ConfigReader.Join("server", "host"), Messages.ConfigExpectedIpAddress, text);
        return null;
    }

    /// <summary>
    /// Whether <paramref name="text"/> is written as an IP address and nothing else. <see cref="IPAddress.TryParse(string?, out IPAddress?)"/>
    /// also takes forms such as <c>1</c> or <c>127.1</c>, and a port or a zone is not part of an address; those are refused here.
    /// </summary>
    private static bool IsIpAddressLiteral(string text, out IPAddress address)
    {
        address = IPAddress.None;
        if (text.Contains('[') || text.Contains(']') || text.Contains('%') || text.Contains(' '))
            return false;
        if (!IPAddress.TryParse(text, out var parsed))
            return false;
        if (parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && parsed.ToString() != text)
            return false;
        if (parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 && !text.Contains(':'))
            return false;
        address = parsed;
        return true;
    }

    /// <summary>
    /// The <c>models</c> section. A model whose mapping is absent is unconfigured, and so is every model when the section is
    /// absent or empty. A mapping that is present needs a non-empty <c>url</c> and <c>model</c>, and takes an optional
    /// <c>apiKey</c> and an optional <c>provider</c>; no other key is known. Every problem is reported, and a model with one
    /// is left out of the result.
    /// </summary>
    private static ModelsConfig ReadModels(ConfigReader reader, YamlMappingNode root, IReadOnlyDictionary<string, string> secrets)
    {
        var models = reader.Mapping(root, "", "models");
        reader.OnlyKnown(models, "models", "embedding", "reranker", "decision");

        ModelConfig? Model(string key, string variable)
        {
            var path = ConfigReader.Join("models", key);
            var map = reader.Mapping(models, "models", key);
            if (map is null)
                return null;
            reader.OnlyKnown(map, path, "provider", "url", "model", "apiKey");
            var provider = ReadProvider(reader, map, path);
            var url = reader.Url(map, path, "url");
            var model = reader.Text(map, path, "model", required: true);
            var apiKey = ReadSecret(reader, map, path, "apiKey", variable, secrets, required: false);
            return url is null || model is null ? null : new ModelConfig(url, model, apiKey, provider);
        }

        return new ModelsConfig(
            Model("embedding", SecretVariables.EmbeddingApiKey),
            Model("reranker", SecretVariables.RerankerApiKey),
            Model("decision", SecretVariables.DecisionApiKey));
    }

    /// <summary>
    /// The <c>provider</c> of a model: absent means the generic protocol, <c>cloudflare</c> in any letter case means
    /// Cloudflare's. Any other value is reported with the values it may take, and the generic protocol is used meanwhile.
    /// </summary>
    private static ModelProvider ReadProvider(ConfigReader reader, YamlMappingNode? map, string path)
    {
        var text = reader.Text(map, path, "provider");
        if (text is null)
            return ModelProvider.Generic;
        if (string.Equals(text, "cloudflare", StringComparison.OrdinalIgnoreCase))
            return ModelProvider.Cloudflare;
        reader.Error(ConfigReader.Join(path, "provider"), Messages.ConfigExpectedProvider, text);
        return ModelProvider.Generic;
    }

    private static LimitsConfig ReadLimits(ConfigReader reader, YamlMappingNode root)
    {
        var limits = reader.Mapping(root, "", "limits");
        reader.OnlyKnown(limits, "limits", "page", "brief");

        var page = reader.Mapping(limits, "limits", "page");
        reader.OnlyKnown(page, "limits.page", "minAliases");

        return new LimitsConfig(
            Count(reader, limits, "limits", "brief", ConfigDefaults.Brief),
            new PageLimits(Count(reader, page, "limits.page", "minAliases", ConfigDefaults.MinAliases)));
    }

    private static ConsolidationConfig ReadConsolidation(ConfigReader reader, YamlMappingNode root)
    {
        var consolidation = reader.Mapping(root, "", "consolidation");
        reader.OnlyKnown(
            consolidation, "consolidation", "remindAfterDays", "remindCandidates", "budgetSeconds", "maxConcurrency", "hintItemLength", "hintResidentLength");
        return new ConsolidationConfig(
            Count(reader, consolidation, "consolidation", "remindAfterDays", ConfigDefaults.RemindAfterDays),
            Count(reader, consolidation, "consolidation", "budgetSeconds", ConfigDefaults.BudgetSeconds),
            Count(reader, consolidation, "consolidation", "maxConcurrency", ConfigDefaults.MaxConcurrency),
            Count(reader, consolidation, "consolidation", "remindCandidates", ConfigDefaults.RemindCandidates),
            Count(reader, consolidation, "consolidation", "hintItemLength", ConfigDefaults.HintItemLength),
            Count(reader, consolidation, "consolidation", "hintResidentLength", ConfigDefaults.HintResidentLength));
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
            Count(reader, retrieval, "retrieval", "candidates", ConfigDefaults.Candidates),
            Count(reader, retrieval, "retrieval", "topK", ConfigDefaults.TopK),
            reader.Probability(retrieval, "retrieval", "minRerankScore"));
    }

    private static BackupConfig ReadBackup(ConfigReader reader, YamlMappingNode root, IReadOnlyDictionary<string, string> secrets)
    {
        var backup = reader.Mapping(root, "", "backup");
        reader.OnlyKnown(backup, "backup", "keep", "webdav");
        return new BackupConfig(reader.Integer(backup, "backup", "keep", ConfigDefaults.Keep, 0, 1000), ReadWebDav(reader, backup, secrets));
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
            return new SecretValue(fromEnvironment.Trim(), SecretSource.Environment, configured);
        if (configured is not null)
            return new SecretValue(configured, SecretSource.ConfigFile, configured);
        if (required)
            reader.Error(ConfigReader.Join(path, key), Messages.ConfigSecretMissing, variable);
        return null;
    }

    /// <summary>A positive count with a default.</summary>
    private static int Count(ConfigReader reader, YamlMappingNode? map, string path, string key, int fallback) =>
        reader.Integer(map, path, key, fallback, 1, 100_000);
}
