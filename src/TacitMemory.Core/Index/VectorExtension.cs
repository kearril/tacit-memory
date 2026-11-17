using System.Runtime.InteropServices;
using System.Security.Cryptography;
using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Index;

/// <summary>
/// The sqlite-vec loadable extension, shipped next to the program as the official v0.1.9 release for its platform. It is
/// native code loaded into the process, so it is checked against the hash recorded here before every load.
/// </summary>
internal static class VectorExtension
{
    public const string Version = "0.1.9";

    /// <summary>
    /// The file name and SHA-256 of the extension in <c>sqlite-vec-0.1.9-loadable-&lt;platform&gt;.tar.gz</c>, for each
    /// runtime the release is built for.
    /// </summary>
    private static readonly Dictionary<string, (string FileName, string Sha256)> Releases = new()
    {
        ["win-x64"] = ("vec0.dll", "fcf98662a7ad9dce394b96a88f91032047823831b951c76636787c312a6476e6"),
        ["osx-arm64"] = ("vec0.dylib", "193e480c50b59a55977d166f4aaf0e1bc8832d6963516e5950f39e4d2ce0b793"),
        ["linux-x64"] = ("vec0.so", "5923730861b86c707cca5602b5f91092f9e52a46706dbc6e269fd4bb9c4498e8"),
    };

    /// <summary>The release for this process's runtime, or <c>null</c> on a runtime that has none.</summary>
    private static readonly (string FileName, string Sha256)? Release =
        Releases.TryGetValue(RuntimeInformation.RuntimeIdentifier, out var release) ? release : null;

    /// <summary>Where the extension is loaded from; on a runtime with no release, the bare name, which never exists.</summary>
    public static string FilePath => Path.Combine(AppContext.BaseDirectory, Release?.FileName ?? "vec0");

    /// <summary>Refuses to go on when the file is missing or is not the recorded release.</summary>
    public static void Verify(MemoryLanguage language)
    {
        var actual = File.Exists(FilePath) ? Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(FilePath))) : null;
        if (actual is null || actual != Release?.Sha256)
        {
            throw new TacitException(
                TacitError.VectorExtensionInvalid,
                Messages.VectorExtensionInvalid.Format(language, FilePath, Version));
        }
    }
}
