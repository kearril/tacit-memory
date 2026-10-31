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
    [Description("Read the user's memory. Pass page to read one detail page in full, with the short code of each section.")]
    public async Task<CallToolResult> Recall(
        [Description("Page ID to read: <project>/<slug>, for example my-app/db-upgrade.")] string? page = null,
        CancellationToken cancellationToken = default) =>
        Reply(await service.RecallAsync(new RecallRequest { Page = page }, cancellationToken));

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
