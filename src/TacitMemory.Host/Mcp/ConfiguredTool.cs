using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TacitMemory.Core;

namespace TacitMemory.Host.Mcp;

/// <summary>
/// A memory tool published with the configured limits in its descriptions. Only the text changes: the SDK's tool keeps its
/// parameters, annotations and the call itself.
/// </summary>
internal sealed class ConfiguredTool(McpServerTool inner, LimitsConfig limits) : DelegatingMcpServerTool(inner)
{
    private readonly Tool _protocolTool = Filled(inner.ProtocolTool, limits);

    public override Tool ProtocolTool => _protocolTool;

    /// <summary>The tool with its placeholders filled, in its description and in its parameters' descriptions alike.</summary>
    private static Tool Filled(Tool tool, LimitsConfig limits)
    {
        var json = JsonSerializer.Serialize(tool, McpJsonUtilities.DefaultOptions);
        return JsonSerializer.Deserialize<Tool>(ToolCatalog.Fill(json, limits), McpJsonUtilities.DefaultOptions)!;
    }
}
