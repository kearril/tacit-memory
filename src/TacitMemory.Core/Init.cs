namespace TacitMemory.Core;

/// <summary>Arguments of <c>tacit init</c>; unset values come from the existing config or the defaults.</summary>
public sealed record InitRequest
{
    public MemoryLanguage? Language { get; init; }

    /// <summary>
    /// Asked for the language when neither <see cref="Language"/> nor the existing config names one, before anything is
    /// written. Without it, init is refused then.
    /// </summary>
    public Func<MemoryLanguage>? AskLanguage { get; init; }

    /// <summary>Memory repository location; relative paths are taken against the current directory.</summary>
    public string? MemoryPath { get; init; }

    /// <summary>
    /// A git address to join: the memory repository is cloned from it instead of created, and the config written names it in
    /// <c>sync.remote</c>. Refused when the remote is empty, the memory directory holds anything but a clone of it, or an
    /// existing config names another remote, or none.
    /// </summary>
    public string? Remote { get; init; }
}

/// <summary>What <c>init</c> did.</summary>
public sealed record InitResult
{
    public required MemoryLanguage Language { get; init; }

    public required string MemoryPath { get; init; }

    /// <summary>Whether a new memory repository was made: created and committed, or cloned from <see cref="InitRequest.Remote"/>.</summary>
    public required bool RepositoryCreated { get; init; }

    /// <summary>Full paths of the files written by this run.</summary>
    public required IReadOnlyList<string> Created { get; init; }

    /// <summary>Full paths of the files that already existed and were left untouched.</summary>
    public required IReadOnlyList<string> Existing { get; init; }

    /// <summary>A summary for the user, in <see cref="Language"/>.</summary>
    public required string Report { get; init; }
}
