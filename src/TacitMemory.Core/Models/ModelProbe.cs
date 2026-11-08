using System.Net;
using System.Text;
using System.Text.Json;

namespace TacitMemory.Core.Models;

/// <summary>
/// Probes the configured models for <c>tacit probe</c>. A probe is one request, with no retry and no cooldown, and a
/// single attempt that has <see cref="Limit"/>. The request is built and its answer read by the same code the service
/// uses, so a probe judges an answer as the service would. A probe changes no model's health and logs nothing.
/// </summary>
internal sealed class ModelProbe
{
    /// <summary>The time one probe request may take.</summary>
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(20);

    /// <summary>The characters of a response body that a result keeps.</summary>
    public const int BodyLimit = 500;

    private const string Replacement = "***";

    private readonly HttpClient _http;
    private readonly TimeProvider _time;
    private readonly IReadOnlyList<string> _secrets;

    private ModelProbe(HttpClient http, TimeProvider time, IReadOnlyList<string> secrets)
    {
        _http = http;
        _time = time;
        _secrets = secrets;
    }

    /// <summary>Probes each role in order. A model the config does not configure is reported as such and never called.</summary>
    public static async Task<IReadOnlyList<ProbeResult>> RunAsync(
        TacitConfig config, IReadOnlyList<ModelRole> roles, TimeProvider time, CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var probe = new ModelProbe(httpClient, time, Secrets(config));
        var results = new List<ProbeResult>(roles.Count);
        foreach (var role in roles)
            results.Add(await probe.ProbeOneAsync(role, config, cancellationToken));
        return results;
    }

    /// <summary>
    /// Sends the one request <paramref name="build"/> makes and reads its answer with <paramref name="read"/>, which returns
    /// <c>null</c> for an answer that is not what was asked for. Used by each model's own probe.
    /// </summary>
    public async Task<ProbeResult> SendAsync<T>(
        ModelRole role,
        Func<HttpRequestMessage> build,
        Func<HttpResponseMessage, CancellationToken, Task<T?>> read,
        CancellationToken cancellationToken) where T : class
    {
        var started = _time.GetUtcNow();
        // The timer ends this attempt alone; cancellation from the caller reaches the request through cancellationToken.
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var timer = _time.CreateTimer(_ => attempt.Cancel(), null, Limit, Timeout.InfiniteTimeSpan);
        try
        {
            using var request = build();
            using var response = await _http.SendAsync(request, attempt.Token);
            var status = (int)response.StatusCode;
            var body = await response.Content.ReadAsStringAsync(attempt.Token);
            if (!response.IsSuccessStatusCode)
                return Failed(role, ProbeFailure.HttpStatus, status, body, started);

            var answer = await ReadAnswerAsync(read, response.StatusCode, body, attempt.Token);
            return answer is null
                ? Failed(role, ProbeFailure.WrongFormat, status, body, started)
                : new ProbeResult(role, ProbeOutcome.Succeeded, null, status, null, Elapsed(started));
        }
        catch (HttpRequestException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProbeResult(role, ProbeOutcome.Failed, ProbeFailure.Network, null, null, Elapsed(started));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The timer ended the attempt; the caller's own cancellation is not a result.
            return new ProbeResult(role, ProbeOutcome.Failed, ProbeFailure.Timeout, null, null, Elapsed(started));
        }
    }

    private Task<ProbeResult> ProbeOneAsync(ModelRole role, TacitConfig config, CancellationToken cancellationToken) => role switch
    {
        ModelRole.Embedding when config.Models.Embedding is { } settings => EmbeddingModel.ProbeAsync(this, settings, cancellationToken),
        ModelRole.Reranker when config.Models.Reranker is { } settings => RerankerModel.ProbeAsync(this, settings, cancellationToken),
        ModelRole.Decision when config.Models.Decision is { } settings => SystemOneDecisionModel.ProbeAsync(this, settings, cancellationToken),
        _ => Task.FromResult(new ProbeResult(role, ProbeOutcome.Unconfigured, null, null, null, TimeSpan.Zero)),
    };

    /// <summary>
    /// Reads the answer from the body the model sent, as the service reads it. A reader that cannot parse the body has no
    /// answer, the same as one that returns <c>null</c>.
    /// </summary>
    private static async Task<T?> ReadAnswerAsync<T>(
        Func<HttpResponseMessage, CancellationToken, Task<T?>> read, HttpStatusCode status, string body, CancellationToken cancellationToken)
        where T : class
    {
        using var reply = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        try
        {
            return await read(reply, cancellationToken);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private ProbeResult Failed(ModelRole role, ProbeFailure failure, int status, string body, DateTimeOffset started) =>
        new(role, ProbeOutcome.Failed, failure, status, Preview(body), Elapsed(started));

    /// <summary>The body with every secret of the config replaced, cut to <see cref="BodyLimit"/> characters.</summary>
    private string Preview(string body)
    {
        foreach (var secret in _secrets)
            body = body.Replace(secret, Replacement, StringComparison.Ordinal);
        return string.Concat(body.EnumerateRunes().Take(BodyLimit).Select(rune => rune.ToString()));
    }

    private TimeSpan Elapsed(DateTimeOffset started) => _time.GetUtcNow() - started;

    private static IReadOnlyList<string> Secrets(TacitConfig config)
    {
        SecretValue?[] secrets = [config.Models.Embedding?.ApiKey, config.Models.Reranker?.ApiKey, config.Models.Decision?.ApiKey, config.Backup.WebDav?.Password];
        return secrets.OfType<SecretValue>().Select(secret => secret.Value).ToList();
    }
}
