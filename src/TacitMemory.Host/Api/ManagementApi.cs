using System.Diagnostics;
using System.Globalization;
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
using TacitMemory.Host.Localization;

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
        options.TypeInfoResolverChain.Insert(0, HostJson.Default);
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.NumberHandling = JsonNumberHandling.Strict;
    }

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Apply(options);
        // Only the generated contracts: a type the API reads or writes must be listed in HostJson.
        options.TypeInfoResolver = HostJson.Default;
        return options;
    }
}

/// <summary>
/// The body of every error the management API returns: the <see cref="TacitError"/> name, and the message in the
/// memory language, ready to show as it is. A config that does not validate, and a recall trace with bad parameters, also
/// list every problem with its key path or parameter name; other errors leave the field out. The error is written as the
/// member's own name (<c>ConfigChanged</c>), not in the camelCase the API's other enums take.
/// </summary>
public sealed record ApiError(
    [property: JsonRequired, JsonConverter(typeof(JsonStringEnumConverter<TacitError>))] TacitError Error,
    string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ConfigProblem>? Problems = null);

/// <summary>
/// The body of <c>PUT /api/config</c>: the whole config to save, and the hash of the file it was read from. Its secrets are
/// not taken; the file keeps the ones it holds.
/// </summary>
/// <param name="Secrets">
/// The secrets to change, by key path: a value writes it into <c>config.yaml</c>, <c>null</c> removes it from the file. A
/// secret left out keeps what the file holds.
/// </param>
public sealed record ConfigSave(string Hash, ConfigFile Config, IReadOnlyDictionary<string, string?>? Secrets = null);

/// <summary>
/// What <c>/api/session</c> and the access-password endpoints answer: whether the caller is a local connection, whether the
/// caller holds a valid session, whether an access password is set, the memory language and the service version.
/// </summary>
public sealed record SessionStatus(bool Local, bool LoggedIn, bool PasswordSet, MemoryLanguage Language, string Version);

/// <summary>The body of <c>POST /api/session</c>: the access password to log in with.</summary>
public sealed record PasswordBody
{
    public required string Password { get; init; }
}

/// <summary>
/// The body of <c>PUT /api/access-password</c>: the password to set, and for a remote caller the password in use now, which
/// it must give correctly. A local caller leaves <c>Current</c> out.
/// </summary>
public sealed record AccessPasswordBody
{
    public required string Password { get; init; }

    public string? Current { get; init; }
}

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
/// Content-Type is not application/json is refused. Then a connection from another device needs a session, the cookie the
/// login sets, unless the path is the session endpoint itself; without one it is refused with 401. A local connection is
/// trusted as it is.
/// </summary>
internal static class ManagementApi
{
    private const string Prefix = "/api";

    /// <summary>The route of the recall trace under <see cref="Prefix"/>, which reads like a GET though it is a POST.</summary>
    private const string RecallTraceRoute = "/recall/trace";

