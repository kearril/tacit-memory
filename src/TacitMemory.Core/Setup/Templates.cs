using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Setup;

/// <summary>
/// The files <c>init</c> writes besides <c>config.yaml</c>: headings only, which are structure words and so the same in
/// every memory language. All text uses LF line endings.
/// </summary>
internal static class Templates
{
    public static readonly string Persona =
        Sections(StructureWords.RedLines, StructureWords.WorkingRules, StructureWords.Preferences, StructureWords.Profile);

    public static readonly string Candidates = Sections(StructureWords.Candidates, StructureWords.RejectionList);

    public static readonly string Workstreams = Sections(StructureWords.Workstream);

    public const string MemoryGitignore = ".cache/\n";

    private static string Sections(params string[] headings) => string.Join("\n", headings.Select(heading => $"## {heading}\n"));
}
