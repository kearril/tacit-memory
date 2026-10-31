using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TacitMemory.Core.Clients;

/// <param name="CreatedContextFile">Whether <c>connect</c> created the context file, so <c>disconnect</c> may delete it again.</param>
internal sealed record BindingRecord(
    [property: JsonPropertyName("client")] string Client,
    [property: JsonPropertyName("contextFile")] string ContextFile,
    [property: JsonPropertyName("createdContextFile")] bool CreatedContextFile);

/// <summary><c>TACIT_HOME/bindings.json</c>: which context files TacitMemory maintains. Read fresh on every use.</summary>
internal sealed class BindingStore(string tacitHome)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private string Path => System.IO.Path.Combine(tacitHome, "bindings.json");

    public IReadOnlyList<BindingRecord> Read() =>
        File.Exists(Path) ? JsonSerializer.Deserialize<BindingsFile>(File.ReadAllText(Path), Json)?.Bindings ?? [] : [];

    public BindingRecord? Find(string client) => Read().FirstOrDefault(binding => binding.Client == client);

    public void Put(BindingRecord binding)
    {
        var bindings = Read().Where(existing => existing.Client != binding.Client).Append(binding).ToList();
        Save(bindings);
    }

    public void Remove(string client) => Save(Read().Where(binding => binding.Client != client).ToList());

    private void Save(List<BindingRecord> bindings)
    {
        Directory.CreateDirectory(tacitHome);
        File.WriteAllText(Path, JsonSerializer.Serialize(new BindingsFile(bindings), Json) + "\n");
    }

    private sealed record BindingsFile([property: JsonPropertyName("bindings")] List<BindingRecord> Bindings);
}
