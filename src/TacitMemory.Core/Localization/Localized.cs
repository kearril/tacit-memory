using System.Globalization;

namespace TacitMemory.Core.Localization;

/// <summary>
/// One piece of human-facing text in every supported memory language. Both translations are constructor arguments,
/// so a missing one does not compile.
/// </summary>
internal sealed record Localized(string Zh, string En)
{
    public string In(MemoryLanguage language) => language switch
    {
        MemoryLanguage.Zh => Zh,
        MemoryLanguage.En => En,
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, null),
    };

    /// <summary>Whether a heading or name is this word in either language; English matches without regard to case.</summary>
    public bool Matches(string text) => text == Zh || string.Equals(text, En, StringComparison.OrdinalIgnoreCase);

    public string Format(MemoryLanguage language, params object?[] args) =>
        string.Format(CultureInfo.InvariantCulture, In(language), args);
}
