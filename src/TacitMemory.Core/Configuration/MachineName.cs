using System.Text.RegularExpressions;

namespace TacitMemory.Core.Configuration;

/// <summary>
/// A machine's identifier: lowercase letters, digits and hyphens, 1 to 32 characters, derived from the host name when not
/// configured.
/// </summary>
internal static partial class MachineName
{
    public const int MaxLength = 32;

    public const string Fallback = "machine";

    /// <summary>Whether <paramref name="value"/> is a valid configured or written machine name.</summary>
    public static bool IsValid(string value) => Valid().IsMatch(value);

    /// <summary>
    /// Normalizes a host name to a valid machine name: strips any domain suffix from the first dot, lowers case, replaces
    /// invalid characters with hyphens, collapses and trims hyphens, caps at 32 characters and falls back to <c>machine</c>.
    /// </summary>
    public static string FromHost(string? hostName)
    {
        var head = (hostName ?? "").Trim();
        var dot = head.IndexOf('.');
        if (dot >= 0)
            head = head[..dot];
        var cleaned = NonName().Replace(head.ToLowerInvariant(), "-");
        var collapsed = Hyphens().Replace(cleaned, "-").Trim('-');
        if (collapsed.Length > MaxLength)
            collapsed = collapsed[..MaxLength].TrimEnd('-');
        return collapsed.Length == 0 ? Fallback : collapsed;
    }

    [GeneratedRegex(@"\A[a-z0-9-]{1,32}\z")]
    private static partial Regex Valid();

    [GeneratedRegex(@"[^a-z0-9-]+")]
    private static partial Regex NonName();

    [GeneratedRegex(@"-{2,}")]
    private static partial Regex Hyphens();
}
