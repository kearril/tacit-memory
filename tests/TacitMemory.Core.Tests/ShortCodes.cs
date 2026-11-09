using System.Security.Cryptography;
using System.Text;

namespace TacitMemory.Core.Tests;

/// <summary>
/// The short code a text carries, as the specification states it: the first six hex digits of the SHA-256 of the text with
/// its line breaks normalized and its ends trimmed. The tests share this one definition, so a change to it is seen once.
/// </summary>
internal static class ShortCodes
{
    public static string ShortOf(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\n").Trim())))[..6].ToLowerInvariant();
}
