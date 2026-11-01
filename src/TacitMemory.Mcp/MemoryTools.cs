using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TacitMemory.Core;

namespace TacitMemory.Mcp;

/// <summary>The memory tools. Each one hands its arguments to <see cref="MemoryService"/>, which checks them.</summary>
[McpServerToolType]
internal sealed class MemoryTools(MemoryService service)
{
    [McpServerTool(Name = "recall", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [McpMeta("anthropic/alwaysLoad", true)]
    [Description("""
        Search the user's memory, or read one detail page in full. Give exactly one of query and page.
        Results come per page with the matching sections in full and a short code for each section.
        """)]
    public async Task<CallToolResult> Recall(
        [Description("What to look for, in natural language and in the memory language; names, terms and file names as written.")] string? query = null,
        [Description("Page ID to read in full: <project>/<slug>, for example my-app/db-upgrade.")] string? page = null,
        [Description("Search only this project.")] string? project = null,
        [Description("Earliest date, YYYY-MM-DD; convert phrases like \"last week\" into dates.")] string? from = null,
        [Description("Latest date, YYYY-MM-DD.")] string? to = null,
        [Description("pages or activities; both when omitted.")] string? kind = null,
        CancellationToken cancellationToken = default) =>
        Reply(await service.RecallAsync(
            new RecallRequest { Query = query, Page = page, Project = project, From = from, To = to, Kind = kind },
            cancellationToken));

    [McpServerTool(Name = "write_page", ReadOnly = false, Idempotent = false, Destructive = true, OpenWorld = false)]
    [Description("""
        Write a detail page: one page per matter, organized in ## sections. Pages are committed to the memory repository.
        op=create: new page; needs page, title, aliases, sections; related is optional.
        op=upsert_section: needs page, section and body; hash is required when the section exists (its short code from recall).
        op=delete_section: needs page, section and hash (its short code from recall).
        op=update_header: needs page and at least one of title, add_aliases, related; related replaces the whole list ([] clears it).
        Writes are checked for secrets (passwords, keys, tokens); never store one.
        """)]
    public async Task<CallToolResult> WritePage(
        [Description("Operation: create, upsert_section, delete_section or update_header.")] string? op = null,
        [Description("Page ID: <project>/<slug>. project is the code repository's name in lowercase, or _topics for a subject that belongs to no project; slug is lowercase words joined by hyphens.")] string? page = null,
        [Description("Page title, one line.")] string? title = null,
        [Description("Other names the user might call this by: spoken phrasings, Chinese and English names, nicknames. At least 2.")] string[]? aliases = null,
        [Description("The page's sections in order; each has a name (the ## heading, without '##') and a Markdown body.")] PageSectionInput[]? sections = null,
        [Description("Page IDs of related pages; each must exist.")] string[]? related = null,
        [Description("The section's name: its ## heading text, without '##'; one line.")] string? section = null,
        [Description("The section's Markdown body; no '#' or '##' headings, use '###' or deeper.")] string? body = null,
        [Description("Short code in brackets from the latest recall; required to replace or delete an existing section.")] string? hash = null,
        [Description("Aliases to add to the page; ones it already has are ignored.")] string[]? add_aliases = null,
        [Description("true only after a write was refused as a possible secret and you made sure with the user that it holds none; skips the secret check.")] bool? confirm_no_secret = null,
        CancellationToken cancellationToken = default) =>
        Reply(await service.WritePageAsync(new WritePageRequest
        {
            Op = op,
            Page = page,
            Title = title,
            Aliases = aliases,
            Sections = sections,
            Related = related,
            Section = section,
            Body = body,
            Hash = hash,
            AddAliases = add_aliases,
            ConfirmNoSecret = confirm_no_secret,
        }, cancellationToken));

    [McpServerTool(Name = "write_recent", ReadOnly = false, Idempotent = false, Destructive = true, OpenWorld = false)]
    [Description("""
        Record an activity in the recent activity log: one line under the day's heading in the month's file.
        op=add_activity: needs text, one line of at most 100 characters that does not start with '['; project and source are optional.
        With replaces (a short code of today's activity), rewrites that activity in place instead of adding one; source then replaces its comment only if given.
        Writes are checked for secrets (passwords, keys, tokens); never store one.
        """)]
    public async Task<CallToolResult> WriteRecent(
        [Description("Operation: add_activity. update_mainline is not available yet.")] string? op = null,
        [Description("The activity in one line, in the memory language; without a [project] prefix, which the project parameter adds.")] string? text = null,
        [Description("The project the activity belongs to: the code repository's name in lowercase, for example my-app.")] string? project = null,
        [Description("The user's own words behind the activity, one line; kept as a comment under the entry.")] string? source = null,
        [Description("The short code of an activity from today to rewrite with this text, from a refusal's list or from recall. Only today's activities match.")] string? replaces = null,
        [Description("Not available yet: mainline items, for update_mainline.")] string[]? items = null,
        [Description("Not available yet: the mainline's short code from recall, for update_mainline.")] string? hash = null,
        [Description("true only after a write was refused as a possible secret and you made sure with the user that it holds none; skips the secret check.")] bool? confirm_no_secret = null,
        CancellationToken cancellationToken = default) =>
        Reply(await service.WriteRecentAsync(new WriteRecentRequest
        {
            Op = op,
            Text = text,
            Project = project,
            Source = source,
            Replaces = replaces,
            Items = items,
            Hash = hash,
            ConfirmNoSecret = confirm_no_secret,
        }, cancellationToken));

    private static CallToolResult Reply(ToolResult result) =>
        new() { Content = [new TextContentBlock { Text = result.Text }], IsError = result.IsError };
}
