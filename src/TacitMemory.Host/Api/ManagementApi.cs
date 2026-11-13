using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using TacitMemory.Core;

namespace TacitMemory.Host.Api;

/// <summary>
/// The JSON of the management API in both directions: camelCase names, and enums as camelCase text. The CLI reads the API
/// with these same options, so the two cannot drift apart.
/// </summary>
public static class ManagementApiJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    /// <summary>
    /// Applies the conventions to the framework's own JSON options as well. The built-in OpenAPI generator takes its schemas
    /// from those options, so the document describes the JSON the API writes; numbers are never written as strings there.
    /// </summary>
    public static void Apply(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.NumberHandling = JsonNumberHandling.Strict;
    }

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Apply(options);
        return options;
    }
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
/// The management API at <c>/api</c>, on the same listeners as <c>/mcp</c>. The guards apply to paths under <c>/api</c>
/// only, and run before anything else: a request whose Origin is not its own origin is refused (a web page on another site
/// is calling it); a local request whose Host is not a loopback name is refused (DNS rebinding); a POST or PUT whose
/// Content-Type is not application/json is refused. Then a connection from another device is refused with 401: there are
/// no sessions yet. There is no token: a local connection is trusted as it is.
/// </summary>
internal static class ManagementApi
{
    private const string Prefix = "/api";

    /// <summary>
    /// The Host names a local request may carry: the IPv4 loopback address, <c>localhost</c> and the IPv6 loopback address,
    /// with or without a port. The port is not checked: only the name matters for DNS rebinding.
    /// </summary>
    private static readonly Regex LoopbackHost = new(@"^(?:127\.0\.0\.1|localhost|\[::1\])(?::\d{1,5})?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Maps the API under <c>/api</c>. <paramref name="isLocal"/> says which peer addresses are local.</summary>
    public static void Map(WebApplication app, MemoryService service, Func<IPAddress?, bool> isLocal)
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
            var local = isLocal(context.Connection.RemoteIpAddress);
            try
            {
                if (Refusal(context, local) is { } refusal)
                {
                    await Json(new ApiError(refusal.Error.ToString(), service.RefusalMessage(refusal.Error)), refusal.Status).ExecuteAsync(context);
                }
                else if (!local)
                {
                    await Json(new ApiError(nameof(TacitError.ApiUnauthorized), service.RefusalMessage(TacitError.ApiUnauthorized)), StatusCodes.Status401Unauthorized)
                        .ExecuteAsync(context);
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

        // The tag names the group in the document; without it the generator takes the process name.
        var api = app.MapGroup(Prefix).WithTags("management");
        api.MapGet("/status", async (HttpContext context) =>
        {
            var report = await service.StatusAsync(McpEndpoint(context), context.RequestAborted);
            return Json(report);
        })
            .Produces<StatusReport>()
            .ProducesErrors();
        api.MapPost("/reindex", async (HttpContext context) =>
        {
            var result = await service.ReindexAsync(context.RequestAborted);
            return Json(result);
        })
            .Produces<ReindexResult>()
            .ProducesErrors(StatusCodes.Status409Conflict, StatusCodes.Status415UnsupportedMediaType);
        api.MapPost("/backup", async (HttpContext context) =>
        {
            var body = await ReadBody<BackupBody>(context, service);
            var result = await service.BackupAsync(body.Target, body.To, context.RequestAborted);
            return Json(result);
        })
            .Accepts<BackupBody>("application/json")
            .Produces<BackupResult>()
            .ProducesErrors(StatusCodes.Status400BadRequest, StatusCodes.Status409Conflict, StatusCodes.Status415UnsupportedMediaType);
        api.MapPost("/restore", async (HttpContext context) =>
        {
            var body = await ReadBody<RestoreBody>(context, service);
            if (body.Preview)
                return Json(await service.PreviewRestoreAsync(body.Path, body.Remote, body.Config, context.RequestAborted));
            return Json(await service.RestoreAsync(body.Path, body.Remote, body.Config, context.RequestAborted));
        })
            .Accepts<RestoreBody>("application/json")
            .Produces<RestorePreview>()
            .AddOpenApiOperationTransformer(RestoreResponse)
            .ProducesErrors(StatusCodes.Status400BadRequest, StatusCodes.Status409Conflict, StatusCodes.Status415UnsupportedMediaType);
        app.MapOpenApi(Prefix + "/openapi.json");
    }

    /// <summary>
    /// Declares the error bodies of an endpoint. Every endpoint can be refused by the guards, refused to a connection from
    /// another device with 401, and can fail in the environment, so 401, 403 and 500 are always declared; the statuses given
    /// are the ones its own operation adds.
    /// </summary>
    private static RouteHandlerBuilder ProducesErrors(this RouteHandlerBuilder builder, params int[] statuses)
    {
        foreach (var status in statuses.Append(StatusCodes.Status401Unauthorized).Append(StatusCodes.Status403Forbidden).Append(StatusCodes.Status500InternalServerError))
            builder.Produces<ApiError>(status);
        return builder;
    }

    /// <summary>
    /// Registers the OpenAPI generator for the management API: the JSON conventions it reads its schemas with, and the
    /// document's own title and server, which would otherwise come from the process name and the request's port.
    /// </summary>
    public static void AddOpenApi(IServiceCollection services)
    {
        services.ConfigureHttpJsonOptions(options => ManagementApiJson.Apply(options.SerializerOptions));
        services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info.Title = "TacitMemory management API";
            // A relative server keeps the snapshot the same on every port the service runs on.
            document.Servers = [new OpenApiServer { Url = "/" }];
            return Task.CompletedTask;
        }));
    }

    /// <summary>
    /// The restore endpoint answers with the preview or with the result, depending on the body; the document says so with a
    /// <c>oneOf</c> over both schemas.
    /// </summary>
    private static async Task RestoreResponse(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var preview = await context.GetOrCreateSchemaAsync(typeof(RestorePreview), null, cancellationToken);
        var result = await context.GetOrCreateSchemaAsync(typeof(RestoreResult), null, cancellationToken);
        var media = operation.Responses![StatusCodes.Status200OK.ToString()].Content!["application/json"];
        media.Schema = new OpenApiSchema { OneOf = [preview, result] };
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

    /// <summary>
    /// The guard that refuses the request, with its status code; <c>null</c> when the request may pass. The Origin rule
    /// applies to every connection; the Host rule only to a local one, since a remote peer is not reached by a name of this
    /// machine; the Content-Type rule to every connection that writes.
    /// </summary>
    private static (TacitError Error, int Status)? Refusal(HttpContext context, bool local)
    {
        if (!SameOrigin(context))
            return (TacitError.ApiOriginRefused, StatusCodes.Status403Forbidden);

        if (local && !LoopbackHost.IsMatch(context.Request.Headers.Host.ToString()))
            return (TacitError.ApiHostRefused, StatusCodes.Status403Forbidden);

        var method = context.Request.Method;
        if ((HttpMethods.IsPost(method) || HttpMethods.IsPut(method)) && !IsJson(context.Request.ContentType))
            return (TacitError.ApiContentTypeRefused, StatusCodes.Status415UnsupportedMediaType);

        return null;
    }

    /// <summary>
    /// Whether the request has no Origin header, or one that is the origin of the request itself: its scheme and its Host
    /// header. A browser page on another site sends its own origin, which never matches.
    /// </summary>
    private static bool SameOrigin(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue("Origin", out var origin))
            return true;
        var own = $"{context.Request.Scheme}://{context.Request.Headers.Host}";
        return string.Equals(origin.ToString(), own, StringComparison.OrdinalIgnoreCase);
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
