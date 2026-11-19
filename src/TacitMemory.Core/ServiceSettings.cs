using TacitMemory.Core.Clients;
using TacitMemory.Core.Configuration;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Maintenance;
using TacitMemory.Core.Status;
using TacitMemory.Core.Upkeep;

namespace TacitMemory.Core;

/// <summary>
/// What the settings page and the prompts page change: <c>config.yaml</c>, the four prompt documents and the bindings. A
/// saved config is applied by reloading the service; a saved prompt document is applied at once.
/// </summary>
public sealed class ServiceSettings
{
    private readonly TacitOptions _options;
    private readonly MaintenanceGate _gate;
    private readonly DerivedUpkeep _upkeep;
    private readonly MaintenanceRunner _maintenance;
    private readonly AccessControl _access;
    private readonly Func<TacitConfig> _config;
    private readonly Func<IReadOnlyList<RestartChange>> _reload;

    /// <param name="reload">Opens the runtime again with <c>config.yaml</c> as it is now; called inside a maintenance operation.</param>
    internal ServiceSettings(
        TacitOptions options, MaintenanceGate gate, DerivedUpkeep upkeep, MaintenanceRunner maintenance, AccessControl access,
        Func<TacitConfig> config, Func<IReadOnlyList<RestartChange>> reload)
    {
        _options = options;
        _gate = gate;
        _upkeep = upkeep;
        _maintenance = maintenance;
        _access = access;
        _config = config;
        _reload = reload;
    }

    private MemoryLanguage Language => _config().Language;

    /// <summary>
    /// The four prompt documents for the prompts page: each file as it is now, or the built-in default where there is no
    /// file. Reads the files only, so it does not wait for a maintenance operation.
    /// </summary>
    public PromptList Prompts() =>
        new([.. Enum.GetValues<PromptName>().Select(PromptView)], [.. ResidentFile.Sections.Select(section => section.Part)]);

    /// <summary>
    /// Checks <paramref name="text"/> as the prompt document <paramref name="name"/> without saving it: its measure beside
    /// the other documents as the service runs them, and every refusal a save of it would give.
    /// </summary>
    public PromptCheck CheckPrompt(PromptName name, string text)
    {
        var running = RunningPrompts();
        return new PromptCheck(
            PromptFiles.Measure(Language, running, name, text),
            PromptFiles.Check(_options.TacitHome, Language, running, name, text));
    }

    /// <summary>The prompt documents the service runs with now.</summary>
    private PromptDocuments RunningPrompts() => _upkeep.PromptState.Running;

    /// <summary>
    /// The prompt document <paramref name="name"/> as it is now, measured beside the documents the service runs with, and
    /// why the service keeps its previous text, if it does.
    /// </summary>
    private PromptDocumentView PromptView(PromptName name)
    {
        var (running, problems) = _upkeep.PromptState;
        return PromptFiles.View(_options.TacitHome, Language, running, name, problems.GetValueOrDefault(name));
    }

    /// <summary>The prompt document named as the management API writes it: <c>instructions</c>, <c>rules</c>, <c>resident</c> or <c>skill</c>.</summary>
    /// <exception cref="TacitException"><see cref="TacitError.PromptUnknown"/> for any other name.</exception>
    public PromptName PromptNamed(string name)
    {
        foreach (var candidate in Enum.GetValues<PromptName>())
        {
            if (string.Equals(candidate.ToString(), name, StringComparison.OrdinalIgnoreCase) && name == name.ToLowerInvariant())
                return candidate;
        }
        throw new TacitException(TacitError.PromptUnknown, RequestMessages.PromptUnknown.Format(Language, name));
    }

    /// <summary>
    /// Saves <paramref name="text"/> as the prompt document <paramref name="name"/>, with LF line breaks, and applies it at
    /// once: the skills are written again, the resident blocks rendered again and the next server instructions built from it.
    /// <paramref name="hash"/> names the file the caller read the text from, <c>null</c> when there was none; a file changed
    /// since is not overwritten. The text is checked first by the rules a load applies, so a refused text changes no file.
    /// </summary>
    /// <exception cref="TacitException">
    /// <see cref="TacitError.PromptChanged"/>, or <see cref="TacitError.PromptRejected"/> with every refusal; in each case
    /// the file is as it was.
    /// </exception>
    public async Task<PromptSaved> SavePromptAsync(PromptName name, string? hash, string text, CancellationToken cancellationToken = default)
    {
        using var hold = await _gate.WriteAsync(cancellationToken);
        var path = PromptFiles.PathOf(_options.TacitHome, name);
        var current = PromptView(name);
        if (current.Hash != hash)
            throw new TacitException(TacitError.PromptChanged, RequestMessages.PromptChanged.Format(Language, Path.GetFileName(path)));

        var content = text.ReplaceLineEndings("\n");
        RefuseUnless(name, content);
        Directory.CreateDirectory(PromptFiles.Folder(_options.TacitHome));
        ConfigWriter.Replace(path, content);
        return Applied(name);
    }

