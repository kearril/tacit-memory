using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Injection;

/// <summary>
/// The server instructions, assembled from parts. Fixed parts (opening, memory rules, language norms, recording
/// criteria) are never cut and must fit <see cref="FixedLimit"/>; the status part is short and always whole; the
/// project list drops names from the end until the whole text fits <see cref="TotalLimit"/>, and vanishes when none
/// fits.
/// </summary>
internal static class ServerInstructionText
{
    public const int TotalLimit = 4000;

    public const int FixedLimit = 2500;

    private const string Opening = """
        TacitMemory is the user's personal memory across sessions. The user's persona is already in your context inside <user_memory>; details live in pages you reach with `recall`.
        """;

    private const string UsingMemory = """
        ## Using memory
        - Keep what you know from memory apart from what you infer from the persona; never present a guess as something you remember.
        - Do not bring up past events the user has not mentioned.
        - When the user's words contradict a persona item, follow what they say now and ask whether to update the memory; do not change it on your own.
        """;

    private const string Recalling = """
        ## When to recall
        - Call `recall` when the user mentions something from the past, or names a person, project or term that is not in your context.
        - Call `recall` with the project name before starting work on a project.
        - When `recall` finds nothing, say you do not remember; do not guess.
        """;

    private const string Writing = """
        ## Writing pages
        - When something worth recording happens, `recall` it first; if a page already covers the matter, update that page instead of creating one.
        - One page per matter. Write the date and, where the wording matters, the user's own words.
        - Never write passwords, API keys, tokens or other secrets.
        - When `write_page` refuses, do what its "Next step" says.
        """;

    /// <summary>The fixed parts, which every session gets in full.</summary>
    public static string Fixed(MemoryLanguage language, RecordingCriteria criteria) => Join(
    [
        Opening,
        UsingMemory,
        Recalling,
        Writing,
        $"""
        ## Language
        Memory is written in {LanguageName(language)}. Write `recall` queries in {LanguageName(language)} too. When you quote or record memory, keep the user's words, code, paths, commands and proper nouns as they are.
        """,
        Criteria(criteria),
    ]);

    /// <summary>Refuses criteria that would push the fixed parts past <see cref="FixedLimit"/>.</summary>
    public static void Check(MemoryLanguage language, RecordingCriteria criteria, string rulesPath)
    {
        var length = Fixed(language, criteria).Length;
        if (length > FixedLimit)
            throw new TacitException(TacitError.RulesInvalid, Messages.InstructionsTooLong.Format(
                language, rulesPath, length, FixedLimit, length - FixedLimit));
    }

    /// <param name="unavailableModels">Auxiliary models whose last call failed.</param>
    /// <param name="skippedFiles">Page files left out of the index because they are not UTF-8.</param>
    public static string Build(
        MemoryLanguage language,
        RecordingCriteria criteria,
        IReadOnlyList<string> unavailableModels,
        IReadOnlyList<string> skippedFiles,
        IReadOnlyList<string> projects)
    {
        var parts = new List<string> { Fixed(language, criteria) };
        var status = new List<string>();
        if (unavailableModels.Count > 0)
            status.Add($"- Unavailable auxiliary models: {string.Join(", ", unavailableModels)}. Memory tools still work, with weaker search and checks.");
        if (skippedFiles.Count > 0)
            status.Add($"- Pages not searchable because they are not UTF-8: {string.Join(", ", skippedFiles)}.");
        if (status.Count > 0)
            parts.Add("## Status\n" + string.Join("\n", status));
        var head = Join(parts);

        var kept = projects.ToList();
        while (true)
        {
            var text = kept.Count == 0 ? head : Join([head, "## Projects\n" + string.Join(", ", kept)]);
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

    /// <summary>The two sections of <c>rules.md</c>, items word for word; an empty section is left out.</summary>
    private static string Criteria(RecordingCriteria criteria) => Join(
        new[] { ("## Worth recording", criteria.WorthRecording), ("## Not recorded", criteria.NotRecorded) }
            .Where(section => section.Item2.Count > 0)
            .Select(section => section.Item1 + "\n" + string.Join("\n", section.Item2.Select(item => "- " + item)))
            .ToList());

    private static string Join(IEnumerable<string> parts) =>
        string.Join("\n\n", parts.Select(part => part.ReplaceLineEndings("\n").Trim()).Where(part => part.Length > 0));

    private static string LanguageName(MemoryLanguage language) => language switch
    {
        MemoryLanguage.Zh => "Chinese",
        MemoryLanguage.En => "English",
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, null),
    };
}
