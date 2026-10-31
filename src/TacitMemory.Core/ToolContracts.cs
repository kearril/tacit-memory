namespace TacitMemory.Core;

/// <summary>
/// What a memory tool hands back to the agent: plain Markdown-style text. <see cref="IsError"/> marks a refusal,
/// whose text reads "Refused / Next step / Related".
/// </summary>
public sealed record ToolResult(string Text, bool IsError);

/// <summary>A <c>recall</c> call as the agent made it; exactly one of query and page is given. The service checks the arguments.</summary>
public sealed record RecallRequest
{
    /// <summary>What to look for, in natural language.</summary>
    public string? Query { get; init; }

    /// <summary>Page ID to read in full: <c>&lt;project&gt;/&lt;slug&gt;</c>.</summary>
    public string? Page { get; init; }

    /// <summary>Search only this project.</summary>
    public string? Project { get; init; }

    /// <summary>Earliest date, <c>YYYY-MM-DD</c>, inclusive.</summary>
    public string? From { get; init; }

    /// <summary>Latest date, <c>YYYY-MM-DD</c>, inclusive.</summary>
    public string? To { get; init; }

    /// <summary><c>pages</c> or <c>activities</c>; both when absent.</summary>
    public string? Kind { get; init; }
}

/// <summary>One section of a page: its <c>##</c> name and its Markdown body.</summary>
public sealed record PageSectionInput(string Name, string Body);

/// <summary>
/// A <c>write_page</c> call as the agent made it. The parameters are flat; which ones are required depends on
/// <see cref="Op"/>, and the service checks them.
/// </summary>
public sealed record WritePageRequest
{
    public string? Op { get; init; }

    /// <summary>Page ID: <c>&lt;project&gt;/&lt;slug&gt;</c>.</summary>
    public string? Page { get; init; }

    public string? Title { get; init; }

    public IReadOnlyList<string>? Aliases { get; init; }

    public IReadOnlyList<PageSectionInput>? Sections { get; init; }

    /// <summary>Page IDs of related pages; every one must exist.</summary>
    public IReadOnlyList<string>? Related { get; init; }
}
