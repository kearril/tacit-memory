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
        """)]
    public async Task<CallToolResult> WritePage(
        [Description("Operation: create.")] string? op = null,
        [Description("Page ID: <project>/<slug>. project is the code repository's name in lowercase, or _topics for a subject that belongs to no project; slug is lowercase words joined by hyphens.")] string? page = null,
        [Description("Page title, one line.")] string? title = null,
        [Description("Other names the user might call this by: spoken phrasings, Chinese and English names, nicknames. At least 2.")] string[]? aliases = null,
        [Description("The page's sections in order; each has a name (the ## heading, without '##') and a Markdown body.")] PageSectionInput[]? sections = null,
        [Description("Page IDs of related pages; each must exist.")] string[]? related = null,
        CancellationToken cancellationToken = default) =>
        Reply(await service.WritePageAsync(new WritePageRequest
        {
            Op = op,
            Page = page,
            Title = title,
            Aliases = aliases,
            Sections = sections,
            Related = related,
        }, cancellationToken));

    private static CallToolResult Reply(ToolResult result) =>
        new() { Content = [new TextContentBlock { Text = result.Text }], IsError = result.IsError };
}
