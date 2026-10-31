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
}
