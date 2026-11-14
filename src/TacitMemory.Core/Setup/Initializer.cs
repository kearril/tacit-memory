using System.Text;
using TacitMemory.Core.Configuration;
using TacitMemory.Core.Git;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Pages;
using TacitMemory.Core.Persona;
using TacitMemory.Core.Recent;
using TacitMemory.Core.Workstreams;
using YamlDotNet.RepresentationModel;

namespace TacitMemory.Core.Setup;

/// <summary><c>tacit init</c>: config templates in <c>TACIT_HOME</c> and the memory repository.</summary>
internal static class Initializer
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static async Task<InitResult> RunAsync(TacitOptions options, InitRequest request, CancellationToken cancellationToken)
    {
        var configPath = Path.Combine(options.TacitHome, "config.yaml");
        var rulesPath = PromptFiles.RulesPath(options.TacitHome);
        var config = ExistingConfig.Read(configPath, options.UserHome);
        var requestedMemory = request.MemoryPath is null ? null : Path.GetFullPath(request.MemoryPath);

        var language = request.Language ?? config.Language ?? LanguageCodes.FromUiCulture(options.UiCulture);
        if (request.Language is { } requestedLanguage && config.Language is { } configLanguage && requestedLanguage != configLanguage)
            throw Conflict(configLanguage, configPath, "language", configLanguage.Code(), requestedLanguage.Code());

        var defaultMemory = Path.Combine(options.TacitHome, "memory");
        var configMemory = config.Exists ? config.MemoryPath ?? defaultMemory : null;
        if (requestedMemory is not null && configMemory is not null && !SamePath(requestedMemory, configMemory))
            throw Conflict(language, configPath, "memory.path", configMemory, requestedMemory);

        var memory = requestedMemory ?? configMemory ?? defaultMemory;
        var git = new GitCli(memory, language);
        var isRepository = git.IsRepositoryRoot();
        if (!isRepository && Directory.Exists(memory) && Directory.EnumerateFileSystemEntries(memory).Any())
            throw new TacitException(TacitError.MemoryDirectoryNotEmpty, Messages.MemoryDirectoryNotEmpty.Format(language, memory));

        var created = new List<string>();
        var existing = new List<string>();
        bool Write(string path, string content)
        {
            if (File.Exists(path))
            {
                existing.Add(path);
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, Utf8);
            created.Add(path);
            return true;
        }

        Write(configPath, ConfigWriter.Render(new ConfigFile { Language = language.Code(), Memory = new MemoryFile(requestedMemory) }, options));
        Write(rulesPath, Templates.Rules(language));

        Directory.CreateDirectory(memory);
        if (!isRepository)
            await git.RunAsync(cancellationToken, "init", "--quiet");
        var skeletonAdded = new[]
        {
            Write(Path.Combine(memory, ".gitignore"), Templates.MemoryGitignore),
            Write(Path.Combine(memory, PersonaFiles.Persona), Templates.Persona(language)),
            Write(Path.Combine(memory, PersonaFiles.Candidates), Templates.Candidates(language)),
            Write(Path.Combine(memory, WorkstreamFile.RelativePath), Templates.Workstreams(language)),
            Write(Path.Combine(memory, RecentFile.Folder, ".gitkeep"), ""),
            Write(Path.Combine(memory, PageId.Folder, ".gitkeep"), ""),
        }.Any(added => added);
        if (!isRepository)
        {
            await git.RunAsync(cancellationToken, "add", "--all");
            await git.RunAsync(cancellationToken, "commit", "--quiet", "--message", "init: create memory repo");
        }

        var report = new StringBuilder();
        report.AppendLine((isRepository ? Messages.InitRepositoryExists : Messages.InitRepositoryCreated).Format(language, memory));
        foreach (var path in created)
            report.AppendLine(Messages.InitCreated.Format(language, path));
        foreach (var path in existing)
            report.AppendLine(Messages.InitExisting.Format(language, path));
        if (isRepository && skeletonAdded)
            report.AppendLine(Messages.InitSkeletonNotCommitted.In(language));
        report.AppendLine(Messages.InitNextSteps.Format(language, configPath));
        report.Append(Messages.InitWebInterface.Format(language, ConfigLoader.ReadLoose(options).Port));

        return new InitResult
        {
            Language = language,
            MemoryPath = memory,
            RepositoryCreated = !isRepository,
            Created = created,
            Existing = existing,
            Report = report.ToString(),
        };
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);

    private static TacitException Conflict(MemoryLanguage language, string configPath, string key, string configured, string given) =>
        new(TacitError.ConflictsWithConfig, Messages.ConflictsWithConfig.Format(language, configPath, key, configured, given));

    /// <summary>
    /// The two settings <c>init</c> must honor from an existing <c>config.yaml</c>. Read leniently: anything
    /// unreadable counts as unset, since validating the config is the service's job.
    /// </summary>
    private sealed record ExistingConfig(bool Exists, MemoryLanguage? Language, string? MemoryPath)
    {
        private static readonly ExistingConfig Unset = new(true, null, null);

        public static ExistingConfig Read(string path, string userHome)
        {
            if (!File.Exists(path))
                return new(false, null, null);
            try
            {
                var yaml = new YamlStream();
                using (var reader = new StreamReader(path, Utf8))
                    yaml.Load(reader);
                if (yaml.Documents.FirstOrDefault()?.RootNode is not YamlMappingNode root)
                    return Unset;

                var language = LanguageCodes.Parse(Scalar(root, "language"));
                var memory = root.Children.TryGetValue(new YamlScalarNode("memory"), out var node) && node is YamlMappingNode section
                    ? Scalar(section, "path")
                    : null;
                return new(true, language, string.IsNullOrWhiteSpace(memory) ? null : HomePaths.Expand(memory, userHome));
            }
            catch (YamlDotNet.Core.YamlException)
            {
                return Unset;
            }
        }

        private static string? Scalar(YamlMappingNode map, string key) =>
            map.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlScalarNode scalar ? scalar.Value : null;
    }
}
