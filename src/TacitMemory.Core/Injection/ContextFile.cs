using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Injection;

/// <summary>
/// Puts the resident block into a client context file and takes it out again, leaving every byte outside the block
/// as it was. The block uses the file's line endings.
/// </summary>
/// <remarks>
/// Appending puts one blank line before the block. A file that ended without a line break gets one before that blank
/// line, and the block then also ends without one, so removal can tell the two cases apart and restore either.
/// </remarks>
internal static class ContextFile
{
    /// <summary>Writes <paramref name="block"/> (LF line breaks) into the file; <c>false</c> when the file already had it.</summary>
    /// <exception cref="TacitException">The file is not UTF-8, or its markers are broken.</exception>
    public static bool Put(string path, string block, MemoryLanguage language)
    {
        var (text, hasBom) = ReadOrRefuse(path, language);
        var newline = TextFile.NewlineOf(text);
        var rendered = block.Replace("\n", newline, StringComparison.Ordinal);

        string updated;
        if (FindBlock(path, text, language) is var (start, end))
            updated = string.Concat(text.AsSpan(0, start), rendered, text.AsSpan(end));
        else if (text.Length == 0)
            updated = rendered + newline;
        else if (text.EndsWith('\n'))
            updated = text + newline + rendered + newline;
        else
            updated = text + newline + newline + rendered;

        if (updated == text)
            return false;
        TextFile.Write(path, updated, hasBom);
        return true;
    }

    /// <summary>
    /// Removes the block with the line breaks put around it. Returns the remaining text, or <c>null</c> when the file
    /// had no block.
    /// </summary>
    /// <exception cref="TacitException">The file is not UTF-8, or its markers are broken.</exception>
    public static string? Remove(string path, MemoryLanguage language)
    {
        var (text, hasBom) = ReadOrRefuse(path, language);
        if (FindBlock(path, text, language) is not var (start, end))
            return null;

        var before = text[..start];
        var after = text[end..];
        if (after.Length == 0)
        {
            // Appended to a file without a final line break: drop both breaks added before the block.
            before = before.EndsWith("\r\n\r\n", StringComparison.Ordinal) ? before[..^4]
                : before.EndsWith("\n\n", StringComparison.Ordinal) ? before[..^2]
                : before;
        }
        else
        {
            after = after.StartsWith("\r\n", StringComparison.Ordinal) ? after[2..] : after.StartsWith('\n') ? after[1..] : after;
            before = before.EndsWith("\r\n\r\n", StringComparison.Ordinal) ? before[..^2]
                : before.EndsWith("\n\n", StringComparison.Ordinal) ? before[..^1]
                : before;
        }

        var updated = before + after;
        TextFile.Write(path, updated, hasBom);
        return updated;
    }

    private static (string Text, bool HasBom) ReadOrRefuse(string path, MemoryLanguage language) =>
        TextFile.Read(path) ?? throw new TacitException(TacitError.FileNotUtf8, Messages.FileNotUtf8.Format(language, path));

    /// <summary>
    /// The block's span, from the start of the begin marker line to the end of the end marker (its line break
    /// excluded); <c>null</c> when there are no markers at all.
    /// </summary>
    internal static (int Start, int End)? FindBlock(string path, string text, MemoryLanguage language)
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
}
