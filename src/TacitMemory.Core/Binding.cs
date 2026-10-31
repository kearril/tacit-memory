namespace TacitMemory.Core;

/// <summary>A client TacitMemory can bind to.</summary>
public enum ClientKind
{
    /// <summary>omp, default profile: <c>~/.omp/agent/AGENTS.md</c> and <c>~/.omp/agent/mcp.json</c>.</summary>
    Omp,
}

/// <summary>What <c>connect</c> or <c>disconnect</c> did, as a summary for the user.</summary>
public sealed record BindingResult(string ContextFile, string Report);

/// <summary>What happened to one bound context file when the resident blocks were rendered.</summary>
public enum BlockState
{
    Written,
    Unchanged,

    /// <summary>The context file was deleted; it is not recreated.</summary>
    FileMissing,

    /// <summary>The block markers are incomplete or repeated; the file is left alone.</summary>
    MarkersBroken,

    /// <summary>The context file is not valid UTF-8; it is left alone.</summary>
    FileNotUtf8,

    /// <summary><c>persona.md</c> is not valid UTF-8; the last rendered block stays.</summary>
    PersonaUnreadable,
}

/// <param name="Message">A one-line summary for the user, in the memory language.</param>
public sealed record BlockRender(string ContextFile, BlockState State, string Message);
