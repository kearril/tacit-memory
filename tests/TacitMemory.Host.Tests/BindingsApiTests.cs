using System.Net;
using System.Text.Json;
using TacitMemory.Core;
using TacitMemory.Core.Tests;
using TacitMemory.Host.Api;

namespace TacitMemory.Host.Tests;

/// <summary>
/// <c>/api/bindings</c> over real HTTP: binding a client by the rules of <c>tacit connect</c>, a refusal with the reason
/// the command gives, the list with what is wrong with each binding, unbinding and a name that is not bound, and the
/// refusal of another device without a session.
/// </summary>
public sealed class BindingsApiTests : IAsyncLifetime
{
    private readonly TestHome _home = new();
    private MemoryService _service = null!;
    private ServiceHost _host = null!;
    private readonly HttpClient _local = PeerClients.Local();
    private readonly HttpClient _remote = PeerClients.Remote();

    public async Task InitializeAsync()
    {
        _home.InitReady();
        _service = _home.Tacit().OpenService(_ => { }, new CapturingLoggerFactory());
        _host = await ServiceHost.TryStartAsync(_service, port: 0, NullLoggerProvider.Instance, PeerClients.OnlyLoopbackIsLocal)
            ?? throw new InvalidOperationException("Port 0 is never in use.");
    }

    public async Task DisposeAsync()
    {
        _local.Dispose();
        _remote.Dispose();
        await _host.DisposeAsync();
        _service.Dispose();
        _home.Dispose();
    }

    private Uri Url(string path = "") => new($"http://127.0.0.1:{_host.Endpoint.Port}/api/bindings{path}");

    private string AgentsMd => Path.Combine(_home.UserHome, ".omp", "agent", "AGENTS.md");

    private string SkillsFolder => Path.Combine(_home.UserHome, ".omp", "agent", "skills");

    private static readonly ConnectRequest Omp = new("Omp", "~/.omp/agent/AGENTS.md", "~/.omp/agent/skills");

    private Task<HttpResponseMessage> PostAsync(ConnectRequest body) =>
        _local.PostAsync(Url(), JsonContent.Create(body, options: RequestJson.Options));

    private static async Task<T> Read<T>(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<T>(await response.Content.ReadAsStringAsync(), ManagementApiJson.Options)!;

    private async Task<BindingList> ListAsync()
    {
        using var response = await _local.GetAsync(Url());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Read<BindingList>(response);
    }

    [Fact]
    public async Task A_client_is_bound_with_the_home_expanded_and_the_mcp_server_to_register()
    {
        Directory.CreateDirectory(SkillsFolder);

        using var response = await PostAsync(Omp);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var done = await Read<BindingDone>(response);
        Assert.Equal("omp", done.Client);
        Assert.NotNull(done.Mcp);
        Assert.EndsWith("/mcp", done.Mcp.Url.AbsoluteUri);
        Assert.Equal("tacit", done.Mcp.Name);
        Assert.True(File.Exists(AgentsMd));
        var list = await ListAsync();
        var binding = Assert.Single(list.Items);
        Assert.Equal(("omp", AgentsMd, SkillsFolder, (BindingProblem?)null), (binding.Client, binding.ContextFile, binding.SkillsFolder, binding.Problem));
        Assert.Equal(done.Mcp, list.Mcp);
    }

    [Fact]
    public async Task A_refusal_gives_the_reason_the_command_gives_and_changes_nothing()
    {
        // A file where the skills folder goes, which connect refuses to build a folder over.
        Directory.CreateDirectory(Path.GetDirectoryName(SkillsFolder)!);
        File.WriteAllText(SkillsFolder, "in the way");
        var expected = Assert.Throws<TacitException>(() => _home.Tacit().Connect(Omp));

        using var response = await PostAsync(Omp);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await Read<ApiError>(response);
        Assert.Equal((expected.Error, expected.Message), (error.Error, error.Message));
        Assert.Empty((await ListAsync()).Items);
    }

    [Fact]
    public async Task A_client_name_outside_the_rules_is_a_bad_request()
    {
        Directory.CreateDirectory(SkillsFolder);

        using var response = await PostAsync(Omp with { Client = "open ai" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(TacitError.ClientNameInvalid, (await Read<ApiError>(response)).Error);
    }

    [Fact]
    public async Task The_list_says_what_is_wrong_with_a_binding()
    {
        Directory.CreateDirectory(SkillsFolder);
        (await PostAsync(Omp)).Dispose();
        File.Delete(AgentsMd);

        Assert.Equal(BindingProblem.ContextFileMissing, Assert.Single((await ListAsync()).Items).Problem);
    }

    [Fact]
    public async Task A_client_is_unbound_and_a_name_that_is_not_bound_is_not_found()
    {
        Directory.CreateDirectory(SkillsFolder);
        (await PostAsync(Omp)).Dispose();

        using var response = await _local.DeleteAsync(Url("/OMP"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var done = await Read<BindingDone>(response);
        Assert.Equal("omp", done.Client);
        Assert.Null(done.Mcp);
        Assert.Empty((await ListAsync()).Items);
        using var again = await _local.DeleteAsync(Url("/omp"));
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal(TacitError.BindingNotFound, (await Read<ApiError>(again)).Error);
    }

    [Fact]
    public async Task Another_device_without_a_session_is_refused()
    {
        using var list = await _remote.GetAsync(Url());
        using var bind = await _remote.PostAsync(Url(), JsonContent.Create(Omp, options: RequestJson.Options));
        using var unbind = await _remote.DeleteAsync(Url("/omp"));

        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, bind.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unbind.StatusCode);
    }
}
