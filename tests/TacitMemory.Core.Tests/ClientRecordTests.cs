using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace TacitMemory.Core.Tests;

/// <summary>
/// The client capability record: what the service keeps of each client that connects, in <c>clients.json</c> under the home
/// directory, one record per client name holding everything that name has declared. Written only when a client is new,
/// declared something new, or was seen on another day; logged only in the first two cases; kept across restarts; left out
/// of every backup.
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
        Assert.False(record.TryGetProperty("version", out _));
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
    public void A_capability_not_yet_recorded_logs_one_more_event_and_joins_the_recorded_ones()
    {
        Service.RecordClient(Observed(capabilities: ["sampling"]));

        Service.RecordClient(Observed(capabilities: ["elicitation"]));

        Assert.Equal(2, EventsFor(Client));
        Assert.Equal(["elicitation", "sampling"], Record().GetProperty("capabilities").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public void An_extension_not_yet_recorded_logs_one_more_event_and_joins_the_recorded_ones()
    {
        Service.RecordClient(Observed(extensions: ["io.example/tasks"]));

        Service.RecordClient(Observed(extensions: ["io.example/apps"]));

        Assert.Equal(2, EventsFor(Client));
        Assert.Equal(["io.example/apps", "io.example/tasks"], Record().GetProperty("extensions").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public void A_sighting_that_declares_less_keeps_what_is_recorded_and_logs_nothing()
    {
        Service.RecordClient(Observed(capabilities: ["sampling", "roots"], extensions: ["io.example/tasks"]));
        var before = File.ReadAllBytes(StateFile);

        Service.RecordClient(Observed(capabilities: [], extensions: []));

        Assert.Equal(1, EventsFor(Client));
        Assert.Equal(before, File.ReadAllBytes(StateFile));
    }

    [Fact]
    public void A_changed_protocol_revision_logs_one_more_event_and_replaces_the_recorded_one()
    {
        Service.RecordClient(Observed(protocol: "2025-11-25"));

        Service.RecordClient(Observed(protocol: "2026-07-28"));

        Assert.Equal(2, EventsFor(Client));
        Assert.Equal("2026-07-28", Record().GetProperty("protocolVersion").GetString());
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
    public void The_name_alone_is_the_key_so_every_version_of_a_client_shares_one_record()
    {
        Service.RecordClient(Observed(version: "1.0.0"));
        Service.RecordClient(Observed(version: "2.0.0"));

        using var document = JsonDocument.Parse(File.ReadAllText(StateFile));
        Assert.Equal(1, document.RootElement.GetProperty("clients").GetArrayLength());
        Assert.Equal(1, EventsFor(Client));
    }

    [Fact]
    public void A_name_that_differs_only_in_case_is_the_same_client()
    {
        Service.RecordClient(Observed(name: "probe-client"));
        Service.RecordClient(Observed(name: "Probe-Client"));

        using var document = JsonDocument.Parse(File.ReadAllText(StateFile));
        Assert.Equal(1, document.RootElement.GetProperty("clients").GetArrayLength());
        // The name stays as first recorded, and the second sighting is not a new client.
        Assert.Equal("probe-client", Record().GetProperty("name").GetString());
        Assert.Single(_logs.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("probe-client", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_logged_event_carries_the_version_the_client_declared()
    {
        Service.RecordClient(Observed(version: "9.8.7"));

        Assert.Contains("9.8.7", Assert.Single(_logs.Entries, entry => entry.Message.Contains(Client, StringComparison.Ordinal)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_record_read_from_the_file_after_a_restart_still_grows()
    {
        Service.RecordClient(Observed(extensions: ["io.example/tasks"]));
        RestartService();

        Service.RecordClient(Observed(extensions: ["io.example/apps"]));

        Assert.Equal(["io.example/apps", "io.example/tasks"], Record().GetProperty("extensions").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(2, EventsFor(Client));
    }

    [Fact]
    public async Task A_file_with_missing_fields_nulls_and_unsorted_values_is_read_without_failing()
    {
        File.WriteAllText(StateFile, """
            {"clients":[null,{"name":"probe-client","lastSeen":"2026-11-02","extensions":["io.example/tasks",null,"io.example/apps","io.example/tasks"]}]}
            """);

        var tacit = _home.Tacit();
        var report = await tacit.OfflineStatus(tacit.LoadConfig());
        var item = Assert.Single(Assert.IsType<ClientsStatus>(report.Clients).Items);
        Assert.Equal(["io.example/apps", "io.example/tasks"], item.Extensions);
        Assert.Empty(item.Capabilities);

        // The running service reads the same file and adds to it.
        Service.RecordClient(Observed(capabilities: ["roots"], extensions: []));
        Assert.Equal(["roots"], Record().GetProperty("capabilities").EnumerateArray().Select(value => value.GetString()));
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

        var result = await Service.Maintenance.BackupAsync(BackupTarget.Local);

        using var zip = ZipFile.OpenRead(result.Location);
        Assert.DoesNotContain(zip.Entries, entry => entry.FullName.Contains("clients.json", StringComparison.Ordinal));
    }
}
