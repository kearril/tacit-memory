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
            ## 红线
            - 不要删库
              <!-- 2026-10-25 原话「别删库」 -->

            ## 领域：network-proxy
            触发：改代理时
            ### 偏好
            - 测速：取中位数

            """);
        Write("candidates.md", """
            ## 候选

            ### 回答先给结论
            归属：偏好
            - 2026-11-01 原话「先说结论」

            """);

        var report = JsonNode.Parse(await _local.GetStringAsync(Url("/api/memory/persona")))!;

        var redLines = report["global"]![0]!;
        Assert.Equal("redLines", redLines["section"]!.GetValue<string>());
        var entry = redLines["entries"]![0]!;
        Assert.Equal("不要删库", entry["text"]!.GetValue<string>());
        Assert.Matches("^[0-9a-f]{6}$", entry["code"]!.GetValue<string>());
        Assert.Equal("<!-- 2026-10-25 原话「别删库」 -->", entry["continuation"]![0]!.GetValue<string>());
        Assert.Equal("Red line without instead", report["personaFile"]!["problems"]![0]!["kind"]!.GetValue<string>());
        Assert.Equal(2, report["personaFile"]!["problems"]![0]!["line"]!.GetValue<int>());
        Assert.Equal("改代理时", report["domains"]![0]!["trigger"]!.GetValue<string>());
        var candidate = report["candidates"]![0]!;
        Assert.Equal(("preferences", "global"), (candidate["section"]!.GetValue<string>(), candidate["scope"]!.GetValue<string>()));
        Assert.EndsWith("candidates.md", report["candidatesFile"]!["path"]!.GetValue<string>());
    }

    [Fact]
    public async Task Another_device_without_a_session_is_refused()
    {
        using var response = await _remote.GetAsync(Url("/api/memory/persona"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_endpoints_are_in_the_openapi_document()
    {
        var document = JsonNode.Parse(await _local.GetStringAsync(Url("/api/openapi.json")))!;

        Assert.NotNull(document["paths"]?["/api/memory/persona"]?["get"]);
        Assert.NotNull(document["components"]?["schemas"]?["PersonaReport"]);
        Assert.NotNull(document["components"]?["schemas"]?["PersonaSection"]);
    }
}
