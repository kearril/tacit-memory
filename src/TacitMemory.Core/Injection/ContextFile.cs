using System.Text;
using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Injection;

/// <summary>
/// Puts the resident block into a client context file and takes it out again, leaving every byte outside the block
/// as it was. The block uses the file's line endings (CRLF if the file has any, otherwise LF) and the file keeps its
/// UTF-8 byte order mark if it had one.
/// </summary>
internal static class ContextFile
{
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Writes <paramref name="block"/> (LF line breaks) into the file; <c>false</c> when the file already had it.</summary>
    /// <exception cref="TacitException">The markers are broken.</exception>
    public static bool Put(string path, string block, MemoryLanguage language)
    {
        var (text, hasBom) = Read(path);
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var rendered = block.Replace("\n", newline, StringComparison.Ordinal);

        string updated;
        if (FindBlock(path, text, language) is var (start, end))
        {
            updated = string.Concat(text.AsSpan(0, start), rendered, text.AsSpan(end));
        }
        else if (text.Length == 0)
        {
            updated = rendered + newline;
        }
        else
        {
            var body = text.EndsWith('\n') ? text : text + newline;
            updated = body + newline + rendered + newline;
        }

        if (updated == text)
            return false;
        Write(path, updated, hasBom);
        return true;
    }

    /// <summary>
    /// Removes the block, its line break and the blank line put before it. Returns the remaining text, or
    /// <c>null</c> when the file had no block.
    /// </summary>
    /// <exception cref="TacitException">The markers are broken.</exception>
    public static string? Remove(string path, MemoryLanguage language)
    {
        var (text, hasBom) = Read(path);
        if (FindBlock(path, text, language) is not var (start, end))
            return null;

        var after = text.AsSpan(end);
        if (after.StartsWith("\r\n"))
            after = after[2..];
        else if (after.StartsWith("\n"))
            after = after[1..];

        var before = text.AsSpan(0, start);
        if (before.EndsWith("\r\n\r\n"))
            before = before[..^2];
        else if (before.EndsWith("\n\n"))
            before = before[..^1];

        var updated = string.Concat(before, after);
        Write(path, updated, hasBom);
        return updated;
    }

    /// <summary>
    /// The block's span, from the start of the begin marker line to the end of the end marker (its line break
    /// excluded); <c>null</c> when there are no markers at all.
    /// </summary>
    private static (int Start, int End)? FindBlock(string path, string text, MemoryLanguage language)
    {
        var begins = new List<(int Line, int Offset)>();
        var ends = new List<(int Line, int Offset)>();
        var offset = 0;
        var lineNumber = 1;
        foreach (var line in text.Split('\n'))
        {
            var content = line.TrimEnd('\r');
            if (content.TrimStart().StartsWith(ResidentBlock.BeginMarkerPrefix, StringComparison.Ordinal))
                begins.Add((lineNumber, offset));
            else if (content.Trim() == ResidentBlock.EndMarker)
                ends.Add((lineNumber, offset + content.Length));
            offset += line.Length + 1;
            lineNumber++;
        }

        if (begins.Count == 0 && ends.Count == 0)
            return null;
        if (begins.Count == 1 && ends.Count == 1 && begins[0].Offset < ends[0].Offset)
            return (begins[0].Offset, ends[0].Offset);

        var lines = string.Join(", ", begins.Concat(ends).Select(marker => marker.Line).Order());
        throw new TacitException(TacitError.MarkersBroken, Messages.MarkersBroken.Format(language, path, lines));
    }

    private static (string Text, bool HasBom) Read(string path)
    {
        if (!File.Exists(path))
            return ("", false);
        var bytes = File.ReadAllBytes(path);
        var hasBom = bytes.AsSpan().StartsWith(Bom);
        return (Utf8.GetString(hasBom ? bytes[3..] : bytes), hasBom);
    }

    private static void Write(string path, string text, bool hasBom)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = Utf8.GetBytes(text);
        File.WriteAllBytes(path, hasBom ? [.. Bom, .. bytes] : bytes);
    }
}
