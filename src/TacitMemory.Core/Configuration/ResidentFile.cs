using System.Text;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Persona;

namespace TacitMemory.Core.Configuration;

/// <summary>One section of the resident block: its heading in <c>resident.md</c>, the persona section it guides, and its title in the block.</summary>
internal sealed record ResidentSection(string Name, PersonaSection Kind, string Title);

/// <summary>
/// <c>resident.md</c>: the shell paragraph and the guidance of the resident block. The text before the first
/// <c>## </c> heading is the shell; the text under each heading in <see cref="Sections"/> is that section's guidance.
/// Headings match exactly, in every language; the text is kept as written. A heading that is not one of the four, or a
/// repeated one, is refused.
/// </summary>
internal static class ResidentFile
{
    public const int MaxLength = 1500;

    /// <summary>The four sections in block order. Everything that names a section takes its name and title from here.</summary>
    public static readonly ResidentSection[] Sections =
    [
        new(ToolValues.PersonaSections.RedLines, PersonaSection.RedLines, "Red lines"),
        new(ToolValues.PersonaSections.WorkingRules, PersonaSection.WorkingRules, "Working rules"),
        new(ToolValues.PersonaSections.Preferences, PersonaSection.Preferences, "Preferences"),
        new(ToolValues.PersonaSections.Profile, PersonaSection.Profile, "Profile"),
    ];

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Reads the file, or returns <c>null</c> when it is absent, so the built-in shell and guidance apply.</summary>
    /// <exception cref="TacitException">The file is longer than <see cref="MaxLength"/>, or names a section wrongly.</exception>
    public static ResidentDocument? Load(string path, MemoryLanguage language)
    {
        if (!File.Exists(path))
            return null;

        var text = ReadText(path);
        if (text.Length > MaxLength)
            throw new TacitException(TacitError.PromptInvalid, Messages.ResidentTooLong.Format(
                language, path, text.Length, MaxLength, text.Length - MaxLength));

        var shell = new List<string>();
        var sections = new Dictionary<PersonaSection, List<string>>();
        var current = shell;
        var lines = text.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (!line.StartsWith("## ", StringComparison.Ordinal))
            {
                current.Add(line);
                continue;
            }

            var name = line[3..].Trim();
            var section = Sections.FirstOrDefault(candidate => candidate.Name == name)
                ?? throw new TacitException(TacitError.PromptInvalid, Messages.ResidentUnknownSection.Format(
                    language, path, index + 1, name, string.Join(", ", Sections.Select(candidate => candidate.Name))));
            if (sections.ContainsKey(section.Kind))
                throw new TacitException(TacitError.PromptInvalid, Messages.ResidentRepeatedSection.Format(
                    language, path, index + 1, name));

            current = [];
            sections[section.Kind] = current;
        }

        return new ResidentDocument(
            Joined(shell) ?? "",
            Guidance(sections, PersonaSection.RedLines),
            Guidance(sections, PersonaSection.WorkingRules),
            Guidance(sections, PersonaSection.Preferences),
            Guidance(sections, PersonaSection.Profile));
    }

    /// <summary>The file's length as the limit counts it: trailing blank lines included, line breaks as LF.</summary>
    public static int FileLength(string path) => ReadText(path).Length;

    private static string ReadText(string path) => File.ReadAllText(path, Utf8).ReplaceLineEndings("\n");

    /// <summary>
    /// The file form of a resident document: the shell, then each section that has guidance under its heading. Read
    /// back by <see cref="Load"/> it gives the same document. No newline ends the text.
    /// </summary>
    public static string Compose(ResidentDocument document)
    {
        var parts = new List<string>();
        if (document.Shell.Length > 0)
            parts.Add(document.Shell);
        foreach (var section in Sections)
        {
            if (GuidanceOf(document, section.Kind) is { } guidance)
                parts.Add($"## {section.Name}\n{guidance}");
        }
        return string.Join("\n\n", parts);
    }

    /// <summary>The guidance <c>resident.md</c> gives a section, or <c>null</c> when it gives none.</summary>
    public static string? GuidanceOf(ResidentDocument document, PersonaSection kind) => kind switch
    {
        PersonaSection.RedLines => document.RedLines,
        PersonaSection.WorkingRules => document.WorkingRules,
        PersonaSection.Preferences => document.Preferences,
        PersonaSection.Profile => document.Profile,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static string? Guidance(Dictionary<PersonaSection, List<string>> sections, PersonaSection kind) =>
        sections.TryGetValue(kind, out var lines) ? Joined(lines) : null;

    /// <summary>The lines joined and trimmed, or <c>null</c> when nothing is left.</summary>
    private static string? Joined(List<string> lines) =>
        string.Join("\n", lines).Trim() is { Length: > 0 } text ? text : null;
}
