using System.Text;
using System.Text.Json.Nodes;

namespace TacitMemory.Core.Tests;

public sealed class BindingTests : IDisposable
{
    private readonly TestHome _home = new();

    public BindingTests() => _home.InitReady();

    public void Dispose() => _home.Dispose();

    private string AgentsMd => Path.Combine(_home.UserHome, ".omp", "agent", "AGENTS.md");

    private string McpJson => Path.Combine(_home.UserHome, ".omp", "agent", "mcp.json");

    private string PersonaMd => Path.Combine(_home.DefaultMemory, "persona.md");

    private string BindingsJson => Path.Combine(_home.TacitHome, "bindings.json");

    private void WriteAgentsMd(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AgentsMd)!);
        File.WriteAllText(AgentsMd, text);
    }

    private const string Persona = """
        ## 红线
        - 不要自行发布版本；改为先请用户确认（理由：影响外部）
          <!-- 2026-10-25 原话「发布前必须问我」 -->

        ## 协作规则
        - 需求不清时 → 先连续追问

        ## 偏好

        ## Profile
        - 系统：Windows 11 x64
        """;

    [Fact]
    public void Connect_appends_the_resident_block_after_the_existing_content()
    {
        File.WriteAllText(PersonaMd, Persona.ReplaceLineEndings("\n"));
        WriteAgentsMd("# My rules\n- be brief\n");

        _home.Tacit().Connect(ClientKind.Omp);

        var text = File.ReadAllText(AgentsMd);
        Assert.StartsWith("# My rules\n- be brief\n\n<!-- tacit:begin", text);
        Assert.EndsWith("</user_memory>\n<!-- tacit:end -->\n", text);
        Assert.Contains("\n## Red lines\n", text);
        Assert.Contains("- 不要自行发布版本；改为先请用户确认（理由：影响外部）\n", text);
        Assert.DoesNotContain("原话", text);
        Assert.Contains("\n## Working rules\n", text);
        Assert.DoesNotContain("## Preferences", text);
        Assert.Contains("\n## Profile\n", text);
        Assert.True(text.IndexOf("## Red lines", StringComparison.Ordinal) < text.IndexOf("## Working rules", StringComparison.Ordinal));
        Assert.True(text.IndexOf("## Working rules", StringComparison.Ordinal) < text.IndexOf("## Profile", StringComparison.Ordinal));
    }

    [Fact]
    public void Connect_creates_agents_md_and_mcp_json_when_missing()
    {
        _home.Tacit().Connect(ClientKind.Omp);

        Assert.StartsWith("<!-- tacit:begin", File.ReadAllText(AgentsMd));
        var server = JsonNode.Parse(File.ReadAllText(McpJson))!["mcpServers"]!["tacit"]!;
        Assert.Equal("http", (string?)server["type"]);
        Assert.Equal("http://127.0.0.1:18888/mcp", (string?)server["url"]);
        Assert.Contains("omp", File.ReadAllText(BindingsJson));
    }

    [Fact]
    public void Connect_keeps_other_mcp_servers_and_fields_in_order()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(McpJson)!);
        File.WriteAllText(McpJson, """{ "$schema": "x", "mcpServers": { "context7": { "type": "http", "url": "https://c7.example/mcp" }, "tacit": { "type": "http", "url": "http://127.0.0.1:1/mcp" } }, "extra": [1, 2] }""");

        _home.Tacit().Connect(ClientKind.Omp);

        var json = JsonNode.Parse(File.ReadAllText(McpJson))!.AsObject();
        Assert.Equal(["$schema", "mcpServers", "extra"], json.Select(property => property.Key));
        var servers = json["mcpServers"]!.AsObject();
        Assert.Equal(["context7", "tacit"], servers.Select(property => property.Key));
        Assert.Equal("https://c7.example/mcp", (string?)servers["context7"]!["url"]);
        Assert.Equal("http://127.0.0.1:18888/mcp", (string?)servers["tacit"]!["url"]);
    }

    [Fact]
    public void Connecting_again_changes_nothing()
    {
        WriteAgentsMd("# My rules\n");
        _home.Tacit().Connect(ClientKind.Omp);
        var agentsWritten = File.GetLastWriteTimeUtc(AgentsMd);
        var mcpWritten = File.GetLastWriteTimeUtc(McpJson);
        var agents = File.ReadAllBytes(AgentsMd);
        Thread.Sleep(50);

        _home.Tacit().Connect(ClientKind.Omp);

        Assert.Equal(agents, File.ReadAllBytes(AgentsMd));
        Assert.Equal(agentsWritten, File.GetLastWriteTimeUtc(AgentsMd));
        Assert.Equal(mcpWritten, File.GetLastWriteTimeUtc(McpJson));
        Assert.Single(JsonNode.Parse(File.ReadAllText(BindingsJson))!["bindings"]!.AsArray());
    }

    [Fact]
    public void Rendering_replaces_only_the_block_and_keeps_every_other_byte()
    {
        WriteAgentsMd("# Before\r\nline\r\n");
        _home.Tacit().Connect(ClientKind.Omp);
        File.AppendAllText(AgentsMd, "\r\n# After\r\n");
        File.WriteAllText(PersonaMd, Persona.ReplaceLineEndings("\n"));

        var results = _home.Tacit().RenderBlocks();

        Assert.Equal(BlockState.Written, Assert.Single(results).State);
        var text = File.ReadAllText(AgentsMd);
        Assert.StartsWith("# Before\r\nline\r\n\r\n<!-- tacit:begin", text);
        Assert.EndsWith("<!-- tacit:end -->\r\n\r\n# After\r\n", text);
        Assert.Contains("- 需求不清时 → 先连续追问\r\n", text);
    }

    [Fact]
    public void Disconnect_restores_the_files_byte_for_byte()
    {
        var original = "# My rules\n- be brief\n"u8.ToArray();
        WriteAgentsMd(Encoding.UTF8.GetString(original));
        var mcp = """
            {
              "mcpServers": {
                "context7": {
                  "type": "http",
                  "url": "https://c7.example/mcp"
                }
              }
            }
            """.ReplaceLineEndings("\n");
        File.WriteAllText(McpJson, mcp);
        File.WriteAllText(PersonaMd, Persona.ReplaceLineEndings("\n"));

        _home.Tacit().Connect(ClientKind.Omp);
        _home.Tacit().Disconnect(ClientKind.Omp);

        Assert.Equal(original, File.ReadAllBytes(AgentsMd));
        Assert.Equal(mcp, File.ReadAllText(McpJson));
        Assert.Empty(JsonNode.Parse(File.ReadAllText(BindingsJson))!["bindings"]!.AsArray());
    }

    [Fact]
    public void Disconnect_removes_an_agents_md_that_connect_created()
    {
        _home.Tacit().Connect(ClientKind.Omp);

        _home.Tacit().Disconnect(ClientKind.Omp);

        Assert.False(File.Exists(AgentsMd));
    }

    [Theory]
    [InlineData("<!-- tacit:begin x -->\nno end\n")]
    [InlineData("no begin\n<!-- tacit:end -->\n")]
    [InlineData("<!-- tacit:begin -->\n<!-- tacit:end -->\n<!-- tacit:begin -->\n<!-- tacit:end -->\n")]
    [InlineData("<!-- tacit:end -->\n<!-- tacit:begin -->\n")]
    public void Broken_markers_are_refused_and_the_file_is_left_alone(string agents)
    {
        WriteAgentsMd(agents);

        var error = Assert.Throws<TacitException>(() => _home.Tacit().Connect(ClientKind.Omp));

        Assert.Equal(TacitError.MarkersBroken, error.Error);
        Assert.Contains(AgentsMd, error.Message);
        Assert.Equal(agents, File.ReadAllText(AgentsMd));
    }

    [Fact]
    public void A_deleted_context_file_is_skipped_and_not_recreated()
    {
        _home.Tacit().Connect(ClientKind.Omp);
        File.Delete(AgentsMd);

        var result = Assert.Single(_home.Tacit().RenderBlocks());

        Assert.Equal(BlockState.FileMissing, result.State);
        Assert.False(File.Exists(AgentsMd));
    }

    [Fact]
    public void A_persona_that_is_not_utf8_leaves_the_last_block_in_place()
    {
        File.WriteAllText(PersonaMd, Persona.ReplaceLineEndings("\n"));
        _home.Tacit().Connect(ClientKind.Omp);
        var before = File.ReadAllBytes(AgentsMd);
        File.WriteAllBytes(PersonaMd, Encoding.Latin1.GetBytes("## Preferences\n- café\n"));

        var result = Assert.Single(_home.Tacit().RenderBlocks());

        Assert.Equal(BlockState.PersonaUnreadable, result.State);
        Assert.Equal(before, File.ReadAllBytes(AgentsMd));
    }

    [Fact]
    public void Persona_headings_are_recognized_in_either_language_and_unknown_sections_follow_the_four()
    {
        File.WriteAllText(PersonaMd, """
            ## 读书
            - 最近在读《思考，快与慢》

            ## Preferences
            - 沟通：先要结论
            - 格式不对的偏好

            ## 红线
            - 不要删库
            """.ReplaceLineEndings("\n"));

        _home.Tacit().Connect(ClientKind.Omp);

        var text = File.ReadAllText(AgentsMd);
        var redLines = text.IndexOf("## Red lines", StringComparison.Ordinal);
        var preferences = text.IndexOf("## Preferences", StringComparison.Ordinal);
        var reading = text.IndexOf("## 读书", StringComparison.Ordinal);
        Assert.True(redLines > 0 && redLines < preferences && preferences < reading);
        Assert.Contains("- 不要删库\n", text);
        Assert.Contains("- 格式不对的偏好\n", text);
        Assert.Contains("- 最近在读《思考，快与慢》\n", text);
    }

    [Fact]
    public void Rendering_follows_bindings_json_on_every_call()
    {
        _home.Tacit().Connect(ClientKind.Omp);
        File.Delete(BindingsJson);

        Assert.Empty(_home.Tacit().RenderBlocks());
    }

    [Fact]
    public void Disconnect_restores_a_file_that_had_no_final_line_break()
    {
        WriteAgentsMd("# My rules\r\n- be brief");

        _home.Tacit().Connect(ClientKind.Omp);
        _home.Tacit().RenderBlocks();
        _home.Tacit().Disconnect(ClientKind.Omp);

        Assert.Equal("# My rules\r\n- be brief", File.ReadAllText(AgentsMd));
    }

    [Fact]
    public void Persona_text_that_breaks_the_format_is_rendered_rather_than_dropped()
    {
        File.WriteAllText(PersonaMd, """
            写在第一个标题之前的话

            ## 偏好
            沟通上喜欢直接，这一段没有写成列表
            * 星号列表也算
            <!-- 2026-10-31 一条独立的注释 -->
            """.ReplaceLineEndings("\n"));

        _home.Tacit().Connect(ClientKind.Omp);

        var text = File.ReadAllText(AgentsMd);
        Assert.Contains("## Preferences\nDescriptions", text);
        Assert.Contains("\n沟通上喜欢直接，这一段没有写成列表\n* 星号列表也算\n", text);
        Assert.Contains("\n写在第一个标题之前的话\n</user_memory>", text);
        Assert.DoesNotContain("独立的注释", text);
    }

    [Fact]
    public void Connect_refuses_a_persona_that_is_not_utf8_and_changes_nothing()
    {
        File.WriteAllBytes(PersonaMd, Encoding.Latin1.GetBytes("## Preferences\n- café\n"));

        var error = Assert.Throws<TacitException>(() => _home.Tacit().Connect(ClientKind.Omp));

        Assert.Equal(TacitError.FileNotUtf8, error.Error);
        Assert.False(File.Exists(AgentsMd));
        Assert.False(File.Exists(McpJson));
        Assert.False(File.Exists(BindingsJson));
    }

    [Fact]
    public void A_context_file_that_is_not_utf8_is_left_alone()
    {
        _home.Tacit().Connect(ClientKind.Omp);
        var gbk = Encoding.Latin1.GetBytes("# régles\n");
        File.WriteAllBytes(AgentsMd, gbk);

        var result = Assert.Single(_home.Tacit().RenderBlocks());

        Assert.Equal(BlockState.FileNotUtf8, result.State);
        Assert.Equal(gbk, File.ReadAllBytes(AgentsMd));
    }

    [Fact]
    public void An_mcp_json_with_comments_is_refused_with_the_entry_to_add_by_hand()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(McpJson)!);
        const string commented = "{\n  // my servers\n  \"mcpServers\": {}\n}\n";
        File.WriteAllText(McpJson, commented);

        var error = Assert.Throws<TacitException>(() => _home.Tacit().Connect(ClientKind.Omp));

        Assert.Equal(TacitError.ClientConfigInvalid, error.Error);
        Assert.Contains("http://127.0.0.1:18888/mcp", error.Message);
        Assert.Equal(commented, File.ReadAllText(McpJson));
    }

    [Fact]
    public void Fields_the_user_added_to_the_tacit_entry_and_the_bom_are_kept()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(McpJson)!);
        File.WriteAllBytes(McpJson, [0xEF, 0xBB, 0xBF, .. "{\n  \"mcpServers\": {\n    \"tacit\": {\n      \"type\": \"http\",\n      \"url\": \"http://127.0.0.1:1/mcp\",\n      \"timeout\": 5000\n    }\n  }\n}\n"u8.ToArray()]);

        _home.Tacit().Connect(ClientKind.Omp);

        var bytes = File.ReadAllBytes(McpJson);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        var server = JsonNode.Parse(Encoding.UTF8.GetString(bytes[3..]))!["mcpServers"]!["tacit"]!;
        Assert.Equal("http://127.0.0.1:18888/mcp", (string?)server["url"]);
        Assert.Equal(5000, (int?)server["timeout"]);
    }

    [Fact]
    public void Disconnect_removes_an_mcp_json_that_connect_created()
    {
        _home.Tacit().Connect(ClientKind.Omp);

        _home.Tacit().Disconnect(ClientKind.Omp);

        Assert.False(File.Exists(McpJson));
    }

    [Fact]
    public void A_broken_bindings_json_is_reported()
    {
        File.WriteAllText(BindingsJson, "{ not json");

        var error = Assert.Throws<TacitException>(() => _home.Tacit().RenderBlocks());

        Assert.Equal(TacitError.BindingsInvalid, error.Error);
        Assert.Contains("bindings.json", error.Message);
    }
}
