using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Logging;

namespace TacitMemory.Core;

/// <summary>A session the service issued: the cookie value, and the instant it stops being valid.</summary>
public sealed record SessionGrant(string Value, DateTimeOffset Expires);

/// <summary>
/// The access password: whether one is set, setting, changing and clearing it, and the sessions a correct login issues.
/// The state lives in <c>access.json</c> under <c>TACIT_HOME</c>, which is not configuration: it holds the password's salted
/// PBKDF2 hash and the key that signs sessions. The file is read on every call, so every instance sees the same state.
/// A missing or damaged file means no password; a damaged one is logged once when the instance is opened.
/// </summary>
public sealed class AccessControl
{
    /// <summary>The fewest characters an access password may have; counted as Unicode characters, not UTF-16 units.</summary>
    internal const int MinimumLength = 8;

    /// <summary>How long a session lasts from the login that issued it. Sessions are not renewed by use.</summary>
    internal static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);

    /// <summary>The wrong passwords from one address that pause logins from it.</summary>
    internal const int FailuresAllowed = 5;

    /// <summary>How long logins from an address wait after <see cref="FailuresAllowed"/> wrong passwords.</summary>
    internal static readonly TimeSpan LoginPause = TimeSpan.FromSeconds(60);

    /// <summary>The most addresses whose wrong passwords are kept at once; the oldest are dropped beyond it.</summary>
    internal const int MaximumTrackedAddresses = 1024;

    /// <summary>The name of the state file under <c>TACIT_HOME</c>.</summary>
    internal const string FileName = "access.json";

    private const int Iterations = 600_000;
    private const int MinimumIterations = 100_000;
    private const int MaximumIterations = 10_000_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int KeyBytes = 32;

    private readonly TacitOptions _options;
    private readonly TacitConfig _config;
    private readonly ServiceLog _log;
    private readonly string _path;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Failures> _failures = [];

    internal AccessControl(TacitOptions options, TacitConfig config, ILogger logger)
    {
        _options = options;
        _config = config;
        _log = new ServiceLog(logger, config.Language);
        _path = Path.Combine(options.TacitHome, FileName);
        if (File.Exists(_path) && ReadState() is null)
            _log.Record(LogLevel.Warning, AccessMessages.StateDamaged, _path);
    }

    /// <summary>The addresses whose wrong passwords are kept now.</summary>
    internal int TrackedAddresses
    {
        get
        {
            lock (_gate)
                return _failures.Count;
        }
    }

    /// <summary>Whether an access password is set. A missing or damaged state file means it is not.</summary>
    public bool PasswordSet => ReadState() is not null;

    /// <summary>
    /// Checks <paramref name="password"/> against the set one and, when it is right, issues a session. A wrong password, or
    /// no password at all, is refused and counted against <paramref name="peer"/>. After <see cref="FailuresAllowed"/> wrong
    /// passwords in a row the address is paused for <see cref="LoginPause"/>, and its attempts are refused without a check.
    /// </summary>
    /// <exception cref="TacitException">The password is wrong, or logins from the address are paused.</exception>
    public SessionGrant Login(string password, IPAddress? peer)
    {
        var address = Address(peer);
        Reserve(address);
        var state = ReadState();
        if (state is null || !Matches(password, state))
            throw WrongPassword();
        Forget(address);
        return Issue(state);
    }

    /// <summary>
    /// Sets the access password, or changes it. A connection from this machine needs no current password. One from another
    /// device must give the current one, which must be right. Every session issued before is invalid afterwards: the signing
    /// key changes with the password. Returns the session of the caller, which only a remote caller needs.
    /// </summary>
    /// <param name="password">The new password, with its spaces; at least <see cref="MinimumLength"/> characters.</param>
    /// <param name="current">The password in use now, given by a remote caller; ignored for a local one.</param>
    /// <param name="local">Whether the request comes from this machine.</param>
    /// <param name="peer">The address the request comes from; a remote caller's wrong current passwords are counted against it.</param>
    /// <exception cref="TacitException">The current password is missing or wrong, logins from the address are paused, or the new one is too short.</exception>
    public SessionGrant SetPassword(string password, string? current, bool local, IPAddress? peer)
    {
        if (!local)
        {
            var address = Address(peer);
            Reserve(address);
            var existing = ReadState();
            if (existing is null || current is null || !Matches(current, existing))
                throw WrongPassword();
            Forget(address);
        }
        if (password.EnumerateRunes().Count() < MinimumLength)
            throw new TacitException(TacitError.PasswordTooShort, AccessMessages.PasswordTooShort.In(_config.Language));

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var state = new State(Hash(password, salt, Iterations), salt, Iterations, RandomNumberGenerator.GetBytes(KeyBytes));
        lock (_gate)
            Write(state);
        return Issue(state);
    }

    /// <summary>
    /// Clears the access password, so the state file is removed and no session is valid any more. Only a connection from
    /// this machine may clear it, and only while <c>server.host</c> names no address that is not loopback.
    /// </summary>
    /// <exception cref="TacitException">The request is from another device, or an extra listener is configured.</exception>
    public void ClearPassword(bool local)
    {
        lock (_gate)
        {
            if (!local)
                throw new TacitException(TacitError.PasswordClearRemoteRefused, AccessMessages.PasswordClearRemoteRefused.In(_config.Language));
            if (_config.ExtraAddress is { } extra)
                throw new TacitException(
                    TacitError.PasswordClearRefused,
                    AccessMessages.PasswordClearRefused.Format(_config.Language, extra));
            if (File.Exists(_path))
                File.Delete(_path);
        }
    }

    /// <summary>
    /// Whether <paramref name="value"/> is a session this service issued under the password now set, and has not expired.
    /// A value that is malformed, altered, or signed under an earlier password is not.
    /// </summary>
    public bool IsSession(string? value)
    {
        if (string.IsNullOrEmpty(value) || ReadState() is not { } state)
            return false;
        var dot = value.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0 || dot == value.Length - 1)
            return false;
        if (!long.TryParse(value.AsSpan(0, dot), NumberStyles.None, CultureInfo.InvariantCulture, out var expiry))
            return false;
        if (!TryFromHex(value[(dot + 1)..], out var signature))
            return false;
        var expected = Sign(state, expiry);
        if (!CryptographicOperations.FixedTimeEquals(signature, expected))
            return false;
        return _options.Time.GetUtcNow() < DateTimeOffset.FromUnixTimeSeconds(expiry);
    }

    private SessionGrant Issue(State state)
    {
        var expires = DateTimeOffset.FromUnixTimeSeconds((_options.Time.GetUtcNow() + SessionLifetime).ToUnixTimeSeconds());
        var expiry = expires.ToUnixTimeSeconds();
        return new SessionGrant($"{expiry.ToString(CultureInfo.InvariantCulture)}.{Convert.ToHexString(Sign(state, expiry))}", expires);
    }

    /// <summary>
    /// Refuses an attempt from a paused address; otherwise counts the attempt as a failure before its password is checked.
    /// Counting first, under the lock, keeps attempts that run at the same time from all being checked before any is counted.
    /// A correct password takes its count back with <see cref="Forget"/>.
    /// </summary>
    private void Reserve(string address)
    {
        lock (_gate)
        {
            var now = _options.Time.GetUtcNow();
            Prune(now);
            if (_failures.TryGetValue(address, out var paused) && paused.PausedUntil is { } until)
                throw LoginLimited(until - now);

            var count = (_failures.TryGetValue(address, out var counted) ? counted.Count : 0) + 1;
            _failures[address] = count >= FailuresAllowed
                ? new Failures(0, now, now + LoginPause)
                : new Failures(count, now, null);
            Prune(now);
        }
    }

    /// <summary>Takes back the count of an address whose password was right.</summary>
    private void Forget(string address)
    {
        lock (_gate)
        {
            Prune(_options.Time.GetUtcNow());
            _failures.Remove(address);
        }
    }

    /// <summary>
    /// Drops the entries no longer needed: those whose pause has ended, and those whose last failure is at least a pause old.
    /// Then the oldest entries go while more than <see cref="MaximumTrackedAddresses"/> remain. Every write does this under the lock.
    /// </summary>
    private void Prune(DateTimeOffset now)
    {
        foreach (var (address, failures) in _failures.ToArray())
        {
            var expired = failures.PausedUntil is { } until ? now >= until : now - failures.LastFailure >= LoginPause;
            if (expired)
                _failures.Remove(address);
        }
        while (_failures.Count > MaximumTrackedAddresses)
            _failures.Remove(_failures.MinBy(entry => entry.Value.LastFailure).Key);
    }

    private TacitException WrongPassword() =>
        new(TacitError.PasswordWrong, AccessMessages.PasswordWrong.In(_config.Language));

    /// <summary>The refusal of a login from a paused address; it says the whole seconds left, and carries them for <c>Retry-After</c>.</summary>
    private TacitException LoginLimited(TimeSpan remaining)
    {
        var seconds = (int)Math.Ceiling(remaining.TotalSeconds);
        return new TacitException(TacitError.LoginLimited, AccessMessages.LoginLimited.Format(_config.Language, seconds)) { RetryAfterSeconds = seconds };
    }

    private static bool Matches(string password, State state) =>
        CryptographicOperations.FixedTimeEquals(Hash(password, state.Salt, state.Iterations), state.Hash);

    private static byte[] Hash(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, HashBytes);

    private static byte[] Sign(State state, long expiry) =>
        HMACSHA256.HashData(state.Key, Encoding.UTF8.GetBytes(expiry.ToString(CultureInfo.InvariantCulture)));

    private static bool TryFromHex(string text, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromHexString(text);
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }

    /// <summary>
    /// The state the file holds, decoded; <c>null</c> when the file is missing, cannot be read, or is not a complete state.
    /// </summary>
    private State? ReadState()
    {
        if (!File.Exists(_path))
            return null;
        try
        {
            var stored = JsonSerializer.Deserialize(File.ReadAllText(_path), StateJson.For<StoredState>());
            if (stored?.PasswordHash is null || stored.Salt is null || stored.SigningKey is null || stored.Iterations is not { } iterations)
                return null;
            if (iterations is < MinimumIterations or > MaximumIterations)
                return null;
            var hash = Decode(stored.PasswordHash, HashBytes);
            var salt = Decode(stored.Salt, SaltBytes);
            var key = Decode(stored.SigningKey, KeyBytes);
            return hash is null || salt is null || key is null ? null : new State(hash, salt, iterations, key);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The bytes of a base64 member when they decode to exactly <paramref name="length"/> bytes; otherwise <c>null</c>.</summary>
    private static byte[]? Decode(string base64, int length)
    {
        try
        {
            var bytes = Convert.FromBase64String(base64);
            return bytes.Length == length ? bytes : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private void Write(State state)
    {
        var stored = new StoredState(
            Convert.ToBase64String(state.Hash), Convert.ToBase64String(state.Salt), state.Iterations, Convert.ToBase64String(state.Key));
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(stored, StateJson.For<StoredState>()));
        File.Move(temporary, _path, overwrite: true);
    }

    private static string Address(IPAddress? peer) => peer?.ToString() ?? "";

    private static readonly JsonSerializerOptions StateJson = new(JsonSerializerDefaults.Web) { WriteIndented = true, TypeInfoResolver = CoreJson.Default };

    /// <summary>The state as the file holds it. Each member is nullable, so a file missing one reads as no state, not as an exception.</summary>
    internal sealed record StoredState(string? PasswordHash, string? Salt, int? Iterations, string? SigningKey);

    /// <summary>The decoded state: the password's hash and salt with their iterations, and the key that signs sessions.</summary>
    private sealed record State(byte[] Hash, byte[] Salt, int Iterations, byte[] Key);

    /// <summary>
    /// The wrong passwords counted from one address, when the last one was, and until when its logins are paused, if they are.
    /// </summary>
    private sealed record Failures(int Count, DateTimeOffset LastFailure, DateTimeOffset? PausedUntil);
}
