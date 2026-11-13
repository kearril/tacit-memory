using System.ComponentModel;
using ModelContextProtocol.Server;
using TacitMemory.Core;

namespace TacitMemory.Host.Mcp;

/// <summary>MCP resources. Each is computed by the service from its current state when it is read.</summary>
[McpServerResourceType]
internal sealed class MemoryResources(MemoryService service)
{
    [McpServerResource(UriTemplate = "tacit://consolidation-report", Name = "consolidation-report", MimeType = "text/markdown")]
    [Description("The memory consolidation report, computed when read: the repository's path, today's date, the last consolidation and the problems found. Read it only while running the tacit-consolidate skill.")]
    public Task<string> ConsolidationReport(CancellationToken cancellationToken) =>
        service.ConsolidationReportAsync(cancellationToken);
}
