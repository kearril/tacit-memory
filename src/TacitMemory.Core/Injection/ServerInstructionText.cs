namespace TacitMemory.Core.Injection;

/// <summary>
/// The server instructions, assembled from parts. Fixed parts (opening, memory rules, language norms) are never cut;
/// variable parts drop items from the end until the whole text fits <see cref="TotalLimit"/>, and vanish when nothing
/// of them fits.
/// </summary>
internal static class ServerInstructionText
{
    public const int TotalLimit = 4000;

    private const string Opening = """
        TacitMemory is the user's personal memory across sessions. The user profile is already in your context inside <user_memory>.
        """;

    private const string UsingMemory = """
        ## Using memory
        - Keep what you know from memory apart from what you infer from the profile; never present a guess as something you remember.
        - Do not bring up past events the user has not mentioned.
        - When the user's words contradict a profile item, follow what they say now and ask whether to update the memory; do not change it on your own.
        """;

    public static string Build(MemoryLanguage language, IReadOnlyList<string> projects)
    {
        var fixedText = Join(
        [
            Opening,
            UsingMemory,
            $"""
            ## Language
            Memory is written in {LanguageName(language)}. When you quote or record memory, keep the user's words, code, paths, commands and proper nouns as they are.
            """,
        ]);

        var kept = projects.ToList();
        while (true)
        {
            var text = kept.Count == 0 ? fixedText : Join([fixedText, "## Projects\n" + string.Join(", ", kept)]);
            if (text.Length <= TotalLimit || kept.Count == 0)
                return text;
            kept.RemoveAt(kept.Count - 1);
        }
    }

    /// <summary>Project names: directories under <c>pages/</c> that do not start with an underscore or a dot.</summary>
    public static IReadOnlyList<string> Projects(string memoryPath)
    {
        var pages = Path.Combine(memoryPath, "pages");
        if (!Directory.Exists(pages))
            return [];
        return Directory.EnumerateDirectories(pages)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => !name.StartsWith('_') && !name.StartsWith('.'))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static string Join(IEnumerable<string> parts) => string.Join("\n\n", parts.Select(part => part.ReplaceLineEndings("\n").Trim()));

    private static string LanguageName(MemoryLanguage language) => language switch
    {
        MemoryLanguage.Zh => "Chinese",
        MemoryLanguage.En => "English",
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, null),
    };
}
