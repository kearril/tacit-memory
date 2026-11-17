namespace TacitMemory.Core.Localization;

/// <summary>The <c>language</c> config values, and the language used before a config names one.</summary>
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

    /// <summary>
    /// The language of what is said before a config names one: a missing or unreadable config, or one without the key.
    /// English, as the command line's help and usage errors are.
    /// </summary>
    public const MemoryLanguage WithoutConfig = MemoryLanguage.En;
}
