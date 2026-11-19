using System.Text;
using TacitMemory.Core.Configuration;
using TacitMemory.Core.Git;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Pages;
using TacitMemory.Core.Persona;
using TacitMemory.Core.Recent;
using TacitMemory.Core.Sync;
using TacitMemory.Core.Workstreams;
using YamlDotNet.RepresentationModel;

namespace TacitMemory.Core.Setup;

/// <summary><c>tacit init</c>: config templates in <c>TACIT_HOME</c> and the memory repository.</summary>
internal static class Initializer
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>The language the caller is asked for when neither the request nor a config names one: there is no config yet.</summary>
    private static MemoryLanguage AskLanguage(InitRequest request) =>
        request.AskLanguage?.Invoke()
        ?? throw new InvalidOperationException("init needs a language: none was given, the config names none, and there is no one to ask.");

    public static async Task<InitResult> RunAsync(TacitOptions options, InitRequest request, CancellationToken cancellationToken)
    {
        var configPath = Path.Combine(options.TacitHome, "config.yaml");
        var config = ExistingConfig.Read(configPath, options.UserHome);
        var requestedMemory = request.MemoryPath is null ? null : Path.GetFullPath(request.MemoryPath);
        var remote = string.IsNullOrWhiteSpace(request.Remote) ? null : request.Remote.Trim();

        // The language is asked for only once every refusal has had its turn, so no one answers and is then refused.
        // Until then the refusals speak the language already known, English when none is.
        var knownLanguage = request.Language ?? config.Language ?? LanguageCodes.WithoutConfig;
        // init never rewrites an existing config, so a language it would choose could not be kept there.
        if (config.Exists && config.Language is null)
            throw new TacitException(TacitError.ConfigInvalid, Messages.InitConfigWithoutLanguage.Format(knownLanguage, configPath));
        if (request.Language is { } requestedLanguage && config.Language is { } configLanguage && requestedLanguage != configLanguage)
            throw Conflict(configLanguage, configPath, "language", configLanguage.Code(), requestedLanguage.Code());

        var defaultMemory = Path.Combine(options.TacitHome, "memory");
        var configMemory = config.Exists ? config.MemoryPath ?? defaultMemory : null;
        if (requestedMemory is not null && configMemory is not null && !SamePath(requestedMemory, configMemory))
            throw Conflict(knownLanguage, configPath, "memory.path", configMemory, requestedMemory);
        if (remote is not null && config.Exists && config.SyncRemote != remote)
        {
            throw config.SyncRemote is null
                ? new TacitException(TacitError.ConflictsWithConfig, Messages.InitRemoteConfigWithout.Format(knownLanguage, configPath, remote))
                : Conflict(knownLanguage, configPath, "sync.remote", config.SyncRemote, remote);
        }

        var memory = requestedMemory ?? configMemory ?? defaultMemory;
        var isRepository = new GitCli(memory, knownLanguage).IsRepositoryRoot();
        var hasEntries = Directory.Exists(memory) && Directory.EnumerateFileSystemEntries(memory).Any();
        if (remote is not null)
        {
            // Running init --remote again on the clone it made only writes what is missing; anything else stays untouched.
            if (hasEntries && !(isRepository && await RemoteOfAsync(memory, knownLanguage, cancellationToken) == remote))
                throw new TacitException(TacitError.MemoryDirectoryNotEmpty, Messages.InitRemoteMemoryTaken.Format(knownLanguage, memory, remote));
            if (!isRepository && (await new GitCli(Path.GetTempPath(), knownLanguage)
                    .RunRemoteAsync(cancellationToken, "ls-remote", "--heads", remote)).Trim().Length == 0)
                throw new TacitException(TacitError.RemoteEmpty, Messages.InitRemoteEmpty.Format(knownLanguage, remote));
        }
        else if (!isRepository && hasEntries)
            throw new TacitException(TacitError.MemoryDirectoryNotEmpty, Messages.MemoryDirectoryNotEmpty.Format(knownLanguage, memory));

        var language = request.Language ?? config.Language ?? AskLanguage(request);
        var git = new GitCli(memory, language);

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

        // A joining machine takes the repository as the remote has it: no skeleton is added and nothing is committed. The
        // clone comes first, so a clone that fails leaves no config naming the remote. Its checkout is already LF, since every
        // git command tacit runs turns core.autocrlf off; the attributes keep manual git commands that way too.
        var cloned = remote is not null && !isRepository;
        if (cloned)
        {
            var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(memory))!;
            Directory.CreateDirectory(parent);
            await new GitCli(parent, language).RunRemoteAsync(
                GitCli.CloneTimeout, cancellationToken, "clone", "--quiet", "--origin", MemorySync.RemoteName, remote!, memory);
            await MemorySync.WriteAttributesAsync(git, cancellationToken);
        }

        Write(configPath, ConfigWriter.Render(
            new ConfigFile { Language = language.Code(), Memory = new MemoryFile(requestedMemory), Sync = new SyncFile(remote) }, options));
        Directory.CreateDirectory(PromptFiles.Folder(options.TacitHome));

        var skeletonAdded = false;
        if (remote is null)
        {
            Directory.CreateDirectory(memory);
            if (!isRepository)
                await git.RunAsync(cancellationToken, "init", "--quiet");
            skeletonAdded = new[]
            {
                Write(Path.Combine(memory, ".gitignore"), Templates.MemoryGitignore),
                Write(MemoryLayout.Full(memory, MemoryLayout.Persona), Templates.Persona),
                Write(MemoryLayout.Full(memory, MemoryLayout.Candidates), Templates.Candidates),
                Write(MemoryLayout.Full(memory, MemoryLayout.Workstreams), Templates.Workstreams),
                Write(MemoryLayout.Full(memory, MemoryLayout.In(MemoryLayout.Recent, ".gitkeep")), ""),
                Write(MemoryLayout.Full(memory, MemoryLayout.In(MemoryLayout.Pages, ".gitkeep")), ""),
            }.Any(added => added);
            if (!isRepository)
            {
                await git.RunAsync(cancellationToken, "add", "--all");
                await git.RunAsync(cancellationToken, "commit", "--quiet", "--message", "init: create memory repo");
            }
        }

        var report = new StringBuilder();
        report.AppendLine(cloned
            ? Messages.InitRepositoryCloned.Format(language, remote, memory)
            : (isRepository ? Messages.InitRepositoryExists : Messages.InitRepositoryCreated).Format(language, memory));
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

    /// <summary>The address of the remote tacit keeps in the repository at <paramref name="memory"/>; <c>null</c> when it has none.</summary>
    private static async Task<string?> RemoteOfAsync(string memory, MemoryLanguage language, CancellationToken cancellationToken)
    {
        var (code, url, _) = await new GitCli(memory, language).TryRunAsync(cancellationToken, "remote", "get-url", MemorySync.RemoteName);
        return code == 0 ? url.Trim() : null;
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);

    private static TacitException Conflict(MemoryLanguage language, string configPath, string key, string configured, string given) =>
        new(TacitError.ConflictsWithConfig, Messages.ConflictsWithConfig.Format(language, configPath, key, configured, given));

    /// <summary>
    /// The settings <c>init</c> must honor from an existing <c>config.yaml</c>. Read leniently: anything unreadable counts as
    /// unset, since validating the config is the service's job.
    /// </summary>
    private sealed record ExistingConfig(bool Exists, MemoryLanguage? Language, string? MemoryPath, string? SyncRemote)
    {
        private static readonly ExistingConfig Unset = new(true, null, null, null);

        public static ExistingConfig Read(string path, string userHome)
        {
            if (!File.Exists(path))
                return new(false, null, null, null);
            try
            {
                var yaml = new YamlStream();
                using (var reader = new StreamReader(path, Utf8))
                    yaml.Load(reader);
                if (yaml.Documents.FirstOrDefault()?.RootNode is not YamlMappingNode root)
                    return Unset;

                var language = LanguageCodes.Parse(Scalar(root, "language"));
                var memory = Scalar(Section(root, "memory"), "path");
                var remote = Scalar(Section(root, "sync"), "remote");
                return new(
                    true,
                    language,
                    string.IsNullOrWhiteSpace(memory) ? null : HomePaths.Expand(memory, userHome),
                    string.IsNullOrWhiteSpace(remote) ? null : remote.Trim());
            }
            catch (YamlDotNet.Core.YamlException)
            {
                return Unset;
            }
        }

        private static YamlMappingNode? Section(YamlMappingNode root, string key) =>
            root.Children.TryGetValue(new YamlScalarNode(key), out var node) ? node as YamlMappingNode : null;

        private static string? Scalar(YamlMappingNode? map, string key) =>
            map is not null && map.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlScalarNode scalar ? scalar.Value : null;
    }
}