    /// <summary>
    /// Returns the prompt document <paramref name="name"/> to its built-in default by deleting its file, and applies that at
    /// once as a save does. Without a file it changes nothing. It is refused when the built-in text would not fit beside
    /// the other documents, as when a long <c>instructions.md</c> leaves no room for the built-in recording criteria.
    /// </summary>
    /// <exception cref="TacitException"><see cref="TacitError.PromptRejected"/>; the file is kept.</exception>
    public async Task<PromptSaved> RestorePromptDefaultAsync(PromptName name, CancellationToken cancellationToken = default)
    {
        using var hold = await _gate.WriteAsync(cancellationToken);
        var path = PromptFiles.PathOf(_options.TacitHome, name);
        if (!File.Exists(path))
            return new PromptSaved(PromptView(name), []);

        RefuseUnless(name, PromptFiles.DefaultText(name));
        File.Delete(path);
        return Applied(name);
    }

    /// <summary>
    /// Applies the prompt documents now, then gives the document <paramref name="name"/> as the service runs it: the view is
    /// read after the apply, as its measure and its problem depend on what the service runs.
    /// </summary>
    private PromptSaved Applied(PromptName name)
    {
        // The resident blocks show resident.md, so a change to it renders them now, and what went wrong is returned.
        var notices = _upkeep.RefreshNow(Refresh.Prompts | (name == PromptName.Resident ? Refresh.Resident : Refresh.None));
        return new PromptSaved(PromptView(name), notices);
    }

    /// <summary>Refuses <paramref name="text"/> as the document <paramref name="name"/> when it breaks a rule, with every refusal.</summary>
    private void RefuseUnless(PromptName name, string text)
    {
        var refusals = PromptFiles.Check(_options.TacitHome, Language, RunningPrompts(), name, text);
        if (refusals.Count == 0)
            return;
        var file = Path.GetFileName(PromptFiles.PathOf(_options.TacitHome, name));
        throw new TacitException(TacitError.PromptRejected, string.Join(" ", refusals))
        {
            Problems = [.. refusals.Select(refusal => new ConfigProblem(file, refusal))],
        };
    }

    /// <summary>
    /// <c>config.yaml</c> for the settings page: as the file holds it, as the service runs it and at its defaults, with what
    /// each key accepts and the hash a save must name. Secrets are given apart, with the file's value of those it holds.
    /// Reads the file only, so it does not wait for a maintenance operation.
    /// </summary>
    /// <exception cref="TacitException">The file does not load, as when it was broken by hand after the service started.</exception>
    public ConfigView ConfigView()
    {
        var path = ConfigFilePath;
        var text = File.ReadAllText(path);
        var loaded = ConfigLoader.Read(_options, path, text);
        return new ConfigView(
            ContentHash.Of(text),
            WithoutSecrets(ConfigFile.From(loaded)),
            WithoutSecrets(ConfigFile.From(_config())),
            ConfigSchema.Defaults(_options),
            ConfigSchema.Keys,
            ConfigSchema.SecretStates(loaded, _options));
    }

    /// <summary>
    /// Saves <paramref name="file"/> as <c>config.yaml</c> and reloads, as one maintenance operation. <paramref name="hash"/>
    /// names the content the caller read the settings from; a file changed since is not overwritten. The secrets stay as the
    /// file holds them. When the reload cannot open the new config, the file goes back to what it was.
    /// </summary>
    /// <exception cref="TacitException">
    /// <see cref="TacitError.ConfigChanged"/>, <see cref="TacitError.ConfigRejected"/> with every problem, or
    /// <see cref="TacitError.ReloadFailed"/>; in each case <c>config.yaml</c> is as it was.
    /// </exception>
    public Task<ConfigSaved> SaveConfigAsync(
        ConfigFile file, string hash, IReadOnlyDictionary<string, string?>? secrets = null, CancellationToken cancellationToken = default) =>
        _maintenance.RunAsync("config", _ => Task.FromResult(SaveConfigCore(file, hash, secrets ?? new Dictionary<string, string?>())), cancellationToken);

