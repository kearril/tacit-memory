using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using TacitMemory.Core;

namespace TacitMemory.Host.Api;

/// <summary>
/// The JSON of the management API in both directions: camelCase names, and enums as camelCase text. The CLI reads the API
/// with these same options, so the two cannot drift apart.
/// </summary>
public static class ManagementApiJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };
}

/// <summary>
/// The body of every error the management API returns: the <see cref="TacitError"/> name, and the message in the
/// memory language, ready to show as it is.
/// </summary>
public sealed record ApiError(string Error, string Message);

/// <summary>
/// The body of <c>POST /api/backup</c>: the target, and for a local backup the absolute directory it goes to. The CLI
/// sends this same record with the same JSON options.
/// </summary>
public sealed record BackupBody
{
    public required BackupTarget Target { get; init; }

    public string? To { get; init; }
}

/// <summary>
/// The body of <c>POST /api/restore</c>: the backup's absolute path, or <c>Remote</c> for the cloud backup; whether to take
/// <c>config.yaml</c> and <c>prompts/</c> from it too; and whether this request only previews. The sources and the rule that
/// a cloud backup is previewed first are checked by the service. The CLI sends this record with the same JSON options.
/// </summary>
public sealed record RestoreBody
{
    public string? Path { get; init; }

    public bool Remote { get; init; }

    public bool Config { get; init; }

    public required bool Preview { get; init; }
}

/// <summary>
/// The management API at <c>/api</c>, on the same listener as <c>/mcp</c>. The guards apply to paths under <c>/api</c>
/// only: a request that carries an Origin header is refused (a web page on another site is calling it), a request whose
/// Host is not this service's loopback address and port is refused (DNS rebinding), and a POST whose Content-Type is not
/// application/json is refused. There is no token: the service listens on loopback only.
/// </summary>
internal static class ManagementApi
{
    private const string Prefix = "/api";

    public static void Map(WebApplication app, MemoryService service)
    {
        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments(Prefix, StringComparison.Ordinal))
            {
                await next(context);
                return;
            }

            // Every call under /api is logged once, refused or not, with the status it was answered with.
            var started = Stopwatch.GetTimestamp();
            var status = StatusCodes.Status500InternalServerError;
            try
            {
                if (Refusal(context) is { } refusal)
                {
                    var message = service.ApiRefusalMessage(refusal.Error, context.Connection.LocalPort);
                    await Json(new ApiError(refusal.Error.ToString(), message), refusal.Status).ExecuteAsync(context);
                }
                else
                {
                    try
                    {
                        await next(context);
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        // Every failure is answered with the same body: a refused operation keeps its TacitError name and
                        // message; a file-system or unexpected failure gets one the service words in the memory language.
                        var failure = Failure(service, e);
                        await Json(new ApiError(failure.Error.ToString(), failure.Message), StatusOf(failure.Error)).ExecuteAsync(context);
                    }
                }

                status = context.Response.StatusCode;
            }
            finally
            {
                service.RecordApiCall(context.Request.Method, context.Request.Path.Value ?? "/", status, Stopwatch.GetElapsedTime(started));
            }
        });

        var api = app.MapGroup(Prefix);
        api.MapGet("/status", async (HttpContext context) =>
        {
            var report = await service.StatusAsync(McpEndpoint(context), context.RequestAborted);
            return Json(report);
        });
        api.MapPost("/reindex", async (HttpContext context) =>
        {
            var result = await service.ReindexAsync(context.RequestAborted);
            return Json(result);
        });
        api.MapPost("/backup", async (HttpContext context) =>
        {
            var body = await ReadBody<BackupBody>(context, service);
            var result = await service.BackupAsync(body.Target, body.To, context.RequestAborted);
            return Json(result);
        });
        api.MapPost("/restore", async (HttpContext context) =>
        {
            var body = await ReadBody<RestoreBody>(context, service);
            if (body.Preview)
                return Json(await service.PreviewRestoreAsync(body.Path, body.Remote, body.Config, context.RequestAborted));
            return Json(await service.RestoreAsync(body.Path, body.Remote, body.Config, context.RequestAborted));
        });
    }

    /// <summary>
    /// Reads the request body as JSON with the management API's options. A body that does not read, or lacks a required
    /// member, is refused as a request error that names the member when the parser does.
    /// </summary>
    private static async Task<T> ReadBody<T>(HttpContext context, MemoryService service) where T : class
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(context.Request.Body, ManagementApiJson.Options, context.RequestAborted)
                ?? throw new JsonException();
        }
        catch (JsonException e)
        {
            throw service.InvalidRequestBody(e.Path);
        }
    }

    /// <summary>The refusal an exception from an operation becomes: a refused operation as it is, a file failure or any other failure as the service words it.</summary>
    private static TacitException Failure(MemoryService service, Exception e) => e switch
    {
        TacitException refused => refused,
        IOException or UnauthorizedAccessException => service.FileFailure(e),
        _ => service.UnexpectedFailure(e),
    };

    /// <summary>A JSON response, serialized here with <see cref="ManagementApiJson.Options"/>.</summary>
    private static IResult Json<T>(T value, int statusCode = StatusCodes.Status200OK) =>
        Results.Text(JsonSerializer.Serialize(value, ManagementApiJson.Options), "application/json; charset=utf-8", statusCode: statusCode);

    /// <summary>The guard that refuses the request, with its status code; <c>null</c> when the request may pass.</summary>
    private static (TacitError Error, int Status)? Refusal(HttpContext context)
    {
        if (context.Request.Headers.ContainsKey("Origin"))
            return (TacitError.ApiOriginRefused, StatusCodes.Status403Forbidden);

        var port = context.Connection.LocalPort;
        var host = context.Request.Headers.Host.ToString();
        if (!string.Equals(host, $"127.0.0.1:{port}", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(host, $"localhost:{port}", StringComparison.OrdinalIgnoreCase))
            return (TacitError.ApiHostRefused, StatusCodes.Status403Forbidden);

        if (HttpMethods.IsPost(context.Request.Method) && !IsJson(context.Request.ContentType))
            return (TacitError.ApiContentTypeRefused, StatusCodes.Status415UnsupportedMediaType);

        return null;
    }

    /// <summary>
    /// The status code of a refused operation: 409 when the memory or the configuration is not in a state the operation
    /// accepts, which the user can fix; 400 for a request the operation does not accept; 500 when the environment failed
    /// (<c>git</c>, the vector extension, the cloud service, the file system) or the service failed unexpectedly.
    /// </summary>
    private static int StatusOf(TacitError error) => error switch
    {
        TacitError.GitFailed or TacitError.VectorExtensionInvalid or TacitError.CloudBackupFailed or TacitError.RestoreFailed
            or TacitError.FileSystemFailed or TacitError.InternalFailure => StatusCodes.Status500InternalServerError,
        TacitError.RequestInvalid => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status409Conflict,
    };

    /// <summary>Whether the media type is application/json; parameters such as a charset do not count.</summary>
    private static bool IsJson(string? contentType) =>
        contentType?.Split(';')[0].Trim().Equals("application/json", StringComparison.OrdinalIgnoreCase) ?? false;

    /// <summary>The MCP endpoint of the port the request arrived on: the address the status reports.</summary>
    private static Uri McpEndpoint(HttpContext context) => new($"http://127.0.0.1:{context.Connection.LocalPort}/mcp");
}
