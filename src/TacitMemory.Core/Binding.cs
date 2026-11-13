namespace TacitMemory.Core;

/// <summary>What <c>connect</c> asks for: the client's name, its context file and its skills folder, as the user gave them.</summary>
/// <param name="Client">The client's name. It is lower-cased, and only letters, digits and hyphens up to 32 characters are accepted.</param>
/// <param name="ContextFile">The context file the resident block goes into. A leading <c>~</c> is the user's home directory.</param>
/// <param name="SkillsFolder">The folder the client scans for skills, where the skill links go. A leading <c>~</c> is the user's home directory.</param>
public sealed record ConnectRequest(string Client, string ContextFile, string SkillsFolder);

/// <summary>What <c>connect</c> or <c>disconnect</c> did, as a summary for the user.</summary>
/// <param name="ContextFile">The bound context file, as an absolute path.</param>
/// <param name="Endpoint">The MCP endpoint the user configures in the client; <c>null</c> for <c>disconnect</c>.</param>
/// <param name="Before">The summary lines before the endpoint block, in the memory language.</param>
/// <param name="EndpointBlock">The endpoint, the transport and the suggested server name, one line each; empty for <c>disconnect</c>.</param>
/// <param name="After">The summary lines after the endpoint block, in the memory language.</param>
public sealed record BindingResult(string ContextFile, Uri? Endpoint, IReadOnlyList<string> Before, IReadOnlyList<string> EndpointBlock, IReadOnlyList<string> After)
{
    /// <summary>The summary for the user: every line in order, one per line, as the plain output prints it.</summary>
    public string Report => string.Join(Environment.NewLine, Before.Concat(EndpointBlock).Concat(After));
}

/// <summary>What the management API returns for a bind or an unbind.</summary>
/// <param name="Client">The client's name as recorded: lower-cased.</param>
/// <param name="Lines">The summary lines in the memory language, without the endpoint block, which <paramref name="Mcp"/> carries.</param>
/// <param name="Mcp">The MCP server the user registers in the client; <c>null</c> for an unbind.</param>
public sealed record BindingDone(string Client, IReadOnlyList<string> Lines, McpServer? Mcp)
{
    internal static BindingDone From(string client, BindingResult result) =>
        new(client, [.. result.Before, .. result.After], result.Endpoint is null ? null : new McpServer(result.Endpoint, McpServer.StreamableHttp, McpServer.SuggestedName));
}

/// <summary>Where a client reaches the MCP server: the endpoint, the transport and the suggested server name.</summary>
public sealed record McpServer(Uri Url, string Transport, string Name)
{
    internal const string StreamableHttp = "Streamable HTTP";
    internal const string SuggestedName = "tacit";
}

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