    /// <summary>
    /// Whether a call is a successful read, which the log leaves out: a GET, or a recall trace, changed nothing, and the Web
    /// UI's polling or a user trying query after query would otherwise fill the log.
    /// </summary>
    private static bool IsQuietRead(string method, string route, int status) =>
        status is >= 200 and < 300
        && (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
            || (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase)
                && string.Equals(route, Prefix + RecallTraceRoute, StringComparison.OrdinalIgnoreCase)));

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
            if (!context.Request.Path.StartsWithSegments(Prefix, StringComparison.OrdinalIgnoreCase))
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
                    await Json(new ApiError(refusal.Error, HostMessages.Refusal(refusal.Error, service.Language)), refusal.Status).ExecuteAsync(context);
                }
                else if (!(local || IsSessionEndpoint(context) || service.Access.IsSession(Cookie(context))))
                {
                    await Json(new ApiError(TacitError.ApiUnauthorized, HostMessages.Refusal(TacitError.ApiUnauthorized, service.Language)), StatusCodes.Status401Unauthorized)
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
                        if (failure.RetryAfterSeconds is { } seconds)
                            context.Response.Headers["Retry-After"] = seconds.ToString(CultureInfo.InvariantCulture);
                        await Json(new ApiError(failure.Error, failure.Message, failure.Problems), StatusOf(failure.Error)).ExecuteAsync(context);
                    }
                }

                status = context.Response.StatusCode;
            }
            finally
            {
                var route = context.Request.Path.Value ?? "/";
                if (!IsQuietRead(context.Request.Method, route, status))
                    service.RecordApiCall(context.Request.Method, route, status, Stopwatch.GetElapsedTime(started));
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
        api.MapGet("/activity", async (HttpContext context) =>
        {
            var report = await service.ActivityAsync(context.RequestAborted);
            return Json(report);
        })
            .Produces<ActivityReport>()
            .ProducesErrors();
        api.MapGet("/logs", () => Json(service.LogDays()))
            .Produces<ServiceLogDays>()
            .ProducesErrors();
        api.MapGet("/logs/{date}", (string date, string? after) => Json(service.ServiceLog(date, after)))
            .Produces<ServiceLogDay>()
            .ProducesErrors(StatusCodes.Status400BadRequest, StatusCodes.Status404NotFound);
        api.MapGet("/backups", () => Json(service.Backups()))
            .Produces<BackupList>()
            .ProducesErrors();
        api.MapGet("/prompts", () => Json(service.Prompts()))
            .Produces<PromptList>()
            .ProducesErrors();
        api.MapPut("/prompts/{name}", async (string name, HttpContext context) =>
        {
            var document = service.PromptNamed(name);
            var body = await ReadBody<PromptSave>(context, service);
            return Json(await service.SavePromptAsync(document, body.Hash, body.Text, context.RequestAborted));
        })
            .Accepts<PromptSave>("application/json")
            .Produces<PromptSaved>()
            .ProducesErrors(StatusCodes.Status400BadRequest, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict, StatusCodes.Status415UnsupportedMediaType);
        api.MapPost("/prompts/{name}/check", async (string name, HttpContext context) =>
        {
            var document = service.PromptNamed(name);
            var body = await ReadBody<PromptDraft>(context, service);
            return Json(service.CheckPrompt(document, body.Text));
        })
            .Accepts<PromptDraft>("application/json")
            .Produces<PromptCheck>()
            .ProducesErrors(StatusCodes.Status400BadRequest, StatusCodes.Status404NotFound, StatusCodes.Status415UnsupportedMediaType);
        api.MapDelete("/prompts/{name}", async (string name, HttpContext context) =>
            Json(await service.RestorePromptDefaultAsync(service.PromptNamed(name), context.RequestAborted)))
            .Produces<PromptSaved>()
            .ProducesErrors(StatusCodes.Status400BadRequest, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict);
        api.MapPost("/probe", async (HttpContext context) =>
        {
            var report = await service.ProbeAsync(context.RequestAborted);
            return Json(report);
        })
            .Produces<ProbeReport>()
            .ProducesErrors(StatusCodes.Status409Conflict, StatusCodes.Status415UnsupportedMediaType);
        api.MapPost(RecallTraceRoute, async (HttpContext context) =>
        {
            var body = await ReadBody<RecallTraceRequest>(context, service);
            return Json(await service.TraceRecallAsync(body, context.RequestAborted));
        })
            .Accepts<RecallTraceRequest>("application/json")
            .Produces<RecallTraceReport>()
            .ProducesErrors(StatusCodes.Status400BadRequest, StatusCodes.Status415UnsupportedMediaType);
        api.MapGet("/memory/persona", async (HttpContext context) =>
        {
            var report = await service.Browse.PersonaAsync(context.RequestAborted);
            return Json(report);
        })
            .Produces<PersonaReport>()
            .ProducesErrors();
        api.MapGet("/memory/workstreams", async (HttpContext context) =>
        {
            var list = await service.Browse.WorkstreamsAsync(context.RequestAborted);
            return Json(list);
        })
            .Produces<WorkstreamList>()
            .ProducesErrors();
        api.MapGet("/memory/workstreams/{name}", async (string name, HttpContext context) =>
        {
            var brief = await service.Browse.WorkstreamBriefAsync(name, context.RequestAborted);
            return Json(brief);
        })
            .Produces<WorkstreamBriefView>()
            .ProducesErrors(StatusCodes.Status404NotFound);
        api.MapGet("/memory/activity", async (HttpContext context) =>
        {
            var months = await service.Browse.ActivityMonthsAsync(context.RequestAborted);
            return Json(months);
        })
            .Produces<ActivityMonths>()
            .ProducesErrors();
        api.MapGet("/memory/activity/{month}", async (string month, HttpContext context) =>
        {
            var view = await service.Browse.ActivityMonthAsync(month, context.RequestAborted);
            return Json(view);
        })
            .Produces<ActivityMonthView>()
            .ProducesErrors(StatusCodes.Status400BadRequest, StatusCodes.Status404NotFound);
        api.MapGet("/memory/pages", async (HttpContext context) =>
        {
            var list = await service.Browse.PagesAsync(context.RequestAborted);
            return Json(list);
        })
            .Produces<PageList>()
            .ProducesErrors();
        api.MapGet("/memory/pages/{workstream}/{slug}", async (string workstream, string slug, HttpContext context) =>
        {
            var page = await service.Browse.PageAsync(workstream, slug, context.RequestAborted);
            return Json(page);
        })
            .Produces<PageView>()
            .ProducesErrors(StatusCodes.Status400BadRequest, StatusCodes.Status404NotFound);
        api.MapGet("/config", () => Json(service.ConfigView()))
            .Produces<ConfigView>()
            .ProducesErrors(StatusCodes.Status409Conflict);
        api.MapPut("/config", async (HttpContext context) =>
        {
            var body = await ReadBody<ConfigSave>(context, service);
            return Json(await service.SaveConfigAsync(body.Config, body.Hash, body.Secrets, context.RequestAborted));
        })
            .Accepts<ConfigSave>("application/json")
            .Produces<ConfigSaved>()
            .ProducesErrors(StatusCodes.Status400BadRequest, StatusCodes.Status409Conflict, StatusCodes.Status415UnsupportedMediaType);
        api.MapGet("/config/secrets/{path}", (string path) => Json(service.Secret(path)))
            .Produces<SecretShown>()
            .ProducesErrors(StatusCodes.Status404NotFound, StatusCodes.Status409Conflict);
        api.MapGet("/bindings", () => Json(service.Bindings()))
            .Produces<BindingList>()
            .ProducesErrors(StatusCodes.Status409Conflict);
        api.MapPost("/bindings", async (HttpContext context) =>
        {
            var body = await ReadBody<ConnectRequest>(context, service);
            return Json(service.Connect(body));
        })
            .Accepts<ConnectRequest>("application/json")
            .Produces<BindingDone>()
            .ProducesErrors(StatusCodes.Status400BadRequest, StatusCodes.Status409Conflict, StatusCodes.Status415UnsupportedMediaType);
        api.MapDelete("/bindings/{client}", (string client) => Json(service.Disconnect(client)))
            .Produces<BindingDone>()
            .ProducesErrors(StatusCodes.Status400BadRequest, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict);
        api.MapGet("/session", (HttpContext context) =>
            Json(SessionOf(service, isLocal(context.Connection.RemoteIpAddress), service.Access.IsSession(Cookie(context)))))
            .Produces<SessionStatus>()
            .ProducesSessionErrors();
        api.MapPost("/session", async (HttpContext context) =>
        {
            var body = await ReadBody<PasswordBody>(context, service);
            var grant = service.Access.Login(body.Password, context.Connection.RemoteIpAddress);
            SetCookie(context, grant);
            return Json(SessionOf(service, isLocal(context.Connection.RemoteIpAddress), loggedIn: true));
        })
            .Accepts<PasswordBody>("application/json")
            .Produces<SessionStatus>()
            .ProducesErrors(StatusCodes.Status400BadRequest, StatusCodes.Status415UnsupportedMediaType, StatusCodes.Status429TooManyRequests)
            .AddOpenApiOperationTransformer(LoginLimitedResponse);
        api.MapDelete("/session", (HttpContext context) =>
        {
            context.Response.Cookies.Delete(SessionCookie, SessionCookieOptions(expires: null));
            return Json(SessionOf(service, isLocal(context.Connection.RemoteIpAddress), loggedIn: false));
        })
            .Produces<SessionStatus>()
            .ProducesSessionErrors();
        api.MapPut("/access-password", async (HttpContext context) =>
        {
            var body = await ReadBody<AccessPasswordBody>(context, service);
            var local = isLocal(context.Connection.RemoteIpAddress);
            var grant = service.Access.SetPassword(body.Password, body.Current, local, context.Connection.RemoteIpAddress);
            // Every earlier session ended with the old password; a remote caller that changed it is given the new session.
            if (!local)
                SetCookie(context, grant);
            return Json(SessionOf(service, local, loggedIn: !local));
        })
            .Accepts<AccessPasswordBody>("application/json")
            .Produces<SessionStatus>()
            .ProducesErrors(StatusCodes.Status400BadRequest, StatusCodes.Status415UnsupportedMediaType, StatusCodes.Status429TooManyRequests)
            .AddOpenApiOperationTransformer(LoginLimitedResponse);
        api.MapDelete("/access-password", (HttpContext context) =>
        {
            var local = isLocal(context.Connection.RemoteIpAddress);
            service.Access.ClearPassword(local);
            return Json(SessionOf(service, local, loggedIn: false));
        })
            .Produces<SessionStatus>()
            .ProducesErrors(StatusCodes.Status409Conflict);
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
    /// another device with 401, and can fail in the environment, so 401, 403 and 500 are declared; the statuses given are the
    /// ones its own operation adds.
    /// </summary>
    private static RouteHandlerBuilder ProducesErrors(this RouteHandlerBuilder builder, params int[] statuses) =>
        builder.ProducesErrorBodies(statuses.Append(StatusCodes.Status401Unauthorized));

    /// <summary>
    /// Declares the error bodies of the session endpoint's reading and logging out, which any connection may make, so no 401
    /// is declared for them. The guards and a failure in the environment still are.
    /// </summary>
    private static RouteHandlerBuilder ProducesSessionErrors(this RouteHandlerBuilder builder) =>
        builder.ProducesErrorBodies([]);

    private static RouteHandlerBuilder ProducesErrorBodies(this RouteHandlerBuilder builder, IEnumerable<int> statuses)
    {
        foreach (var status in statuses.Append(StatusCodes.Status403Forbidden).Append(StatusCodes.Status500InternalServerError))
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
        services.AddOpenApi(options =>
        {
            // The frontend's type generator reads OpenAPI 3.0 and 3.1 only.
            options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_1;
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "TacitMemory management API";
                // A relative server keeps the snapshot the same on every port the service runs on.
                document.Servers = [new OpenApiServer { Url = "/" }];
                // The preview's report is a computed property, which the generator leaves out of the required list; it is always written.
                if (document.Components?.Schemas?.TryGetValue(nameof(RestorePreview), out var preview) == true && preview is OpenApiSchema previewSchema)
                {
                    previewSchema.Required ??= new HashSet<string>();
                    previewSchema.Required.Add("report");
                }
                // The generator drops the nullability of a dictionary's values; a secret set to null is removed from the file.
                if (document.Components?.Schemas?.TryGetValue(nameof(ConfigSave), out var save) == true
                    && save.Properties?.TryGetValue("secrets", out var secrets) == true
                    && secrets.AdditionalProperties is OpenApiSchema secretValue)
                {
                    secretValue.Type = JsonSchemaType.String | JsonSchemaType.Null;
                }
                // The generator writes null into a shared enum's values when the enum is first met as nullable, so the
                // enum itself reads as nullable everywhere; where it may be null, the reference already says so.
                foreach (var schema in document.Components?.Schemas?.Values.OfType<OpenApiSchema>() ?? [])
                {
                    if (schema.Enum is { } values)
                        schema.Enum = [.. values.Where(value => value is not null && value.GetValueKind() != JsonValueKind.Null)];
                }
                return Task.CompletedTask;
            });
        });
    }

    /// <summary>
    /// Declares the <c>Retry-After</c> header of a login-limit answer: the seconds left until logins from the address may be
    /// tried again.
    /// </summary>
    private static Task LoginLimitedResponse(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var response = (OpenApiResponse)operation.Responses![StatusCodes.Status429TooManyRequests.ToString()];
        response.Headers ??= new Dictionary<string, IOpenApiHeader>();
        response.Headers["Retry-After"] = new OpenApiHeader
        {
            Description = "Seconds until logins from this address may be tried again.",
            Schema = new OpenApiSchema { Type = JsonSchemaType.Integer },
        };
        return Task.CompletedTask;
    }

    /// <summary>
    /// The restore endpoint answers with the preview or with the result, depending on the body; the document says so with a
    /// <c>oneOf</c> over both schemas.
    /// </summary>
    private static async Task RestoreResponse(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var preview = await context.GetOrCreateSchemaAsync(typeof(RestorePreview), null, cancellationToken);
        var result = await context.GetOrCreateSchemaAsync(typeof(RestoreResult), null, cancellationToken);
        var media = (OpenApiMediaType)operation.Responses![StatusCodes.Status200OK.ToString()].Content!["application/json"];
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
            return await JsonSerializer.DeserializeAsync(context.Request.Body, ManagementApiJson.Options.For<T>(), context.RequestAborted)
                ?? throw new JsonException();
        }
        catch (JsonException e)
        {
            var field = e.Path?.TrimStart('$').TrimStart('.');
            throw new TacitException(
                TacitError.RequestInvalid,
                string.IsNullOrEmpty(field)
                    ? HostMessages.ApiBodyInvalid.In(service.Language)
                    : HostMessages.ApiBodyInvalidField.Format(service.Language, field));
        }
    }

    /// <summary>
    /// The refusal an exception from an operation becomes: a refused operation as it is; a file failure naming the reason the
    /// system gave; any other failure logged with its kind and message by the service, and answered without them.
    /// </summary>
    private static TacitException Failure(MemoryService service, Exception e)
    {
        switch (e)
        {
            case TacitException refused:
                return refused;
            case IOException or UnauthorizedAccessException:
                return new TacitException(TacitError.FileSystemFailed, HostMessages.ApiFileFailed.Format(service.Language, e.Message), e);
            default:
                service.RecordUnexpectedFailure(e);
                return new TacitException(TacitError.InternalFailure, HostMessages.ApiInternalFailure.In(service.Language), e);
        }
    }

    /// <summary>The name of the session cookie. The management API reads and sets it, and its path is <c>/api</c>.</summary>
    private const string SessionCookie = "tacit_session";

    /// <summary>What the session endpoints and the access-password endpoints answer about the caller and the service.</summary>
    private static SessionStatus SessionOf(MemoryService service, bool local, bool loggedIn) =>
        new(local, loggedIn, service.Access.PasswordSet, service.Language, service.Version);

    /// <summary>The session cookie the request carries, if any.</summary>
    private static string? Cookie(HttpContext context) => context.Request.Cookies[SessionCookie];

    /// <summary>
    /// Whether the request is for the session endpoint, which any connection may use without a session: reading the session,
    /// logging in and logging out.
    /// </summary>
    private static bool IsSessionEndpoint(HttpContext context) =>
        context.Request.Path.Equals("/api/session", StringComparison.OrdinalIgnoreCase)
        && (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsDelete(context.Request.Method));

    /// <summary>
    /// Sets the session cookie: HttpOnly, SameSite=Strict, Path=/api, expiring with the session. Not Secure, since there is no
    /// HTTPS.
    /// </summary>
    private static void SetCookie(HttpContext context, SessionGrant grant) =>
        context.Response.Cookies.Append(SessionCookie, grant.Value, SessionCookieOptions(grant.Expires));

    private static CookieOptions SessionCookieOptions(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Path = "/api",
        Expires = expires,
        IsEssential = true,
    };

    /// <summary>A JSON response, serialized here with <see cref="ManagementApiJson.Options"/>.</summary>
    internal static IResult Json<T>(T value, int statusCode = StatusCodes.Status200OK) =>
        Results.Text(JsonSerializer.Serialize(value, ManagementApiJson.Options.For<T>()), "application/json; charset=utf-8", statusCode: statusCode);

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
    /// (<c>git</c>, the cloud service, the file system) or the service failed unexpectedly.
    /// </summary>
    private static int StatusOf(TacitError error) => error switch
    {
        TacitError.GitFailed or TacitError.CloudBackupFailed or TacitError.RestoreFailed
            or TacitError.ReloadFailed or TacitError.FileSystemFailed or TacitError.InternalFailure => StatusCodes.Status500InternalServerError,
        TacitError.RequestInvalid or TacitError.PasswordTooShort or TacitError.ConfigRejected or TacitError.PromptRejected
            or TacitError.ClientNameInvalid => StatusCodes.Status400BadRequest,
        TacitError.PasswordWrong => StatusCodes.Status401Unauthorized,
        TacitError.PasswordClearRemoteRefused => StatusCodes.Status403Forbidden,
        TacitError.SecretNotFound or TacitError.BindingNotFound or TacitError.PromptUnknown
            or TacitError.LogDateNotFound or TacitError.MemoryNotFound => StatusCodes.Status404NotFound,
        TacitError.LoginLimited => StatusCodes.Status429TooManyRequests,
        _ => StatusCodes.Status409Conflict,
    };

    /// <summary>Whether the media type is application/json; parameters such as a charset do not count.</summary>
    private static bool IsJson(string? contentType) =>
        contentType?.Split(';')[0].Trim().Equals("application/json", StringComparison.OrdinalIgnoreCase) ?? false;

    /// <summary>The MCP endpoint of the port the request arrived on: the address the status reports.</summary>
    private static Uri McpEndpoint(HttpContext context) => new($"http://127.0.0.1:{context.Connection.LocalPort}/mcp");
}
