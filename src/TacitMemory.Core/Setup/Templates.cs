using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Setup;

/// <summary>The files <c>init</c> writes besides <c>config.yaml</c>, in the memory language. All text uses LF line endings.</summary>
internal static class Templates
{
    public static string Persona(MemoryLanguage language) =>
        Sections(language, StructureWords.RedLines, StructureWords.WorkingRules, StructureWords.Preferences, StructureWords.Profile);

    public static string Candidates(MemoryLanguage language) =>
        Sections(language, StructureWords.Candidates, StructureWords.RejectionList);

    public static string Workstreams(MemoryLanguage language) => Sections(language, StructureWords.Workstream);

    public const string MemoryGitignore = ".cache/\n";

    private static string Sections(MemoryLanguage language, params Localized[] headings) =>
        string.Join("\n", headings.Select(heading => $"## {heading.In(language)}\n"));
}
