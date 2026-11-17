using System.Text.Json;
using System.Text.Json.Serialization;

namespace TacitMemory.Core.Tests;

/// <summary>A key path as the contracts write it on the wire, such as <c>retrieval.topK</c>.</summary>
internal static class ConfigKeyWire
{
    public static string Dotted(this ConfigKeyPath path) =>
        JsonSerializer.Serialize(path, ConfigKeyWireJson.Default.ConfigKeyPath).Trim('"');
}

[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(ConfigKeyPath))]
internal sealed partial class ConfigKeyWireJson : JsonSerializerContext;
