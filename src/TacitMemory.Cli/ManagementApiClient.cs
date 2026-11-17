using System.Text;
using System.Text.Json;
using TacitMemory.Core;
using TacitMemory.Host.Api;

namespace TacitMemory.Cli;

/// <summary>
/// The one place the CLI calls the management API at <c>127.0.0.1:{port}/api</c>: the base address, the JSON, and the
/// failures of a call as exceptions. Each failure carries the message the command shows; the command pipeline maps the
/// exception type to an exit code. Commands that need the service call through this class and nothing else.
/// </summary>
internal sealed class ManagementApiClient(int port, MemoryLanguage language, Tacit tacit) : IDisposable
{
    private readonly HttpClient _http = new()
    {
        BaseAddress = new Uri($"http://127.0.0.1:{port}/"),
        Timeout = Timeout.InfiniteTimeSpan,
    };

    /// <summary>Sends GET to <c>api/{route}</c> and reads the JSON reply. <paramref name="timeout"/> bounds the whole call; without it, the call waits.</summary>
    /// <exception cref="ServiceNotRunningException">Nothing answered the connection, or the call timed out.</exception>
    /// <exception cref="ForeignServiceException">Something answered on the port, but not with the management API.</exception>
    /// <exception cref="ServiceRefusedException">The service refused the call; the exception carries its error body.</exception>
    public async Task<T> GetAsync<T>(string route, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        where T : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/{route}");
        return await SendAsync<T>(request, timeout, cancellationToken);
    }

    /// <summary>
    /// Sends POST with <paramref name="body"/> as JSON, or an empty object without one, to <c>api/{route}</c> and reads the
    /// JSON reply. There is no timeout:
    /// a maintenance operation answers once it has finished, however long that takes.
    /// </summary>
    /// <exception cref="ServiceNotRunningException">Nothing answered the connection.</exception>
    /// <exception cref="ForeignServiceException">Something answered on the port, but not with the management API.</exception>
    /// <exception cref="ServiceRefusedException">The service refused the call; the exception carries its error body.</exception>
    public async Task<T> PostAsync<T>(string route, object? body = null, CancellationToken cancellationToken = default)
        where T : class
    {
        var json = body is null ? "{}" : JsonSerializer.Serialize(body, ManagementApiJson.Options.GetTypeInfo(body.GetType()));
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/{route}")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        return await SendAsync<T>(request, timeout: null, cancellationToken);
    }

    /// <summary>The failure for a port nothing answers on, naming the command that starts the service.</summary>
    private ServiceNotRunningException NotRunning() => new(tacit.ServiceNotRunningMessage(language, port, portAnsweredByOther: false));

    /// <summary>The failure for a port another program answers on.</summary>
    private ForeignServiceException Foreign() => new(tacit.ServiceNotRunningMessage(language, port, portAnsweredByOther: true));

    private async Task<T> SendAsync<T>(HttpRequestMessage request, TimeSpan? timeout, CancellationToken cancellationToken)
        where T : class
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout is { } limit)
            deadline.CancelAfter(limit);

        try
        {
            using var response = await _http.SendAsync(request, deadline.Token);
            var text = await response.Content.ReadAsStringAsync(deadline.Token);
            if (!response.IsSuccessStatusCode)
            {
                if (TryRead<ApiError>(text) is { } error)
                    throw new ServiceRefusedException(error.Error, error.Message);
                throw Foreign();
            }

            return TryRead<T>(text) ?? throw Foreign();
        }
        catch (HttpRequestException)
        {
            throw NotRunning();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The deadline passed: the port is not answering in time, which counts as not running.
            throw NotRunning();
        }
    }

    public void Dispose() => _http.Dispose();

    private static T? TryRead<T>(string text) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize(text, ManagementApiJson.Options.For<T>());
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Nothing answered the call: the port refused the connection, or the call did not finish in time.</summary>
internal sealed class ServiceNotRunningException(string message) : Exception(message);

/// <summary>Something answered on the port, but it is not the management API.</summary>
internal sealed class ForeignServiceException(string message) : Exception(message);

/// <summary>The service refused the call with an error body; <see cref="Exception.Message"/> is that body's message.</summary>
internal sealed class ServiceRefusedException(TacitError error, string message) : Exception(message)
{
    public TacitError Error { get; } = error;
}
