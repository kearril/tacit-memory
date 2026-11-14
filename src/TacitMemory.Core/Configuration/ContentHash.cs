using System.Security.Cryptography;
using System.Text;

namespace TacitMemory.Core.Configuration;

/// <summary>
/// The hash that names the content of a file a page edits: <c>config.yaml</c> or a prompt document. A save names the hash
/// it read, and is refused once the file holds something else.
/// </summary>
internal static class ContentHash
{
    public static string Of(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
