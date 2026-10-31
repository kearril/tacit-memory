namespace TacitMemory.Core;

/// <summary>Why a facade operation was refused.</summary>
public enum TacitError
{
    /// <summary>The memory directory has files in it but is not a Git repository.</summary>
    MemoryDirectoryNotEmpty,

    /// <summary>An option given to <c>init</c> differs from what the existing <c>config.yaml</c> says.</summary>
    ConflictsWithConfig,

    /// <summary><c>config.yaml</c> or the memory repository does not exist yet; <c>tacit init</c> creates them.</summary>
    NotInitialized,

    /// <summary><c>config.yaml</c> cannot be parsed or has wrong settings; the message names every key path.</summary>
    ConfigInvalid,

    /// <summary><c>rules.md</c> breaks its limits.</summary>
    RulesInvalid,

    /// <summary>A context file's resident block markers are incomplete or repeated.</summary>
    MarkersBroken,

    /// <summary>A client's MCP config cannot be read as JSON.</summary>
    ClientConfigInvalid,

    /// <summary>A <c>git</c> command failed.</summary>
    GitFailed,
}

/// <summary>A refused facade operation. <see cref="Exception.Message"/> is localized and meant for the user.</summary>
public sealed class TacitException(TacitError error, string message, Exception? inner = null) : Exception(message, inner)
{
    public TacitError Error { get; } = error;
}
