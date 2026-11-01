using TacitMemory.Core.Clients;
using TacitMemory.Core.Configuration;
using TacitMemory.Core.Setup;
using TacitMemory.Core.Skills;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Show;

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
    /// Writes <c>skills/tacit-memory/SKILL.md</c> from the built-in text and <c>prompts/skill.md</c>. Needs no running
    /// service, and leaves the file alone when its content is unchanged. Refuses a <c>skill.md</c> that breaks its limits.
    /// </summary>
    public SkillResult GenerateSkill() =>
        MemorySkill.Write(options.TacitHome, PromptFiles.ReadSkill(options.TacitHome, ConfigLoader.LanguageOf(options)));

    /// <summary>
    /// What <c>tacit show</c> prints for the server instructions, the resident block, the memory skill or the
    /// <c>remember</c> prompt: each part with its source and length. Reads the same sources the service reads and needs no
    /// running service. Loading the config may create <c>prompts/</c> and rewrite a missing <c>rules.md</c>, as the service
    /// does at startup; nothing else is written. With <paramref name="useDefault"/> it returns only the built-in text, which
    /// can be saved as the file that replaces it, and then needs no config.
    /// </summary>
    public ShowResult Show(ShowTarget target, bool useDefault = false)
    {
        if (!useDefault)
            return Viewer.Show(options, LoadConfig(), target);
        if (target == ShowTarget.Remember)
            throw NoBuiltInText("remember");
        return Viewer.Default(target);
    }

    /// <summary>
    /// What <c>tacit show tools</c> prints, from the tool descriptions the caller reads from <c>TacitMemory.Mcp</c>. Only the
    /// language of the memory is read from the config, and falls back to the system UI language when it does not load.
    /// </summary>
    public ShowResult ShowTools(IReadOnlyList<ToolDescription> tools, bool useDefault = false)
    {
        if (useDefault)
            throw NoBuiltInText("tools");
        return Viewer.Tools(ConfigLoader.LanguageOf(options), tools);
    }

    private TacitException NoBuiltInText(string target) =>
        new(TacitError.NoBuiltInText, Messages.NoBuiltInText.Format(ConfigLoader.LanguageOf(options), target));

    /// <summary>
    /// Binds a client: renders the resident block into its context file now, registers the MCP server and records
    /// the binding. Running it again changes nothing.
    /// </summary>
    public BindingResult Connect(ClientKind client) => Binder.Connect(options, client);

    /// <summary>Removes the resident block, the MCP registration and the binding record of a client.</summary>
    public BindingResult Disconnect(ClientKind client) => Binder.Disconnect(options, client);

    /// <summary>Renders the current persona and <c>resident.md</c> into every bound context file, reading <c>bindings.json</c> afresh.</summary>
    public IReadOnlyList<BlockRender> RenderBlocks()
    {
        var config = LoadConfig();
        return Binder.RenderAll(options, config, config.Prompts);
    }

    /// <summary>
    /// Starts the memory service's state: loads the config, renders every bound context file once, and watches the
    /// memory repository. <paramref name="notice"/> receives, on a background thread, one line for the user per
    /// context file rendered after <c>persona.md</c> changes, or the reason rendering failed.
    /// </summary>
    public MemoryService OpenService(Action<string> notice) => new(options, LoadConfig(), notice);
}
