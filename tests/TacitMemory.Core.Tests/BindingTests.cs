using System.Text;
using System.Text.Json.Nodes;

namespace TacitMemory.Core.Tests;

/// <summary>
/// The binding of a client name to a context file and a skills folder, through the facade: what <c>connect</c> and
/// <c>disconnect</c> refuse and write, the resident block they keep, and the record they leave in <c>bindings.json</c>.
/// </summary>
public sealed class BindingTests : IDisposable
{
    private readonly TestHome _home = new();

    public BindingTests() => _home.InitReady();

    public void Dispose() => _home.Dispose();

    private string AgentsMd => Path.Combine(_home.UserHome, ".omp", "agent", "AGENTS.md");

    private string SkillsFolder => Path.Combine(_home.UserHome, ".omp", "agent", "skills");

    private string McpJson => Path.Combine(_home.UserHome, ".omp", "agent", "mcp.json");

    private string PersonaMd => Path.Combine(_home.DefaultMemory, "persona.md");

    private string BindingsJson => Path.Combine(_home.TacitHome, "bindings.json");

    private void WriteAgentsMd(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AgentsMd)!);
        File.WriteAllText(AgentsMd, text);
    }

    /// <summary>The one binding record in <c>bindings.json</c>.</summary>
    private JsonObject Record() => Assert.Single(JsonNode.Parse(File.ReadAllText(BindingsJson))!["bindings"]!.AsArray())!.AsObject();

    /// <summary>
    /// Every entry under the test root, with the bytes of each file. A refused call must leave this exactly as it was; a
    /// successful one is the only kind that may change it.
    /// </summary>
    private List<string> Snapshot() =>
        Directory.EnumerateFileSystemEntries(_home.Root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(path => Directory.Exists(path) ? $"dir {path}" : $"file {path} {Convert.ToBase64String(File.ReadAllBytes(path))}")
            .ToList();

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

        _home.Bind();

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
    public void Connect_creates_the_context_file_and_records_the_binding_with_its_paths()
    {
        _home.Bind();

        Assert.StartsWith("<!-- tacit:begin", File.ReadAllText(AgentsMd));
        var record = Record();
        Assert.Equal(["client", "contextFile", "skillsFolder", "createdContextFile"], record.Select(property => property.Key));
        Assert.Equal("omp", (string?)record["client"]);
        Assert.Equal(AgentsMd, (string?)record["contextFile"]);
        Assert.Equal(SkillsFolder, (string?)record["skillsFolder"]);
        Assert.True((bool?)record["createdContextFile"]);
    }

    [Fact]
    public void Connect_reports_the_mcp_endpoint_the_user_configures_in_the_client()
    {
        var result = _home.Bind();

        Assert.Equal(new Uri("http://127.0.0.1:18888/mcp"), result.Endpoint);
        Assert.Contains("http://127.0.0.1:18888/mcp", result.Report);
        Assert.Contains("Streamable HTTP", result.Report);
        Assert.Contains("tacit", result.Report);
        Assert.Contains("server.port", result.Report);
    }

    [Fact]
    public void Connect_never_reads_or_writes_an_mcp_config()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(McpJson)!);
        File.WriteAllText(McpJson, "{ // tacit does not read this file, comments and all\n  \"mcpServers\": {}\n}\n");
        var before = File.ReadAllBytes(McpJson);

        _home.Bind();
        Assert.Equal(before, File.ReadAllBytes(McpJson));

        _home.Unbind();
        Assert.Equal(before, File.ReadAllBytes(McpJson));
    }

    [Fact]
    public void Connect_creates_no_mcp_config_when_none_exists()
    {
        _home.Bind();

        Assert.False(File.Exists(McpJson));
    }

    [Fact]
    public void Connect_lower_cases_the_client_name_in_the_record_and_the_report()
    {
        var result = _home.Bind("OMP");

        Assert.Equal("omp", (string?)Record()["client"]);
        Assert.Contains("omp", result.Report);
        Assert.DoesNotContain("OMP", result.Report);
    }

    [Fact]
    public void Connect_expands_a_leading_tilde_to_the_user_home_and_records_absolute_paths()
    {
        Directory.CreateDirectory(SkillsFolder);

        var result = _home.Tacit().Connect(new ConnectRequest("omp", "~/.omp/agent/AGENTS.md", "~/.omp/agent/skills"));

        Assert.Equal(AgentsMd, result.ContextFile);
        Assert.Equal(AgentsMd, (string?)Record()["contextFile"]);
        Assert.Equal(SkillsFolder, (string?)Record()["skillsFolder"]);
        Assert.True(File.Exists(AgentsMd));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a_b")]
    [InlineData("omp!")]
    [InlineData("open ai")]
    [InlineData("ümlaut")]
    [InlineData("a.b")]
    public void A_client_name_outside_the_rules_is_refused_and_writes_nothing(string name)
    {
        var before = Snapshot();

        var error = Assert.Throws<TacitException>(() => _home.Tacit().Connect(new ConnectRequest(name, AgentsMd, SkillsFolder)));

        Assert.Equal(TacitError.ClientNameInvalid, error.Error);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void A_client_name_of_32_characters_is_accepted_and_one_of_33_is_refused()
    {
        _home.Bind(new string('a', 32));
        var before = Snapshot();

        var error = Assert.Throws<TacitException>(() => _home.Tacit().Connect(new ConnectRequest(new string('b', 33), AgentsMd, SkillsFolder)));

        Assert.Equal(TacitError.ClientNameInvalid, error.Error);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void A_name_bound_to_other_paths_is_refused_until_it_is_disconnected()
    {
        _home.Bind();
        var other = Path.Combine(_home.UserHome, "other", "AGENTS.md");
        Directory.CreateDirectory(Path.GetDirectoryName(other)!);
        var before = Snapshot();

        var error = Assert.Throws<TacitException>(() => _home.Tacit().Connect(new ConnectRequest("omp", other, SkillsFolder)));

        Assert.Equal(TacitError.BindingConflict, error.Error);
        Assert.Contains("tacit disconnect --client omp", error.Message);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void Connecting_the_same_name_and_paths_again_changes_nothing_in_the_context_file()
    {
        WriteAgentsMd("# My rules\n");
        _home.Bind();
        var agents = File.ReadAllBytes(AgentsMd);
        var agentsWritten = File.GetLastWriteTimeUtc(AgentsMd);
        Thread.Sleep(50);

        _home.Bind();

        Assert.Equal(agents, File.ReadAllBytes(AgentsMd));
        Assert.Equal(agentsWritten, File.GetLastWriteTimeUtc(AgentsMd));
        Assert.Single(JsonNode.Parse(File.ReadAllText(BindingsJson))!["bindings"]!.AsArray());
    }

    [Fact]
    public void A_context_file_whose_folder_does_not_exist_is_refused_and_nothing_is_written()
    {
        Directory.CreateDirectory(SkillsFolder);
        var missing = Path.Combine(_home.UserHome, ".omp", "agnet", "AGENTS.md");
        var before = Snapshot();

        var error = Assert.Throws<TacitException>(() => _home.Tacit().Connect(new ConnectRequest("omp", missing, SkillsFolder)));

        Assert.Equal(TacitError.ContextFolderMissing, error.Error);
        Assert.Contains(Path.GetDirectoryName(missing)!, error.Message);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void A_skills_folder_that_does_not_exist_is_refused_and_not_created()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AgentsMd)!);
        var before = Snapshot();

        var error = Assert.Throws<TacitException>(() => _home.Tacit().Connect(new ConnectRequest("omp", AgentsMd, SkillsFolder)));

        Assert.Equal(TacitError.SkillsFolderMissing, error.Error);
        Assert.Contains(SkillsFolder, error.Message);
        Assert.False(Directory.Exists(SkillsFolder));
        Assert.Equal(before, Snapshot());
    }

    [Theory]
    [InlineData("<!-- tacit:begin x -->\nno end\n")]
    [InlineData("no begin\n<!-- tacit:end -->\n")]
    [InlineData("<!-- tacit:begin -->\n<!-- tacit:end -->\n<!-- tacit:begin -->\n<!-- tacit:end -->\n")]
    [InlineData("<!-- tacit:end -->\n<!-- tacit:begin -->\n")]
    public void Broken_markers_are_refused_and_nothing_is_written(string agents)
    {
        WriteAgentsMd(agents);
        Directory.CreateDirectory(SkillsFolder);
        var before = Snapshot();

        var error = Assert.Throws<TacitException>(() => _home.Tacit().Connect(new ConnectRequest("omp", AgentsMd, SkillsFolder)));

        Assert.Equal(TacitError.MarkersBroken, error.Error);
        Assert.Contains(AgentsMd, error.Message);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void A_deleted_context_file_is_skipped_and_not_recreated()
    {
        _home.Bind();
        File.Delete(AgentsMd);

        var result = Assert.Single(_home.Tacit().RenderBlocks());

        Assert.Equal(BlockState.FileMissing, result.State);
        Assert.False(File.Exists(AgentsMd));
    }

    [Fact]
    public void Rendering_replaces_only_the_block_and_keeps_every_other_byte()
    {
        WriteAgentsMd("# Before\r\nline\r\n");
        _home.Bind();
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
    public void Disconnect_restores_the_context_file_byte_for_byte()
    {
        var original = "# My rules\n- be brief\n"u8.ToArray();
        WriteAgentsMd(Encoding.UTF8.GetString(original));
        File.WriteAllText(PersonaMd, Persona.ReplaceLineEndings("\n"));

        _home.Bind();
        _home.Unbind();

        Assert.Equal(original, File.ReadAllBytes(AgentsMd));
        Assert.Empty(JsonNode.Parse(File.ReadAllText(BindingsJson))!["bindings"]!.AsArray());
    }

    [Fact]
    public void Disconnect_removes_a_context_file_that_connect_created_once_it_is_empty()
    {
        _home.Bind();

        _home.Unbind();

        Assert.False(File.Exists(AgentsMd));
    }

    [Fact]
    public void Disconnect_keeps_an_empty_context_file_that_connect_did_not_create()
    {
        WriteAgentsMd("");
        _home.Bind();

        _home.Unbind();

        Assert.True(File.Exists(AgentsMd));
        Assert.Equal("", File.ReadAllText(AgentsMd));
    }

    [Fact]
    public void Disconnect_restores_a_file_that_had_no_final_line_break()
    {
        WriteAgentsMd("# My rules\r\n- be brief");

        _home.Bind();
        _home.Tacit().RenderBlocks();
        _home.Unbind();

        Assert.Equal("# My rules\r\n- be brief", File.ReadAllText(AgentsMd));
    }

    [Fact]
    public void Disconnect_of_a_name_that_is_not_bound_is_refused_and_changes_nothing()
    {
        _home.Bind();
        var before = Snapshot();

        var error = Assert.Throws<TacitException>(() => _home.Unbind("nobody"));

        Assert.Equal(TacitError.BindingNotFound, error.Error);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void Disconnect_takes_the_name_in_any_case()
    {
        _home.Bind();

        _home.Unbind("OMP");

        Assert.Empty(JsonNode.Parse(File.ReadAllText(BindingsJson))!["bindings"]!.AsArray());
    }

    [Fact]
    public void Disconnect_reports_a_context_file_that_is_already_gone_and_still_removes_the_record()
    {
        _home.Bind();
        File.Delete(AgentsMd);

        var result = _home.Unbind();

        Assert.Contains(AgentsMd, result.Report);
        Assert.Null(result.Endpoint);
        Assert.Empty(JsonNode.Parse(File.ReadAllText(BindingsJson))!["bindings"]!.AsArray());
    }

    [Fact]
    public void Disconnect_works_while_the_config_does_not_load()
    {
        _home.Bind();
        File.WriteAllText(Path.Combine(_home.TacitHome, "config.yaml"), "models: [\n");

        _home.Unbind();

        Assert.Empty(JsonNode.Parse(File.ReadAllText(BindingsJson))!["bindings"]!.AsArray());
    }

    [Fact]
    public void A_persona_that_is_not_utf8_leaves_the_last_block_in_place()
    {
        File.WriteAllText(PersonaMd, Persona.ReplaceLineEndings("\n"));
        _home.Bind();
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

        _home.Bind();

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
        _home.Bind();
        File.Delete(BindingsJson);

        Assert.Empty(_home.Tacit().RenderBlocks());
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

        _home.Bind();

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
        Directory.CreateDirectory(SkillsFolder);
        var before = Snapshot();

        var error = Assert.Throws<TacitException>(() => _home.Tacit().Connect(new ConnectRequest("omp", AgentsMd, SkillsFolder)));

        Assert.Equal(TacitError.FileNotUtf8, error.Error);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void A_context_file_that_is_not_utf8_is_left_alone()
    {
        _home.Bind();
        var gbk = Encoding.Latin1.GetBytes("# régles\n");
        File.WriteAllBytes(AgentsMd, gbk);

        var result = Assert.Single(_home.Tacit().RenderBlocks());

        Assert.Equal(BlockState.FileNotUtf8, result.State);
        Assert.Equal(gbk, File.ReadAllBytes(AgentsMd));
    }

    [Fact]
    public void A_broken_bindings_json_is_reported()
    {
        File.WriteAllText(BindingsJson, "{ not json");

        var error = Assert.Throws<TacitException>(() => _home.Tacit().RenderBlocks());

        Assert.Equal(TacitError.BindingsInvalid, error.Error);
        Assert.Contains("bindings.json", error.Message);
    }

    private static readonly string[] SkillNames = ["tacit-memory", "tacit-remember", "tacit-checkup"];

    private string OtherAgentsMd => Path.Combine(_home.UserHome, "beta", "AGENTS.md");

    private string OtherSkillsFolder => Path.Combine(_home.UserHome, "beta", "skills");

    private string LinkOf(string name) => Path.Combine(SkillsFolder, name);

    /// <summary>Whether <paramref name="path"/> is a junction or symbolic link entry, whatever it points to.</summary>
    private static bool IsLinkEntry(string path) => new DirectoryInfo(path).LinkTarget is not null;

    private static int CountBlocks(string text) => text.Split("<!-- tacit:begin").Length - 1;

    private JsonObject RecordOf(string client) =>
        JsonNode.Parse(File.ReadAllText(BindingsJson))!["bindings"]!.AsArray().Select(node => node!.AsObject()).Single(record => (string?)record["client"] == client);

    private int RecordCount() => JsonNode.Parse(File.ReadAllText(BindingsJson))!["bindings"]!.AsArray().Count;

    [Theory]
    [InlineData("omp", "beta")]
    [InlineData("beta", "omp")]
    public void Clients_sharing_both_paths_keep_one_block_and_three_links_until_the_last_one_leaves(string first, string second)
    {
        File.WriteAllText(PersonaMd, Persona.ReplaceLineEndings("\n"));
        _home.Bind("omp");
        _home.Bind("beta");

        Assert.Equal(1, CountBlocks(File.ReadAllText(AgentsMd)));
        Assert.All(SkillNames, name => Assert.True(IsLinkEntry(LinkOf(name))));

        _home.Unbind(first);

        Assert.Equal(1, CountBlocks(File.ReadAllText(AgentsMd)));
        Assert.All(SkillNames, name => Assert.True(IsLinkEntry(LinkOf(name))));
        Assert.Single(JsonNode.Parse(File.ReadAllText(BindingsJson))!["bindings"]!.AsArray());

        _home.Unbind(second);

        Assert.False(File.Exists(AgentsMd));
        Assert.All(SkillNames, name => Assert.False(Directory.Exists(LinkOf(name))));
        Assert.Equal(0, RecordCount());
    }

    [Fact]
    public void A_client_sharing_only_the_context_file_reuses_the_block_and_the_file_goes_with_the_last()
    {
        _home.Bind();
        Directory.CreateDirectory(OtherSkillsFolder);

        var result = _home.Tacit().Connect(new ConnectRequest("beta", AgentsMd, OtherSkillsFolder));

        Assert.Contains("已由 omp 使用", result.Report);
        Assert.True((bool?)RecordOf("beta")["createdContextFile"]);
        Assert.Equal(1, CountBlocks(File.ReadAllText(AgentsMd)));

        var first = _home.Unbind("omp");

        Assert.Contains("仍被 beta 使用，保留", first.Report);
        Assert.Equal(1, CountBlocks(File.ReadAllText(AgentsMd)));
        Assert.All(SkillNames, name => Assert.True(IsLinkEntry(Path.Combine(OtherSkillsFolder, name))));

        _home.Unbind("beta");

        Assert.False(File.Exists(AgentsMd));
    }

    [Fact]
    public void A_context_file_shared_by_two_clients_renders_once_with_one_result()
    {
        File.WriteAllText(PersonaMd, Persona.ReplaceLineEndings("\n"));
        _home.Bind();
        _home.Bind("beta");
        File.WriteAllText(PersonaMd, "## 偏好\n- 沟通：先要结论\n");

        var result = Assert.Single(_home.Tacit().RenderBlocks());

        Assert.Equal(BlockState.Written, result.State);
        Assert.Equal(1, CountBlocks(File.ReadAllText(AgentsMd)));
        Assert.Contains("- 沟通：先要结论\n", File.ReadAllText(AgentsMd));
    }

    [Fact]
    public void On_windows_a_path_spelled_in_other_case_is_the_same_place()
    {
        if (!OperatingSystem.IsWindows())
            return;
        File.WriteAllText(PersonaMd, Persona.ReplaceLineEndings("\n"));
        _home.Bind();

        var result = _home.Tacit().Connect(new ConnectRequest("beta", AgentsMd.ToUpperInvariant(), SkillsFolder.ToUpperInvariant()));

        Assert.Contains("已由 omp 使用", result.Report);
        Assert.Equal(1, CountBlocks(File.ReadAllText(AgentsMd)));
        Assert.Single(_home.Tacit().RenderBlocks());

        var first = _home.Unbind("omp");

        Assert.Contains("仍被 beta 使用，保留", first.Report);
        Assert.True(File.Exists(AgentsMd));
        Assert.All(SkillNames, name => Assert.True(IsLinkEntry(LinkOf(name))));
    }

    [Fact]
    public void A_name_bound_to_other_shared_paths_is_refused_and_writes_nothing()
    {
        _home.Bind();
        Directory.CreateDirectory(Path.GetDirectoryName(OtherAgentsMd)!);
        Directory.CreateDirectory(OtherSkillsFolder);
        var before = Snapshot();

        var error = Assert.Throws<TacitException>(() => _home.Tacit().Connect(new ConnectRequest("omp", OtherAgentsMd, OtherSkillsFolder)));

        Assert.Equal(TacitError.BindingConflict, error.Error);
        Assert.Equal(before, Snapshot());
    }
}
