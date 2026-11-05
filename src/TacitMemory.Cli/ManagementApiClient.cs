using System.Text;
using System.Text.Json;
using TacitMemory.Core;
using TacitMemory.Mcp;

namespace TacitMemory.Cli;

/// <summary>
/// The one place the CLI calls the management API at <c>127.0.0.1:{port}/api</c>: the base address, the JSON, the
/// failure of a call turned into a message and exit code 1, and the hint that starts the service. Commands that need
/// the service call through this class and nothing else.
/// </summary>
internal sealed class ManagementApiClient(TacitConfig config, Tacit tacit) : IDisposable
{
    private readonly HttpClient _http = new()
    {
        BaseAddress = new Uri($"http://127.0.0.1:{config.Port}/"),
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
    /// Sends POST with <paramref name="body"/> as JSON to <c>api/{route}</c> and reads the JSON reply. There is no timeout:
    /// a maintenance operation answers once it has finished, however long that takes.
    /// </summary>
    /// <exception cref="ServiceNotRunningException">Nothing answered the connection.</exception>
    /// <exception cref="ForeignServiceException">Something answered on the port, but not with the management API.</exception>
    /// <exception cref="ServiceRefusedException">The service refused the call; the exception carries its error body.</exception>
    public async Task<T> PostAsync<T>(string route, object body, CancellationToken cancellationToken = default)
        where T : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/{route}")
        {
            Content = new StringContent(JsonSerializer.Serialize(body, ManagementApiJson.Options), Encoding.UTF8, "application/json"),
        };
        return await SendAsync<T>(request, timeout: null, cancellationToken);
    }

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
                if (TryRead<ApiError>(text) is { Error: not null } error)
                    throw new ServiceRefusedException(error.Error, error.Message);
                throw new ForeignServiceException();
            }

            return TryRead<T>(text) ?? throw new ForeignServiceException();
        }
        catch (HttpRequestException)
        {
            throw new ServiceNotRunningException();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The deadline passed: the port is not answering in time, which counts as not running.
            throw new ServiceNotRunningException();
        }
    }

    /// <summary>
    /// Writes why a call failed and returns exit code 1: the service's own message when it refused, otherwise the hint to
    /// start it, naming the other program when one answers on the port.
    /// </summary>
    public int Fail(Exception failure)
    {
        Console.Error.WriteLine(failure switch
        {
            ServiceRefusedException refused => refused.Message,
            ForeignServiceException => tacit.ServiceNotRunningMessage(config, portAnsweredByOther: true),
            ServiceNotRunningException => tacit.ServiceNotRunningMessage(config, portAnsweredByOther: false),
            _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, "Not a management API failure."),
        });
        return 1;
    }

    public void Dispose() => _http.Dispose();

    private static T? TryRead<T>(string text) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(text, ManagementApiJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Nothing answered the call: the port refused the connection, or the call did not finish in time.</summary>
internal sealed class ServiceNotRunningException : Exception
{
}

/// <summary>Something answered on the port, but it is not the management API.</summary>
internal sealed class ForeignServiceException : Exception
{
}

/// <summary>The service refused the call with an error body; <see cref="Exception.Message"/> is that body's message.</summary>
internal sealed class ServiceRefusedException(string error, string message) : Exception(message)
{
    public string Error { get; } = error;
}
