using System.Text.Json;
using Microsoft.Extensions.Logging;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Logging;

namespace TacitMemory.Core.ClientRecords;

/// <summary>
/// The client capability record in <c>clients.json</c> under the home directory: one record per client name, with the day and
/// protocol revision of its last request and everything it has declared. It is runtime state, not memory. It has its own
/// lock, so it never waits for a memory write, and a file that cannot be written is logged as a warning and never fails the
/// request that saw the client. The file is read once, when the first client is seen; after that the service writes what
/// it holds, so a change made to the file while the service runs is overwritten. Nothing here calls a model or the network.
/// </summary>
internal sealed class ClientRecordStore(string path, TimeProvider time, ServiceLog log)
{
    /// <summary>The file's name under the home directory.</summary>
    public const string FileName = "clients.json";

    /// <summary>The longest text kept from one declared value; a longer one is cut.</summary>
    private const int MaxLength = 200;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true, TypeInfoResolver = CoreJson.Default };

    private readonly Lock _gate = new();
    private List<ClientStatus>? _clients;

    public static string PathFor(TacitOptions options) => Path.Combine(options.TacitHome, FileName);

    /// <summary>
    /// Records a sighting. A client is known by its name alone: sessions of one client declare different versions,
    /// capabilities and extensions, so the record keeps everything the name has declared so far and never takes any of it
    /// away. The file is written, and one event logged, when the client is new, its protocol revision changed, or a
    /// sighting declared something not yet recorded. A client seen again on another day only has its date written.
    /// Otherwise nothing is written and nothing is logged.
    /// </summary>
    public void Observe(ClientObservation observation)
    {
        var name = Plain(observation.Name);
        if (name.Length == 0)
            return;
        var version = Plain(observation.Version);
        var protocol = Plain(observation.ProtocolVersion);

        lock (_gate)
        {
            var clients = _clients ??= ParseOrEmpty();
            var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
            var index = clients.FindIndex(client => string.Equals(client.Name, name, StringComparison.OrdinalIgnoreCase));
            var previous = index >= 0 ? clients[index] : null;
            var capabilities = Union(previous?.Capabilities, Plain(observation.Capabilities));
            var extensions = Union(previous?.Extensions, Plain(observation.Extensions));
            var changed = previous is null
                || previous.ProtocolVersion != protocol
                || previous.Capabilities.Count != capabilities.Count
                || previous.Extensions.Count != extensions.Count;
            if (!changed && previous!.LastSeen == today)
                return;

            // The name stays as first recorded, so a sighting that differs only in case does not rewrite it.
            var current = new ClientStatus(previous?.Name ?? name, today, protocol, capabilities, extensions);
            if (index >= 0)
                clients[index] = current;
            else
                clients.Add(current);
            Save(clients);

            if (changed)
                log.Record(
                    LogLevel.Information, LogMessages.ClientRecorded,
                    current.Name, version, protocol, Joined(capabilities), Joined(extensions));
        }
    }

    /// <summary>
    /// The recorded clients as the file holds them, for the status report. Reads only: it writes nothing and never throws. When
    /// the file cannot be read, <paramref name="unreadable"/> says why and the list is empty.
    /// </summary>
    public static (IReadOnlyList<ClientStatus> Clients, string? Unreadable) ReadFor(string path)
    {
        try
        {
            return (ParseFile(path), null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return ([], e.Message);
        }
    }

    /// <summary>The file's clients, with the same cleaning and order as a sighting. A missing file is an empty record.</summary>
    private static List<ClientStatus> ParseFile(string path)
    {
        if (!File.Exists(path))
            return [];
        var file = JsonSerializer.Deserialize(File.ReadAllText(path), Json.For<ClientRecordFile>());
        // A file edited by hand can hold null where a record or a value belongs; those are left out, not failed on.
        return (file?.Clients ?? [])
            .Where(client => client is not null)
            .Select(client => client with
            {
                Name = Plain(client.Name ?? ""),
                ProtocolVersion = Plain(client.ProtocolVersion ?? ""),
                Capabilities = Union(null, Plain(client.Capabilities ?? [])),
                Extensions = Union(null, Plain(client.Extensions ?? [])),
            })
            .Where(client => client.Name.Length > 0)
            .ToList();
    }

    /// <summary>What was recorded together with what a sighting declared, each value once, in ordinal order.</summary>
    private static List<string> Union(IReadOnlyList<string>? recorded, IReadOnlyList<string> declared) =>
        (recorded ?? []).Concat(declared).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

    /// <summary>The file's clients for a sighting to add to. A file that cannot be read is logged and starts the record empty.</summary>
    private List<ClientStatus> ParseOrEmpty()
    {
        try
        {
            return ParseFile(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            log.Record(LogLevel.Warning, LogMessages.ClientRecordUnreadable, path, e.Message);
            return [];
        }
    }

    /// <summary>Writes the whole record through a temporary file, so a reader never sees half of it. A failure is logged only.</summary>
    private void Save(List<ClientStatus> clients)
    {
        var temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new ClientRecordFile(clients), Json.For<ClientRecordFile>()));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.Record(LogLevel.Warning, LogMessages.ClientRecordWriteFailed, path, e.Message);
            try
            {
                File.Delete(temporary);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                // The temporary file is only a leftover of a failed write; the record itself is unaffected.
            }
        }
    }

    /// <summary>
    /// A declared value without control characters, trimmed and cut to <see cref="MaxLength"/>. The client's text is shown in the
    /// status report and the log, so nothing a client sends can move the cursor or change a terminal's state.
    /// </summary>
    internal static string Plain(string text)
    {
        var kept = new string(text.Where(character => !char.IsControl(character)).ToArray()).Trim();
        return kept.Length <= MaxLength ? kept : kept[..MaxLength];
    }

    private static List<string> Plain(IEnumerable<string> values) =>
        values.Where(value => value is not null).Select(Plain).Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).ToList();

    private static string Joined(IEnumerable<string> values)
    {
        var text = string.Join(", ", values);
        return text.Length == 0 ? "-" : text;
    }

    /// <summary>The shape of <c>clients.json</c>: one object with the list of clients.</summary>
    internal sealed record ClientRecordFile(IReadOnlyList<ClientStatus>? Clients);
}
