using ModelContextProtocol.Client;
using TacitMemory.Core;
using TacitMemory.Core.Tests;

namespace TacitMemory.Mcp.Tests;

public sealed class McpHostTests : IAsyncLifetime
{
    private readonly TestHome _home = new();
    private MemoryService _service = null!;
    private McpHost _host = null!;

    public async Task InitializeAsync()
    {
        _home.InitReady();
        Directory.CreateDirectory(Path.Combine(_home.DefaultMemory, "pages", "tacit-memory"));
        _service = _home.Tacit().OpenService(_ => { });
        _host = await McpHost.TryStartAsync(_service, port: 0) ?? throw new InvalidOperationException("Port 0 is never in use.");
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        _service.Dispose();
        _home.Dispose();
    }

    private Task<McpClient> ConnectAsync() =>
        McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = _host.Endpoint,
            TransportMode = HttpTransportMode.StreamableHttp,
        }));

    [Fact]
    public async Task A_client_receives_the_server_instructions_when_it_connects()
    {
        await using var client = await ConnectAsync();

        Assert.Equal("tacit", client.ServerInfo?.Name);
        var instructions = Assert.IsType<string>(client.ServerInstructions);
        Assert.StartsWith("TacitMemory is the user's personal memory across sessions.", instructions);
        Assert.Contains("Memory is written in Chinese.", instructions);
        Assert.Contains("## When to recall", instructions);
        Assert.Contains("## Writing pages", instructions);
        Assert.Contains("## Worth recording\n- 用户纠正或打断 Agent", instructions);
        Assert.EndsWith("## Projects\ntacit-memory", instructions);
    }

    [Fact]
    public async Task Recall_and_write_page_are_listed_with_their_annotations_and_prompts_are_empty()
    {
        await using var client = await ConnectAsync();

        var tools = (await client.ListToolsAsync()).ToDictionary(tool => tool.Name);

        Assert.Equal(["recall", "write_page"], tools.Keys.Order());
        var recall = tools["recall"].ProtocolTool;
        Assert.True(recall.Annotations?.ReadOnlyHint);
        Assert.True(recall.Annotations?.IdempotentHint);
        Assert.False(recall.Annotations?.OpenWorldHint);
        Assert.True(recall.Meta?["anthropic/alwaysLoad"]?.GetValue<bool>());
        var write = tools["write_page"].ProtocolTool;
        Assert.False(write.Annotations?.ReadOnlyHint);
        Assert.True(write.Annotations?.DestructiveHint);
        Assert.False(write.Annotations?.IdempotentHint);
        Assert.False(write.Annotations?.OpenWorldHint);
        Assert.Empty(await client.ListPromptsAsync());
    }

    [Fact]
    public async Task A_page_written_through_the_tool_reads_back_through_recall()
    {
        await using var client = await ConnectAsync();

        var written = await client.CallToolAsync("write_page", new Dictionary<string, object?>
        {
            ["op"] = "create",
            ["page"] = "tacit-memory/db-upgrade",
            ["title"] = "数据库升级后扩展丢失",
            ["aliases"] = new[] { "全文检索插件失效", "升级后搜索不到中文" },
            ["sections"] = new[] { new Dictionary<string, string> { ["name"] = "根因", ["body"] = "扩展没有重装。" } },
        });
        var read = await client.CallToolAsync("recall", new Dictionary<string, object?> { ["page"] = "tacit-memory/db-upgrade" });

        Assert.NotEqual(true, written.IsError);
        Assert.NotEqual(true, read.IsError);
        Assert.Contains("## 根因 [", Text(read));
        Assert.Contains("扩展没有重装。", Text(read));
    }

    [Fact]
    public async Task An_upsert_with_the_hash_from_recall_replaces_the_section_through_the_tool()
    {
        await using var client = await ConnectAsync();
        await client.CallToolAsync("write_page", new Dictionary<string, object?>
        {
            ["op"] = "create",
            ["page"] = "tacit-memory/db-upgrade",
            ["title"] = "数据库升级后扩展丢失",
            ["aliases"] = new[] { "全文检索插件失效", "升级后搜索不到中文" },
            ["sections"] = new[] { new Dictionary<string, string> { ["name"] = "根因", ["body"] = "扩展没有重装。" } },
        });
        var recalled = Text(await client.CallToolAsync("recall", new Dictionary<string, object?> { ["page"] = "tacit-memory/db-upgrade" }));
        var hash = System.Text.RegularExpressions.Regex.Match(recalled, @"## 根因 \[([0-9a-f]{6})\]").Groups[1].Value;

        var written = await client.CallToolAsync("write_page", new Dictionary<string, object?>
        {
            ["op"] = "upsert_section",
            ["page"] = "tacit-memory/db-upgrade",
            ["section"] = "根因",
            ["body"] = "扩展没有随升级重装。",
            ["hash"] = hash,
        });
        var read = await client.CallToolAsync("recall", new Dictionary<string, object?> { ["page"] = "tacit-memory/db-upgrade" });

        Assert.NotEqual(true, written.IsError);
        Assert.Contains("Replaced section 根因 [", Text(written));
        Assert.Contains("扩展没有随升级重装。", Text(read));
        Assert.DoesNotContain("扩展没有重装。", Text(read));
    }

    [Fact]
    public async Task Add_aliases_through_the_tool_reaches_the_page_header()
    {
        await using var client = await ConnectAsync();
        await client.CallToolAsync("write_page", new Dictionary<string, object?>
        {
            ["op"] = "create",
            ["page"] = "tacit-memory/db-upgrade",
            ["title"] = "数据库升级后扩展丢失",
            ["aliases"] = new[] { "全文检索插件失效", "升级后搜索不到中文" },
            ["sections"] = new[] { new Dictionary<string, string> { ["name"] = "根因", ["body"] = "扩展没有重装。" } },
        });

        var written = await client.CallToolAsync("write_page", new Dictionary<string, object?>
        {
            ["op"] = "update_header",
            ["page"] = "tacit-memory/db-upgrade",
            ["add_aliases"] = new[] { "PGroonga 失效" },
        });
        var read = await client.CallToolAsync("recall", new Dictionary<string, object?> { ["page"] = "tacit-memory/db-upgrade" });

        Assert.NotEqual(true, written.IsError);
        Assert.Contains("Aliases: 全文检索插件失效; 升级后搜索不到中文; PGroonga 失效", Text(read));
    }

    [Fact]
    public async Task A_refused_call_is_marked_as_an_error_and_names_the_missing_parameter()
    {
        await using var client = await ConnectAsync();

        var result = await client.CallToolAsync("write_page", new Dictionary<string, object?> { ["page"] = "tacit-memory/x" });

        Assert.True(result.IsError);
        Assert.StartsWith("Refused: Missing parameter op.\nNext step: ", Text(result));
    }

    private static string Text(ModelContextProtocol.Protocol.CallToolResult result) =>
        string.Concat(result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(block => block.Text));

    [Fact]
    public async Task A_second_service_on_a_taken_port_is_refused()
    {
        Assert.Equal("127.0.0.1", _host.Endpoint.Host);
        Assert.Null(await McpHost.TryStartAsync(_service, _host.Endpoint.Port));
    }
}
