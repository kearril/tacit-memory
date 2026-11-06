using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Clients;

/// <summary>One binding: the client's name, its context file, its skills folder, and whether <c>connect</c> created the context file.</summary>
/// <param name="CreatedContextFile">Whether <c>connect</c> created the context file, so <c>disconnect</c> may delete it again once it is empty.</param>
internal sealed record BindingRecord(
    [property: JsonPropertyName("client")] string Client,
    [property: JsonPropertyName("contextFile")] string ContextFile,
    [property: JsonPropertyName("skillsFolder")] string SkillsFolder,
    [property: JsonPropertyName("createdContextFile")] bool CreatedContextFile);

/// <summary><c>TACIT_HOME/bindings.json</c>: which context files TacitMemory maintains. Read fresh on every use.</summary>
internal sealed class BindingStore(string tacitHome, MemoryLanguage language)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private string FilePath => Path.Combine(tacitHome, "bindings.json");

    /// <exception cref="TacitException">The file is not valid JSON.</exception>
    public IReadOnlyList<BindingRecord> Read()
    {
        if (!File.Exists(FilePath))
            return [];
        try
        {
            return JsonSerializer.Deserialize<BindingsFile>(File.ReadAllText(FilePath), Json)?.Bindings ?? [];
        }
        catch (JsonException e)
        {
            throw new TacitException(TacitError.BindingsInvalid, Messages.BindingsInvalid.Format(language, FilePath, e.Message), e);
        }
    }

    public BindingRecord? Find(string client) => Read().FirstOrDefault(binding => binding.Client == client);

    public void Put(BindingRecord binding) =>
        Save([.. Read().Where(existing => existing.Client != binding.Client), binding]);

    public void Remove(string client) => Save([.. Read().Where(binding => binding.Client != client)]);

    private void Save(List<BindingRecord> bindings)
    {
        Directory.CreateDirectory(tacitHome);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(new BindingsFile(bindings), Json) + "\n");
    }

    private sealed record BindingsFile([property: JsonPropertyName("bindings")] List<BindingRecord> Bindings);
}
