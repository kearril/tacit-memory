using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TacitMemory.Core;

namespace TacitMemory.Host.Mcp;

/// <summary>
/// The memory tools' definitions, read from the attributes on <see cref="MemoryTools"/> that the MCP server publishes when
/// a client lists them. Reading them here keeps <c>tacit show tools</c> in step with what clients receive. The descriptions
/// name the configured limits through placeholders, which <see cref="Fill"/> replaces with the same values for both.
/// </summary>
public static class ToolCatalog
{
    /// <summary>The memory tools in declaration order, their descriptions filled in from <paramref name="limits"/>.</summary>
    public static IReadOnlyList<ToolDescription> Describe(LimitsConfig limits) =>
        MemberTools()
            .Select(tool => new ToolDescription(
                tool.Attribute.Name ?? tool.Method.Name,
                Fill(TextOf(tool.Method), limits),
                tool.Method.GetParameters()
                    .Where(parameter => parameter.ParameterType != typeof(CancellationToken) && parameter.ParameterType != typeof(RequestContext<CallToolRequestParams>))
                    .Select(parameter => new ParameterDescription(parameter.Name ?? "", TypeName(parameter.ParameterType), Fill(TextOf(parameter), limits)))
                    .ToList()))
            .ToList();

    /// <summary>The memory tools as the MCP server publishes them, each with its descriptions filled in from <paramref name="limits"/>.</summary>
    internal static IReadOnlyList<McpServerTool> Tools(MemoryTools target, LimitsConfig limits) =>
        MemberTools()
            .Select(tool => (McpServerTool)new ConfiguredTool(McpServerTool.Create(tool.Method, target), limits))
            .ToList();

    /// <summary>
    /// Replaces the placeholders that name a configured limit with its value: <c>{minAliases}</c>.
    /// The values are plain integers, so the same replacement is safe on the JSON text of a tool as well.
    /// </summary>
    internal static string Fill(string text, LimitsConfig limits) =>
        text.Replace("{minAliases}", limits.Page.MinAliases.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

    private static IEnumerable<(MethodInfo Method, McpServerToolAttribute Attribute)> MemberTools() =>
        typeof(MemoryTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => (Method: method, Attribute: method.GetCustomAttribute<McpServerToolAttribute>()))
            .Where(tool => tool.Attribute is not null)
            .Select(tool => (tool.Method, Attribute: tool.Attribute!));

    private static string TextOf(MemberInfo member) =>
        member.GetCustomAttribute<DescriptionAttribute>()?.Description.Trim() ?? "";

    private static string TextOf(ParameterInfo parameter) =>
        parameter.GetCustomAttribute<DescriptionAttribute>()?.Description.Trim() ?? "";

    /// <summary>A parameter type in C# spelling, with nullability dropped: <c>string[]</c>, <c>bool</c>, <c>PageSectionInput[]</c>.</summary>
    private static string TypeName(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (underlying.IsArray)
            return TypeName(underlying.GetElementType()!) + "[]";
        return underlying == typeof(string) ? "string" : underlying == typeof(bool) ? "bool" : underlying.Name;
    }
}
