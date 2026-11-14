using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TacitMemory.Core;
using TacitMemory.Core.Tests;
using TacitMemory.Host.Api;

namespace TacitMemory.Host.Tests;

/// <summary>
/// <c>/api/prompts</c> over real HTTP: the four prompt documents with their text, whether the built-in default applies and
/// the hash and the measure of each file; checks, saves and returns to the default; and the refusal of another device without a session.
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
        using var put = await _remote.PutAsync(Url("/api/prompts/skill"), Body(null, "x"));
        using var delete = await _remote.DeleteAsync(Url("/api/prompts/skill"));
        using var check = await _remote.PostAsync(Url("/api/prompts/skill/check"), Draft("x"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, put.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, delete.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, check.StatusCode);
    }

    private static JsonContent Draft(string text) =>
        JsonContent.Create(new PromptDraft(text), options: ManagementApiJson.Options);

    [Fact]
    public async Task Each_document_carries_its_measure()
    {
        var list = await ReadAsync();

        var instructions = list.Documents[0].Measure;
        Assert.Equal(3000, instructions.Limit);
        Assert.Equal(4000 - instructions.Length, instructions.ActivityRoom);
        Assert.Equal(new PromptItems(5, 3, 10), list.Documents[1].Measure.Items);
        Assert.Equal(new PromptMeasure(0, 3000, null, null), list.Documents[3].Measure);
    }

    [Fact]
    public async Task A_check_answers_with_the_measure_and_writes_no_file()
    {
        using var response = await _local.PostAsync(Url("/api/prompts/skill/check"), Draft("页面标题用中文。"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var check = await Read<PromptCheck>(response);
        Assert.Equal(new PromptMeasure(8, 3000, null, null), check.Measure);
        Assert.Empty(check.Problems);
        Assert.False(File.Exists(Path.Combine(Prompts, "skill.md")));
    }

    [Fact]
    public async Task A_check_of_a_text_that_breaks_its_rules_is_200_with_every_problem()
    {
        var items = string.Concat(Enumerable.Range(1, 11).Select(i => $"- 条目 {i}\n"));

        using var response = await _local.PostAsync(Url("/api/prompts/rules/check"), Draft("## 值得记\n" + items + "## 不记\n" + items));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var check = await Read<PromptCheck>(response);
        Assert.Equal(new PromptItems(11, 11, 10), check.Measure.Items);
        Assert.Equal(2, check.Problems.Count);
    }

    [Fact]
    public async Task A_check_of_an_unknown_document_is_404()
    {
        using var response = await _local.PostAsync(Url("/api/prompts/tools/check"), Draft("x"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static JsonContent Body(string? hash, string text) =>
        JsonContent.Create(new PromptSave(hash, text), options: ManagementApiJson.Options);

    private static async Task<T> Read<T>(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<T>(await response.Content.ReadAsStringAsync(), ManagementApiJson.Options)!;

    [Fact]
    public async Task A_first_save_without_a_hash_writes_the_file_and_answers_with_the_document()
    {
        using var response = await _local.PutAsync(Url("/api/prompts/skill"), Body(null, "页面标题用中文。\r\n"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = await Read<PromptSaved>(response);
        Assert.False(saved.Document.BuiltIn);
        Assert.Equal("页面标题用中文。\n", saved.Document.Text);
        Assert.Empty(saved.Notices);
        Assert.Equal("页面标题用中文。\n", File.ReadAllText(Path.Combine(Prompts, "skill.md")));
    }

    [Fact]
    public async Task A_text_that_breaks_its_rules_is_400_with_every_problem_and_no_file()
    {
        using var response = await _local.PutAsync(Url("/api/prompts/skill"), Body(null, "---\nname: mine\n---\n"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await Read<ApiError>(response);
        Assert.Equal(nameof(TacitError.PromptRejected), error.Error);
        Assert.Equal("skill.md", Assert.Single(error.Problems!).Path);
        Assert.False(File.Exists(Path.Combine(Prompts, "skill.md")));
    }

    [Fact]
    public async Task A_save_based_on_an_old_hash_is_409_and_the_file_stays()
    {
        File.WriteAllText(Path.Combine(Prompts, "skill.md"), "第一版\n");
        var hash = (await ReadAsync()).Documents.Single(document => document.Name == PromptName.Skill).Hash;
        File.WriteAllText(Path.Combine(Prompts, "skill.md"), "手改的第二版\n");

        using var stale = await _local.PutAsync(Url("/api/prompts/skill"), Body(hash, "页面上的版本\n"));
        using var unseen = await _local.PutAsync(Url("/api/prompts/skill"), Body(null, "页面上的版本\n"));

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(nameof(TacitError.PromptChanged), (await Read<ApiError>(stale)).Error);
        Assert.Equal(HttpStatusCode.Conflict, unseen.StatusCode);
        Assert.Equal("手改的第二版\n", File.ReadAllText(Path.Combine(Prompts, "skill.md")));
    }

    [Fact]
    public async Task Delete_returns_the_document_to_its_built_in_text()
    {
        File.WriteAllText(Path.Combine(Prompts, "rules.md"), "## 值得记\n- 自定义的标准\n");

        using var response = await _local.DeleteAsync(Url("/api/prompts/rules"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var restored = await Read<PromptSaved>(response);
        Assert.True(restored.Document.BuiltIn);
        Assert.StartsWith("## 值得记\n- 用户纠正或打断 Agent", restored.Document.Text);
        Assert.False(File.Exists(Path.Combine(Prompts, "rules.md")));
    }

    [Theory]
    [InlineData("tools")]
    [InlineData("Skill")]
    public async Task An_unknown_document_is_404(string name)
    {
        using var put = await _local.PutAsync(Url($"/api/prompts/{name}"), Body(null, "x"));
        using var delete = await _local.DeleteAsync(Url($"/api/prompts/{name}"));

        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        Assert.Equal(nameof(TacitError.PromptUnknown), (await Read<ApiError>(put)).Error);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }
}
