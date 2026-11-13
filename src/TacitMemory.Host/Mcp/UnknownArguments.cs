using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace TacitMemory.Host.Mcp;

/// <summary>
/// The argument names a call sent that its tool's input schema does not declare. The SDK drops such names while it binds
/// the parameters, so they are read from the raw call. The schema is the only list of known names; values are never looked at.
/// </summary>
internal static class UnknownArguments
{
    public static IReadOnlyList<string> Of(RequestContext<CallToolRequestParams> request)
    {
        var given = request.Params?.Arguments;
        if (given is null || given.Count == 0)
            return [];

        var declared = DeclaredNames(request.MatchedPrimitive as McpServerTool ?? throw new InvalidOperationException("The call matched no tool."));
        return given.Keys.Where(name => !declared.Contains(name)).ToList();
    }

    private static HashSet<string> DeclaredNames(McpServerTool tool) =>
        tool.ProtocolTool.InputSchema.TryGetProperty("properties", out var properties)
            ? properties.EnumerateObject().Select(property => property.Name).ToHashSet()
            : [];
}
