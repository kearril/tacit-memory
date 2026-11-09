using System.Text;
using TacitMemory.Core.Injection;

namespace TacitMemory.Core.Skills;

/// <summary>What <see cref="Tacit.GenerateSkills"/> did: every generated skill, in the order of <see cref="SkillSet.Names"/>.</summary>
public sealed record SkillResult(IReadOnlyList<GeneratedSkill> Skills);

/// <param name="Name">The skill's name, which is also its folder's name and the name of its client link.</param>
/// <param name="Path">Its <c>SKILL.md</c>.</param>
/// <param name="Written">Whether this call wrote the file; a file that already holds the text is left alone.</param>
public sealed record GeneratedSkill(string Name, string Path, bool Written);

/// <summary>
/// The skills TacitMemory generates under <c>TACIT_HOME/skills/</c>: the memory skill and the two memory commands.
/// Each lives in a folder of its own name, and <c>connect</c> links each folder into the client's skill folder.
/// </summary>
internal static class SkillSet
{
    /// <summary>Every generated skill, by name; also the names of the client links.</summary>
    public static readonly IReadOnlyList<string> Names = [MemorySkill.Name, RememberSkill.Name, ConsolidationSkill.Name];

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary><c>TACIT_HOME/skills/&lt;name&gt;</c>, the directory the client link of that skill points to.</summary>
    public static string Folder(string tacitHome, string name) => Path.Combine(tacitHome, "skills", name);

    /// <summary>Each skill's link in the client's skill folder <paramref name="clientSkills"/>, with the skill folder it points to.</summary>
    public static IReadOnlyList<(string Link, string SkillFolder)> Links(string clientSkills, string tacitHome) =>
        Names.Select(name => (Path.Combine(clientSkills, name), Folder(tacitHome, name))).ToList();

    /// <summary>
    /// Writes every skill, leaving each file alone when it already holds exactly the text it would get.
    /// <paramref name="userConventions"/> is the text of <c>prompts/skill.md</c>, which only the memory skill carries; the
    /// consolidation command writes its structure words in <paramref name="language"/>.
    /// </summary>
    public static SkillResult Write(string tacitHome, MemoryLanguage language, string? userConventions) =>
        new([
            WriteOne(tacitHome, MemorySkill.Name, MemorySkill.Compose(userConventions)),
            WriteOne(tacitHome, RememberSkill.Name, RememberSkill.Compose()),
            WriteOne(tacitHome, ConsolidationSkill.Name, ConsolidationSkill.Compose(language)),
        ]);

    private static GeneratedSkill WriteOne(string tacitHome, string name, string text)
    {
        var path = Path.Combine(Folder(tacitHome, name), "SKILL.md");
        if (File.Exists(path) && File.ReadAllText(path, Utf8) == text)
            return new GeneratedSkill(name, path, false);
        TextFile.Write(path, text, hasBom: false);
        return new GeneratedSkill(name, path, true);
    }
}
