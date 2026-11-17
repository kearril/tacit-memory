using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using TacitMemory.Core;
using TacitMemory.Core.Tests;
using TacitMemory.Host.Mcp;

namespace TacitMemory.Host.Tests;

public sealed class ServiceHostTests : IAsyncLifetime
{
    private readonly TestHome _home = new();
    private readonly ModelDouble _models = new() { EmbeddingDown = true, RerankDown = true };
    private MemoryService _service = null!;
    private ServiceHost _host = null!;

    public async Task InitializeAsync()
    {
        _home.InitReady(models: _models);
        Directory.CreateDirectory(Path.Combine(_home.DefaultMemory, "pages", "tacit-memory"));
        _service = _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);
        _host = await ServiceHost.TryStartAsync(_service, port: 0, NullLoggerProvider.Instance) ?? throw new InvalidOperationException("Port 0 is never in use.");
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        _service.Dispose();
        _models.Dispose();
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
        // Written before the first connection, so the instructions are first built with this activity in them.
        Directory.CreateDirectory(Path.Combine(_home.DefaultMemory, "recent"));
        File.WriteAllText(Path.Combine(_home.DefaultMemory, "recent", "2026-11.md"), "## 2026-11-02\n- [tacit-memory] MCP 接缝测试活动\n");
        await using var client = await ConnectAsync();

