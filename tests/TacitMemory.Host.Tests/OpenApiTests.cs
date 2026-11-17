using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using TacitMemory.Core;
using TacitMemory.Core.Tests;
using TacitMemory.Host.Api;

namespace TacitMemory.Host.Tests;

/// <summary>
/// The management API's OpenAPI document: served under <c>/api</c> behind the same guards, and equal to the snapshot at
/// <c>web/openapi.json</c>, which the frontend generates its types from. Running with <c>TACIT_UPDATE_OPENAPI</c> set
/// rewrites the snapshot instead of failing.
/// </summary>
public sealed class OpenApiTests : IAsyncLifetime
{
    private const string UpdateVariable = "TACIT_UPDATE_OPENAPI";

    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly TestHome _home = new();
    private readonly ModelDouble _models = new() { EmbeddingDown = true, RerankDown = true };
    private readonly CapturingLoggerFactory _logs = new();
    private MemoryService _service = null!;
    private ServiceHost _host = null!;
    private HttpClient _http = null!;

    public async Task InitializeAsync()
    {
        _home.InitReady(models: _models);
        _service = _home.Tacit().OpenService(_ => { }, _logs);
        _host = await ServiceHost.TryStartAsync(_service, port: 0, NullLoggerProvider.Instance) ?? throw new InvalidOperationException("Port 0 is never in use.");
        _http = new HttpClient();
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _host.DisposeAsync();
        _service.Dispose();
        _models.Dispose();
        _home.Dispose();
    }

    private Uri Url(string path) => new($"http://127.0.0.1:{_host.Endpoint.Port}{path}");

    [Fact]
    public async Task The_document_is_served_under_api_and_describes_the_status_report()
    {
        using var response = await _http.GetAsync(Url("/api/openapi.json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal("3.1.2", document["openapi"]!.GetValue<string>());
        var status = document["paths"]!["/api/status"]!["get"]!["responses"]!["200"]!["content"]!["application/json"]!["schema"]!;
        Assert.Equal("#/components/schemas/StatusReport", status["$ref"]!.GetValue<string>());
    }

    [Fact]
    public async Task The_document_describes_the_activity_report()
    {
        var document = JsonNode.Parse(await _http.GetStringAsync(Url("/api/openapi.json")))!;

        var responses = document["paths"]?["/api/activity"]?["get"]?["responses"];
        var schema = responses?["200"]?["content"]?["application/json"]?["schema"];
        Assert.Equal("#/components/schemas/ActivityReport", schema?["$ref"]?.GetValue<string>());
        Assert.NotNull(responses?["401"]);
    }

    [Fact]
    public async Task The_document_describes_the_backup_list()
    {
        var document = JsonNode.Parse(await _http.GetStringAsync(Url("/api/openapi.json")))!;

        var responses = document["paths"]?["/api/backups"]?["get"]?["responses"];
        var schema = responses?["200"]?["content"]?["application/json"]?["schema"];
        Assert.Equal("#/components/schemas/BackupList", schema?["$ref"]?.GetValue<string>());
        Assert.NotNull(responses?["401"]);
    }

    [Fact]
    public async Task The_document_describes_the_service_log_with_its_three_levels()
    {
        var document = JsonNode.Parse(await _http.GetStringAsync(Url("/api/openapi.json")))!;

        var days = document["paths"]?["/api/logs"]?["get"]?["responses"]?["200"]?["content"]?["application/json"]?["schema"];
        Assert.Equal("#/components/schemas/ServiceLogDays", days?["$ref"]?.GetValue<string>());
        var responses = document["paths"]?["/api/logs/{date}"]?["get"]?["responses"];
        var day = responses?["200"]?["content"]?["application/json"]?["schema"];
        Assert.Equal("#/components/schemas/ServiceLogDay", day?["$ref"]?.GetValue<string>());
        Assert.NotNull(responses?["400"]);
        Assert.NotNull(responses?["404"]);
        var levels = document["components"]?["schemas"]?["ServiceLogLevel"]?["enum"]?.AsArray().Select(level => level?.GetValue<string>());
        Assert.Equal(["information", "warning", "error"], levels);
    }

    [Fact]
    public async Task The_document_describes_the_probe_report()
    {
        var document = JsonNode.Parse(await _http.GetStringAsync(Url("/api/openapi.json")))!;

        var responses = document["paths"]?["/api/probe"]?["post"]?["responses"];
        var schema = responses?["200"]?["content"]?["application/json"]?["schema"];
        Assert.Equal("#/components/schemas/ProbeReport", schema?["$ref"]?.GetValue<string>());
        Assert.NotNull(responses?["409"]);
        var result = document["components"]?["schemas"]?["ProbeResult"]?["properties"];
        Assert.NotNull(result?["elapsedMilliseconds"]);
        Assert.Null(result?["elapsed"]);
    }

    [Fact]
    public async Task The_document_is_refused_by_the_same_guards_as_the_other_endpoints()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Url("/api/openapi.json"));
        request.Headers.TryAddWithoutValidation("Origin", "https://evil.example");

        using var response = await _http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var error = JsonSerializer.Deserialize<ApiError>(await response.Content.ReadAsStringAsync(), ManagementApiJson.Options)!;
        Assert.Equal(TacitError.ApiOriginRefused, error.Error);
    }

