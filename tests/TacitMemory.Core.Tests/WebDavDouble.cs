using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace TacitMemory.Core.Tests;

/// <summary>
/// A WebDAV service on 127.0.0.1 for the cloud backup tests, answering the generic methods the backup uses: PUT, MOVE,
/// MKCOL, GET and DELETE. Collections exist only once made, as on a real server: a PUT or MKCOL whose parent is missing
/// answers 409. The tests can require HTTP basic authentication, refuse MOVE as a server without atomic replacement
/// does, make a method answer a given status, or drop a PUT after part of its body has arrived. Every request is kept in
/// order with the headers the backup depends on, and files are kept by their absolute path on the service.
/// </summary>
internal sealed class WebDavDouble : IDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentQueue<WebDavRequest> _requests = new();
    private readonly ConcurrentDictionary<string, byte[]> _files = new();
    private readonly ConcurrentDictionary<string, byte> _collections = new();
    private readonly ConcurrentDictionary<string, int> _forcedStatus = new();
    private readonly ConcurrentDictionary<string, long> _dropAfter = new();
    private (string User, string Password)? _credentials;

    public WebDavDouble()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        _app = builder.Build();
        _app.Run(HandleAsync);
        _app.StartAsync().GetAwaiter().GetResult();
        Address = new Uri($"http://127.0.0.1:{new Uri(_app.Urls.Single()).Port}/");
        _collections["/"] = 0;
        _collections["/dav/"] = 0;
    }

    /// <summary>The service's root address, ending in <c>/</c>. Paths in this double are absolute, as they appear in a request.</summary>
    public Uri Address { get; }

    /// <summary>Whether MOVE is supported. A server without it answers 405 to MOVE, and the backup must not fall back to overwriting.</summary>
    public bool MoveSupported { get; set; } = true;

    /// <summary>Every request received, in the order it arrived.</summary>
    public IReadOnlyList<WebDavRequest> Requests => [.. _requests];

    /// <summary>Makes a collection at <paramref name="path"/>, an absolute path such as <c>/dav/tacit/</c>.</summary>
    public void CreateCollection(string path) => _collections[NormalizeCollection(path)] = 0;

    /// <summary>The bytes stored at <paramref name="path"/>, an absolute path such as <c>/dav/tacit/tacit-backup.zip</c>; <c>null</c> when there is no file.</summary>
    public byte[]? FileAt(string path) => _files.GetValueOrDefault(path);

    /// <summary>Stores a file at <paramref name="path"/>, for a test that starts from a remote backup that is already there.</summary>
    public void PutFile(string path, byte[] content)
    {
        _collections.TryAdd(ParentOf(path), 0);
        _files[path] = content;
    }

    /// <summary>Requires HTTP basic authentication with this user and password; every other request answers 401.</summary>
    public void RequireBasic(string user, string password) => _credentials = (user, password);

    /// <summary>Makes every request of <paramref name="method"/> answer <paramref name="status"/> without doing anything; <c>null</c> restores the normal answers.</summary>
    public void Answer(string method, int? status)
    {
        if (status is { } code)
            _forcedStatus[method] = code;
        else
            _forcedStatus.TryRemove(method, out _);
    }

    /// <summary>The next PUT reads this many bytes of its body and then drops the connection, as a network failure does mid-upload.</summary>
    public void DropNextPutAfter(long bytes) => _dropAfter["PUT"] = bytes;

    /// <summary>The Authorization header that passes basic authentication with this user and password.</summary>
    public static string BasicAuthorization(string user, string password) =>
        "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}"));

    private async Task HandleAsync(HttpContext http)
    {
        var method = http.Request.Method;
        var path = http.Request.Path.Value ?? "/";
        var authorization = http.Request.Headers.Authorization.ToString();
        var destination = http.Request.Headers["Destination"].ToString();
        var overwrite = http.Request.Headers["Overwrite"].ToString();
        _requests.Enqueue(new WebDavRequest(
            method,
            path,
            authorization.Length == 0 ? null : authorization,
            destination.Length == 0 ? null : destination,
            overwrite.Length == 0 ? null : overwrite));

        if (_credentials is { } credentials && authorization != BasicAuthorization(credentials.User, credentials.Password))
        {
            http.Response.Headers.WWWAuthenticate = "Basic realm=\"tacit-dav\"";
            http.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (_forcedStatus.TryGetValue(method, out var forced))
        {
            http.Response.StatusCode = forced;
            return;
        }

        switch (method)
        {
            case "PUT":
                await PutAsync(http, path);
                break;
            case "MKCOL":
                Mkcol(http, path);
                break;
            case "MOVE":
                Move(http, path, destination, overwrite);
                break;
            case "GET":
                await GetAsync(http, path);
                break;
            case "DELETE":
                Delete(http, path);
                break;
            default:
                http.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                break;
        }
    }

    private async Task PutAsync(HttpContext http, string path)
    {
        if (!_collections.ContainsKey(ParentOf(path)))
        {
            http.Response.StatusCode = StatusCodes.Status409Conflict;
            return;
        }

        if (_dropAfter.TryRemove("PUT", out var dropAfter))
        {
            // Read part of the body, then cut the connection: the client sees a failure and the stored file is untouched.
            var partial = new byte[dropAfter];
            await http.Request.Body.ReadExactlyAsync(partial);
            http.Abort();
            return;
        }

        using var buffer = new MemoryStream();
        await http.Request.Body.CopyToAsync(buffer);
        var existed = _files.ContainsKey(path);
        _files[path] = buffer.ToArray();
        http.Response.StatusCode = existed ? StatusCodes.Status204NoContent : StatusCodes.Status201Created;
    }

    private void Mkcol(HttpContext http, string path)
    {
        var collection = NormalizeCollection(path);
        if (_collections.ContainsKey(collection) || _files.ContainsKey(path))
        {
            http.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return;
        }

        if (!_collections.ContainsKey(ParentOf(collection)))
        {
            http.Response.StatusCode = StatusCodes.Status409Conflict;
            return;
        }

        _collections[collection] = 0;
        http.Response.StatusCode = StatusCodes.Status201Created;
    }

    private void Move(HttpContext http, string path, string destination, string overwrite)
    {
        if (!MoveSupported)
        {
            http.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return;
        }

        if (!_files.TryGetValue(path, out var content) || !Uri.TryCreate(destination, UriKind.Absolute, out var target))
        {
            http.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var to = target.AbsolutePath;
        var existed = _files.ContainsKey(to);
        if (existed && overwrite == "F")
        {
            http.Response.StatusCode = StatusCodes.Status412PreconditionFailed;
            return;
        }

        if (!_collections.ContainsKey(ParentOf(to)))
        {
            http.Response.StatusCode = StatusCodes.Status409Conflict;
            return;
        }

        _files[to] = content;
        _files.TryRemove(path, out _);
        http.Response.StatusCode = existed ? StatusCodes.Status204NoContent : StatusCodes.Status201Created;
    }

    private async Task GetAsync(HttpContext http, string path)
    {
        if (!_files.TryGetValue(path, out var content))
        {
            http.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        http.Response.StatusCode = StatusCodes.Status200OK;
        http.Response.ContentLength = content.Length;
        await http.Response.Body.WriteAsync(content);
    }

    private void Delete(HttpContext http, string path)
    {
        http.Response.StatusCode = _files.TryRemove(path, out _) ? StatusCodes.Status204NoContent : StatusCodes.Status404NotFound;
    }

    private static string NormalizeCollection(string path) => path.EndsWith('/') ? path : path + "/";

    private static string ParentOf(string path)
    {
        var trimmed = path.TrimEnd('/');
        var cut = trimmed.LastIndexOf('/');
        return cut <= 0 ? "/" : trimmed[..(cut + 1)];
    }

    public void Dispose() => _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
}

/// <summary>One request the double received: its method, its path, and the headers the backup sets.</summary>
/// <param name="Method">The HTTP method, as sent.</param>
/// <param name="Path">The request path, such as <c>/dav/tacit/tacit-backup.zip.part</c>.</param>
/// <param name="Authorization">The Authorization header; <c>null</c> when none was sent.</param>
/// <param name="Destination">The Destination header of a MOVE; <c>null</c> otherwise.</param>
/// <param name="Overwrite">The Overwrite header of a MOVE; <c>null</c> otherwise.</param>
internal sealed record WebDavRequest(string Method, string Path, string? Authorization, string? Destination, string? Overwrite);
