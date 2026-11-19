using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace TacitMemory.Core.Tests;

/// <summary>
/// The access password and its sessions through the facade: setting, checking, clearing and changing it, the rules of who
/// may do each, the sessions it signs, the state file that holds it, and what a damaged or missing file means.
/// </summary>
public sealed class AccessPasswordTests : IDisposable
{
    private const string Password = "correct horse";
    private static readonly IPAddress Peer = IPAddress.Parse("192.0.2.10");
    private static readonly IPAddress OtherPeer = IPAddress.Parse("192.0.2.11");

    /// <summary>A distinct address for each number, for the tests that fill the table.</summary>
    private static IPAddress NumberedPeer(int number) =>
        new(new byte[] { 10, (byte)(number >> 16), (byte)(number >> 8), (byte)number });

    private readonly TestHome _home = new();
    private readonly CapturingLoggerFactory _logs = new();

    public AccessPasswordTests() => _home.InitReady();

    public void Dispose() => _home.Dispose();

    private string StateFile => Path.Combine(_home.TacitHome, "access.json");

    private AccessControl Access(CapturingLoggerFactory? logs = null) => _home.Tacit().OpenAccess(logs ?? _logs);

    private static void RefusedWith(TacitError expected, Action act)
    {
        var error = Assert.Throws<TacitException>(act);
        Assert.Equal(expected, error.Error);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Fact]
    public void A_new_home_has_no_access_password_and_init_creates_no_state_file()
    {
        Assert.False(File.Exists(StateFile));
        Assert.False(Access().PasswordSet);
    }

    [Fact]
    public void A_set_password_is_checked_by_login_and_a_wrong_one_is_refused()
    {
        var access = Access();

        access.SetPassword(Password, current: null, local: true, Peer);

        Assert.True(access.PasswordSet);
        Assert.NotNull(access.Login(Password, Peer));
        RefusedWith(TacitError.PasswordWrong, () => access.Login("correct horsf", Peer));
        RefusedWith(TacitError.PasswordWrong, () => access.Login(Password + " ", Peer));
    }

    [Fact]
    public void A_password_shorter_than_eight_characters_is_refused_and_eight_are_accepted()
    {
        var access = Access();

        RefusedWith(TacitError.PasswordTooShort, () => access.SetPassword("1234567", current: null, local: true, Peer));
        Assert.False(access.PasswordSet);

        access.SetPassword("12345678", current: null, local: true, Peer);
        Assert.True(access.PasswordSet);
    }

    [Fact]
    public void Eight_characters_count_by_character_not_by_utf16_unit()
    {
        var access = Access();

        // Seven CJK characters plus one surrogate pair (one character, two UTF-16 units): eight characters.
        access.SetPassword("一二三四五六七😀", current: null, local: true, Peer);

        Assert.True(access.PasswordSet);
        RefusedWith(TacitError.PasswordTooShort, () => Access().SetPassword("一二三四五六😀", current: null, local: true, Peer));
    }

    [Fact]
    public void Leading_and_trailing_spaces_are_part_of_the_password()
    {
        var access = Access();

        access.SetPassword(" spaced one ", current: null, local: true, Peer);

        Assert.NotNull(access.Login(" spaced one ", Peer));
        RefusedWith(TacitError.PasswordWrong, () => access.Login("spaced one", Peer));
    }

    [Fact]
    public void A_local_change_needs_no_current_password_but_a_remote_one_must_give_it()
    {
        var access = Access();
        access.SetPassword(Password, current: null, local: true, Peer);

        access.SetPassword("second password", current: null, local: true, Peer);
        Assert.NotNull(access.Login("second password", Peer));

        RefusedWith(TacitError.PasswordWrong, () => access.SetPassword("third password", current: null, local: false, Peer));
        RefusedWith(TacitError.PasswordWrong, () => access.SetPassword("third password", current: "not it", local: false, Peer));
        Assert.NotNull(access.Login("second password", Peer));

        access.SetPassword("third password", current: "second password", local: false, Peer);
        Assert.NotNull(access.Login("third password", Peer));
    }

