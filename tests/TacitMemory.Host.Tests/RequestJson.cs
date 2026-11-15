using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using TacitMemory.Host.Api;

namespace TacitMemory.Host.Tests;

/// <summary>
/// The management API's conventions for the request bodies tests write, which are often anonymous objects. The API's own
/// options know only the types it reads and writes, so these add reflection for the rest; answers are still read with the
/// API's options, which is how a type missing from its contracts shows.
/// </summary>
internal static class RequestJson
{
    public static JsonSerializerOptions Options { get; } = new(ManagementApiJson.Options)
    {
        TypeInfoResolver = JsonTypeInfoResolver.Combine(ManagementApiJson.Options.TypeInfoResolver, new DefaultJsonTypeInfoResolver()),
    };
}
