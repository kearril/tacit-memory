namespace TacitMemory.Core;

/// <summary>Why a facade operation was refused.</summary>
public enum TacitError
{
    /// <summary>The memory directory has files in it but is not a Git repository.</summary>
    MemoryDirectoryNotEmpty,

    /// <summary>An option given to <c>init</c> differs from what the existing <c>config.yaml</c> says.</summary>
    ConflictsWithConfig,

    /// <summary>A <c>git</c> command failed.</summary>
    GitFailed,
}

/// <summary>A refused facade operation. <see cref="Exception.Message"/> is localized and meant for the user.</summary>
public sealed class TacitException(TacitError error, string message, Exception? inner = null) : Exception(message, inner)
{
    public TacitError Error { get; } = error;
}