    [Fact]
    public void Wrong_current_passwords_from_a_remote_change_are_paused_like_wrong_logins()
    {
        var access = Access();
        access.SetPassword(Password, current: null, local: true, Peer);

        for (var i = 0; i < 5; i++)
            RefusedWith(TacitError.PasswordWrong, () => access.SetPassword("second password", current: "wrong wrong", local: false, Peer));
        RefusedWith(TacitError.LoginLimited, () => access.SetPassword("second password", current: Password, local: false, Peer));

        _home.Clock.Advance(AccessControl.LoginPause);
        var grant = access.SetPassword("second password", current: Password, local: false, Peer);
        Assert.True(access.IsSession(grant.Value));
    }

    [Fact]
    public void A_pause_that_has_ended_is_dropped_from_the_table_on_the_next_write()
    {
        var access = Access();
        for (var i = 0; i < 5; i++)
            RefusedWith(TacitError.PasswordWrong, () => access.Login("wrong wrong", Peer));
        Assert.Equal(1, access.TrackedAddresses);

        _home.Clock.Advance(AccessControl.LoginPause);
        RefusedWith(TacitError.PasswordWrong, () => access.Login("wrong wrong", OtherPeer));

        Assert.Equal(1, access.TrackedAddresses);
    }

    [Fact]
    public void An_address_whose_last_failure_is_older_than_a_pause_is_dropped_on_the_next_write()
    {
        var access = Access();
        RefusedWith(TacitError.PasswordWrong, () => access.Login("wrong wrong", Peer));

        _home.Clock.Advance(AccessControl.LoginPause);
        RefusedWith(TacitError.PasswordWrong, () => access.Login("wrong wrong", OtherPeer));

        Assert.Equal(1, access.TrackedAddresses);
    }

    [Fact]
    public void The_table_keeps_no_more_than_its_limit_of_addresses_and_drops_the_oldest()
    {
        var access = Access();
        for (var i = 0; i < AccessControl.MaximumTrackedAddresses + 50; i++)
        {
            _home.Clock.Advance(TimeSpan.FromMilliseconds(1));
            RefusedWith(TacitError.PasswordWrong, () => access.Login("wrong wrong", NumberedPeer(i)));
        }

        Assert.Equal(AccessControl.MaximumTrackedAddresses, access.TrackedAddresses);
    }

