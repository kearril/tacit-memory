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

    /// <summary>The section to write, by name: for <c>upsert_section</c> and <c>delete_section</c>.</summary>
    public string? Section { get; init; }

    /// <summary>The Markdown body for <c>upsert_section</c>; the section's text, without its <c>##</c> line.</summary>
    public string? Body { get; init; }

    /// <summary>The short code the agent last read for the section; required to replace or delete it.</summary>
    public string? Hash { get; init; }

    /// <summary>Aliases to add for <c>update_header</c>; the page keeps the aliases it has.</summary>
    public IReadOnlyList<string>? AddAliases { get; init; }

    /// <summary>
    /// The agent made sure with the user that the write holds no secret: the credential check is skipped. For
    /// <c>create</c>, <c>upsert_section</c> and <c>update_header</c>.
    /// </summary>
    public bool? ConfirmNoSecret { get; init; }
}

/// <summary>
/// A <c>write_recent</c> call as the agent made it. The parameters are flat; which ones are required depends on
/// <see cref="Op"/>, and the service checks them.
/// </summary>
public sealed record WriteRecentRequest
{
    public string? Op { get; init; }

    /// <summary>The activity's text: one line, without the project label.</summary>
    public string? Text { get; init; }

    /// <summary>The project the activity belongs to; lowercased before it is checked.</summary>
    public string? Project { get; init; }

    /// <summary>The user's own words behind the activity, kept as a comment under the entry.</summary>
    public string? Source { get; init; }

    /// <summary>The activity this one replaces; not available yet.</summary>
    public string? Replaces { get; init; }

    /// <summary>The whole mainline for <c>update_mainline</c>, in order; an empty list clears it.</summary>
    public IReadOnlyList<string>? Items { get; init; }

    /// <summary>The version code of the mainline the agent read, required by <c>update_mainline</c>.</summary>
    public string? Hash { get; init; }

    /// <summary>
    /// The agent made sure with the user that the write holds no secret: the credential check is skipped.
    /// </summary>
    public bool? ConfirmNoSecret { get; init; }
}
