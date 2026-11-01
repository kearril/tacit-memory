using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Server;
using TacitMemory.Core;

namespace TacitMemory.Mcp;

/// <summary>
/// The memory tools' definitions, read from the attributes on <see cref="MemoryTools"/> that the MCP server publishes when
/// a client lists them. Reading them here keeps <c>tacit show tools</c> in step with what clients receive.
/// </summary>
public static class ToolCatalog
{
    /// <summary>The memory tools in declaration order.</summary>
    public static IReadOnlyList<ToolDescription> Describe() =>
        typeof(MemoryTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => (Method: method, Attribute: method.GetCustomAttribute<McpServerToolAttribute>()))
            .Where(tool => tool.Attribute is not null)
            .Select(tool => new ToolDescription(
                tool.Attribute!.Name ?? tool.Method.Name,
                TextOf(tool.Method),
                tool.Method.GetParameters()
                    .Where(parameter => parameter.ParameterType != typeof(CancellationToken))
                    .Select(parameter => new ParameterDescription(parameter.Name ?? "", TypeName(parameter.ParameterType), TextOf(parameter)))
                    .ToList()))
            .ToList();

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