    [Theory]
    [InlineData("/api/session", "post")]
    [InlineData("/api/access-password", "put")]
    public async Task The_login_limit_answer_declares_the_retry_after_header(string path, string method)
    {
        var document = JsonNode.Parse(await _http.GetStringAsync(Url("/api/openapi.json")))!;

        var header = document["paths"]?[path]?[method]?["responses"]?["429"]?["headers"]?["Retry-After"];
        Assert.NotNull(header);
        Assert.Equal("integer", header["schema"]?["type"]?.GetValue<string>());
    }

    [Theory]
    [InlineData("/api/session", "post")]
    [InlineData("/api/access-password", "put")]
    public async Task The_endpoints_that_take_a_json_body_declare_415(string path, string method)
    {
        var document = JsonNode.Parse(await _http.GetStringAsync(Url("/api/openapi.json")))!;

        Assert.NotNull(document["paths"]?[path]?[method]?["responses"]?["415"]);
    }

    [Theory]
    [InlineData("get")]
    [InlineData("delete")]
    public async Task The_session_endpoint_declares_no_401_for_reading_or_logging_out(string method)
    {
        var document = JsonNode.Parse(await _http.GetStringAsync(Url("/api/openapi.json")))!;

        var responses = document["paths"]?["/api/session"]?[method]?["responses"];
        Assert.NotNull(responses?["200"]);
        Assert.Null(responses?["401"]);
    }

    [Fact]
    public async Task The_error_body_names_its_error_from_one_enum_with_the_names_as_written()
    {
        var document = JsonNode.Parse(await _http.GetStringAsync(Url("/api/openapi.json")))!;

        var schemas = document["components"]?["schemas"];
        Assert.Equal("#/components/schemas/TacitError", schemas?["ApiError"]?["properties"]?["error"]?["$ref"]?.GetValue<string>());
        var names = schemas?["TacitError"]?["enum"]?.AsArray().Select(name => name!.GetValue<string>()).ToArray() ?? [];
        Assert.Equal(Enum.GetNames<TacitError>(), names);
    }

    [Fact]
    public async Task The_page_types_and_the_resident_sections_are_enums_named_as_they_are_written()
    {
        var document = JsonNode.Parse(await _http.GetStringAsync(Url("/api/openapi.json")))!;

        var schemas = document["components"]?["schemas"];
        string[] Names(string schema) => schemas?[schema]?["enum"]?.AsArray().Select(name => name!.GetValue<string>()).ToArray() ?? [];
        Assert.Equal(ToolValues.PageTypes.All, Names("PageType"));
        Assert.Equal(["red_lines", "working_rules", "preferences", "profile", "situational_rules", "workstreams"], Names("ResidentPart"));
    }

    [Fact]
    public async Task An_enum_holds_only_its_names_and_a_nullable_use_says_so_where_it_refers_to_it()
    {
        var document = JsonNode.Parse(await _http.GetStringAsync(Url("/api/openapi.json")))!;

        var schemas = document["components"]!["schemas"]!.AsObject();
        Assert.All(
            schemas.Where(schema => schema.Value?["enum"] is not null),
            schema => Assert.DoesNotContain(schema.Value!["enum"]!.AsArray(), value => value is null));
        var type = schemas["PageListItem"]!["properties"]!["type"]!["oneOf"]!.AsArray();
        Assert.Contains(type, option => option?["type"]?.GetValue<string>() == "null");
        Assert.Contains(type, option => option?["$ref"]?.GetValue<string>() == "#/components/schemas/PageType");
    }

    [Fact]
    public async Task A_config_save_declares_that_a_secret_may_be_null_which_removes_it()
    {
        var document = JsonNode.Parse(await _http.GetStringAsync(Url("/api/openapi.json")))!;

        var value = document["components"]?["schemas"]?["ConfigSave"]?["properties"]?["secrets"]?["additionalProperties"]?["type"]?.AsArray()
            .Select(type => type!.GetValue<string>()).ToArray();
        Assert.Equal(["null", "string"], value!);
    }

    [Fact]
    public async Task The_restore_preview_declares_its_report_as_required()
    {
        var document = JsonNode.Parse(await _http.GetStringAsync(Url("/api/openapi.json")))!;

        var required = document["components"]?["schemas"]?["RestorePreview"]?["required"]?.AsArray()
            .Select(name => name!.GetValue<string>()).ToArray() ?? [];
        Assert.Contains("report", required!);
    }

    [Fact]
    public async Task The_document_matches_the_snapshot_in_the_web_folder()
    {
        var actual = Canonical(await _http.GetStringAsync(Url("/api/openapi.json")));
        var snapshot = Path.Combine(RepositoryRoot(), "web", "openapi.json");

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(UpdateVariable)))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(snapshot)!);
            File.WriteAllText(snapshot, actual + "\n");
            return;
        }

        Assert.True(File.Exists(snapshot), $"The OpenAPI snapshot is missing: {snapshot}. Run this test with {UpdateVariable}=1 to write it.");
        var expected = Canonical(File.ReadAllText(snapshot));
        Assert.True(
            expected == actual,
            $"The management API document no longer matches {snapshot}. If the change is intended, run this test with {UpdateVariable}=1 "
            + "to rewrite the snapshot, then regenerate the frontend types from it.");
    }

    /// <summary>
    /// The document in the form the snapshot is kept in: indented with line feeds whatever the platform's line ending is,
    /// so the snapshot file is the same bytes on every machine.
    /// </summary>
    private static string Canonical(string json) => JsonNode.Parse(json)!.ToJsonString(CanonicalOptions).ReplaceLineEndings("\n");

    /// <summary>The directory above the test assembly that holds the solution file.</summary>
    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TacitMemory.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException("The solution folder was not found above the test assembly.");
    }
}
