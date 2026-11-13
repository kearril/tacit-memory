using System.Net;
using System.Text.Json;
using TacitMemory.Core;
using TacitMemory.Core.Tests;
using TacitMemory.Host.Api;

namespace TacitMemory.Host.Tests;

/// <summary>
/// <c>POST /api/probe</c> over real HTTP: the three models probed as the saved <c>config.yaml</c> configures them, a model
/// that fails, a configuration saved after the service started, a saved configuration that cannot be loaded, and the
/// refusal of another device without a session.
/// </summary>
public sealed class ProbeApiTests : IAsyncLifetime
{
    private readonly TestHome _home = new();
    private readonly ModelDouble _models = new();
    private MemoryService _service = null!;
    private ServiceHost _host = null!;
    private readonly HttpClient _local = PeerClients.Local();
    private readonly HttpClient _remote = PeerClients.Remote();

    public async Task InitializeAsync()
    {
        _home.InitReady(models: _models);
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
        _models.Dispose();
        _home.Dispose();
    }

    private Uri Url => new($"http://127.0.0.1:{_host.Endpoint.Port}/api/probe");

    private Task<HttpResponseMessage> PostAsync(HttpClient client) =>
        client.PostAsync(Url, new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

    private async Task<ProbeReport> ProbeAsync()
    {
        using var response = await PostAsync(_local);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonSerializer.Deserialize<ProbeReport>(await response.Content.ReadAsStringAsync(), ManagementApiJson.Options)!;
    }

    [Fact]
    public async Task Probes_the_three_models_in_order_and_gives_the_time_in_milliseconds()
    {
        using var response = await PostAsync(_local);
        var body = await response.Content.ReadAsStringAsync();

        var report = JsonSerializer.Deserialize<ProbeReport>(body, ManagementApiJson.Options)!;
        Assert.Equal([ModelRole.Embedding, ModelRole.Reranker, ModelRole.Decision], report.Results.Select(result => result.Role));
        Assert.All(report.Results, result => Assert.Equal(ProbeOutcome.Succeeded, result.Outcome));
        Assert.Contains("\"elapsedMilliseconds\":", body);
        Assert.DoesNotContain("\"elapsed\":", body);
        Assert.Equal(3, report.Lines.Count(line => line.Level is not null));
    }

    [Fact]
    public async Task A_model_that_fails_is_reported_without_failing_the_request()
    {
        _models.EmbeddingDown = true;

        var report = await ProbeAsync();

        var embedding = report.Results.Single(result => result.Role == ModelRole.Embedding);
        Assert.Equal(ProbeOutcome.Failed, embedding.Outcome);
        Assert.NotNull(embedding.Failure);
        Assert.All(report.Results.Where(result => result.Role != ModelRole.Embedding), result => Assert.Equal(ProbeOutcome.Succeeded, result.Outcome));
    }

    [Fact]
    public async Task Reads_the_configuration_saved_after_the_service_started()
    {
        File.WriteAllText(Path.Combine(_home.TacitHome, "config.yaml"), "");
        var calls = (_models.EmbeddingCalls, _models.RerankCalls, _models.DecisionCalls);

        var report = await ProbeAsync();

        Assert.All(report.Results, result => Assert.Equal(ProbeOutcome.Unconfigured, result.Outcome));
        Assert.Equal(calls, (_models.EmbeddingCalls, _models.RerankCalls, _models.DecisionCalls));
    }

    [Fact]
    public async Task A_saved_configuration_that_cannot_be_loaded_is_a_conflict()
    {
        File.WriteAllText(Path.Combine(_home.TacitHome, "config.yaml"), "server: [");

        using var response = await PostAsync(_local);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = JsonSerializer.Deserialize<ApiError>(await response.Content.ReadAsStringAsync(), ManagementApiJson.Options)!;
        Assert.Equal(nameof(TacitError.ConfigInvalid), error.Error);
    }

    [Fact]
    public async Task Another_device_without_a_session_is_refused_with_401()
    {
        using var response = await PostAsync(_remote);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
