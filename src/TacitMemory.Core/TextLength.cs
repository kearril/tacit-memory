using System.Globalization;

namespace TacitMemory.Core;

/// <summary>The length the memory limits count: text elements, so a character made of several code points counts once.</summary>
internal static class TextLength
{
    public static int Of(string text) => new StringInfo(text).LengthInTextElements;
}
