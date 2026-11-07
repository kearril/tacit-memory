using Microsoft.Extensions.Logging;
using TacitMemory.Core.Clients;
using TacitMemory.Core.Configuration;
using TacitMemory.Core.Setup;
using TacitMemory.Core.Skills;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Show;
using TacitMemory.Core.Status;

namespace TacitMemory.Core;

/// <summary>The facade: the only entry point into TacitMemory's memory rules.</summary>
public sealed class Tacit(TacitOptions options)
{
    /// <summary>
    /// Creates the config templates and the memory repository. Existing files are never overwritten; an existing
    /// repository only gets its missing skeleton files, uncommitted.
    /// </summary>
    public Task<InitResult> InitAsync(InitRequest request, CancellationToken cancellationToken = default) =>
        Initializer.RunAsync(options, request, cancellationToken);

    /// <summary>
    /// Loads and validates <c>config.yaml</c> and the prompt documents in <c>prompts/</c>, writing <c>prompts/rules.md</c>
    /// again from the template if it was deleted.
    /// </summary>
    public TacitConfig LoadConfig() => ConfigLoader.Load(options);

    /// <summary>
    /// Writes every skill under <c>skills/</c>: the memory skill, from the built-in text and <c>prompts/skill.md</c>, and
    /// the two memory commands, from the built-in text alone. Needs no running service, and leaves each file alone when its
    /// content is unchanged. Refuses a <c>skill.md</c> that breaks its limits.
    /// </summary>
    public SkillResult GenerateSkills()
    {
        var language = ConfigLoader.LanguageOf(options);
        return SkillSet.Write(options.TacitHome, language, PromptFiles.ReadSkill(options.TacitHome, language));
    }

    /// <summary>
    /// What <c>tacit show</c> prints for the server instructions, the resident block, the memory skill or a memory
    /// command: each part with its source and length. Reads the same sources the service reads and needs no running
    /// service. Loading the config may create <c>prompts/</c> and rewrite a missing <c>rules.md</c>, as the service does at
    /// startup; nothing else is written. With <paramref name="useDefault"/> it returns only the built-in text, which can be
    /// saved as the file that replaces it, and then needs no config; a memory command has no file that replaces it, so it
    /// refuses. The instructions end with the checkup reminder when one is due, read from the memory repository's history;
    /// no model is called.
    /// </summary>
    public async Task<ShowResult> ShowAsync(ShowTarget target, bool useDefault = false, CancellationToken cancellationToken = default)
    {
        if (!useDefault)
            return await Viewer.Show(options, LoadConfig(), target, cancellationToken);
        if (target is ShowTarget.Remember or ShowTarget.Checkup)
            throw NoBuiltInText(target == ShowTarget.Remember ? "remember" : "checkup");
        return Viewer.Default(target);
    }

    /// <summary>
    /// What <c>tacit show tools</c> prints, from the tool descriptions the caller reads from <c>TacitMemory.Mcp</c>. The
    /// memory language comes from <paramref name="config"/>, which the caller loads first: a config that does not load
    /// stops the command before any description is shown.
    /// </summary>
    public ShowResult ShowTools(TacitConfig config, IReadOnlyList<ToolDescription> tools) =>
        Viewer.Tools(config.Language, tools);

    private TacitException NoBuiltInText(string target) =>
        new(TacitError.NoBuiltInText, Messages.NoBuiltInText.Format(ConfigLoader.LanguageOf(options), target));

    /// <summary>
    /// Binds a client: writes the skills, renders the resident block into its context file now and links the skills into its
    /// skills folder, then records the binding. The MCP server is not registered here; the result carries the endpoint the user
    /// configures in the client. Running it again with the same paths only fills in what is missing. Refusals change no file.
    /// </summary>
    /// <exception cref="TacitException">A refusal: a bad client name, a name bound to other paths, a missing folder, a taken link name, and so on.</exception>
    public BindingResult Connect(ConnectRequest request) => Binder.Connect(options, request);

    /// <summary>Removes the resident block, the skill links and the binding record of a client; the paths come from the record.</summary>
    /// <exception cref="TacitException">No binding has that name.</exception>
    public BindingResult Disconnect(string client) => Binder.Disconnect(options, client);

    /// <summary>Renders the current persona and <c>resident.md</c> into every bound context file, reading <c>bindings.json</c> afresh.</summary>
    public IReadOnlyList<BlockRender> RenderBlocks()
    {
        var config = LoadConfig();
        return Binder.RenderAll(options, config, config.Prompts);
    }

    /// <summary>
    /// Starts the memory service's state: loads the config, renders every bound context file once, and watches the
    /// memory repository. <paramref name="notice"/> receives, on a background thread, one line for the user per
    /// context file rendered after <c>persona.md</c> changes, or the reason rendering failed. Every event is also
    /// written to the log that <paramref name="loggerFactory"/> creates, in the memory language; a caller that keeps no
    /// log passes <c>NullLoggerFactory.Instance</c>.
    /// </summary>
    public MemoryService OpenService(Action<string> notice, ILoggerFactory loggerFactory) =>
        new(options, LoadConfig(), notice, loggerFactory.CreateLogger(MemoryService.LogCategory));

    /// <summary>
    /// What <c>tacit status</c> reports when no service answers: the service's state, the shared columns and where the
    /// logs are. Needs the loaded config and reads only local files: it calls no model and touches no network.
    /// <paramref name="portAnsweredByOther"/> says another program answered on the configured port, so the report says so.
    /// </summary>
    public Task<StatusReport> OfflineStatus(TacitConfig config, bool portAnsweredByOther = false, CancellationToken cancellationToken = default) =>
        StatusReporter.OfflineAsync(options, config, portAnsweredByOther, cancellationToken);

    /// <summary>
    /// Reads the port and the memory language from <c>config.yaml</c> without validating it, for the status report of a
    /// config that does not load: the port the service is probed on, and the language of the messages. Reads no secret.
    /// </summary>
    public LooseSettings ReadLooseSettings() => ConfigLoader.ReadLoose(options);

    /// <summary>
    /// What <c>tacit status</c> reports when <c>config.yaml</c> does not load: every wrong key path, the logs location, and
    /// the service column with its endpoint. The service is shown as not running, or as held by another program when
    /// <paramref name="portAnsweredByOther"/> says so. When a service answers, the caller keeps that service's report and
    /// takes only the config column from this one.
    /// </summary>
    public StatusReport InvalidConfigStatus(TacitException error, LooseSettings settings, bool portAnsweredByOther = false) =>
        StatusReporter.InvalidConfig(options, error, settings, portAnsweredByOther);

    /// <summary>The line a command that needs the running service prints when none answers, in the memory language.</summary>
    public string ServiceNotRunningMessage(MemoryLanguage language, int port, bool portAnsweredByOther) =>
        StatusReporter.NotRunningHint(language, port, portAnsweredByOther);
}
