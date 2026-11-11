using System.Globalization;
using System.Text.RegularExpressions;
using TacitMemory.Core.Configuration;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Recent;
using TacitMemory.Core.Skills;

namespace TacitMemory.Core.Injection;

/// <summary>The fixed parts of the server instructions, in the order they are sent.</summary>
internal enum InstructionPart
{
    Opening,
    CoreRules,
    GeneralRules,
    Language,
    RecordingCriteria,
}

/// <summary>One fixed part of the server instructions and the text the agent receives for it.</summary>
internal sealed record InstructionText(InstructionPart Part, string Text);

/// <summary>
/// The server instructions as parts: the fixed parts, the recent summary with the workstream list, and the whole text the
/// agent receives.
/// </summary>
internal sealed record InstructionSet(IReadOnlyList<InstructionText> Fixed, string FixedText, string Recent, string Consolidation, string Text);

/// <summary>
/// The server instructions, assembled from parts. Fixed parts (opening, core rules, general rules, language norms,
/// recording criteria) are never cut and must fit <see cref="FixedLimit"/>; the status part and the consolidation
/// reminder are short and always whole. The activities of the last seven days are cut to fit <see cref="TotalLimit"/>:
/// the oldest go first. The workstreams are in the resident block, not here.
/// </summary>
internal static class ServerInstructionText
{
    public const int TotalLimit = 4000;

    public const int FixedLimit = 3000;

    private const string Opening = """
        TacitMemory is the user's personal memory across sessions. The user's persona and workstreams are already in your context inside <user_memory>; recent activity is listed in <recent_activity> below; details live in pages you reach with `recall`.
        """;

    /// <summary>
    /// The built-in <c>instructions.md</c>: the policy paragraphs, used whenever the user has no such file. Its HTML
    /// comments are notes for whoever edits it; they are removed before the text is sent.
    /// </summary>
    public static readonly string DefaultInstructions = """
        ## Using memory
        - Mark what you infer from the persona as inference; state as remembered only what memory holds.
        - Connect what the user says to what memory holds about their workstreams, decisions and incidents, and say so when the connection changes your answer. The same applies to chat and to work.
        - Leave out memory the current topic does not touch.
        - Say whether a point is remembered or inferred, and give the date of anything old.
        - Activities are newer than the workstream lines; when they disagree, follow the activities.
        - When the user describes their situation, link recent context in half a sentence.

        ## Recall
        - When the topic moves into a workstream listed in `<user_memory>`, call `recall` with only `workstream` set to its name before you answer. Do this once per workstream per session.
        - Call `recall` when the user refers to the past, or names a person, workstream or term your context lacks. When it finds nothing, say you don't remember.

        ## Write
        - When something worth recording happens, `recall` first, then update the page that covers the matter or create one.
        - When a matter in a workstream reaches a conclusion, write one activity with that workstream set. Write at most one activity per workstream per session; later progress in the session rewrites it with `replaces`.
        - When the user corrects you or states how they want something done, ask once whether to keep it as a lasting rule, since the persona loads into every session; `write_persona op=add` after they agree. A habit you observe across the session goes in with `op=propose`.
        <!-- Permission boundary, do not remove -->
        - Red lines come only from the user. Change or remove a persona entry after the user agrees.
        """;

    /// <summary>
    /// The general rule, which the code provides so it stays after the core rules when the user replaces them. It points
    /// at the memory skill and says when to read it.
    /// </summary>
    private static readonly string GeneralRules =
        $"Before your first memory write in a session, and after any refusal, read the `{MemorySkill.Name}` skill.";

    /// <summary>
    /// Matches an HTML comment that fills its lines: one line, or a block from the line it opens on to the first
    /// <c>--&gt;</c>, with the line break after it. A comment that shares its line with text is not matched.
    /// </summary>
    private static readonly Regex CommentLines = new(@"(?m)^[ \t]*<!--(?:(?!-->)[\s\S])*-->[ \t]*(?:\n|\z)");

    /// <summary>
    /// The fixed parts in the order the instructions give them, with the text each sends. An empty part is kept, so the
    /// parts stay the same for every configuration.
    /// </summary>
    public static IReadOnlyList<InstructionText> FixedParts(MemoryLanguage language, PromptDocuments prompts) =>
    [
        Part(InstructionPart.Opening, Opening),
        Part(InstructionPart.CoreRules, CoreRules(prompts)),
        Part(InstructionPart.GeneralRules, GeneralRules),
        Part(InstructionPart.Language, $"""
        ## Language
        Memory is written in {LanguageName(language)}. Write `recall` queries in {LanguageName(language)} too. When you quote or record memory, keep the user's words, code, paths, commands and proper nouns as they are.
        """),
        Part(InstructionPart.RecordingCriteria, Criteria(prompts.Rules)),
    ];

