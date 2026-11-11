using TacitMemory.Core;
using TacitMemory.Mcp;

namespace TacitMemory.Mcp.Tests;

public sealed class ToolCatalogTests
{
    private static readonly LimitsConfig DefaultLimits = Limits(minAliases: 2);

    private static LimitsConfig Limits(int minAliases) => new(3000, new PageLimits(minAliases));

    [Fact]
    public void The_write_tool_descriptions_state_no_removed_limit_number()
    {
        var tools = ToolCatalog.Describe(DefaultLimits).ToDictionary(tool => tool.Name);

        foreach (var name in new[] { "write_recent", "write_persona" })
        {
            Assert.DoesNotMatch(@"\d+ characters", tools[name].Description);
            Assert.DoesNotMatch(@"at most \d+", tools[name].Description);
            Assert.DoesNotMatch(@"\d+ entries", tools[name].Description);
        }
        foreach (var parameter in tools.Values.SelectMany(tool => tool.Parameters))
            Assert.DoesNotMatch(@"\d+ characters", parameter.Description);
        Assert.DoesNotContain('{', tools["write_persona"].Description);
    }

    [Fact]
    public void The_catalog_lists_every_memory_tool_with_its_parameters_described()
    {
        var tools = ToolCatalog.Describe(DefaultLimits).ToDictionary(tool => tool.Name);

        Assert.Equal(["recall", "write_page", "write_persona", "write_recent"], tools.Keys.Order());
        foreach (var tool in tools.Values)
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Description), tool.Name);
            Assert.NotEmpty(tool.Parameters);
            Assert.All(tool.Parameters, parameter => Assert.False(string.IsNullOrWhiteSpace(parameter.Description), $"{tool.Name}.{parameter.Name}"));
        }
    }

    [Fact]
    public void The_write_tools_state_the_credential_rule_as_where_it_is_kept()
    {
        var tools = ToolCatalog.Describe(DefaultLimits).ToDictionary(tool => tool.Name);

        foreach (var name in new[] { "write_page", "write_recent", "write_persona" })
        {
            Assert.Contains("never its value", tools[name].Description);
            Assert.DoesNotContain("never store one", tools[name].Description);
        }
    }

    [Fact]
    public void The_catalog_gives_parameter_names_and_types_as_clients_see_them()
    {
        var tools = ToolCatalog.Describe(DefaultLimits).ToDictionary(tool => tool.Name);

        Assert.Contains(tools["write_page"].Parameters, parameter => parameter.Name == "op" && parameter.Type == "string");
        Assert.Contains(tools["write_page"].Parameters, parameter => parameter.Name == "aliases" && parameter.Type == "string[]");
        Assert.Contains(tools["write_persona"].Parameters, parameter => parameter.Name == "confirm_no_secret" && parameter.Type == "bool");
        Assert.DoesNotContain(tools.Values.SelectMany(tool => tool.Parameters), parameter => parameter.Type.Contains("CancellationToken"));
    }

    [Fact]
    public void The_configured_minimum_of_aliases_fills_the_description_that_names_it()
    {
        var tools = ToolCatalog.Describe(Limits(minAliases: 3)).ToDictionary(tool => tool.Name);

        Assert.Contains("At least 3.", Parameter(tools["write_page"], "aliases"));
        foreach (var tool in tools.Values)
        {
            Assert.DoesNotContain("by default", tool.Description);
            Assert.DoesNotContain('{', tool.Description);
        }
    }

    [Fact]
    public void The_default_minimum_of_aliases_fills_the_same_description()
    {
        var tools = ToolCatalog.Describe(DefaultLimits).ToDictionary(tool => tool.Name);

        Assert.Contains("At least 2.", Parameter(tools["write_page"], "aliases"));
    }

    private static string Parameter(ToolDescription tool, string name) =>
        tool.Parameters.Single(parameter => parameter.Name == name).Description;
}
