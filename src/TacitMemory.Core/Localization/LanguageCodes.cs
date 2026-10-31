using System.Globalization;

namespace TacitMemory.Core.Localization;

/// <summary>The <c>language</c> config values and how a system UI culture maps onto them.</summary>
internal static class LanguageCodes
{
    public static string Code(this MemoryLanguage language) => language switch
    {
        MemoryLanguage.Zh => "zh",
        MemoryLanguage.En => "en",
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, null),
    };

    public static MemoryLanguage? Parse(string? code) => code switch
    {
        "zh" => MemoryLanguage.Zh,
        "en" => MemoryLanguage.En,
        _ => null,
    };

    /// <summary>Chinese UI of any region means <c>zh</c>; everything else <c>en</c>.</summary>
    public static MemoryLanguage FromUiCulture(CultureInfo culture) =>
        culture.TwoLetterISOLanguageName == "zh" ? MemoryLanguage.Zh : MemoryLanguage.En;
}