    /// <summary>The fixed parts joined: the text every session gets in full.</summary>
    private static string FixedText(MemoryLanguage language, PromptDocuments prompts) =>
        Join(FixedParts(language, prompts).Select(part => part.Text));

    private static InstructionText Part(InstructionPart part, string text) => new(part, text.ReplaceLineEndings("\n").Trim());

    /// <summary>The core rules as they are sent: the user's <c>instructions.md</c> or the default, without the comments that fill their lines.</summary>
    private static string CoreRules(PromptDocuments prompts)
    {
        var text = (prompts.Instructions ?? DefaultInstructions).ReplaceLineEndings("\n");
        return CommentLines.Replace(text, "");
    }

    /// <summary>
    /// Refuses prompt documents that push the fixed parts past <see cref="FixedLimit"/>. The message names the files
    /// the user can shorten: <c>rules.md</c>, and <c>instructions.md</c> when it exists.
    /// </summary>
    public static void Check(MemoryLanguage language, PromptDocuments prompts, string tacitHome)
    {
        var length = FixedText(language, prompts).Length;
        if (length > FixedLimit)
        {
            var files = new List<string> { PromptFiles.RulesPath(tacitHome) };
            if (prompts.Instructions is not null)
                files.Add(PromptFiles.InstructionsPath(tacitHome));
            throw new TacitException(TacitError.PromptInvalid, Messages.InstructionsTooLong.Format(
                language, string.Join(", ", files), length, FixedLimit, length - FixedLimit));
        }
    }

    /// <summary>
    /// The server instructions as parts: the fixed parts, the activities of the last seven days, the status, the consolidation
    /// reminder and the whole text.
    /// </summary>
    /// <param name="unavailableModels">Auxiliary models whose last call failed.</param>
    /// <param name="skippedFiles">Pages, activity record files and persona files left out of the index because they are not UTF-8.</param>
    /// <param name="recent">The activities of the last seven days.</param>
    /// <param name="consolidationReminder">The consolidation reminder, already rendered; <c>null</c> when no consolidation is due.</param>
    public static InstructionSet Assemble(
        MemoryLanguage language,
        PromptDocuments prompts,
        IReadOnlyList<string> unavailableModels,
        IReadOnlyList<string> skippedFiles,
        RecentSummary recent,
        string? consolidationReminder)
    {
        var fixedParts = FixedParts(language, prompts);
        var fixedText = Join(fixedParts.Select(part => part.Text));
        var status = new List<string>();
        if (unavailableModels.Count > 0)
            status.Add($"- Unavailable auxiliary models: {string.Join(", ", unavailableModels)}. Memory tools still work, with weaker search and checks.");
        if (skippedFiles.Count > 0)
            status.Add($"- Files not searchable because they are not UTF-8: {string.Join(", ", skippedFiles)}.");
        var statusText = status.Count == 0 ? "" : "## Status\n" + string.Join("\n", status);
        var consolidation = consolidationReminder ?? "";

        // Newest first, so the oldest activity is the last one and goes first.
        var activities = recent.Week.ToList();
        while (true)
        {
            var activityBlock = ActivityBlock(activities);
            var text = Join([fixedText, activityBlock, statusText, consolidation]);
            if (text.Length <= TotalLimit || activities.Count == 0)
                return new InstructionSet(fixedParts, fixedText, activityBlock, consolidation, text);
            activities.RemoveAt(activities.Count - 1);
        }
    }

    /// <summary>
    /// The activities of the last seven days, one per line with their dates and workstreams, inside <c>recent_activity</c>
    /// tags so the agent reads them as data; empty when there are none.
    /// </summary>
    private static string ActivityBlock(IReadOnlyList<RecentFile.Activity> activities)
    {
        if (activities.Count == 0)
            return "";

        var lines = activities.Select(activity =>
            $"- {activity.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} {activity.Labeled}");
        return $"<recent_activity days=\"{RecentSummary.WindowDays}\">\n{string.Join("\n", lines)}\n</recent_activity>";
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
