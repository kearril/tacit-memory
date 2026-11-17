using System.Text;
using System.Text.RegularExpressions;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Configuration;

/// <summary>
/// The prompt documents in <c>TACIT_HOME/prompts/</c>. <see cref="Load"/> runs when the config is loaded and
/// <see cref="Reload"/> whenever the folder changes while the service runs. Each document is checked there; a document
/// that is absent falls back to its built-in default.
/// </summary>
internal static partial class PromptFiles
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>The longest <c>skill.md</c>, in characters; a longer one refuses startup.</summary>
    public const int SkillLimit = 3000;

    public static string Folder(string tacitHome) => Path.Combine(tacitHome, "prompts");

    public static string RulesPath(string tacitHome) => Path.Combine(Folder(tacitHome), "rules.md");

    public static string InstructionsPath(string tacitHome) => Path.Combine(Folder(tacitHome), "instructions.md");

    public static string SkillPath(string tacitHome) => Path.Combine(Folder(tacitHome), "skill.md");

    public static string ResidentPath(string tacitHome) => Path.Combine(Folder(tacitHome), "resident.md");

    public static string PathOf(string tacitHome, PromptName name) => name switch
    {
        PromptName.Instructions => InstructionsPath(tacitHome),
        PromptName.Rules => RulesPath(tacitHome),
        PromptName.Resident => ResidentPath(tacitHome),
        PromptName.Skill => SkillPath(tacitHome),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    /// <summary>
    /// The built-in text of a document in the form of its file, which applies while the file is absent: the recording
    /// criteria are in the memory language, and <c>skill.md</c> has none.
    /// </summary>
    public static string DefaultText(PromptName name, MemoryLanguage language) => name switch
    {
        PromptName.Instructions => ServerInstructionText.DefaultInstructions,
        PromptName.Rules => RulesFile.Compose(RulesFile.Default),
        PromptName.Resident => ResidentFile.Compose(ResidentBlock.Default),
        PromptName.Skill => "",
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    /// <summary>
    /// Checks <paramref name="text"/> as the content of the document <paramref name="name"/>, by the rules a load applies to
    /// its file, and the fixed part of the server instructions with the other documents as <paramref name="running"/> holds
    /// them. Returns every refusal, and none when the text may be saved. The fixed part is checked even when the text
    /// breaks a rule of its own, as long as its length can still be counted.
    /// </summary>
    public static IReadOnlyList<string> Check(
        string tacitHome, MemoryLanguage language, PromptDocuments running, PromptName name, string text)
    {
        var path = PathOf(tacitHome, name);
        text = text.ReplaceLineEndings("\n");
        var problems = new List<string>();
        var candidate = running;
        switch (name)
        {
            case PromptName.Instructions:
                candidate = running with { Instructions = text };
                break;
            case PromptName.Rules:
                var criteria = RulesFile.Read(text);
                problems.AddRange(RulesFile.Problems(criteria, path, language));
                candidate = running with { Rules = criteria };
                break;
            case PromptName.Resident:
                Refusal(() => ResidentFile.Parse(text, path, language));
                break;
            case PromptName.Skill:
                Refusal(() => ParseSkill(text, path, language));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(name), name, null);
        }
        Refusal(() => ServerInstructionText.Check(language, candidate, tacitHome));
        return problems;

        void Refusal(Action check)
        {
            try
            {
                check();
            }
            catch (TacitException e)
            {
                problems.Add(e.Message);
            }
        }
    }

    /// <summary>
    /// How much of its limit <paramref name="text"/> takes as the content of the document <paramref name="name"/>, with the
    /// other documents as <paramref name="running"/> holds them; counted however the text breaks its rules.
    /// </summary>
    public static PromptMeasure Measure(MemoryLanguage language, PromptDocuments running, PromptName name, string text)
    {
        text = text.ReplaceLineEndings("\n");
        switch (name)
        {
            case PromptName.Instructions:
                return Fixed(running with { Instructions = text }, null);
            case PromptName.Rules:
                var criteria = RulesFile.Read(text);
                return Fixed(running with { Rules = criteria },
                    new PromptItems(criteria.WorthRecording.Count, criteria.NotRecorded.Count, RulesFile.MaxItemsPerSection));
            case PromptName.Resident:
                return new PromptMeasure(text.Length, ResidentFile.MaxLength, null, null);
            case PromptName.Skill:
                return new PromptMeasure(text.Length, SkillLimit, null, null);
            default:
                throw new ArgumentOutOfRangeException(nameof(name), name, null);
        }

        PromptMeasure Fixed(PromptDocuments candidate, PromptItems? items)
        {
            var length = ServerInstructionText.FixedLength(language, candidate);
            return new PromptMeasure(length, ServerInstructionText.FixedLimit, ServerInstructionText.TotalLimit - length, items);
        }
    }

    /// <summary>
    /// A document as its file holds it, with the file's hash, or its built-in text when the file is absent; measured beside
    /// the other documents as <paramref name="running"/> holds them. <paramref name="problem"/> is why the service keeps
    /// the document's previous text instead of the file's, if it does.
    /// </summary>
    public static PromptDocumentView View(
        string tacitHome, MemoryLanguage language, PromptDocuments running, PromptName name, string? problem)
    {
        var path = PathOf(tacitHome, name);
        if (!File.Exists(path))
        {
            var builtIn = DefaultText(name, language);
            return new PromptDocumentView(name, builtIn, BuiltIn: true, Hash: null, Measure(language, running, name, builtIn), problem);
        }
        var text = File.ReadAllText(path, Utf8);
        return new PromptDocumentView(name, text, BuiltIn: false, ContentHash.Of(text), Measure(language, running, name, text), problem);
    }

    /// <summary>
    /// Reads every prompt document, throwing for the first one that breaks its limits or format.
    /// </summary>
    public static PromptDocuments Load(string tacitHome, MemoryLanguage language)
    {
        Directory.CreateDirectory(Folder(tacitHome));
        return new PromptDocuments(
            RulesFile.Load(RulesPath(tacitHome), language),
            ReadInstructions(tacitHome),
            ResidentFile.Load(ResidentPath(tacitHome), language),
            ReadSkill(tacitHome, language));
    }

    /// <summary>
    /// Reads the prompt documents again while the service runs. A document that fails its checks keeps its previous text
    /// and its refusal is added to <paramref name="problems"/> under its name; the others take their new text.
    /// <c>rules.md</c> and <c>instructions.md</c> count toward the fixed limit together, so when they exceed it together
    /// both keep their previous text and both carry the refusal.
    /// </summary>
    public static PromptDocuments Reload(
        string tacitHome, MemoryLanguage language, PromptDocuments previous, Dictionary<PromptName, string> problems)
    {
        Directory.CreateDirectory(Folder(tacitHome));
        var candidate = new PromptDocuments(
            Keep(PromptName.Rules, () => RulesFile.Load(RulesPath(tacitHome), language), previous.Rules, problems),
            Keep(PromptName.Instructions, () => ReadInstructions(tacitHome), previous.Instructions, problems),
            Keep(PromptName.Resident, () => ResidentFile.Load(ResidentPath(tacitHome), language), previous.Resident, problems),
            Keep(PromptName.Skill, () => ReadSkill(tacitHome, language), previous.Skill, problems));
        try
        {
            ServerInstructionText.Check(language, candidate, tacitHome);
            return candidate;
        }
        catch (TacitException e)
        {
            problems.TryAdd(PromptName.Rules, e.Message);
            problems.TryAdd(PromptName.Instructions, e.Message);
            return candidate with { Rules = previous.Rules, Instructions = previous.Instructions };
        }
    }

    /// <summary><c>instructions.md</c> as written, or <c>null</c> when the file is absent.</summary>
    public static string? ReadInstructions(string tacitHome)
    {
        var path = InstructionsPath(tacitHome);
        return File.Exists(path) ? File.ReadAllText(path, Utf8) : null;
    }

    /// <summary>
    /// <c>skill.md</c> as the user's writing conventions, or <c>null</c> when the file is absent or blank. It must not
    /// start with frontmatter, because the skill's name and description belong to TacitMemory, and must stay within
    /// <see cref="SkillLimit"/>. It is not checked for language.
    /// </summary>
    public static string? ReadSkill(string tacitHome, MemoryLanguage language)
    {
        var path = SkillPath(tacitHome);
        return File.Exists(path) ? ParseSkill(File.ReadAllText(path, Utf8), path, language) : null;
    }

    /// <summary>The writing conventions in a text written as <c>skill.md</c>, checked as <see cref="ReadSkill"/> checks the file.</summary>
    public static string? ParseSkill(string text, string path, MemoryLanguage language)
    {
        if (FrontmatterStart().IsMatch(text))
            throw new TacitException(TacitError.PromptInvalid, Messages.SkillHasFrontmatter.Format(language, path));
        if (text.Length > SkillLimit)
            throw new TacitException(TacitError.PromptInvalid, Messages.SkillTooLong.Format(
                language, path, text.Length, SkillLimit, text.Length - SkillLimit));
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    /// <summary>
    /// Runs one document's reader. A refusal keeps the previous value and is recorded; an I/O error propagates, so the
    /// caller tries again later.
    /// </summary>
    private static T Keep<T>(PromptName name, Func<T> read, T previous, Dictionary<PromptName, string> problems)
    {
        try
        {
            return read();
        }
        catch (Exception e) when (e is TacitException or UnauthorizedAccessException)
        {
            problems[name] = e.Message;
            return previous;
        }
    }

    /// <summary>A file that opens with a <c>---</c> line, the start of YAML frontmatter.</summary>
    [GeneratedRegex(@"\A\s*---[ \t]*(\r?\n|\z)")]
    private static partial Regex FrontmatterStart();
}
