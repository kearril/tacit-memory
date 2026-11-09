using System.Text;

namespace TacitMemory.Core.Injection;

/// <summary>
/// Reads and writes user-owned text files without disturbing them: strict UTF-8 (anything else is refused rather
/// than mangled), the byte order mark kept as found, and writes that replace the file in one step so a failure never
/// leaves it half written.
/// </summary>
internal static class TextFile
{
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>The text and whether it had a BOM; <c>null</c> when the file is not valid UTF-8. A missing file reads as empty.</summary>
    public static (string Text, bool HasBom)? Read(string path)
    {
        if (!File.Exists(path))
            return ("", false);
        var bytes = ReadSharedBytes(path);
        var hasBom = bytes.AsSpan().StartsWith(Bom);
        try
        {
            return (StrictUtf8.GetString(hasBom ? bytes[3..] : bytes), hasBom);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    /// <summary>
    /// The file's bytes, read while other processes may still write, replace or delete it. A plain read denies writers
    /// for its duration, so a background read of the repository would refuse a manual edit or a write from outside the
    /// service while it runs.
    /// </summary>
    public static byte[] ReadSharedBytes(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>
    /// Replaces the file in one step. The temporary file has a name of its own, so two writers of the same file (a render
    /// and a manual <c>connect</c>, say) never share one, and it is removed if the replacement fails.
    /// </summary>
    public static void Write(string path, string text, bool hasBom)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = StrictUtf8.GetBytes(text);
        var temporary = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tacit-tmp";
        try
        {
            File.WriteAllBytes(temporary, hasBom ? [.. Bom, .. bytes] : bytes);
            Replace(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    /// <summary>
    /// Moves the temporary file over the target. Windows refuses the replacement while another process holds the target
    /// open, so the move is tried a few times, a short pause apart, before the failure is thrown.
    /// </summary>
    private static void Replace(string temporary, string path)
    {
        const int attempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temporary, path, overwrite: true);
                return;
            }
            catch (Exception e) when ((e is IOException or UnauthorizedAccessException) && attempt < attempts)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(50));
            }
        }
    }

    /// <summary>The file's line break: CRLF if it has any, otherwise LF.</summary>
    public static string NewlineOf(string text) => text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    /// <summary>The carriage return written before each line feed in <paramref name="text"/>'s style: <c>\r</c> for CRLF, otherwise nothing.</summary>
    public static string CarriageReturnOf(string text) => NewlineOf(text) == "\r\n" ? "\r" : "";
}
