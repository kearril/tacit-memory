using System.Security.Cryptography;
using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Index;

/// <summary>
/// The sqlite-vec loadable extension, shipped next to the program as the official v0.1.9 Windows x64 release. It is
/// native code loaded into the process, so it is checked against the hash recorded here before every load.
/// </summary>
internal static class VectorExtension
{
    public const string Version = "0.1.9";

    /// <summary>SHA-256 of <c>vec0.dll</c> from <c>sqlite-vec-0.1.9-loadable-windows-x86_64.tar.gz</c>.</summary>
    public const string Sha256 = "fcf98662a7ad9dce394b96a88f91032047823831b951c76636787c312a6476e6";

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "vec0.dll");

    /// <summary>Refuses to go on when the file is missing or is not the recorded release.</summary>
    public static void Verify(MemoryLanguage language)
    {
        var actual = File.Exists(FilePath) ? Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(FilePath))) : null;
        if (actual != Sha256)
        {
            throw new TacitException(
                TacitError.VectorExtensionInvalid,
                Messages.VectorExtensionInvalid.Format(language, FilePath, Version));
        }
    }
}
