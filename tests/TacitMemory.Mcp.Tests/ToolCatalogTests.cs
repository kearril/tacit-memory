using TacitMemory.Mcp;

namespace TacitMemory.Mcp.Tests;

public sealed class ToolCatalogTests
{
    [Fact]
    public void The_catalog_lists_every_memory_tool_with_its_parameters_described()
    {
        var tools = ToolCatalog.Describe().ToDictionary(tool => tool.Name);

        Assert.Equal(["recall", "write_page", "write_persona", "write_recent"], tools.Keys.Order());
        foreach (var tool in tools.Values)
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Description), tool.Name);
            Assert.NotEmpty(tool.Parameters);
            Assert.All(tool.Parameters, parameter => Assert.False(string.IsNullOrWhiteSpace(parameter.Description), $"{tool.Name}.{parameter.Name}"));
        }
    }

    [Fact]
    public void The_catalog_gives_parameter_names_and_types_as_clients_see_them()
    {
        var tools = ToolCatalog.Describe().ToDictionary(tool => tool.Name);

        Assert.Contains(tools["write_page"].Parameters, parameter => parameter.Name == "op" && parameter.Type == "string");
        Assert.Contains(tools["write_recent"].Parameters, parameter => parameter.Name == "items" && parameter.Type == "string[]");
        Assert.Contains(tools["write_persona"].Parameters, parameter => parameter.Name == "confirm_no_secret" && parameter.Type == "bool");
        Assert.DoesNotContain(tools.Values.SelectMany(tool => tool.Parameters), parameter => parameter.Type.Contains("CancellationToken"));
    }
}
