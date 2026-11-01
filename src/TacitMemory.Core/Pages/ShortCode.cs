using System.Security.Cryptography;
using System.Text;

namespace TacitMemory.Core.Pages;

/// <summary>
/// Short codes: the first 6 hex digits of the SHA-256 of an item's normalized text. They are never written into
/// files; any change to the item gives it a new code, so a stale code is detected rather than acted on.
/// </summary>
internal static class ShortCode
{
    public const int Length = 6;

    /// <summary>Codes for <paramref name="texts"/> in order; identical texts get <c>-2</c>, <c>-3</c>… by position.</summary>
    public static IReadOnlyList<string> For(IEnumerable<string> texts)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var codes = new List<string>();
        foreach (var text in texts)
        {
            var code = Of(text);
            var count = seen[code] = seen.GetValueOrDefault(code) + 1;
            codes.Add(count == 1 ? code : $"{code}-{count}");
        }

        return codes;
    }

    /// <summary>The code of a page section: its name and its body, both as normalized by <see cref="Normalize"/>.</summary>
    public static string SectionText(string name, string body) => name.Trim() + "\n" + Normalize(body);

    /// <summary>LF line breaks and no blank lines or spaces around the text.</summary>
    public static string Normalize(string text) => text.ReplaceLineEndings("\n").Trim();

    /// <summary>The code of already-normalized text.</summary>
    internal static string Of(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..Length];
}
