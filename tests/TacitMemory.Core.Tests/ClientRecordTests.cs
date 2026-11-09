using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace TacitMemory.Core.Tests;

/// <summary>
/// The client capability record: what the service keeps of each client that connects, in <c>clients.json</c> under the home
/// directory. Written only when a client is new, changed, or seen on another day; logged only when new or changed; kept
/// across restarts; left out of every backup.
/// </summary>
public sealed class ClientRecordTests : IDisposable
{
    private const string Client = "probe-client";

    private readonly TestHome _home = new();
    private readonly ModelDouble _models = new();
    private readonly CapturingLoggerFactory _logs = new();
    private MemoryService? _service;

    public ClientRecordTests() => _home.InitReady(models: _models);

    public void Dispose()
    {
        _service?.Dispose();
        _models.Dispose();
        _home.Dispose();
    }

    private MemoryService Service => _service ??= _home.Tacit().OpenService(_ => { }, _logs);

    private string StateFile => Path.Combine(_home.TacitHome, "clients.json");

    private static ClientObservation Observed(
        string name = Client, string version = "1.2.3", string protocol = "2026-07-28",
        string[]? capabilities = null, string[]? extensions = null) =>
        new(name, version, protocol, capabilities ?? ["sampling"], extensions ?? ["io.example/tasks"]);

    private int EventsFor(string name) => _logs.Entries.Count(entry => entry.Message.Contains(name, StringComparison.Ordinal));

    private JsonElement Record(int index = 0)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(StateFile));
        return document.RootElement.GetProperty("clients")[index].Clone();
    }

    private void RestartService()
    {
        _service?.Dispose();
        _service = null;
    }

    [Fact]
    public void A_first_sighting_writes_the_state_file_and_logs_one_event()
    {
        Service.RecordClient(Observed());

        var record = Record();
        Assert.Equal(Client, record.GetProperty("name").GetString());
        Assert.Equal("1.2.3", record.GetProperty("version").GetString());
        Assert.Equal("2026-07-28", record.GetProperty("protocolVersion").GetString());
        Assert.Equal(TestHome.Today.ToString("yyyy-MM-dd"), record.GetProperty("lastSeen").GetString());
        Assert.Equal(["sampling"], record.GetProperty("capabilities").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(["io.example/tasks"], record.GetProperty("extensions").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(1, EventsFor(Client));
    }

    [Fact]
    public void The_same_client_again_on_the_same_day_logs_nothing_and_writes_nothing()
    {
        Service.RecordClient(Observed());
        var before = File.ReadAllBytes(StateFile);

        Service.RecordClient(Observed());
        Service.RecordClient(Observed());

        Assert.Equal(1, EventsFor(Client));
        Assert.Equal(before, File.ReadAllBytes(StateFile));
    }

    [Fact]
    public void A_changed_capability_logs_one_more_event_and_replaces_the_record()
    {
        Service.RecordClient(Observed(capabilities: ["sampling"]));

        Service.RecordClient(Observed(capabilities: ["sampling", "elicitation"]));

        Assert.Equal(2, EventsFor(Client));
        Assert.Equal(["sampling", "elicitation"], Record().GetProperty("capabilities").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public void A_changed_extension_logs_one_more_event()
    {
        Service.RecordClient(Observed(extensions: ["io.example/tasks"]));

        Service.RecordClient(Observed(extensions: []));

        Assert.Equal(2, EventsFor(Client));
    }

    [Fact]
    public void The_same_client_on_another_day_updates_the_date_and_logs_nothing()
    {
        Service.RecordClient(Observed());
        _home.Clock.Advance(TimeSpan.FromDays(1));

        Service.RecordClient(Observed());

        Assert.Equal(1, EventsFor(Client));
        Assert.Equal(TestHome.Today.AddDays(1).ToString("yyyy-MM-dd"), Record().GetProperty("lastSeen").GetString());
    }

    [Fact]
    public void Name_and_version_together_are_the_key_so_each_version_is_its_own_record()
    {
        Service.RecordClient(Observed(version: "1.0.0"));
        Service.RecordClient(Observed(version: "2.0.0"));

        using var document = JsonDocument.Parse(File.ReadAllText(StateFile));
        Assert.Equal(2, document.RootElement.GetProperty("clients").GetArrayLength());
        Assert.Equal(2, EventsFor(Client));
    }

    [Fact]
    public void Control_characters_in_what_a_client_declares_are_removed_before_it_is_kept()
    {
        Service.RecordClient(Observed(name: "probe\u001b[31m-client", extensions: ["io.example/\u0007tasks"]));

        var text = File.ReadAllText(StateFile);
        Assert.DoesNotContain('\u001b', text);
        Assert.DoesNotContain('\u0007', text);
        Assert.Equal("probe[31m-client", Record().GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_record_is_still_there_after_the_service_restarts()
    {
        Service.RecordClient(Observed());
        RestartService();

        // The restarted service knows the client from the file: the same client again is not a new event.
        Service.RecordClient(Observed());
        Assert.Equal(1, EventsFor(Client));

        var tacit = _home.Tacit();
        var report = await tacit.OfflineStatus(tacit.LoadConfig());
        Assert.Equal(Client, Assert.Single(Assert.IsType<ClientsStatus>(report.Clients).Items).Name);
    }

    [Fact]
    public void A_state_file_that_cannot_be_written_is_logged_as_a_warning_and_the_call_returns()
    {
        Directory.CreateDirectory(StateFile);

        Service.RecordClient(Observed());

        var warning = Assert.Single(_logs.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains(StateFile, warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_state_file_is_not_in_a_backup()
    {
        Service.RecordClient(Observed());
        Assert.True(File.Exists(StateFile));

        var result = await Service.BackupAsync(BackupTarget.Local);

        using var zip = ZipFile.OpenRead(result.Location);
        Assert.DoesNotContain(zip.Entries, entry => entry.FullName.Contains("clients.json", StringComparison.Ordinal));
    }
}
