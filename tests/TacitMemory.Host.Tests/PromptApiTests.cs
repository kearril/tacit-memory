using System.Net;
using System.Text.Json;
using TacitMemory.Core;
using TacitMemory.Core.Tests;
using TacitMemory.Host.Api;

namespace TacitMemory.Host.Tests;

/// <summary>
/// <c>/api/prompts</c> over real HTTP: the four prompt documents with their text, whether the built-in default applies and
/// the hash of each file; and the refusal of another device without a session.
/// </summary>
public sealed class PromptApiTests : IAsyncLifetime
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

    private Uri Url(string path) => new($"http://127.0.0.1:{_host.Endpoint.Port}{path}");

    private string Prompts => Path.Combine(_home.TacitHome, "prompts");

    private async Task<PromptList> ReadAsync()
    {
        using var response = await _local.GetAsync(Url("/api/prompts"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonSerializer.Deserialize<PromptList>(await response.Content.ReadAsStringAsync(), ManagementApiJson.Options)!;
    }

    [Fact]
    public async Task Without_files_the_four_documents_are_their_built_in_texts_without_a_hash()
    {
        var list = await ReadAsync();

        Assert.Equal([PromptName.Instructions, PromptName.Rules, PromptName.Resident, PromptName.Skill], list.Documents.Select(document => document.Name));
        Assert.All(list.Documents, document => Assert.True(document.BuiltIn));
        Assert.All(list.Documents, document => Assert.Null(document.Hash));
        Assert.Contains("## Recall", list.Documents[0].Text);
        Assert.StartsWith("## 值得记\n", list.Documents[1].Text);
        Assert.Contains("## red_lines", list.Documents[2].Text);
        Assert.Equal("", list.Documents[3].Text);
    }

    [Fact]
    public async Task A_file_is_shown_as_written_with_its_hash()
    {
        File.WriteAllText(Path.Combine(Prompts, "skill.md"), "Use Chinese page titles.\n");

        var skill = (await ReadAsync()).Documents.Single(document => document.Name == PromptName.Skill);

        Assert.False(skill.BuiltIn);
        Assert.Equal("Use Chinese page titles.\n", skill.Text);
        Assert.Matches("^[0-9a-f]{64}$", skill.Hash!);
    }

    [Fact]
    public async Task The_names_are_written_in_lower_case()
    {
        var json = await _local.GetStringAsync(Url("/api/prompts"));

        Assert.Contains("\"name\":\"instructions\"", json);
        Assert.Contains("\"name\":\"skill\"", json);
    }

    [Fact]
    public async Task Another_device_without_a_session_is_refused()
    {
        using var response = await _remote.GetAsync(Url("/api/prompts"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
