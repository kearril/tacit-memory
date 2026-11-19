using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using TacitMemory.Core.Configuration;
using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Backup;

/// <summary>Where in the cloud backup a failure happened; the error message names it.</summary>
internal enum CloudStage
{
    Connect,
    Authentication,
    Upload,
    Replace,
    Download,
}

/// <summary>
/// The WebDAV client of the cloud backup. It uses only the generic methods (PUT, MOVE, MKCOL, DELETE, GET) and HTTP basic
/// authentication, which is sent only when a username is configured. <see cref="ReplaceAsync"/> replaces the remote
/// backup atomically: it uploads <c>&lt;file&gt;.part</c> and then moves it over the configured file, so a failed upload
/// leaves the previous backup in place. No request is retried, and a service without MOVE is an error, never a plain
/// overwrite. Failures are <see cref="TacitError.CloudBackupFailed"/> naming the stage and the reason; they never carry
/// the password or the credentials of the URL.
/// </summary>
internal sealed class WebDavClient(WebDavConfig config, MemoryLanguage language) : IDisposable
{
    /// <summary>How long one request may take, from sending it to the last byte of its answer, the body included.</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(10);

    private static readonly HttpMethod MkcolMethod = new("MKCOL");
    private static readonly HttpMethod MoveMethod = new("MOVE");

    // The deadline is set per request (see Attempt), so HttpClient's own timeout is off: it would not cover a body read
    // that starts after the headers came back.
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly AuthenticationHeaderValue? _authorization = config.Username is null ? null : BasicAuthorization(config);

    /// <summary>Uploads <paramref name="localFile"/> as the remote backup, replacing the one there.</summary>
    /// <exception cref="TacitException">The upload or the replacement failed; the remote backup that was there is unchanged.</exception>
    public async Task ReplaceAsync(string localFile, CancellationToken cancellationToken)
    {
        var part = new Uri(config.Url, config.File + ".part");
        try
        {
            await UploadAsync(part, localFile, cancellationToken);
            await MoveAsync(part, new Uri(config.Url, config.File), cancellationToken);
        }
        catch (TacitException)
        {
            await DeleteQuietlyAsync(part);
            throw;
        }
    }

    /// <summary>
    /// Downloads the remote backup into <paramref name="localFile"/>, replacing a file there. A service without the backup
    /// is a failure that says so. A download that fails leaves no partial file behind. The ten-minute deadline covers the
    /// transfer of the body as well as the headers.
    /// </summary>
    /// <exception cref="TacitException">The download failed; the remote backup is unchanged.</exception>
    public async Task DownloadAsync(string localFile, CancellationToken cancellationToken)
    {
        using var attempt = new Attempt(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(config.Url, config.File));
        try
        {
            using var response = await Exchange(request, CloudStage.Download, attempt, HttpCompletionOption.ResponseHeadersRead);
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw Failure(CloudStage.Download, BackupMessages.CloudReasonMissing);
            Ensure(response, CloudStage.Download);

            // Opened before the transfer, so a local disk failure is not mistaken for a failed transfer.
            await using var target = File.Create(localFile);
            await using var source = await response.Content.ReadAsStreamAsync(attempt.Token);
            await source.CopyToAsync(target, attempt.Token);
        }
        catch (Exception e) when (e is HttpRequestException or IOException)
        {
            DeleteLocalQuietly(localFile);
            throw Failure(CloudStage.Download, BackupMessages.CloudReasonInterrupted);
        }
        catch (OperationCanceledException) when (attempt.TimedOut)
        {
            DeleteLocalQuietly(localFile);
            throw Failure(CloudStage.Download, BackupMessages.CloudReasonTimeout);
        }
        catch (OperationCanceledException)
        {
            DeleteLocalQuietly(localFile);
            throw;
        }
    }

    private static void DeleteLocalQuietly(string localFile)
    {
        try
        {
            File.Delete(localFile);
        }
        catch (IOException)
        {
            // Nothing more to do: the failure that matters is already on its way to the caller.
        }
    }

    public void Dispose() => _http.Dispose();

    /// <summary>Sends the file to the part name. A missing folder is made once, and the file is sent once more.</summary>
    private async Task UploadAsync(Uri part, string localFile, CancellationToken cancellationToken)
    {
        using (var first = await PutFileAsync(part, localFile, cancellationToken))
        {
            if (first.StatusCode is not (HttpStatusCode.NotFound or HttpStatusCode.Conflict))
            {
                Ensure(first, CloudStage.Upload);
                return;
            }
        }

        // Only the folder's last level is made. 405 says the folder is there already, so it is not a failure.
        using (var made = await SendAsync(new HttpRequestMessage(MkcolMethod, config.Url), CloudStage.Upload, cancellationToken))
        {
            if (made.StatusCode is not HttpStatusCode.MethodNotAllowed)
                Ensure(made, CloudStage.Upload);
        }

        using var retry = await PutFileAsync(part, localFile, cancellationToken);
        Ensure(retry, CloudStage.Upload);
    }

