using System.Net;
using System.Text.Json;
using TacitMemory.Core;
using TacitMemory.Core.Tests;
using TacitMemory.Host.Api;

namespace TacitMemory.Host.Tests;

/// <summary>
/// <c>GET</c> and <c>PUT /api/config</c> over real HTTP: the settings as the file holds them and as the service runs them,
/// with their defaults and limits; a save that validates, is written and takes effect without a restart; a save that does
/// not validate, or that was based on a file changed since; and the refusal of another device without a session.
/// </summary>
public sealed class ConfigApiTests : IAsyncLifetime
{
    private readonly TestHome _home = new();
    private MemoryService _service = null!;
    private ServiceHost _host = null!;
    private readonly HttpClient _local = PeerClients.Local();
    private readonly HttpClient _remote = PeerClients.Remote();

    public async Task InitializeAsync()
    {
        _home.InitReady("retrieval: { topK: 3 }\n");
        _home.EnvironmentVariables["TACIT_RERANKER_API_KEY"] = "env-key";
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

    private Uri Url => new($"http://127.0.0.1:{_host.Endpoint.Port}/api/config");

    private string ConfigPath => Path.Combine(_home.TacitHome, "config.yaml");

    private async Task<ConfigView> GetAsync()
    {
        using var response = await _local.GetAsync(Url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonSerializer.Deserialize<ConfigView>(await response.Content.ReadAsStringAsync(), ManagementApiJson.Options)!;
    }

    private Task<HttpResponseMessage> PutAsync(ConfigSave body, HttpClient? client = null) =>
        (client ?? _local).PutAsync(Url, JsonContent.Create(body, options: RequestJson.Options));

    private static async Task<T> Read<T>(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<T>(await response.Content.ReadAsStringAsync(), ManagementApiJson.Options)!;

    private void Hand(string slug, string title, string body)
    {
        var path = Path.Combine(_home.DefaultMemory, "pages", "p", slug + ".md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $"---\ntitle: {title}\naliases: [别名一, 别名二]\ndates: {{ first: 2026-10-01, last: 2026-10-05 }}\n---\n\n{body}\n");
    }

    [Fact]
    public async Task Gives_each_setting_as_the_file_holds_it_as_the_service_runs_it_and_its_default()
    {
        var view = await GetAsync();

        Assert.Equal(3, view.File.Retrieval.TopK);
        Assert.Equal(3, view.Running.Retrieval.TopK);
        Assert.Equal(5, view.Defaults.Retrieval.TopK);
        Assert.Equal(18888, view.Defaults.Server.Port);
        Assert.NotNull(view.Defaults.Decision.SamePage);
        Assert.NotEmpty(view.Hash);
        var topK = Assert.Single(view.Keys, key => key.Path == ConfigKeyPath.RetrievalTopK);
        Assert.Equal(1, topK.Min);
        Assert.False(topK.Restart);
        Assert.True(Assert.Single(view.Keys, key => key.Path == ConfigKeyPath.ServerPort).Restart);
        Assert.True(Assert.Single(view.Keys, key => key.Path == ConfigKeyPath.MemoryPath).ReadOnly);
    }

    [Fact]
    public async Task A_saved_setting_takes_effect_at_once_without_a_restart()
    {
        foreach (var slug in (string[])["a", "b", "c"])
            Hand(slug, $"部署问题 {slug}", "## 经过\n部署失败");
        var view = await GetAsync();

        using var response = await PutAsync(new ConfigSave(view.Hash, view.File with { Retrieval = view.File.Retrieval with { TopK = 1 } }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = await Read<ConfigSaved>(response);
        Assert.NotEqual(view.Hash, saved.Hash);
        Assert.Contains("topK: 1\n", File.ReadAllText(ConfigPath));
        Assert.DoesNotContain("#", File.ReadAllText(ConfigPath));
        Assert.Contains("1 result(s).", (await _service.Tools.RecallAsync(new RecallRequest { Query = "部署失败" })).Text);
        Assert.Equal(saved.Hash, (await GetAsync()).Hash);
    }

    [Fact]
    public async Task A_config_that_does_not_validate_is_refused_with_every_problem_and_the_file_is_left_as_it_was()
    {
        var before = File.ReadAllText(ConfigPath);
        var view = await GetAsync();
        var config = view.File with
        {
            Retrieval = view.File.Retrieval with { TopK = 0 },
            Decision = view.File.Decision with { SamePage = new ThresholdFile(0.2, 0.8) },
        };

        using var response = await PutAsync(new ConfigSave(view.Hash, config));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await Read<ApiError>(response);
        Assert.Equal(TacitError.ConfigRejected, error.Error);
        Assert.Equal(["decision.samePage", "retrieval.topK"], error.Problems!.Select(problem => problem.Path));
        Assert.All(error.Problems!, problem => Assert.NotEmpty(problem.Message));
        Assert.Equal(before, File.ReadAllText(ConfigPath));
    }

    [Fact]
    public async Task A_save_based_on_a_file_changed_since_is_refused_and_the_change_is_kept()
    {
        var view = await GetAsync();
        File.AppendAllText(ConfigPath, "backup: { keep: 2 }\n");
        var edited = File.ReadAllText(ConfigPath);

        using var response = await PutAsync(new ConfigSave(view.Hash, view.File));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(TacitError.ConfigChanged, (await Read<ApiError>(response)).Error);
        Assert.Equal(edited, File.ReadAllText(ConfigPath));
    }

    [Fact]
    public async Task A_setting_that_waits_for_a_restart_is_saved_and_reported_with_its_running_value()
    {
        var view = await GetAsync();
        var config = view.File with
        {
            Server = view.File.Server with { Port = 19001 },
            Limits = view.File.Limits with { Page = new PageLimitsFile(MinAliases: 3) },
            Retrieval = view.File.Retrieval with { TopK = 2 },
        };

        using var response = await PutAsync(new ConfigSave(view.Hash, config));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = await Read<ConfigSaved>(response);
        Assert.Equal(
            [new RestartChange(ConfigKeyPath.ServerPort, "18888", "19001"), new RestartChange(ConfigKeyPath.LimitsPageMinAliases, "2", "3")],
            saved.Restart);
        var after = await GetAsync();
        Assert.Equal(19001, after.File.Server.Port);
        Assert.Equal(18888, after.Running.Server.Port);
        Assert.Equal(2, after.Running.Limits.Page!.MinAliases);
        Assert.Equal(2, after.Running.Retrieval.TopK);
    }

    [Fact]
    public async Task A_read_only_setting_cannot_be_changed_from_the_page()
    {
        var before = File.ReadAllText(ConfigPath);
        var view = await GetAsync();

        using var response = await PutAsync(new ConfigSave(view.Hash, view.File with { Language = "en", Memory = new MemoryFile("~/elsewhere") }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await Read<ApiError>(response);
        Assert.Equal(["memory.path", "language"], error.Problems!.Select(problem => problem.Path));
        Assert.Equal(before, File.ReadAllText(ConfigPath));
    }

    [Fact]
    public async Task An_extra_listening_address_needs_an_access_password_first()
    {
        var view = await GetAsync();

        using var response = await PutAsync(new ConfigSave(view.Hash, view.File with { Server = view.File.Server with { Host = "0.0.0.0" } }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("server.host", Assert.Single((await Read<ApiError>(response)).Problems!).Path);
    }

    [Fact]
    public async Task A_file_edited_by_hand_shows_its_values_beside_the_running_ones()
    {
        File.WriteAllText(ConfigPath, File.ReadAllText(ConfigPath).Replace("topK: 3", "topK: 7"));

        var view = await GetAsync();

        Assert.Equal(7, view.File.Retrieval.TopK);
        Assert.Equal(3, view.Running.Retrieval.TopK);
    }

    [Fact]
    public async Task Gives_where_each_secret_comes_from_and_the_file_s_value_alone()
    {
        using var response = await _local.GetAsync(Url);
        var view = await Read<ConfigView>(response);

        Assert.Equal(
            [
                new SecretState(ConfigKeyPath.ModelsEmbeddingApiKey, null, "TACIT_EMBEDDING_API_KEY", null),
                new SecretState(ConfigKeyPath.ModelsRerankerApiKey, SecretSource.Environment, "TACIT_RERANKER_API_KEY", null),
                new SecretState(ConfigKeyPath.ModelsDecisionApiKey, SecretSource.ConfigFile, "TACIT_DECISION_API_KEY", "test-key"),
                new SecretState(ConfigKeyPath.BackupWebDavPassword, null, "TACIT_WEBDAV_PASSWORD", null),
            ],
            view.Secrets);
        Assert.DoesNotContain("env-key", JsonSerializer.Serialize(view, ManagementApiJson.Options));
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task A_secret_is_kept_when_left_out_written_when_given_and_removed_when_null()
    {
        var view = await GetAsync();
        using (var kept = await PutAsync(new ConfigSave(view.Hash, view.File with { Retrieval = view.File.Retrieval with { TopK = 4 } })))
            Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
        Assert.Contains("apiKey: test-key", File.ReadAllText(ConfigPath));

        view = await GetAsync();
        using (var written = await PutAsync(new ConfigSave(view.Hash, view.File, new Dictionary<string, string?> { ["models.decision.apiKey"] = "new-key" })))
            Assert.Equal(HttpStatusCode.OK, written.StatusCode);
        Assert.Contains("apiKey: new-key", File.ReadAllText(ConfigPath));
        Assert.Equal("new-key", (await GetAsync()).Secrets.Single(secret => secret.Path == ConfigKeyPath.ModelsDecisionApiKey).Value);

        view = await GetAsync();
        using (var removed = await PutAsync(new ConfigSave(view.Hash, view.File, new Dictionary<string, string?> { ["models.decision.apiKey"] = null })))
            Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.DoesNotContain("apiKey", File.ReadAllText(ConfigPath));
        Assert.Null((await GetAsync()).Secrets.Single(secret => secret.Path == ConfigKeyPath.ModelsDecisionApiKey).Source);
    }

    [Fact]
    public async Task A_key_that_is_not_a_secret_cannot_be_changed_as_one()
    {
        var view = await GetAsync();

        using var response = await PutAsync(new ConfigSave(view.Hash, view.File, new Dictionary<string, string?> { ["retrieval.topK"] = "9" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("retrieval.topK", Assert.Single((await Read<ApiError>(response)).Problems!).Path);
    }

    [Fact]
    public async Task A_model_switched_off_leaves_the_file_and_the_running_service_without_it()
    {
        var view = await GetAsync();

        using var response = await PutAsync(new ConfigSave(view.Hash, view.File with { Models = view.File.Models with { Reranker = null } }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("reranker", File.ReadAllText(ConfigPath));
        var after = await GetAsync();
        Assert.Null(after.Running.Models.Reranker);
        Assert.NotNull(after.Running.Models.Embedding);
        var status = await _service.Reports.StatusAsync(new Uri("http://127.0.0.1:18888/mcp"));
        Assert.Contains(status.Models!.Items, model => model.Role == "reranker" && model.State == ModelState.Unconfigured);
    }

    [Fact]
    public async Task A_provider_that_is_not_known_is_refused_on_its_own_key()
    {
        var view = await GetAsync();
        var config = view.File with { Models = view.File.Models with { Decision = view.File.Models.Decision! with { Provider = "openai" } } };

        using var response = await PutAsync(new ConfigSave(view.Hash, config));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("models.decision.provider", Assert.Single((await Read<ApiError>(response)).Problems!).Path);
    }

    [Fact]
    public async Task Another_device_without_a_session_is_refused()
    {
        using var read = await _remote.GetAsync(Url);
        using var save = await PutAsync(new ConfigSave("x", new ConfigFile()), _remote);

        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, save.StatusCode);
    }
}
