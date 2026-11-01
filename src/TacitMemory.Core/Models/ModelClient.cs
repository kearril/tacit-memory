using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace TacitMemory.Core.Models;

/// <summary>
/// Sends the requests of the three auxiliary models. A request may take several attempts: network errors, attempt
/// timeouts, 429 and 5xx are tried again, up to <see cref="MaxAttempts"/> in all. The pauses between attempts are
/// 0.5, 1, 2 and 4 seconds, except that a 429 waits its <c>Retry-After</c>. The whole request, attempts and pauses
/// together, has <see cref="Budget"/>; an attempt's own timeout, <see cref="AttemptTimeout"/>, is cut to what is left.
/// Only a request that finally fails counts against the model's health, and only once.
/// </summary>
internal sealed class ModelClient(HttpClient http, TimeProvider time)
{
    private const int MaxAttempts = 5;
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan[] Pauses = [TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)];

    /// <summary>
    /// The answer, or <c>null</c> when the request fails. <paramref name="read"/> returns <c>null</c> for an answer that
    /// is not what was asked for, which fails the request without a retry. A model that is cooling down is not called.
    /// </summary>
    public async Task<T?> SendAsync<T>(
        ModelHealth health,
        Func<HttpRequestMessage> build,
        Func<HttpResponseMessage, CancellationToken, Task<T?>> read,
        CancellationToken cancellationToken) where T : class
    {
        if (health.CoolingDown)
            return null;

        var deadline = time.GetUtcNow() + Budget;
        for (var attempt = 1; ; attempt++)
        {
            var remaining = deadline - time.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
                break;
            var outcome = await AttemptAsync(build, read, Shorter(AttemptTimeout, remaining), cancellationToken);
            if (outcome.Value is { } value)
            {
                health.Succeeded();
                return value;
            }
            if (!outcome.Retry || attempt == MaxAttempts)
                break;

            var pause = outcome.RetryAfter ?? Pauses[attempt - 1];
            if (time.GetUtcNow() + pause >= deadline)
                break;
            await Task.Delay(pause, time, cancellationToken);
        }

        health.Failed();
        return null;
    }

    private async Task<(T? Value, bool Retry, TimeSpan? RetryAfter)> AttemptAsync<T>(
        Func<HttpRequestMessage> build,
        Func<HttpResponseMessage, CancellationToken, Task<T?>> read,
        TimeSpan limit,
        CancellationToken cancellationToken) where T : class
    {
        // The timer cancels this attempt alone; cancellation from the caller reaches the request through cancellationToken.
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var timer = time.CreateTimer(_ => attempt.Cancel(), null, limit, Timeout.InfiniteTimeSpan);
        try
        {
            using var request = build();
            using var response = await http.SendAsync(request, attempt.Token);
            var code = (int)response.StatusCode;
            if (code == 429)
                return (null, true, RetryAfter(response.Headers.RetryAfter));
            if (code >= 500)
                return (null, true, null);
            if (!response.IsSuccessStatusCode)
                return (null, false, null);
            return (await read(response, attempt.Token), false, null);
        }
        catch (HttpRequestException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, true, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The attempt timed out.
            return (null, true, null);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException
                                      && !cancellationToken.IsCancellationRequested)
        {
            return (null, false, null);
        }
    }

    private TimeSpan? RetryAfter(RetryConditionHeaderValue? value)
    {
        if (value?.Delta is { } delta)
            return delta;
        if (value?.Date is { } date)
        {
            var wait = date - time.GetUtcNow();
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }
        return null;
    }

    private static TimeSpan Shorter(TimeSpan a, TimeSpan b) => a <= b ? a : b;
}