    /// <summary>Moves the part file over the backup. A service that refuses MOVE is an error that says so.</summary>
    private async Task MoveAsync(Uri part, Uri target, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(MoveMethod, part);
        request.Headers.Add("Destination", target.AbsoluteUri);
        request.Headers.Add("Overwrite", "T");
        using var response = await SendAsync(request, CloudStage.Replace, cancellationToken);
        if (response.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented)
            throw Failure(CloudStage.Replace, BackupMessages.CloudReasonUnsupported, (int)response.StatusCode);
        Ensure(response, CloudStage.Replace);
    }

    private async Task<HttpResponseMessage> PutFileAsync(Uri url, string localFile, CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(localFile);
        using var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = new StreamContent(stream) };
        request.Content.Headers.ContentLength = stream.Length;
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        return await SendAsync(request, CloudStage.Upload, cancellationToken);
    }

    /// <summary>Removes the part file after a failure. Best effort: the service may be gone, which the caller has already reported.</summary>
    private async Task DeleteQuietlyAsync(Uri part)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, part);
            using var response = await SendAsync(request, CloudStage.Replace, CancellationToken.None);
        }
        catch (TacitException)
        {
            // Nothing more to do: the failure that matters is already on its way to the caller.
        }
    }

    /// <summary>Sends one request under its own deadline and reads its whole answer.</summary>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CloudStage stage, CancellationToken cancellationToken)
    {
        using var attempt = new Attempt(cancellationToken);
        return await Exchange(request, stage, attempt, HttpCompletionOption.ResponseContentRead);
    }

    /// <summary>
    /// Sends one request with the credentials, under <paramref name="attempt"/>'s deadline. A failure to reach the service is a
    /// connection failure; any other transport failure is a transfer cut short at <paramref name="stage"/>, and a deadline that
    /// passes is a timeout at that stage.
    /// </summary>
    private async Task<HttpResponseMessage> Exchange(
        HttpRequestMessage request, CloudStage stage, Attempt attempt, HttpCompletionOption completion)
    {
        if (_authorization is not null)
            request.Headers.Authorization = _authorization;
        try
        {
            return await _http.SendAsync(request, completion, attempt.Token);
        }
        catch (HttpRequestException e) when (IsUnreachable(e))
        {
            throw Failure(CloudStage.Connect, BackupMessages.CloudReasonUnreachable);
        }
        catch (Exception e) when (e is HttpRequestException or IOException)
        {
            throw Failure(stage, BackupMessages.CloudReasonInterrupted);
        }
        catch (OperationCanceledException) when (attempt.TimedOut)
        {
            throw Failure(stage, BackupMessages.CloudReasonTimeout);
        }
    }

    /// <summary>Throws unless the answer is a success. 401 is an authentication failure at whichever step asked for it.</summary>
    private void Ensure(HttpResponseMessage response, CloudStage stage)
    {
        if (response.IsSuccessStatusCode)
            return;

        var code = (int)response.StatusCode;
        throw code == (int)HttpStatusCode.Unauthorized
            ? Failure(CloudStage.Authentication, BackupMessages.CloudReasonStatus, code)
            : Failure(stage, BackupMessages.CloudReasonStatus, code);
    }

    private TacitException Failure(CloudStage stage, Localized reason, params object[] args) =>
        new(TacitError.CloudBackupFailed, BackupMessages.CloudBackupFailed.Format(language, StageName(stage).In(language), reason.Format(language, args)));

    private static Localized StageName(CloudStage stage) => stage switch
    {
        CloudStage.Connect => BackupMessages.CloudStageConnect,
        CloudStage.Authentication => BackupMessages.CloudStageAuthentication,
        CloudStage.Upload => BackupMessages.CloudStageUpload,
        CloudStage.Replace => BackupMessages.CloudStageReplace,
        CloudStage.Download => BackupMessages.CloudStageDownload,
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null),
    };

    /// <summary>Whether the service could not be reached at all: the name does not resolve, or the connection is refused or cannot be made.</summary>
    private static bool IsUnreachable(HttpRequestException e) =>
        e.HttpRequestError is HttpRequestError.NameResolutionError
        || e.InnerException is SocketException
        {
            SocketErrorCode: SocketError.ConnectionRefused or SocketError.HostNotFound or SocketError.HostUnreachable
                or SocketError.NetworkUnreachable or SocketError.TimedOut,
        };

    private static AuthenticationHeaderValue BasicAuthorization(WebDavConfig config) => new(
        "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{config.Username}:{config.Password?.Value}")));

    /// <summary>
    /// One request's deadline: <see cref="RequestTimeout"/> from its start, linked to the caller's cancellation. Whether the
    /// deadline or the caller ended it is what <see cref="TimedOut"/> tells.
    /// </summary>
    private sealed class Attempt : IDisposable
    {
        private readonly CancellationToken _caller;
        private readonly CancellationTokenSource _source;

        public Attempt(CancellationToken caller)
        {
            _caller = caller;
            _source = CancellationTokenSource.CreateLinkedTokenSource(caller);
            _source.CancelAfter(RequestTimeout);
        }

        public CancellationToken Token => _source.Token;

        /// <summary>Whether the deadline ended the request, rather than the caller.</summary>
        public bool TimedOut => !_caller.IsCancellationRequested && _source.IsCancellationRequested;

        public void Dispose() => _source.Dispose();
    }
}
