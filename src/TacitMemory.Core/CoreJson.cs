using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using TacitMemory.Core.Backup;
using TacitMemory.Core.ClientRecords;
using TacitMemory.Core.Clients;
using TacitMemory.Core.Consolidation;
using TacitMemory.Core.Models;

namespace TacitMemory.Core;

/// <summary>
/// The JSON contracts of every type Core reads or writes, generated when the project builds, so no JSON goes through
/// reflection and trimming the release cannot take away what it needs. Each place keeps its own
/// <see cref="JsonSerializerOptions"/> for naming, indentation and escaping, and takes its contracts from here; a type
/// missing here fails at its first use, in the tests as in the release.
/// </summary>
[JsonSerializable(typeof(AccessControl.StoredState))]
[JsonSerializable(typeof(BackupManifest))]
[JsonSerializable(typeof(CloudBackupRecord))]
[JsonSerializable(typeof(SyncRecord))]
[JsonSerializable(typeof(ClientRecordStore.ClientRecordFile))]
[JsonSerializable(typeof(BindingStore.BindingsFile))]
[JsonSerializable(typeof(JudgmentPool.CacheKeyContent))]
[JsonSerializable(typeof(Dictionary<string, double>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(EmbeddingModel.Request), TypeInfoPropertyName = "EmbeddingRequest")]
[JsonSerializable(typeof(EmbeddingModel.Response), TypeInfoPropertyName = "EmbeddingResponse")]
[JsonSerializable(typeof(RerankerModel.Request), TypeInfoPropertyName = "RerankerRequest")]
[JsonSerializable(typeof(RerankerModel.Response), TypeInfoPropertyName = "RerankerResponse")]
[JsonSerializable(typeof(RerankerModel.CloudflareRequest))]
[JsonSerializable(typeof(CloudflareEnvelope<RerankerModel.CloudflareResult>))]
[JsonSerializable(typeof(CloudflareEnvelope<JsonObject>))]
[JsonSerializable(typeof(JsonObject))]
internal sealed partial class CoreJson : JsonSerializerContext;

public static class JsonContracts
{
    /// <summary>The contract of <typeparamref name="T"/> under <paramref name="options"/>, which takes it from their resolver.</summary>
    public static JsonTypeInfo<T> For<T>(this JsonSerializerOptions options) => (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
}
