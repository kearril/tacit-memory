using TacitMemory.Core.Clients;
using TacitMemory.Core.Configuration;
using TacitMemory.Core.Setup;

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
    /// Loads and validates <c>config.yaml</c> and <c>rules.md</c>, writing <c>rules.md</c> again from the template
    /// if it was deleted.
    /// </summary>
    public TacitConfig LoadConfig() => ConfigLoader.Load(options);

    /// <summary>
    /// Binds a client: renders the resident block into its context file now, registers the MCP server and records
    /// the binding. Running it again changes nothing.
    /// </summary>
    public BindingResult Connect(ClientKind client) => Binder.Connect(options, client);

    /// <summary>Removes the resident block, the MCP registration and the binding record of a client.</summary>
    public BindingResult Disconnect(ClientKind client) => Binder.Disconnect(options, client);

    /// <summary>Renders the current persona into every bound context file, reading <c>bindings.json</c> afresh.</summary>
    public IReadOnlyList<BlockRender> RenderBlocks() => Binder.RenderAll(options, LoadConfig());

    /// <summary>
    /// Starts the memory service's state: loads the config, renders every bound context file once, and watches the
    /// memory repository. <paramref name="rendered"/> is called, on a background thread, for each context file
    /// rendered after a change to <c>persona.md</c>.
    /// </summary>
    public MemoryService OpenService(Action<BlockRender> rendered) => new(options, LoadConfig(), rendered);
}