        Assert.Equal("tacit", client.ServerInfo?.Name);
        var instructions = Assert.IsType<string>(client.ServerInstructions);
        Assert.StartsWith("TacitMemory is the user's personal memory across sessions.", instructions);
        Assert.Contains("Memory is written in Chinese.", instructions);
        Assert.Contains("## Recall", instructions);
        Assert.Contains("`recall` first, then update the page", instructions);
        Assert.Contains("## Worth recording\n- The user corrects or interrupts the agent", instructions);
        Assert.Contains("tacit-memory", instructions);
        Assert.Contains("<recent_activity days=\"7\">\n- 2026-11-02 [tacit-memory] MCP 接缝测试活动\n</recent_activity>", instructions);
        Assert.DoesNotContain("## Workstreams", instructions);
    }

    [Fact]
    public async Task A_client_on_the_2026_07_28_revision_runs_without_a_session()
    {
        await using var client = await ConnectAsync();

        Assert.Equal("2026-07-28", client.NegotiatedProtocolVersion);
        Assert.Null(client.SessionId);
        Assert.StartsWith("TacitMemory is the user's personal memory across sessions.", client.ServerInstructions);
        Assert.Equal(["recall", "write_page", "write_persona", "write_recent"], (await client.ListToolsAsync()).Select(tool => tool.Name).Order());
    }

    [Theory]
    [InlineData("2025-03-26")]
    [InlineData("2025-06-18")]
    [InlineData("2025-11-25")]
    public async Task A_client_on_an_older_revision_still_connects_through_initialize_without_a_session(string version)
    {
        await using var client = await McpClient.CreateAsync(
            new HttpClientTransport(new HttpClientTransportOptions { Endpoint = _host.Endpoint, TransportMode = HttpTransportMode.StreamableHttp }),
            new McpClientOptions { ProtocolVersion = version });

        Assert.Equal(version, client.NegotiatedProtocolVersion);
        Assert.Null(client.SessionId);
        Assert.StartsWith("TacitMemory is the user's personal memory across sessions.", client.ServerInstructions);
        var recalled = await client.CallToolAsync("recall", new Dictionary<string, object?> { ["query"] = "数据库" });
        Assert.NotEqual(true, recalled.IsError);
    }

    [Fact]
    public async Task A_raw_initialize_answer_carries_no_session_header()
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, _host.Endpoint)
        {
            Content = new StringContent(
                """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"raw","version":"1"}}}""",
                System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");

        using var response = await http.SendAsync(request);

        Assert.True(response.IsSuccessStatusCode);
        Assert.False(response.Headers.Contains("Mcp-Session-Id"));
    }

    [Fact]
    public async Task The_server_info_version_is_the_version_the_management_api_reports()
    {
        await using var client = await ConnectAsync();
        using var http = new HttpClient();

        var session = JsonNode.Parse(await http.GetStringAsync(new Uri(_host.Endpoint, "/api/session")))!;

        Assert.Equal(session["version"]!.GetValue<string>(), client.ServerInfo?.Version);
    }

    [Fact]
    public async Task Recall_and_the_write_tools_are_listed_with_their_annotations()
    {
        await using var client = await ConnectAsync();

        var tools = (await client.ListToolsAsync()).ToDictionary(tool => tool.Name);

        Assert.Equal(["recall", "write_page", "write_persona", "write_recent"], tools.Keys.Order());
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
        var recent = tools["write_recent"].ProtocolTool;
        Assert.False(recent.Annotations?.ReadOnlyHint);
        Assert.True(recent.Annotations?.DestructiveHint);
        Assert.False(recent.Annotations?.IdempotentHint);
        Assert.False(recent.Annotations?.OpenWorldHint);
        Assert.Equal(
            ["confirm_no_secret", "replaces", "source", "text", "workstream"],
            recent.InputSchema.GetProperty("properties").EnumerateObject().Select(property => property.Name).Order());
        var persona = tools["write_persona"].ProtocolTool;
        Assert.False(persona.Annotations?.ReadOnlyHint);
        Assert.True(persona.Annotations?.DestructiveHint);
        Assert.False(persona.Annotations?.IdempotentHint);
        Assert.False(persona.Annotations?.OpenWorldHint);
        Assert.Equal(
            ["confirm_no_secret", "hash", "op", "persona_section", "scope", "source", "text", "trigger"],
            persona.InputSchema.GetProperty("properties").EnumerateObject().Select(property => property.Name).Order());
    }

    [Fact]
    public async Task The_write_tools_list_their_required_parameters_and_the_values_of_their_enumerated_ones()
    {
        await using var client = await ConnectAsync();

        var tools = (await client.ListToolsAsync()).ToDictionary(tool => tool.Name);

        Assert.Equal(["op", "page"], Required(tools["write_page"].ProtocolTool));
        Assert.Equal(["text"], Required(tools["write_recent"].ProtocolTool));
        Assert.Equal(["op"], Required(tools["write_persona"].ProtocolTool));
        Assert.Empty(Required(tools["recall"].ProtocolTool));
        Assert.Equal(["create", "upsert_section", "delete_section", "update_header"], Values(tools["write_page"].ProtocolTool, "op"));
        Assert.Equal(["add", "propose", "replace", "delete"], Values(tools["write_persona"].ProtocolTool, "op"));
        Assert.Equal(["red_lines", "working_rules", "preferences", "profile"], Values(tools["write_persona"].ProtocolTool, "persona_section"));
        Assert.Equal(["incident", "decision", "entity", "work"], Values(tools["write_page"].ProtocolTool, "type"));
        Assert.Equal(["pages", "activities", "rules", "persona"], Values(tools["recall"].ProtocolTool, "kind"));
        Assert.Equal(
            ["domain", "from", "kind", "page", "query", "to", "workstream"],
            tools["recall"].ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject().Select(property => property.Name).Order());
        Assert.False(tools["write_page"].ProtocolTool.InputSchema.GetProperty("properties").GetProperty("page").TryGetProperty("enum", out _));
    }

    [Fact]
    public async Task The_descriptions_carry_the_configured_limits_and_match_the_catalog()
    {
        // The limits are read once at start, so the service is restarted on the config the test writes.
        await _host.DisposeAsync();
        _service.Dispose();
        File.AppendAllText(Path.Combine(_home.TacitHome, "config.yaml"), "limits: { page: { minAliases: 3 } }\n");
        _service = _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);
        _host = await ServiceHost.TryStartAsync(_service, port: 0, NullLoggerProvider.Instance) ?? throw new InvalidOperationException("Port 0 is never in use.");

        await using var client = await ConnectAsync();
        var served = (await client.ListToolsAsync()).ToDictionary(tool => tool.Name);
        var catalog = ToolCatalog.Describe(_service.Config.Limits).ToDictionary(tool => tool.Name);

        Assert.Contains("At least 3.", Parameter(served["write_page"].ProtocolTool, "aliases"));
        foreach (var tool in served.Values)
        {
            var description = tool.ProtocolTool.Description ?? "";
            Assert.DoesNotMatch(@"\d+ characters", description);
            Assert.DoesNotContain("by default", description);
            Assert.DoesNotContain('{', description);
        }
        foreach (var tool in served.Values)
        {
            Assert.Equal(catalog[tool.Name].Description, tool.ProtocolTool.Description);
            foreach (var parameter in catalog[tool.Name].Parameters.Where(parameter => parameter.Description.Length > 0))
                Assert.Equal(parameter.Description, Parameter(tool.ProtocolTool, parameter.Name));
        }
    }

    private static string Parameter(ModelContextProtocol.Protocol.Tool tool, string parameter) =>
        tool.InputSchema.GetProperty("properties").GetProperty(parameter).GetProperty("description").GetString()!;


    [Fact]
    public async Task Write_persona_describes_each_op_on_its_own_line()
    {
        await using var client = await ConnectAsync();

        var description = (await client.ListToolsAsync()).ToDictionary(tool => tool.Name)["write_persona"].ProtocolTool.Description ?? "";
        var lines = description.Split('\n', StringSplitOptions.TrimEntries);

        foreach (var op in new[] { "add", "propose", "replace", "delete" })
            Assert.Contains(lines, line => line.StartsWith($"op={op}:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_prompt_list_answers_with_no_prompts()
    {
        await using var client = await ConnectAsync();

        Assert.Empty(await client.ListPromptsAsync());
    }

    [Fact]
    public async Task The_old_checkup_report_uri_is_not_a_resource()
    {
        await using var client = await ConnectAsync();

        await Assert.ThrowsAnyAsync<ModelContextProtocol.McpException>(() => client.ReadResourceAsync("tacit://checkup-report").AsTask());
    }

    [Fact]
    public async Task The_consolidation_report_is_listed_as_a_resource_and_reads_back_the_memory_path_and_the_report()
    {
        await using var client = await ConnectAsync();

        var resource = Assert.Single(await client.ListResourcesAsync());
        Assert.Equal("tacit://consolidation-report", resource.Uri);
        Assert.Contains("tacit-consolidate", resource.Description, StringComparison.Ordinal);

        var result = await client.ReadResourceAsync("tacit://consolidation-report");
        var text = string.Concat(result.Contents.OfType<ModelContextProtocol.Protocol.TextResourceContents>().Select(contents => contents.Text));
        Assert.Contains(_home.DefaultMemory, text);
        var headings = new[]
        {
            "## A. Automatic\n",
            "### Format and rule violations\n",
            "### Persona candidates to remove\n",
            "### Weeks to summarize\n",
            "### Months to summarize\n",
            "### Workstream material\n",
            "## B. For proposals\n",
            "### Workstream proposal material\n",
            "### Persona evidence\n",
            "### Incident lessons\n",
            "### Changed pages\n",
            "### Suspected duplicate pages\n",
            "### Conflicting persona entries\n",
            "### Domain hygiene\n",
            "### Length hints\n",
        };
        foreach (var heading in headings)
            Assert.Contains(heading, text);
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
            ["type"] = "incident",
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
    public async Task An_activity_written_through_the_tool_lands_in_the_month_file()
    {
        await using var client = await ConnectAsync();

        var written = await client.CallToolAsync("write_recent", new Dictionary<string, object?>
        {
            ["text"] = "想清楚了画像里缺的是人的倾向",
            ["workstream"] = "tacit-memory",
        });

        Assert.NotEqual(true, written.IsError);
        Assert.Matches(@"Added activity \[[0-9a-f]{6}\]\.", Text(written));
        Assert.Equal(
            "## 2026-11-02\n- [tacit-memory] 想清楚了画像里缺的是人的倾向\n",
            File.ReadAllText(Path.Combine(_home.DefaultMemory, "recent", "2026-11.md")));
    }

    [Fact]
    public async Task A_repeat_of_todays_activity_comes_back_as_a_normal_reply_that_names_it()
    {
        await using var client = await ConnectAsync();
        var first = await client.CallToolAsync("write_recent", new Dictionary<string, object?>
        {
            ["text"] = "先查了索引",
        });
        _models.Choose = (_, options) => options.ToDictionary(
            option => option.Key,
            option => option.Value.Contains("先查了索引", StringComparison.Ordinal) ? 0.95 : 0.0);

        var repeated = await client.CallToolAsync("write_recent", new Dictionary<string, object?>
        {
            ["text"] = "索引查完了，问题在分词器",
        });

        var code = System.Text.RegularExpressions.Regex.Match(Text(first), @"\[([0-9a-f]{6})\]").Groups[1].Value;
        Assert.NotEqual(true, repeated.IsError);
        Assert.Contains($"Not added: today's activity [{code}]", Text(repeated));
        Assert.Contains($"replaces set to {code}", Text(repeated));
        Assert.Equal(
            "## 2026-11-02\n- 先查了索引\n",
            File.ReadAllText(Path.Combine(_home.DefaultMemory, "recent", "2026-11.md")));
    }

    [Fact]
    public async Task An_activity_written_through_the_tool_is_recalled_by_kind()
    {
        await using var client = await ConnectAsync();
        await client.CallToolAsync("write_recent", new Dictionary<string, object?>
        {
            ["text"] = "想清楚了画像里缺的是人的倾向",
            ["workstream"] = "tacit-memory",
        });

        var read = await client.CallToolAsync("recall", new Dictionary<string, object?> { ["query"] = "画像", ["kind"] = "activities" });

        Assert.NotEqual(true, read.IsError);
        Assert.Contains("activity 2026-11-02", Text(read));
        Assert.Contains("想清楚了画像里缺的是人的倾向", Text(read));
    }

    [Fact]
    public async Task The_removed_parameters_come_back_refused_by_name_with_the_consolidation_as_the_next_step()
    {
        await using var client = await ConnectAsync();

        var refused = await client.CallToolAsync("write_recent", new Dictionary<string, object?>
        {
            ["op"] = "add_activity",
            ["text"] = "一件事",
            ["items"] = new[] { "不该传" },
            ["hash"] = "e3b0c4",
        });

        Assert.True(refused.IsError);
        Assert.StartsWith(
            "Refused:\n- Unknown parameter op.\n- Unknown parameter items.\n- Unknown parameter hash.\nNext step: ",
            Text(refused));
        Assert.Contains("The workstream list is maintained by the tacit-consolidate skill.", Text(refused));
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
            ["type"] = "incident",
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
            ["type"] = "incident",
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

    [Fact]
    public async Task An_argument_the_schema_does_not_declare_reaches_the_service_and_is_named()
    {
        await using var client = await ConnectAsync();

        var written = await client.CallToolAsync("write_page", new Dictionary<string, object?>
        {
            ["op"] = "upsert_section",
            ["page"] = "tacit-memory/x",
            ["section"] = "根因",
            ["content"] = "扩展没有重装。",
        });
        var recalled = await client.CallToolAsync("recall", new Dictionary<string, object?> { ["query"] = "数据库", ["limit"] = 5, ["content"] = null });

        Assert.True(written.IsError);
        Assert.StartsWith("Refused: Unknown parameter content.\nNext step: ", Text(written));
        Assert.True(recalled.IsError);
        Assert.StartsWith("Refused:\n", Text(recalled));
        Assert.Contains("- Unknown parameter limit.", Text(recalled));
        Assert.Contains("- Unknown parameter content.", Text(recalled));
    }

    [Fact]
    public async Task An_op_outside_the_listed_values_still_reaches_the_service_which_refuses_it()
    {
        await using var client = await ConnectAsync();

        var result = await client.CallToolAsync("write_page", new Dictionary<string, object?> { ["op"] = "rewrite", ["page"] = "tacit-memory/x" });

        Assert.True(result.IsError);
        Assert.StartsWith("Refused: Unknown op \"rewrite\".\nNext step: ", Text(result));
    }

    [Fact]
    public async Task A_persona_entry_written_through_the_tool_lands_in_persona_md_and_a_refusal_is_an_error()
    {
        await using var client = await ConnectAsync();

        var written = await client.CallToolAsync("write_persona", new Dictionary<string, object?>
        {
            ["op"] = "add",
            ["persona_section"] = "preferences",
            ["text"] = "沟通：先要结论",
            ["source"] = "先要结论",
        });
        var refused = await client.CallToolAsync("write_persona", new Dictionary<string, object?>
        {
            ["op"] = "add",
            ["persona_section"] = "preferences",
            ["text"] = "沟通：先要结论",
        });

        Assert.NotEqual(true, written.IsError);
        Assert.Matches(@"\[[0-9a-f]{6}\]", Text(written));
        Assert.Contains(
            "## Preferences\n- 沟通：先要结论\n  <!-- 2026-11-02 quote「先要结论」 -->\n",
            File.ReadAllText(Path.Combine(_home.DefaultMemory, "persona.md")));
        Assert.True(refused.IsError);
        Assert.Contains("source", Text(refused));
    }

    [Fact]
    public async Task A_domain_entry_with_its_scope_and_trigger_lands_in_its_block_through_the_tool()
    {
        await using var client = await ConnectAsync();

        var written = await client.CallToolAsync("write_persona", new Dictionary<string, object?>
        {
            ["op"] = "add",
            ["persona_section"] = "working_rules",
            ["text"] = "切换节点 → 先备份配置",
            ["source"] = "先备份",
            ["scope"] = "domain:network-proxy",
            ["trigger"] = "改代理、分流或测速时",
        });

        Assert.NotEqual(true, written.IsError);
        Assert.Contains(
            "## Domain: network-proxy\nTrigger: 改代理、分流或测速时\n### Working rules\n- 切换节点 → 先备份配置\n  <!-- 2026-11-02 quote「先备份」 -->\n",
            File.ReadAllText(Path.Combine(_home.DefaultMemory, "persona.md")));
    }

    [Fact]
    public async Task Replace_and_delete_through_the_tool_check_their_parameters_and_take_a_short_code_from_a_refusal()
    {
        await using var client = await ConnectAsync();
        var persona = Path.Combine(_home.DefaultMemory, "persona.md");
        File.WriteAllText(persona, "## Preferences\n- 沟通：先要结论\n  <!-- 2026-10-21 quote「先说结论」 -->\n- 反感客套和铺垫\n");

        var misplaced = await client.CallToolAsync("write_persona", new Dictionary<string, object?>
        {
            ["op"] = "replace",
            ["hash"] = "abc123",
            ["persona_section"] = "preferences",
            ["text"] = "沟通：先说结论",
            ["source"] = "先说结论",
        });
        var noHash = await client.CallToolAsync("write_persona", new Dictionary<string, object?> { ["op"] = "delete" });
        var deleteWithText = await client.CallToolAsync("write_persona", new Dictionary<string, object?>
        {
            ["op"] = "delete",
            ["hash"] = "abc123",
            ["text"] = "沟通：先要结论",
        });

        Assert.True(misplaced.IsError);
        Assert.Contains("persona_section", Text(misplaced));
        Assert.True(noHash.IsError);
        Assert.DoesNotContain("Related:", Text(noHash));
        Assert.Contains("Call recall with kind=persona", Text(noHash));
        Assert.True(deleteWithText.IsError);
        Assert.Contains("text", Text(deleteWithText));
        Assert.Equal("## Preferences\n- 沟通：先要结论\n  <!-- 2026-10-21 quote「先说结论」 -->\n- 反感客套和铺垫\n", File.ReadAllText(persona));

        var listed = Text(await client.CallToolAsync("recall", new Dictionary<string, object?> { ["kind"] = "persona" }));
        var code = System.Text.RegularExpressions.Regex.Match(listed, @"\[([0-9a-f]{6})\] 沟通：先要结论").Groups[1].Value;
        var replaced = await client.CallToolAsync("write_persona", new Dictionary<string, object?>
        {
            ["op"] = "replace",
            ["hash"] = code,
            ["text"] = "沟通：先说结论",
            ["source"] = "先说结论",
        });
        var refreshed = System.Text.RegularExpressions.Regex.Match(listed, @"\[([0-9a-f]{6})\] 反感客套和铺垫").Groups[1].Value;
        var deleted = await client.CallToolAsync("write_persona", new Dictionary<string, object?>
        {
            ["op"] = "delete",
            ["hash"] = refreshed,
        });

        Assert.NotEqual(true, replaced.IsError);
        Assert.NotEqual(true, deleted.IsError);
        Assert.Equal("## Preferences\n- 沟通：先说结论\n  <!-- 2026-11-02 quote「先说结论」 -->\n", File.ReadAllText(persona));
    }

    [Fact]
    public async Task A_candidate_proposed_through_the_tool_lands_in_candidates_md_and_bad_proposals_are_errors()
    {
        await using var client = await ConnectAsync();

        var proposed = await client.CallToolAsync("write_persona", new Dictionary<string, object?>
        {
            ["op"] = "propose",
            ["persona_section"] = "preferences",
            ["text"] = "沟通：先要结论",
            ["source"] = "先要结论",
        });
        var redLine = await client.CallToolAsync("write_persona", new Dictionary<string, object?>
        {
            ["op"] = "propose",
            ["persona_section"] = "red_lines",
            ["text"] = "不要删库；改为先备份",
            ["source"] = "别删库",
        });
        var withHash = await client.CallToolAsync("write_persona", new Dictionary<string, object?>
        {
            ["op"] = "propose",
            ["persona_section"] = "preferences",
            ["text"] = "沟通：先要结论",
            ["source"] = "先要结论",
            ["hash"] = "a3f9c2",
        });
        var noSource = await client.CallToolAsync("write_persona", new Dictionary<string, object?>
        {
            ["op"] = "propose",
            ["persona_section"] = "preferences",
            ["text"] = "沟通：先要结论",
        });

        Assert.NotEqual(true, proposed.IsError);
        Assert.Contains("Recorded as a new candidate (scope: global)", Text(proposed));
        Assert.DoesNotMatch(@"\[[0-9a-f]{6}", Text(proposed));
        Assert.Contains(
            "### 沟通：先要结论\nBelongs to: Preferences\n- 2026-11-02 quote「先要结论」\n",
            File.ReadAllText(Path.Combine(_home.DefaultMemory, "candidates.md")));
        Assert.True(redLine.IsError);
        Assert.Contains("Refused:", Text(redLine));
        Assert.True(withHash.IsError);
        Assert.Contains("hash", Text(withHash));
        Assert.True(noSource.IsError);
        Assert.Contains("source", Text(noSource));
    }

    [Fact]
    public async Task A_proposal_merged_into_a_candidate_or_matching_a_rejected_one_is_an_ordinary_reply_not_an_error()
    {
        await using var client = await ConnectAsync();
        _models.EmbeddingDown = false;
        File.WriteAllText(
            Path.Combine(_home.DefaultMemory, "candidates.md"),
            "## Candidates\n\n### 沟通：先要结论\nBelongs to: Preferences\n- 2026-10-27 quote「先要结论」\n\n"
            + "## Rejection list\n\n### 每次提交前都运行全量测试\nBelongs to: Working rules\n- 2026-10-20 rejected quote「算了，别跑了」\n");

        _models.Choose = (_, options) => options.ToDictionary(
            option => option.Key,
            option => option.Value.Contains("先要结论", StringComparison.Ordinal) ? 0.95
                : option.Key == options.Keys.Last() ? 0.05 : 0.0);
        var merged = await client.CallToolAsync("write_persona", new Dictionary<string, object?>
        {
            ["op"] = "propose",
            ["persona_section"] = "preferences",
            ["text"] = "沟通：先要结论",
            ["source"] = "先要结论",
        });

        _models.Choose = (_, options) => options.ToDictionary(
            option => option.Key,
            option => option.Value.Contains("全量测试", StringComparison.Ordinal) ? 0.95
                : option.Key == options.Keys.Last() ? 0.05 : 0.0);
        var refused = await client.CallToolAsync("write_persona", new Dictionary<string, object?>
        {
            ["op"] = "propose",
            ["persona_section"] = "working_rules",
            ["text"] = "提交前 → 先跑测试",
            ["source"] = "提交前跑全量测试",
        });

        Assert.NotEqual(true, merged.IsError);
        Assert.Contains("Merged into the candidate \"沟通：先要结论\" (scope: global)", Text(merged));
        Assert.NotEqual(true, refused.IsError);
        Assert.Contains("2026-10-20", Text(refused));
        Assert.Contains(
            "- 2026-11-02 quote「先要结论」\n",
            File.ReadAllText(Path.Combine(_home.DefaultMemory, "candidates.md")));
    }

    private static string Text(ModelContextProtocol.Protocol.CallToolResult result) =>
        string.Concat(result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(block => block.Text));

    private static string[] Required(ModelContextProtocol.Protocol.Tool tool) =>
        tool.InputSchema.TryGetProperty("required", out var required)
            ? required.EnumerateArray().Select(name => name.GetString()!).Order().ToArray()
            : [];

    private static string[] Values(ModelContextProtocol.Protocol.Tool tool, string parameter) =>
        tool.InputSchema.GetProperty("properties").GetProperty(parameter).GetProperty("enum").EnumerateArray().Select(value => value.GetString()!).ToArray();

    [Fact]
    public async Task A_second_service_on_a_taken_port_is_refused()
    {
        Assert.Equal("127.0.0.1", _host.Endpoint.Host);
        Assert.Null(await ServiceHost.TryStartAsync(_service, _host.Endpoint.Port, NullLoggerProvider.Instance));
    }
}
