using System.Text.RegularExpressions;

namespace TacitMemory.Core.Clients;

/// <summary>A client's name as the user types it: letters, digits and hyphens, up to 32 characters, stored in lower case.</summary>
internal static partial class ClientName
{
    /// <summary>The name in lower case. The message is English, as usage errors are.</summary>
    /// <exception cref="TacitException">The name is empty, longer than 32 characters, or holds any other character.</exception>
    public static string Normalize(string? name)
    {
        var lower = (name ?? "").ToLowerInvariant();
        if (Valid().IsMatch(lower))
            return lower;
        throw new TacitException(
            TacitError.ClientNameInvalid,
            $"The client name '{name}' is not valid: use only letters, digits and hyphens, up to 32 characters.");
    }

    [GeneratedRegex(@"\A[a-z0-9-]{1,32}\z")]
    private static partial Regex Valid();
}
