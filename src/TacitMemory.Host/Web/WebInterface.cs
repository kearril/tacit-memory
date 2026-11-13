using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using TacitMemory.Core;

namespace TacitMemory.Host.Web;

/// <summary>
/// The web interface: the files of the web directory under <c>/ui</c>, and <c>/</c> redirected to <c>/ui/</c>. Any connection
/// may reach it; logging in is for the management API only. A path under <c>/ui</c> that names no file gets the entry page,
/// except a path whose last segment has an extension, which gets a 404 so a missing script is not answered with HTML. A
/// path that tries to leave the web directory is a 404. Without a built entry page every path under <c>/ui</c> gets the
/// not-built text.
/// </summary>
internal static class WebInterface
{
    private const string Prefix = "/ui";
    private const string EntryFile = "index.html";
    private const string HashedFolder = "assets";
    private const string NoStore = "no-store";
    private const string NoCache = "no-cache";
    private const string LongLived = "public, max-age=31536000, immutable";

    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    /// <summary>Maps the web interface. <paramref name="webRoot"/> is the directory the built files are read from.</summary>
    public static void Map(WebApplication app, MemoryService service, string webRoot)
    {
        var root = Path.GetFullPath(webRoot).TrimEnd(Path.DirectorySeparatorChar);
        app.Use(async (context, next) =>
        {
            if (context.Request.Method is not ("GET" or "HEAD"))
            {
                await next(context);
                return;
            }

            var path = context.Request.Path.Value ?? "";
            if (path == "/")
            {
                context.Response.Redirect(Prefix + "/");
                return;
            }
            if (path != Prefix && !path.StartsWith(Prefix + "/", StringComparison.Ordinal))
            {
                await next(context);
                return;
            }

            if (!File.Exists(Path.Combine(root, EntryFile)))
            {
                await Results.Text(service.WebNotBuiltMessage(), "text/plain; charset=utf-8", statusCode: StatusCodes.Status404NotFound)
                    .ExecuteAsync(context);
                return;
            }

            var segments = path.Length == Prefix.Length
                ? Array.Empty<string>()
                : path[(Prefix.Length + 1)..].Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (!segments.All(IsPlainSegment))
            {
                await NotFound(context);
                return;
            }

            var file = segments.Length == 0 ? null : FileWithin(root, segments);
            if (file is not null)
            {
                await Serve(context, file, CacheControlFor(segments));
                return;
            }
            if (segments.Length > 0 && Path.HasExtension(segments[^1]))
            {
                await NotFound(context);
                return;
            }
            await Serve(context, Path.Combine(root, EntryFile), NoStore);
        });
    }

    /// <summary>A segment that names one entry in a folder: not empty, not <c>.</c> or <c>..</c>, and no separator or drive marker.</summary>
    private static bool IsPlainSegment(string segment) =>
        segment is not ("." or "..") && !segment.Contains('\\') && !segment.Contains('/') && !segment.Contains(':') && !segment.Contains('\0');

    /// <summary>The file the segments name, when it exists inside the web directory; <c>null</c> otherwise.</summary>
    private static string? FileWithin(string root, string[] segments)
    {
        var full = Path.GetFullPath(Path.Combine([root, .. segments]));
        // The separator in the check stops a sibling directory that starts with the same name from passing.
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return null;
        return File.Exists(full) ? full : null;
    }

    /// <summary>The entry page is never cached; the hashed files under the assets folder are cached for good; other files revalidate.</summary>
    private static string CacheControlFor(string[] segments) =>
        segments.Length > 1 && segments[0] == HashedFolder ? LongLived : NoCache;

    private static Task Serve(HttpContext context, string file, string cacheControl)
    {
        context.Response.Headers.CacheControl = cacheControl;
        var contentType = ContentTypes.TryGetContentType(file, out var type) ? type : "application/octet-stream";
        return Results.File(file, contentType).ExecuteAsync(context);
    }

    private static Task NotFound(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return Task.CompletedTask;
    }
}
