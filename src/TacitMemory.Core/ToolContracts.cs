namespace TacitMemory.Core;

/// <summary>
/// What a memory tool hands back to the agent: plain Markdown-style text. <see cref="IsError"/> marks a refusal,
/// whose text reads "Refused / Next step / Related". <see cref="Written"/> is true when the call changed a memory file.
/// </summary>
public sealed record ToolResult(string Text, bool IsError, bool Written = false);

/// <summary>
/// A <c>recall</c> call as the agent made it; exactly one of query and page is given, unless the kind is <c>persona</c> or
/// <c>rules</c>, which take neither. The service checks the arguments.
/// </summary>
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

    /// <summary><c>pages</c>, <c>activities</c>, <c>rules</c> or <c>persona</c>; pages and activities together when absent.</summary>
    public string? Kind { get; init; }

    /// <summary>The domain label whose rules to read; only with <c>kind=rules</c>, and absent for every domain.</summary>
    public string? Domain { get; init; }

    /// <summary>
    /// Parameter names the call gave that the tool's input schema does not declare, as the agent wrote them. The service
    /// refuses the call and names each one; the values are never looked at. Empty when every name is known.
    /// </summary>
    public IReadOnlyList<string>? UnknownParameters { get; init; }
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

    /// <summary>The page's type: one of <see cref="ToolValues.PageTypes"/>. Required by <c>create</c>; <c>update_header</c> may change it.</summary>
    public string? Type { get; init; }

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

    /// <summary>
    /// Parameter names the call gave that the tool's input schema does not declare, as the agent wrote them. The service
    /// refuses the call and names each one; the values are never looked at. Empty when every name is known.
    /// </summary>
    public IReadOnlyList<string>? UnknownParameters { get; init; }
}

/// <summary>
/// A <c>write_recent</c> call as the agent made it. Only <see cref="Text"/> is required; the service checks it.
/// </summary>
public sealed record WriteRecentRequest
{
    /// <summary>The activity's text: one line, without the project label.</summary>
    public string? Text { get; init; }

    /// <summary>The project the activity belongs to; lowercased before it is checked.</summary>
    public string? Project { get; init; }

    /// <summary>The user's own words behind the activity, kept as a comment under the entry.</summary>
    public string? Source { get; init; }

    /// <summary>The short code of today's activity this one rewrites in place.</summary>
    public string? Replaces { get; init; }

    /// <summary>
    /// The agent made sure with the user that the write holds no secret: the credential check is skipped.
    /// </summary>
    public bool? ConfirmNoSecret { get; init; }

    /// <summary>
    /// Parameter names the call gave that the tool's input schema does not declare, as the agent wrote them. The service
    /// refuses the call and names each one; the values are never looked at. Empty when every name is known.
    /// </summary>
    public IReadOnlyList<string>? UnknownParameters { get; init; }
}

/// <summary>
/// A <c>write_persona</c> call as the agent made it. The parameters are flat; which ones are required depends on
/// <see cref="Op"/>, and the service checks them.
/// </summary>
public sealed record WritePersonaRequest
{
    public string? Op { get; init; }

    /// <summary>The section to write to: <c>red_lines</c>, <c>working_rules</c>, <c>preferences</c> or <c>profile</c>.</summary>
    public string? Section { get; init; }

    /// <summary>The entry's text: one line, without its source comment.</summary>
    public string? Text { get; init; }

    /// <summary>The user's own words behind the entry, one line; kept as a comment under it.</summary>
    public string? Source { get; init; }

    /// <summary>The short code of an existing entry; for the operations that change one.</summary>
    public string? Hash { get; init; }

    /// <summary>
    /// The scope the entry goes into: <c>global</c> when omitted, <c>project:&lt;name&gt;</c> or <c>domain:&lt;label&gt;</c>.
    /// Used by add only.
    /// </summary>
    public string? Scope { get; init; }

    /// <summary>
    /// The trigger of a domain: one line saying when the domain applies. Required when add creates a new domain, and ignored
    /// for a domain that exists and for the other scopes. Used by add only.
    /// </summary>
    public string? Trigger { get; init; }

    /// <summary>
    /// The agent made sure with the user that the write holds no secret: the credential check is skipped.
    /// </summary>
    public bool? ConfirmNoSecret { get; init; }

    /// <summary>
    /// Parameter names the call gave that the tool's input schema does not declare, as the agent wrote them. The service
    /// refuses the call and names each one; the values are never looked at. Empty when every name is known.
    /// </summary>
    public IReadOnlyList<string>? UnknownParameters { get; init; }
}

/// <summary>A memory tool as the MCP server describes it to clients.</summary>
/// <param name="Name">The tool's name.</param>
/// <param name="Description">What the tool is for, as clients receive it.</param>
/// <param name="Parameters">The tool's parameters in order; the cancellation token is not one of them.</param>
public sealed record ToolDescription(string Name, string Description, IReadOnlyList<ParameterDescription> Parameters);

/// <summary>One parameter of a memory tool: its name, its type and what it is for.</summary>
public sealed record ParameterDescription(string Name, string Type, string Description);
