using System.Text.Json;

namespace TacitMemory.Core.Backup;

/// <summary>
/// The <c>manifest.json</c> inside every backup. <see cref="BackupArchive"/> writes it; a restore reads it. A backup made by an
/// older version may lack <c>kind</c>, <c>memoryHeadSubject</c> or other members, so every member is nullable except the
/// file list, which a restore cannot do without.
/// </summary>
internal sealed record BackupManifest(
    DateTimeOffset? GeneratedAt,
    string? TacitVersion,
    string? Kind,
    string? MemoryHead,
    string? MemoryHeadSubject,
    IReadOnlyList<string>? Files)
{
    /// <summary>The JSON of the manifest, in both directions: camelCase names, indented for a person to read.</summary>
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, TypeInfoResolver = CoreJson.Default };
}
