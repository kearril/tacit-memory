namespace TacitMemory.Core;

/// <summary>Arguments of <c>tacit init</c>; unset values come from the existing config or the defaults.</summary>
public sealed record InitRequest
{
    public MemoryLanguage? Language { get; init; }

    /// <summary>Memory repository location; relative paths are taken against the current directory.</summary>
    public string? MemoryPath { get; init; }
}

/// <summary>What <c>init</c> did.</summary>
public sealed record InitResult
{
    public required MemoryLanguage Language { get; init; }

    public required string MemoryPath { get; init; }

    /// <summary>Whether a new memory repository was created and committed.</summary>
    public required bool RepositoryCreated { get; init; }

    /// <summary>Full paths of the files written by this run.</summary>
    public required IReadOnlyList<string> Created { get; init; }

    /// <summary>Full paths of the files that already existed and were left untouched.</summary>
    public required IReadOnlyList<string> Existing { get; init; }

    /// <summary>A summary for the user, in <see cref="Language"/>.</summary>
    public required string Report { get; init; }
}
