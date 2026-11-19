using System.Net;
using System.Text.Json.Nodes;
using TacitMemory.Core;
using TacitMemory.Core.Tests;

namespace TacitMemory.Host.Tests;

/// <summary>
/// The read-only memory endpoints over real HTTP: their shapes, the refusal of another device without a session, and
/// their place in the OpenAPI document.
/// </summary>
public sealed class MemoryApiTests : IAsyncLifetime
{
    private readonly TestHome _home = new();
    private MemoryService _service = null!;
    private ServiceHost _host = null!;
    private HttpClient _local = null!;
    private HttpClient _remote = null!;

    public async Task InitializeAsync()
    {
        _home.InitReady();
        _service = _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);
        _host = await ServiceHost.TryStartAsync(_service, port: 0, NullLoggerProvider.Instance, PeerClients.OnlyLoopbackIsLocal)
            ?? throw new InvalidOperationException("Port 0 is never in use.");
        _local = PeerClients.Local();
        _remote = PeerClients.Remote();
    }

    public async Task DisposeAsync()
    {
        _local.Dispose();
        _remote.Dispose();
        await _host.DisposeAsync();
        _service.Dispose();
        _home.Dispose();
    }

    private Uri Url(string path) => new($"http://127.0.0.1:{_host.Endpoint.Port}{path}");

    private void Write(string file, string text) =>
        File.WriteAllText(Path.Combine(_home.DefaultMemory, file), text.ReplaceLineEndings("\n"));

    [Fact]
    public async Task The_persona_answers_with_its_sections_blocks_candidates_and_files()
    {
        Write("persona.md", """
            ## Red lines
            - 不要删库
              <!-- 2026-10-25 quote「别删库」 -->

            ## Preferences
            随手写的一行

            ## Domain: network-proxy
            Trigger: 改代理时
            ### Preferences
            - 测速：取中位数

            """);
        Write("candidates.md", """
            ## Candidates

            ### 回答先给结论
            Belongs to: Preferences
            - 2026-11-01 quote「先说结论」

            """);

        var report = JsonNode.Parse(await _local.GetStringAsync(Url("/api/memory/persona")))!;

        var redLines = report["global"]![0]!;
        Assert.Equal("redLines", redLines["section"]!.GetValue<string>());
        var entry = redLines["entries"]![0]!;
        Assert.Equal("不要删库", entry["text"]!.GetValue<string>());
        Assert.Matches("^[0-9a-f]{6}$", entry["code"]!.GetValue<string>());
        var source = entry["sources"]![0]!;
        Assert.Equal(("2026-10-25", "别删库"), (source["date"]!.GetValue<string>(), source["quote"]!.GetValue<string>()));
        Assert.Equal("Unrecognized content", report["personaFile"]!["problems"]![0]!["kind"]!.GetValue<string>());
        Assert.Equal(6, report["personaFile"]!["problems"]![0]!["line"]!.GetValue<int>());
        Assert.Equal("改代理时", report["domains"]![0]!["trigger"]!.GetValue<string>());
        var candidate = report["candidates"]![0]!;
        Assert.Equal(("preferences", "global"), (candidate["section"]!.GetValue<string>(), candidate["scope"]!.GetValue<string>()));
        Assert.EndsWith("candidates.md", report["candidatesFile"]!["path"]!.GetValue<string>());
    }

    [Fact]
    public async Task The_pages_answer_with_the_list_and_one_page_with_its_sections()
    {
        var created = await _service.Tools.WritePageAsync(new WritePageRequest
        {
            Op = "create",
            Type = ToolValues.PageTypes.Work,
            Page = "ops/rollback",
            Title = "部署回滚",
            Aliases = ["回滚方案", "部署失败回滚"],
            Sections = [new PageSectionInput("产出", "回滚脚本。"), new PageSectionInput("Leftovers", "- 还没做回滚演练")],
        });
        Assert.False(created.IsError, created.Text);

        var list = JsonNode.Parse(await _local.GetStringAsync(Url("/api/memory/pages")))!;
        var page = JsonNode.Parse(await _local.GetStringAsync(Url("/api/memory/pages/ops/rollback")))!;

        Assert.Equal("ops/rollback", list["pages"]![0]!["id"]!.GetValue<string>());
        Assert.Equal("work", list["pages"]![0]!["type"]!.GetValue<string>());
        Assert.Equal("部署回滚", page["title"]!.GetValue<string>());
        var section = page["sections"]![0]!;
        Assert.Equal(("产出", "回滚脚本。"), (section["name"]!.GetValue<string>(), section["body"]!.GetValue<string>().Trim()));
        Assert.Null(section["recognized"]);
        Assert.Equal("leftovers", page["sections"]![1]!["recognized"]!.GetValue<string>());
        Assert.EndsWith("rollback.md", page["file"]!["path"]!.GetValue<string>());
    }

    [Fact]
    public async Task The_workstreams_answer_with_the_list_and_a_brief()
    {
        Write("workstreams.md", "## Workstream\n\n### ops\nBackground: 运维。\n");
        var created = await _service.Tools.WritePageAsync(new WritePageRequest
        {
            Op = "create",
            Type = ToolValues.PageTypes.Decision,
            Page = "ops/rollback",
            Title = "部署回滚",
            Aliases = ["回滚方案", "部署失败回滚"],
            Sections = [new PageSectionInput("决定", "先回滚。")],
        });
        Assert.False(created.IsError, created.Text);

        var list = JsonNode.Parse(await _local.GetStringAsync(Url("/api/memory/workstreams")))!;
        var brief = JsonNode.Parse(await _local.GetStringAsync(Url("/api/memory/workstreams/ops")))!;

        Assert.Equal("ops", list["established"]![0]!["name"]!.GetValue<string>());
        Assert.Equal(1, list["established"]![0]!["pages"]!.GetValue<int>());
        Assert.Equal("运维。", brief["background"]!.GetValue<string>());
        Assert.Equal("decision", brief["groups"]![0]!["type"]!.GetValue<string>());
        Assert.Equal("ops/rollback", brief["groups"]![0]!["pages"]![0]!["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task The_activity_answers_with_the_months_and_a_month_by_day()
    {
        Directory.CreateDirectory(Path.Combine(_home.DefaultMemory, "recent"));
        Write("recent/2026-10.md", "## 2026-10-05\n- [ops] 回滚演练完成\n  <!-- quote「演练一下」 -->\n## Summary\n- 2026-W41：演练\n");

        var months = JsonNode.Parse(await _local.GetStringAsync(Url("/api/memory/activity")))!;
        var month = JsonNode.Parse(await _local.GetStringAsync(Url("/api/memory/activity/2026-10")))!;

        Assert.Equal("2026-10", months["months"]![0]!.GetValue<string>());
        Assert.NotNull(months["current"]);
        var activity = month["days"]![0]!["activities"]![0]!;
        Assert.Equal(("ops", "回滚演练完成"), (activity["workstream"]!.GetValue<string>(), activity["text"]!.GetValue<string>()));
        Assert.Equal("演练一下", activity["sources"]![0]!["quote"]!.GetValue<string>());
        Assert.Equal("week", month["summaries"]![0]!["kind"]!.GetValue<string>());
        Assert.EndsWith("2026-10.md", month["file"]!["path"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("/api/memory/activity/2025-01", HttpStatusCode.NotFound, "MemoryNotFound")]
    [InlineData("/api/memory/activity/2025-13", HttpStatusCode.BadRequest, "RequestInvalid")]
    [InlineData("/api/memory/workstreams/nothing", HttpStatusCode.NotFound, "MemoryNotFound")]
    [InlineData("/api/memory/pages/ops/nothing", HttpStatusCode.NotFound, "MemoryNotFound")]
    [InlineData("/api/memory/pages/Ops!/x", HttpStatusCode.BadRequest, "RequestInvalid")]
    public async Task A_missing_page_is_404_and_a_bad_id_400(string path, HttpStatusCode status, string code)
    {
        using var response = await _local.GetAsync(Url(path));

        Assert.Equal(status, response.StatusCode);
        Assert.Contains(code, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/api/memory/persona")]
    [InlineData("/api/memory/pages")]
    [InlineData("/api/memory/pages/ops/rollback")]
    [InlineData("/api/memory/workstreams")]
    [InlineData("/api/memory/workstreams/ops")]
    [InlineData("/api/memory/activity")]
    [InlineData("/api/memory/activity/2026-10")]
    public async Task Another_device_without_a_session_is_refused(string path)
    {
        using var response = await _remote.GetAsync(Url(path));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_endpoints_are_in_the_openapi_document()
    {
        var document = JsonNode.Parse(await _local.GetStringAsync(Url("/api/openapi.json")))!;

        Assert.NotNull(document["paths"]?["/api/memory/persona"]?["get"]);
        Assert.NotNull(document["components"]?["schemas"]?["PersonaReport"]);
        Assert.NotNull(document["components"]?["schemas"]?["PersonaSection"]);
        Assert.NotNull(document["paths"]?["/api/memory/pages"]?["get"]);
        Assert.NotNull(document["paths"]?["/api/memory/pages/{workstream}/{slug}"]?["get"]?["responses"]?["404"]);
        Assert.NotNull(document["components"]?["schemas"]?["PageView"]);
        Assert.NotNull(document["paths"]?["/api/memory/workstreams"]?["get"]);
        Assert.NotNull(document["paths"]?["/api/memory/workstreams/{name}"]?["get"]?["responses"]?["404"]);
        Assert.NotNull(document["components"]?["schemas"]?["WorkstreamBriefView"]);
        Assert.NotNull(document["paths"]?["/api/memory/activity"]?["get"]);
        Assert.NotNull(document["paths"]?["/api/memory/activity/{month}"]?["get"]?["responses"]?["400"]);
        Assert.NotNull(document["paths"]?["/api/memory/activity/{month}"]?["get"]?["responses"]?["404"]);
        Assert.NotNull(document["components"]?["schemas"]?["ActivityMonthView"]);
    }
}