    [Fact]
    public async Task Wrong_passwords_sent_at_the_same_time_from_one_address_are_counted_before_any_is_checked()
    {
        var access = Access();
        access.SetPassword(Password, current: null, local: true, Peer);

        var refusals = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() =>
        {
            try
            {
                access.Login("wrong wrong", Peer);
                return (TacitError?)null;
            }
            catch (TacitException refusal)
            {
                return refusal.Error;
            }
        })));

        Assert.Equal(5, refusals.Count(error => error == TacitError.PasswordWrong));
        Assert.Equal(15, refusals.Count(error => error == TacitError.LoginLimited));
    }

    [Fact]
    public void A_remote_change_is_answered_with_a_new_session_that_works()
    {
        var access = Access();
        access.SetPassword(Password, current: null, local: true, Peer);

        var grant = access.SetPassword("second password", current: Password, local: false, Peer);

        Assert.True(access.IsSession(grant.Value));
    }

    [Fact]
    public void Setting_or_changing_the_password_invalidates_every_earlier_session()
    {
        var access = Access();
        access.SetPassword(Password, current: null, local: true, Peer);
        var earlier = access.Login(Password, Peer);
        Assert.True(access.IsSession(earlier.Value));

        access.SetPassword("second password", current: null, local: true, Peer);

        Assert.False(access.IsSession(earlier.Value));
    }

    [Fact]
    public void Clearing_and_setting_again_invalidates_the_sessions_of_the_first_password()
    {
        var access = Access();
        access.SetPassword(Password, current: null, local: true, Peer);
        var earlier = access.Login(Password, Peer);

        access.ClearPassword(local: true);
        access.SetPassword(Password, current: null, local: true, Peer);

        Assert.False(access.IsSession(earlier.Value));
        Assert.True(access.IsSession(access.Login(Password, Peer).Value));
    }

    [Fact]
    public void Clearing_returns_to_no_password_and_no_login_succeeds()
    {
        var access = Access();
        access.SetPassword(Password, current: null, local: true, Peer);

        access.ClearPassword(local: true);

        Assert.False(access.PasswordSet);
        Assert.False(File.Exists(StateFile));
        RefusedWith(TacitError.PasswordWrong, () => access.Login(Password, Peer));
    }

    [Fact]
    public void A_remote_connection_cannot_clear_the_password()
    {
        var access = Access();
        access.SetPassword(Password, current: null, local: true, Peer);

        RefusedWith(TacitError.PasswordClearRemoteRefused, () => access.ClearPassword(local: false));

        Assert.True(access.PasswordSet);
    }

    [Fact]
    public void Clearing_is_refused_while_server_host_names_an_address_that_is_not_loopback()
    {
        _home.InitReady("server:\n  host: 127.0.0.2\n");
        var access = Access();
        access.SetPassword(Password, current: null, local: true, Peer);

        RefusedWith(TacitError.PasswordClearRefused, () => access.ClearPassword(local: true));

        Assert.True(access.PasswordSet);
    }

    [Fact]
    public void A_password_is_never_written_in_plain_text_and_the_hash_has_salt_and_iterations()
    {
        var access = Access();

        access.SetPassword(Password, current: null, local: true, Peer);

        var text = File.ReadAllText(StateFile);
        Assert.DoesNotContain(Password, text, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(text);
        var root = json.RootElement;
        Assert.True(root.TryGetProperty("iterations", out var iterations));
        Assert.True(iterations.GetInt32() >= 100_000, "PBKDF2 needs a large iteration count.");
        Assert.True(root.EnumerateObject().Count() >= 4, "The hash, its salt, its iterations and the signing key are stored.");
    }

    [Fact]
    public void Two_sets_of_the_same_password_store_different_salts()
    {
        var access = Access();
        access.SetPassword(Password, current: null, local: true, Peer);
        var first = File.ReadAllText(StateFile);

        access.SetPassword(Password, current: null, local: true, Peer);

        Assert.NotEqual(first, File.ReadAllText(StateFile));
    }

    [Fact]
    public void The_password_survives_a_new_instance_and_so_do_its_sessions()
    {
        var first = Access();
        first.SetPassword(Password, current: null, local: true, Peer);
        var session = first.Login(Password, Peer);

        var restarted = Access();

        Assert.True(restarted.PasswordSet);
        Assert.True(restarted.IsSession(session.Value));
    }

    [Fact]
    public void A_session_lasts_thirty_days_from_login_and_is_not_renewed_by_use()
    {
        var access = Access();
        access.SetPassword(Password, current: null, local: true, Peer);
        var session = access.Login(Password, Peer);

        _home.Clock.Advance(TimeSpan.FromDays(15));
        Assert.True(access.IsSession(session.Value));
        _home.Clock.Advance(TimeSpan.FromDays(15) - TimeSpan.FromSeconds(2));
        Assert.True(access.IsSession(session.Value));
        _home.Clock.Advance(TimeSpan.FromSeconds(3));
        Assert.False(access.IsSession(session.Value));
    }

    [Fact]
    public void The_grant_states_its_expiry_thirty_days_after_login()
    {
        var access = Access();
        access.SetPassword(Password, current: null, local: true, Peer);

        var session = access.Login(Password, Peer);

        Assert.Equal(_home.Clock.GetUtcNow() + TimeSpan.FromDays(30), session.Expires);
    }

    [Fact]
    public void A_tampered_or_malformed_session_value_is_refused()
    {
        var access = Access();
        access.SetPassword(Password, current: null, local: true, Peer);
        var session = access.Login(Password, Peer).Value;
        var dot = session.IndexOf('.');
        var expiry = long.Parse(session[..dot]);
        var signature = session[(dot + 1)..];

        // A later expiry under the old signature.
        Assert.False(access.IsSession($"{expiry + 3600}.{signature}"));
        // The signature with its last character changed.
        var flipped = signature[..^1] + (signature[^1] == 'A' ? 'B' : 'A');
        Assert.False(access.IsSession($"{expiry}.{flipped}"));
        Assert.False(access.IsSession($"{expiry}"));
        Assert.False(access.IsSession("not a session"));
        Assert.False(access.IsSession(""));
        Assert.False(access.IsSession(null));
        Assert.True(access.IsSession(session));
    }

    [Fact]
    public void A_session_is_not_valid_when_no_password_is_set()
    {
        var access = Access();
        access.SetPassword(Password, current: null, local: true, Peer);
        var session = access.Login(Password, Peer).Value;
        access.ClearPassword(local: true);

        Assert.False(Access().IsSession(session));
    }

    [Fact]
    public void A_damaged_state_file_counts_as_no_password_and_logs_a_warning()
    {
        File.WriteAllText(StateFile, "{ this is not the state file");

        var access = Access();

        Assert.False(access.PasswordSet);
        var warning = Assert.Single(_logs.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.DoesNotContain(Password, warning.Message, StringComparison.Ordinal);
        RefusedWith(TacitError.PasswordWrong, () => access.Login(Password, Peer));
    }

    [Fact]
    public void A_state_file_with_a_missing_member_counts_as_no_password()
    {
        File.WriteAllText(StateFile, """{"iterations": 600000}""");

        Assert.False(Access().PasswordSet);
    }

    [Fact]
    public void A_damaged_file_is_replaced_by_the_next_set()
    {
        File.WriteAllText(StateFile, "garbage");
        var access = Access();

        access.SetPassword(Password, current: null, local: true, Peer);

        Assert.True(Access().PasswordSet);
        Assert.NotNull(Access().Login(Password, Peer));
    }

    [Fact]
    public async Task A_manual_backup_does_not_contain_the_state_file()
    {
        Access().SetPassword(Password, current: null, local: true, Peer);
        using var service = _home.Tacit().OpenService(_ => { }, _logs);

        var result = await service.Maintenance.BackupAsync(BackupTarget.Local);

        using var zip = ZipFile.OpenRead(result.Location);
        Assert.DoesNotContain(zip.Entries, entry => entry.FullName.Contains("access.json", StringComparison.Ordinal));
    }

    [Fact]
    public void A_service_with_server_host_beside_loopback_is_refused_to_start_until_a_password_is_set()
    {
        _home.InitReady("server:\n  host: 127.0.0.2\n");
        using var service = _home.Tacit().OpenService(_ => { }, _logs);

        var refusal = Assert.Throws<TacitException>(service.CheckExtraListenerAllowed);
        Assert.Equal(TacitError.ConfigInvalid, refusal.Error);
        Assert.Contains("server.host", refusal.Message, StringComparison.Ordinal);

        service.Access.SetPassword(Password, current: null, local: true, Peer);
        service.CheckExtraListenerAllowed();
    }

    [Fact]
    public void A_service_without_server_host_needs_no_password_to_start()
    {
        using var service = _home.Tacit().OpenService(_ => { }, _logs);

        service.CheckExtraListenerAllowed();
    }

    [Fact]
    public void Setting_and_clearing_the_password_leave_config_yaml_alone()
    {
        var before = File.ReadAllText(Path.Combine(_home.TacitHome, "config.yaml"));
        var access = Access();

        access.SetPassword(Password, current: null, local: true, Peer);
        access.ClearPassword(local: true);

        Assert.Equal(before, File.ReadAllText(Path.Combine(_home.TacitHome, "config.yaml")));
    }
}
