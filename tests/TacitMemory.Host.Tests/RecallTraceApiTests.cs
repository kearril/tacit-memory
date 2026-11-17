using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using TacitMemory.Core;
using TacitMemory.Core.Tests;
using TacitMemory.Host.Api;

namespace TacitMemory.Host.Tests;

/// <summary>
/// <c>POST /api/recall/trace</c> over real HTTP: the report's shape, a request with bad parameters refused with every
/// problem, the refusal of another device without a session, and the endpoint in the OpenAPI document.
/// </summary>
public sealed class RecallTraceApiTests : IAsyncLifetime
{
    private readonly TestHome _home = new();
    private readonly CapturingLoggerFactory _logs = new();
    private MemoryService _service = null!;
    private ServiceHost _host = null!;
    private HttpClient _local = null!;
    private HttpClient _remote = null!;

    public async Task InitializeAsync()
    {
        _home.InitReady();
        _service = _home.Tacit().OpenService(_ => { }, _logs);
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

    private static JsonContent Body(object request) => JsonContent.Create(request, options: RequestJson.Options);

    [Fact]
    public async Task A_trace_answers_with_settings_stages_results_text_and_units()
    {
        var created = await _service.WritePageAsync(new WritePageRequest
        {
            Op = "create",
            Type = ToolValues.PageTypes.Work,
            Page = "ops/rollback",
            Title = "部署回滚",
            Aliases = ["回滚方案", "部署失败回滚"],
            Sections = [new PageSectionInput("回滚步骤", "部署失败时先回滚。")],
        });
        Assert.False(created.IsError, created.Text);

        using var response = await _local.PostAsync(Url("/api/recall/trace"), Body(new { query = "部署", topK = 3 }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal(3, json["settings"]!["topK"]!["value"]!.GetValue<double>());
        Assert.Equal("phrase", json["fullText"]!.GetValue<string>());
        var result = json["results"]!.AsArray()[0]!;
        Assert.Equal("page", result["kind"]!.GetValue<string>());
        Assert.Equal("ops/rollback", result["page"]!.GetValue<string>());
        var shown = result["sections"]!.AsArray()[0]!;
        Assert.Equal("回滚步骤", shown["name"]!.GetValue<string>());
        Assert.Null(shown["recognized"]);
        Assert.Contains("ops/rollback", json["text"]!.GetValue<string>());
        var unit = json["units"]!.AsArray().First(unit => unit!["section"]?.GetValue<string>() == "回滚步骤")!;
        Assert.Equal("shown", unit["fate"]!.GetValue<string>());
        Assert.Equal(1, unit["result"]!.GetValue<int>());
        Assert.NotNull(unit["fusedRank"]);
        var report = JsonSerializer.Deserialize<RecallTraceReport>(json.ToJsonString(), ManagementApiJson.Options)!;
        Assert.Equal(report.Results.Count, report.Counts.Results);
    }

    [Fact]
    public async Task Bad_parameters_are_refused_with_400_and_every_problem()
    {
        using var response = await _local.PostAsync(Url("/api/recall/trace"), Body(new { query = "", from = "昨天", candidates = 0 }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = JsonSerializer.Deserialize<ApiError>(await response.Content.ReadAsStringAsync(), ManagementApiJson.Options)!;
        Assert.Equal(TacitError.RequestInvalid, error.Error);
        Assert.Equal(["query", "from", "candidates"], error.Problems!.Select(problem => problem.Path));
    }

    [Fact]
    public async Task A_successful_trace_is_not_logged_and_a_refused_one_is_logged_without_its_query()
    {
        using var ok = await _local.PostAsync(Url("/api/recall/trace"), Body(new { query = "部署回滚的秘密查询" }));
        using var refused = await _local.PostAsync(Url("/api/recall/trace"), Body(new { query = "另一条秘密查询", topK = 0 }));

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        await _logs.WaitForAsync(entry => entry.Message.Contains("/api/recall/trace", StringComparison.Ordinal));
        var traced = _logs.Entries.Where(entry => entry.Message.Contains("/api/recall/trace", StringComparison.Ordinal)).ToList();
        Assert.Contains("400", Assert.Single(traced).Message);
        Assert.DoesNotContain(_logs.Entries, entry => entry.Message.Contains("秘密查询", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Another_device_without_a_session_is_refused()
    {
        using var response = await _remote.PostAsync(Url("/api/recall/trace"), Body(new { query = "部署" }));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_endpoint_is_in_the_openapi_document_with_its_report()
    {
        var document = JsonNode.Parse(await _local.GetStringAsync(Url("/api/openapi.json")))!;

        var post = document["paths"]?["/api/recall/trace"]?["post"];
        Assert.NotNull(post);
        Assert.NotNull(post["requestBody"]);
        Assert.NotNull(post["responses"]?["400"]);
        Assert.NotNull(document["components"]?["schemas"]?["RecallTraceReport"]);
        Assert.NotNull(document["components"]?["schemas"]?["UnitFate"]);
    }
}
