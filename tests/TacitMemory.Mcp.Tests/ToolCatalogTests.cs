using TacitMemory.Core;
using TacitMemory.Mcp;

namespace TacitMemory.Mcp.Tests;

public sealed class ToolCatalogTests
{
    private static readonly LimitsConfig DefaultLimits = Limits(itemLength: 200, minAliases: 2);

    private static LimitsConfig Limits(int itemLength, int minAliases) =>
        new(new PersonaLimits(5, 15, 10, 10), itemLength, 5, new PageLimits(50, 30, minAliases), 10, 10, 60);

    [Fact]
    public void The_write_persona_description_states_the_configured_domain_limits()
    {
        var limits = DefaultLimits with { TriggerLength = 25, DomainEntries = 4, Domains = 3 };

        var description = ToolCatalog.Describe(limits).ToDictionary(tool => tool.Name)["write_persona"].Description;

        Assert.Contains("at most 25 characters", description);
        Assert.Contains("at most 4 entries", description);
        Assert.Contains("at most 3 domains", description);
        Assert.DoesNotContain('{', description);
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
    public void The_catalog_gives_parameter_names_and_types_as_clients_see_them()
    {
        var tools = ToolCatalog.Describe(DefaultLimits).ToDictionary(tool => tool.Name);

        Assert.Contains(tools["write_page"].Parameters, parameter => parameter.Name == "op" && parameter.Type == "string");
        Assert.Contains(tools["write_page"].Parameters, parameter => parameter.Name == "aliases" && parameter.Type == "string[]");
        Assert.Contains(tools["write_persona"].Parameters, parameter => parameter.Name == "confirm_no_secret" && parameter.Type == "bool");
        Assert.DoesNotContain(tools.Values.SelectMany(tool => tool.Parameters), parameter => parameter.Type.Contains("CancellationToken"));
    }

    [Fact]
    public void The_configured_limits_fill_the_descriptions_that_name_them()
    {
        var tools = ToolCatalog.Describe(Limits(itemLength: 150, minAliases: 3)).ToDictionary(tool => tool.Name);

        Assert.Contains("At least 3.", Parameter(tools["write_page"], "aliases"));
        Assert.Contains("at most 150 characters", tools["write_recent"].Description);
        Assert.Contains("(150 characters)", tools["write_persona"].Description);
        foreach (var tool in tools.Values)
        {
            Assert.DoesNotContain("100 characters", tool.Description);
            Assert.DoesNotContain("by default", tool.Description);
            Assert.DoesNotContain('{', tool.Description);
        }
    }

    [Fact]
    public void The_default_limits_fill_the_same_descriptions()
    {
        var tools = ToolCatalog.Describe(DefaultLimits).ToDictionary(tool => tool.Name);

        Assert.Contains("At least 2.", Parameter(tools["write_page"], "aliases"));
        Assert.Contains("at most 200 characters", tools["write_recent"].Description);
        Assert.Contains("(200 characters)", tools["write_persona"].Description);
    }

    private static string Parameter(ToolDescription tool, string name) =>
        tool.Parameters.Single(parameter => parameter.Name == name).Description;
}
