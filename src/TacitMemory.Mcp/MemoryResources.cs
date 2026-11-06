using System.ComponentModel;
using ModelContextProtocol.Server;
using TacitMemory.Core;

namespace TacitMemory.Mcp;

/// <summary>MCP resources. Each is computed by the service from its current state when it is read.</summary>
[McpServerResourceType]
internal sealed class MemoryResources(MemoryService service)
{
    [McpServerResource(UriTemplate = "tacit://checkup-report", Name = "checkup-report", MimeType = "text/markdown")]
    [Description("The memory checkup report, computed when read: the repository's path, today's date, the last checkup and the problems found. Read it only while running the tacit-checkup skill.")]
    public Task<string> CheckupReport(CancellationToken cancellationToken) =>
        service.CheckupReportAsync(cancellationToken);
}