    private ConfigSaved SaveConfigCore(ConfigFile file, string hash, IReadOnlyDictionary<string, string?> secrets)
    {
        var path = ConfigFilePath;
        var current = File.ReadAllText(path);
        if (ContentHash.Of(current) != hash)
            throw new TacitException(TacitError.ConfigChanged, RequestMessages.ConfigChanged.In(Language));

        var held = ConfigLoader.Read(_options, path, current);
        var unknown = secrets.Keys.Where(key => ConfigKeys.Secrets.All(secret => secret.Path != key)).ToList();
        if (unknown.Count > 0)
        {
            var refused = unknown.Select(key => new ConfigProblem(key, RequestMessages.NotASecretKey.In(Language))).ToList();
            throw new TacitException(TacitError.ConfigRejected, string.Join(" ", refused.Select(problem => problem.Message))) { Problems = refused };
        }

        var kept = ConfigFile.From(held);
        var withSecrets = ConfigKeys.Secrets.Aggregate(
            file, (result, secret) => result.WithSecret(secret, secrets.TryGetValue(secret.Path, out var value) ? value : kept.ValueAt(secret)));
        var content = ConfigWriter.Render(withSecrets, _options);
        TacitConfig saved;
        try
        {
            saved = ConfigLoader.Read(_options, path, content);
        }
        catch (TacitException e) when (e.Error == TacitError.ConfigInvalid)
        {
            throw new TacitException(TacitError.ConfigRejected, e.Message, e) { Problems = e.Problems };
        }

        var (before, after) = (ConfigFile.From(held), ConfigFile.From(saved));
        var problems = ConfigKeys.ReadOnly
            .Where(key => before.ValueAt(key) != after.ValueAt(key))
            .Select(key => new ConfigProblem(key.Path, RequestMessages.ConfigReadOnly.In(Language)))
            .ToList();
        if (saved.ExtraAddress is not null && !_access.PasswordSet)
            problems.Add(new ConfigProblem(ConfigKeys.Host.Path, RequestMessages.ConfigExtraNeedsPassword.In(Language)));
        if (problems.Count > 0)
            throw new TacitException(TacitError.ConfigRejected, string.Join(" ", problems.Select(problem => problem.Message))) { Problems = problems };

        ConfigWriter.Replace(path, content);
        IReadOnlyList<RestartChange> restart;
        try
        {
            restart = _reload();
        }
        catch
        {
            ConfigWriter.Replace(path, current);
            throw;
        }

        return new ConfigSaved(ContentHash.Of(content), restart);
    }

    private string ConfigFilePath => Path.Combine(_options.TacitHome, "config.yaml");

    /// <summary>The settings with every secret left out, as they leave the service.</summary>
    private static ConfigFile WithoutSecrets(ConfigFile file) =>
        ConfigKeys.Secrets.Aggregate(file, (result, secret) => result.WithSecret(secret, null));

    /// <summary>
    /// The bindings and what is wrong with each, as the status report's bindings column has them, with the MCP server a bound
    /// client registers.
    /// </summary>
    /// <exception cref="TacitException"><c>bindings.json</c> cannot be read.</exception>
    public BindingList Bindings() => new(BindingChecks.Produce(_options, Language).Items, McpServer.On(_config().Port));

    /// <summary>
    /// Binds a client by the rules of <c>tacit connect</c>; a refusal changes no file. Holds the render lock, so the service
    /// does not render the resident blocks while the binding is written.
    /// </summary>
    /// <exception cref="TacitException">A refusal, with the reason <c>tacit connect</c> gives.</exception>
    public BindingDone Connect(ConnectRequest request)
    {
        lock (_upkeep.RenderLock)
            return BindingDone.From(ClientName.Normalize(request.Client), Binder.Connect(_options, request));
    }

    /// <summary>Unbinds a client by the rules of <c>tacit disconnect</c>, under the render lock as <see cref="Connect"/>.</summary>
    /// <exception cref="TacitException">No binding has that name.</exception>
    public BindingDone Disconnect(string client)
    {
        lock (_upkeep.RenderLock)
            return BindingDone.From(ClientName.Normalize(client), Binder.Disconnect(_options, client));
    }
}
