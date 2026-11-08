using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace TacitMemory.Core.Models;

/// <summary>The HTTP request every auxiliary model sends: a JSON POST to the configured URL, exactly as configured.</summary>
internal static class ModelRequest
{
    /// <summary>
    /// A POST of <paramref name="body"/> to <see cref="ModelConfig.Url"/>. The model's key, when it has one, goes in an
    /// <c>Authorization: Bearer</c> header; without a key no authentication header is sent at all.
    /// </summary>
    public static HttpRequestMessage Post(ModelConfig config, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, config.Url) { Content = JsonContent.Create(body) };
        if (config.ApiKey is { } key)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Value);
        return request;
    }
}
