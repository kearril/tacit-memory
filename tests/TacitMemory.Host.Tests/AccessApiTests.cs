using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TacitMemory.Core;
using TacitMemory.Core.Tests;
using TacitMemory.Host.Api;

namespace TacitMemory.Host.Tests;

/// <summary>
/// The session and access-password endpoints over real HTTP, with the host's locality narrowed to <c>127.0.0.1</c>: a login
/// from another device, its cookie and what it reaches, the pauses after wrong passwords, the rules of setting and clearing the
/// password, and what a restart keeps.
/// </summary>
public sealed class AccessApiTests : IAsyncLifetime
{
    private const string Password = "correct horse";
    private const string Changed = "second password";
    private const string CookieName = "tacit_session";

    private readonly TestHome _home = new();
    private readonly ModelDouble _models = new() { EmbeddingDown = true, RerankDown = true };
    private readonly CapturingLoggerFactory _logs = new();
    private readonly List<HttpClient> _clients = [];
    private MemoryService? _service;
    private ServiceHost? _host;
    private HttpClient _local = null!;
    private HttpClient _remote = null!;

    public async Task InitializeAsync()
    {
        _home.InitReady(models: _models);
        _local = Track(PeerClients.Local());
        _remote = Track(PeerClients.Remote());
        await StartAsync();
    }

    public async Task DisposeAsync()
    {
        await StopAsync();
        foreach (var client in _clients)
            client.Dispose();
        _models.Dispose();
        _home.Dispose();
    }

    private HttpClient Track(HttpClient client)
    {
        _clients.Add(client);
        return client;
    }

    /// <summary>Starts a service and host on the home, as a restart would.</summary>
    private async Task StartAsync()
    {
        _service = _home.Tacit().OpenService(_ => { }, _logs);
        _host = await ServiceHost.TryStartAsync(_service, port: 0, NullLoggerProvider.Instance, PeerClients.OnlyLoopbackIsLocal)
            ?? throw new InvalidOperationException("Port 0 is never in use.");
    }

    /// <summary>Stops the service and host, as a restart would, leaving the home as it is.</summary>
    private async Task StopAsync()
    {
        if (_host is not null)
            await _host.DisposeAsync();
        _service?.Dispose();
        _host = null;
        _service = null;
    }

    private int Port => _host!.Endpoint.Port;

    private Uri Url(string path) => new($"http://127.0.0.1:{Port}{path}");

    private string StateFile => Path.Combine(_home.TacitHome, "access.json");

    private sealed record Reply(HttpStatusCode Status, string Text, HttpResponseHeaders Headers)
    {
        public ApiError Error => JsonSerializer.Deserialize<ApiError>(Text, ManagementApiJson.Options)!;

        public SessionStatus Session => JsonSerializer.Deserialize<SessionStatus>(Text, ManagementApiJson.Options)!;

        /// <summary>The value of the session cookie this reply sets; <c>null</c> when it sets none.</summary>
        public string? SetCookieValue => Cookie(Headers)?.Value;

        /// <summary>The whole Set-Cookie line this reply sets, for its attributes.</summary>
        public string? SetCookieLine => Cookie(Headers)?.Line;
    }

    private sealed record CookieLine(string Value, string Line);

    private static CookieLine? Cookie(HttpResponseHeaders headers)
    {
        if (!headers.TryGetValues("Set-Cookie", out var values))
            return null;
        var line = values.FirstOrDefault(value => value.StartsWith(CookieName + "=", StringComparison.Ordinal));
        if (line is null)
            return null;
        var value = line[(CookieName.Length + 1)..].Split(';')[0];
        return new CookieLine(value, line);
    }

