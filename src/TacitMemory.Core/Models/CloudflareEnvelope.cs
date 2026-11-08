using System.Text.Json.Serialization;

namespace TacitMemory.Core.Models;

/// <summary>
/// The envelope Cloudflare Workers AI wraps every answer in: <c>{result, success, errors, messages}</c>. An answer
/// whose <c>success</c> is not <c>true</c>, or that has no <c>result</c>, is not an answer, so <see cref="Answer"/>
/// is <c>null</c> for it.
/// </summary>
internal sealed record CloudflareEnvelope<T>(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("result")] T? Result) where T : class
{
    public T? Answer => Success ? Result : null;
}