    private static async Task<Reply> Send(HttpClient client, HttpMethod method, Uri url, object? body = null, string? cookie = null, string? origin = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body, RequestJson.Options), Encoding.UTF8, "application/json");
        if (cookie is not null)
            request.Headers.TryAddWithoutValidation("Cookie", $"{CookieName}={cookie}");
        if (origin is not null)
            request.Headers.TryAddWithoutValidation("Origin", origin);
        using var response = await client.SendAsync(request);
        return new Reply(response.StatusCode, await response.Content.ReadAsStringAsync(), response.Headers);
    }

    private Task<Reply> Remote(HttpMethod method, string path, object? body = null, string? cookie = null) =>
        Send(_remote, method, Url(path), body, cookie);

    private Task<Reply> Local(HttpMethod method, string path, object? body = null) =>
        Send(_local, method, Url(path), body);

    private static readonly object Body = new { password = Password };

    /// <summary>Sets the password from this machine, as the first step of most tests.</summary>
    private async Task SetPasswordLocally(string password = Password)
    {
        var reply = await Local(HttpMethod.Put, "/api/access-password", new { password });
        Assert.Equal(HttpStatusCode.OK, reply.Status);
    }

    /// <summary>Logs in from another device and returns the cookie value; the test fails when the login does not succeed.</summary>
    private async Task<string> LoginFromRemote(string password = Password)
    {
        var reply = await Remote(HttpMethod.Post, "/api/session", new { password });
        Assert.Equal(HttpStatusCode.OK, reply.Status);
        return Assert.IsType<string>(reply.SetCookieValue);
    }

    // ---- The session endpoint ------------------------------------------------------------------------------------

    [Fact]
    public async Task GET_session_is_open_to_a_remote_connection_and_tells_it_so()
    {
        var reply = await Remote(HttpMethod.Get, "/api/session");

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        var session = reply.Session;
        Assert.False(session.Local);
        Assert.False(session.LoggedIn);
        Assert.False(session.PasswordSet);
        Assert.Equal(MemoryLanguage.Zh, session.Language);
        Assert.False(string.IsNullOrWhiteSpace(session.Version));
    }

    [Fact]
    public async Task GET_session_tells_a_local_connection_it_is_local_and_whether_a_password_is_set()
    {
        await SetPasswordLocally();

        var session = (await Local(HttpMethod.Get, "/api/session")).Session;

        Assert.True(session.Local);
        Assert.True(session.PasswordSet);
        Assert.False(session.LoggedIn);
    }

    [Fact]
    public async Task Session_endpoint_responses_are_the_same_shape_as_the_other_api_responses()
    {
        var reply = await Remote(HttpMethod.Get, "/api/session");

        Assert.Equal(["language", "local", "loggedIn", "passwordSet", "version"], JsonDocument.Parse(reply.Text).RootElement.EnumerateObject().Select(p => p.Name).Order());
    }

    // ---- Login ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_remote_api_call_without_a_session_is_401_and_the_session_endpoint_is_not()
    {
        await SetPasswordLocally();

        var status = await Remote(HttpMethod.Get, "/api/status");
        var session = await Remote(HttpMethod.Get, "/api/session");

        Assert.Equal(HttpStatusCode.Unauthorized, status.Status);
        Assert.Equal(TacitError.ApiUnauthorized, status.Error.Error);
        Assert.Equal(HttpStatusCode.OK, session.Status);
    }

    [Fact]
    public async Task A_login_with_the_right_password_sets_a_session_cookie_that_opens_the_api()
    {
        await SetPasswordLocally();

        var reply = await Remote(HttpMethod.Post, "/api/session", Body);

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        Assert.True(reply.Session.LoggedIn);
        var cookie = Assert.IsType<string>(reply.SetCookieValue);
        Assert.Equal(HttpStatusCode.OK, (await Remote(HttpMethod.Get, "/api/status", cookie: cookie)).Status);
        Assert.Equal(HttpStatusCode.OK, (await Remote(HttpMethod.Get, "/api/openapi.json", cookie: cookie)).Status);
    }

    [Fact]
    public async Task The_session_cookie_has_the_required_attributes_and_no_secure_flag()
    {
        await SetPasswordLocally();

        var line = Assert.IsType<string>((await Remote(HttpMethod.Post, "/api/session", Body)).SetCookieLine);

        var attributes = line.Split(';').Select(part => part.Trim().ToLowerInvariant()).ToArray();
        Assert.Contains("httponly", attributes);
        Assert.Contains("samesite=strict", attributes);
        Assert.Contains("path=/api", attributes);
        Assert.Contains(attributes, attribute => attribute.StartsWith("expires=", StringComparison.Ordinal));
        Assert.DoesNotContain("secure", attributes);
    }

    [Fact]
    public async Task A_wrong_password_is_401_with_the_password_wrong_error_and_sets_no_cookie()
    {
        await SetPasswordLocally();

        var reply = await Remote(HttpMethod.Post, "/api/session", new { password = "correct horsf" });

        Assert.Equal(HttpStatusCode.Unauthorized, reply.Status);
        Assert.Equal(TacitError.PasswordWrong, reply.Error.Error);
        Assert.False(string.IsNullOrWhiteSpace(reply.Error.Message));
        Assert.Null(reply.SetCookieValue);
    }

    [Fact]
    public async Task A_login_while_no_password_is_set_is_a_wrong_password()
    {
        var reply = await Remote(HttpMethod.Post, "/api/session", Body);

        Assert.Equal(HttpStatusCode.Unauthorized, reply.Status);
        Assert.Equal(TacitError.PasswordWrong, reply.Error.Error);
    }

    [Fact]
    public async Task A_login_body_without_a_password_is_a_request_error()
    {
        var reply = await Remote(HttpMethod.Post, "/api/session", new { });

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        Assert.Equal(TacitError.RequestInvalid, reply.Error.Error);
    }

    [Fact]
    public async Task Five_wrong_passwords_pause_logins_from_that_address_for_sixty_seconds_and_then_they_resume()
    {
        await SetPasswordLocally();
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Remote(HttpMethod.Post, "/api/session", new { password = "wrong wrong" })).Status);

        var paused = await Remote(HttpMethod.Post, "/api/session", Body);
        Assert.Equal((HttpStatusCode)429, paused.Status);
        Assert.Equal(TacitError.LoginLimited, paused.Error.Error);
        Assert.Contains("60", paused.Error.Message, StringComparison.Ordinal);

        _home.Clock.Advance(TimeSpan.FromSeconds(59));
        var stillPaused = await Remote(HttpMethod.Post, "/api/session", Body);
        Assert.Equal((HttpStatusCode)429, stillPaused.Status);
        Assert.Contains("1", stillPaused.Error.Message, StringComparison.Ordinal);

        _home.Clock.Advance(TimeSpan.FromSeconds(1));
        var resumed = await Remote(HttpMethod.Post, "/api/session", Body);
        Assert.Equal(HttpStatusCode.OK, resumed.Status);
    }

    [Fact]
    public async Task A_paused_login_says_in_retry_after_how_many_seconds_are_left()
    {
        await SetPasswordLocally();
        for (var i = 0; i < 5; i++)
            await Remote(HttpMethod.Post, "/api/session", new { password = "wrong wrong" });

        var paused = await Remote(HttpMethod.Post, "/api/session", Body);
        Assert.Equal("60", RetryAfter(paused));

        _home.Clock.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal("40", RetryAfter(await Remote(HttpMethod.Post, "/api/session", Body)));
    }

    [Fact]
    public async Task A_remote_change_with_wrong_current_passwords_is_limited_like_a_login()
    {
        await SetPasswordLocally();
        var cookie = await LoginFromRemote();
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Remote(HttpMethod.Put, "/api/access-password", new { password = Changed, current = "wrong wrong" }, cookie)).Status);

        var paused = await Remote(HttpMethod.Put, "/api/access-password", new { password = Changed, current = Password }, cookie);
        Assert.Equal((HttpStatusCode)429, paused.Status);
        Assert.Equal(TacitError.LoginLimited, paused.Error.Error);
        Assert.Equal("60", RetryAfter(paused));
        Assert.Null(paused.SetCookieValue);

        _home.Clock.Advance(TimeSpan.FromSeconds(60));
        var resumed = await Remote(HttpMethod.Put, "/api/access-password", new { password = Changed, current = Password }, cookie);
        Assert.Equal(HttpStatusCode.OK, resumed.Status);
    }

    private static string? RetryAfter(Reply reply) =>
        reply.Headers.TryGetValues("Retry-After", out var values) ? values.Single() : null;

    [Fact]
    public async Task A_pause_refuses_the_right_password_too_so_a_guess_gains_nothing()
    {
        await SetPasswordLocally();
        for (var i = 0; i < 5; i++)
            await Remote(HttpMethod.Post, "/api/session", new { password = "wrong wrong" });

        var reply = await Remote(HttpMethod.Post, "/api/session", Body);

        Assert.Equal((HttpStatusCode)429, reply.Status);
        Assert.Null(reply.SetCookieValue);
    }

    [Fact]
    public async Task A_successful_login_clears_the_count_of_wrong_passwords()
    {
        await SetPasswordLocally();
        for (var i = 0; i < 4; i++)
            await Remote(HttpMethod.Post, "/api/session", new { password = "wrong wrong" });
        Assert.Equal(HttpStatusCode.OK, (await Remote(HttpMethod.Post, "/api/session", Body)).Status);

        for (var i = 0; i < 4; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Remote(HttpMethod.Post, "/api/session", new { password = "wrong wrong" })).Status);
        Assert.Equal(HttpStatusCode.OK, (await Remote(HttpMethod.Post, "/api/session", Body)).Status);
    }

    [Fact]
    public async Task Wrong_passwords_are_counted_even_while_no_password_is_set()
    {
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Remote(HttpMethod.Post, "/api/session", Body)).Status);

        Assert.Equal((HttpStatusCode)429, (await Remote(HttpMethod.Post, "/api/session", Body)).Status);
    }

    [Fact]
    public async Task The_pause_is_per_address_so_this_machine_can_still_log_in()
    {
        await SetPasswordLocally();
        for (var i = 0; i < 5; i++)
            await Remote(HttpMethod.Post, "/api/session", new { password = "wrong wrong" });

        var local = await Local(HttpMethod.Post, "/api/session", Body);

        Assert.Equal(HttpStatusCode.OK, local.Status);
    }

    // ---- Logout and the cookie's life -----------------------------------------------------------------------------

    [Fact]
    public async Task Logout_expires_the_cookie_of_this_browser_so_the_client_without_it_is_refused()
    {
        await SetPasswordLocally();
        var cookie = await LoginFromRemote();

        var logout = await Remote(HttpMethod.Delete, "/api/session", cookie: cookie);

        Assert.Equal(HttpStatusCode.OK, logout.Status);
        Assert.False(logout.Session.LoggedIn);
        Assert.Contains("expires=", logout.SetCookieLine ?? "", StringComparison.OrdinalIgnoreCase);
        // The browser has dropped the expired cookie, so its next request carries none.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Remote(HttpMethod.Get, "/api/status")).Status);
    }

    [Fact]
    public async Task Logout_does_not_revoke_a_copied_cookie_which_only_a_password_change_ends()
    {
        await SetPasswordLocally();
        var copied = await LoginFromRemote();

        await Remote(HttpMethod.Delete, "/api/session", cookie: copied);

        Assert.Equal(HttpStatusCode.OK, (await Remote(HttpMethod.Get, "/api/status", cookie: copied)).Status);
        await Local(HttpMethod.Put, "/api/access-password", new { password = Changed });
        Assert.Equal(HttpStatusCode.Unauthorized, (await Remote(HttpMethod.Get, "/api/status", cookie: copied)).Status);
    }

    [Fact]
    public async Task Logout_without_a_session_succeeds()
    {
        var reply = await Remote(HttpMethod.Delete, "/api/session");

        Assert.Equal(HttpStatusCode.OK, reply.Status);
    }

    [Fact]
    public async Task A_session_is_refused_after_thirty_days_and_not_before()
    {
        await SetPasswordLocally();
        var cookie = await LoginFromRemote();

        _home.Clock.Advance(TimeSpan.FromDays(30) - TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.OK, (await Remote(HttpMethod.Get, "/api/status", cookie: cookie)).Status);

        _home.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Remote(HttpMethod.Get, "/api/status", cookie: cookie)).Status);
    }

    [Fact]
    public async Task A_session_is_not_renewed_by_use()
    {
        await SetPasswordLocally();
        var cookie = await LoginFromRemote();

        _home.Clock.Advance(TimeSpan.FromDays(20));
        Assert.Equal(HttpStatusCode.OK, (await Remote(HttpMethod.Get, "/api/status", cookie: cookie)).Status);
        _home.Clock.Advance(TimeSpan.FromDays(10) + TimeSpan.FromSeconds(1));

        Assert.Equal(HttpStatusCode.Unauthorized, (await Remote(HttpMethod.Get, "/api/status", cookie: cookie)).Status);
    }

    [Fact]
    public async Task A_cookie_that_was_changed_is_refused()
    {
        await SetPasswordLocally();
        var cookie = await LoginFromRemote();
        var dot = cookie.IndexOf('.', StringComparison.Ordinal);
        var later = $"{long.Parse(cookie[..dot]) + 3600}{cookie[dot..]}";
        var flipped = cookie[..^1] + (cookie[^1] == 'A' ? 'B' : 'A');

        Assert.Equal(HttpStatusCode.Unauthorized, (await Remote(HttpMethod.Get, "/api/status", cookie: later)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Remote(HttpMethod.Get, "/api/status", cookie: flipped)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Remote(HttpMethod.Get, "/api/status", cookie: "garbage")).Status);
    }

    [Fact]
    public async Task A_session_reaches_every_api_endpoint_the_local_connection_reaches()
    {
        await SetPasswordLocally();
        var cookie = await LoginFromRemote();

        Assert.Equal(HttpStatusCode.OK, (await Remote(HttpMethod.Get, "/api/status", cookie: cookie)).Status);
        Assert.Equal(HttpStatusCode.OK, (await Remote(HttpMethod.Post, "/api/reindex", new { }, cookie: cookie)).Status);
    }

    [Fact]
    public async Task A_session_does_not_open_mcp_to_another_device()
    {
        await SetPasswordLocally();
        var cookie = await LoginFromRemote();

        var reply = await Remote(HttpMethod.Post, "/mcp", new { }, cookie: cookie);

        Assert.Equal(HttpStatusCode.Forbidden, reply.Status);
        Assert.Equal(TacitError.McpRemoteRefused, reply.Error.Error);
    }

    [Fact]
    public async Task The_guards_still_run_before_the_session_check_for_a_remote_connection()
    {
        await SetPasswordLocally();
        var cookie = await LoginFromRemote();

        var reply = await Send(_remote, HttpMethod.Get, Url("/api/status"), cookie: cookie, origin: "https://evil.example");

        Assert.Equal(HttpStatusCode.Forbidden, reply.Status);
        Assert.Equal(TacitError.ApiOriginRefused, reply.Error.Error);
    }

    [Fact]
    public async Task A_session_survives_a_restart_of_the_host()
    {
        await SetPasswordLocally();
        var cookie = await LoginFromRemote();

        await StopAsync();
        await StartAsync();

        Assert.Equal(HttpStatusCode.OK, (await Remote(HttpMethod.Get, "/api/status", cookie: cookie)).Status);
    }

    // ---- Setting and changing the password ------------------------------------------------------------------------

    [Fact]
    public async Task A_local_set_needs_no_current_password_and_reports_the_password_as_set()
    {
        var reply = await Local(HttpMethod.Put, "/api/access-password", Body);

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        Assert.True(reply.Session.PasswordSet);
        Assert.True(File.Exists(StateFile));
    }

    [Fact]
    public async Task A_password_shorter_than_eight_characters_is_400_and_changes_nothing()
    {
        var reply = await Local(HttpMethod.Put, "/api/access-password", new { password = "1234567" });

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        Assert.Equal(TacitError.PasswordTooShort, reply.Error.Error);
        Assert.False(File.Exists(StateFile));
    }

    [Fact]
    public async Task A_remote_set_without_a_session_is_401_and_sets_nothing()
    {
        var reply = await Remote(HttpMethod.Put, "/api/access-password", new { password = Password, current = (string?)null });

        Assert.Equal(HttpStatusCode.Unauthorized, reply.Status);
        Assert.Equal(TacitError.ApiUnauthorized, reply.Error.Error);
        Assert.False(File.Exists(StateFile));
    }

    [Fact]
    public async Task A_remote_change_needs_the_current_password_and_refuses_a_wrong_one()
    {
        await SetPasswordLocally();
        var cookie = await LoginFromRemote();

        var missing = await Remote(HttpMethod.Put, "/api/access-password", new { password = Changed }, cookie);
        var wrong = await Remote(HttpMethod.Put, "/api/access-password", new { password = Changed, current = "not the password" }, cookie);

        Assert.Equal(HttpStatusCode.Unauthorized, missing.Status);
        Assert.Equal(TacitError.PasswordWrong, missing.Error.Error);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.Status);
        Assert.Equal(TacitError.PasswordWrong, wrong.Error.Error);
        Assert.Equal(HttpStatusCode.OK, (await Remote(HttpMethod.Post, "/api/session", new { password = Password })).Status);
    }

    [Fact]
    public async Task A_remote_change_with_the_current_password_gives_the_caller_a_new_session_and_ends_the_old_ones()
    {
        await SetPasswordLocally();
        var earlier = await LoginFromRemote();

        var reply = await Remote(HttpMethod.Put, "/api/access-password", new { password = Changed, current = Password }, earlier);

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        var fresh = Assert.IsType<string>(reply.SetCookieValue);
        Assert.NotEqual(earlier, fresh);
        Assert.Equal(HttpStatusCode.OK, (await Remote(HttpMethod.Get, "/api/status", cookie: fresh)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Remote(HttpMethod.Get, "/api/status", cookie: earlier)).Status);
    }

    [Fact]
    public async Task A_change_from_another_device_ends_the_sessions_of_the_other_devices()
    {
        await SetPasswordLocally();
        var first = await LoginFromRemote();
        var second = await LoginFromRemote();

        await Remote(HttpMethod.Put, "/api/access-password", new { password = Changed, current = Password }, first);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Remote(HttpMethod.Get, "/api/status", cookie: second)).Status);
    }

    [Fact]
    public async Task A_local_change_ends_every_remote_session()
    {
        await SetPasswordLocally();
        var cookie = await LoginFromRemote();

        var reply = await Local(HttpMethod.Put, "/api/access-password", new { password = Changed });

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Remote(HttpMethod.Get, "/api/status", cookie: cookie)).Status);
        Assert.Equal(HttpStatusCode.OK, (await Remote(HttpMethod.Post, "/api/session", new { password = Changed })).Status);
    }

    // ---- Clearing the password -------------------------------------------------------------------------------------

    [Fact]
    public async Task A_local_clear_returns_to_no_password_and_no_login_succeeds()
    {
        await SetPasswordLocally();

        var reply = await Local(HttpMethod.Delete, "/api/access-password");

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        Assert.False(reply.Session.PasswordSet);
        Assert.False(File.Exists(StateFile));
        Assert.Equal(TacitError.PasswordWrong, (await Remote(HttpMethod.Post, "/api/session", Body)).Error.Error);
    }

    [Fact]
    public async Task A_remote_clear_with_a_session_is_403_and_the_password_stays()
    {
        await SetPasswordLocally();
        var cookie = await LoginFromRemote();

        var reply = await Remote(HttpMethod.Delete, "/api/access-password", cookie: cookie);

        Assert.Equal(HttpStatusCode.Forbidden, reply.Status);
        Assert.Equal(TacitError.PasswordClearRemoteRefused, reply.Error.Error);
        Assert.True((await Local(HttpMethod.Get, "/api/session")).Session.PasswordSet);
        Assert.Equal(HttpStatusCode.OK, (await Remote(HttpMethod.Get, "/api/status", cookie: cookie)).Status);
    }

    [Fact]
    public async Task A_remote_clear_without_a_session_is_401()
    {
        await SetPasswordLocally();

        var reply = await Remote(HttpMethod.Delete, "/api/access-password");

        Assert.Equal(HttpStatusCode.Unauthorized, reply.Status);
        Assert.True(File.Exists(StateFile));
    }

    [Fact]
    public async Task A_clear_ends_the_sessions_that_were_issued_under_the_password()
    {
        await SetPasswordLocally();
        var cookie = await LoginFromRemote();
        await Local(HttpMethod.Delete, "/api/access-password");
        await Local(HttpMethod.Put, "/api/access-password", new { password = Password });

        Assert.Equal(HttpStatusCode.Unauthorized, (await Remote(HttpMethod.Get, "/api/status", cookie: cookie)).Status);
    }

    [Fact]
    public async Task Neither_the_password_nor_a_cookie_value_reaches_the_log()
    {
        await SetPasswordLocally();
        await Remote(HttpMethod.Post, "/api/session", new { password = "wrong one here" });
        var cookie = await LoginFromRemote();
        await Remote(HttpMethod.Put, "/api/access-password", new { password = Changed, current = Password }, cookie);

        var text = _logs.AllText;

        Assert.DoesNotContain(Password, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Changed, text, StringComparison.Ordinal);
        Assert.DoesNotContain("wrong one here", text, StringComparison.Ordinal);
        Assert.DoesNotContain(cookie, text, StringComparison.Ordinal);
    }
}
