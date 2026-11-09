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
/// The server instructions as parts: the fixed parts, the recent summary with the project list, and the whole text the
/// agent receives.
/// </summary>
internal sealed record InstructionSet(IReadOnlyList<InstructionText> Fixed, string FixedText, string Recent, string Consolidation, string Text);

/// <summary>
/// The server instructions, assembled from parts. Fixed parts (opening, core rules, general rules, language norms,
/// recording criteria) are never cut and must fit <see cref="FixedLimit"/>; the status part and the consolidation
/// reminder are short and always whole. The activities of the last seven days are cut to fit <see cref="TotalLimit"/>:
/// the oldest go first. The mainline, the recent weeks and the projects are in the resident block, not here.
/// </summary>
internal static class ServerInstructionText
{
    public const int TotalLimit = 4000;

    public const int FixedLimit = 2500;

    private const string Opening = """
        TacitMemory is the user's personal memory across sessions. The user's persona is already in your context inside <user_memory>; recent activity is listed in <recent_activity> below; details live in pages you reach with `recall`.
        """;

    private const string UsingMemory = """
        ## Using memory
        - Keep what you know from memory apart from what you infer from the persona; never present a guess as something you remember.
        - Do not bring up past events the user has not mentioned.
        - When the user describes their situation, mention recent context in half a sentence.
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
        - One page per matter.
        """;

    private const string WritingRecent = """
        ## Writing activities and mainline
        - Write an activity when a main matter concludes; one per session, and later progress uses `replaces`.
        """;

    private const string WritingPersona = """
        ## Writing persona
        - Propose a habit, preference or working rule the user states, even in passing, with write_persona op=propose and its section; it becomes a candidate.
        <!-- Permission boundary, do not remove: the next two rules -->
        - Use op=add only when the user explicitly asks you to remember; only the user sets a red line.
        - If the user's words contradict a persona entry, follow them, propose nothing, ask whether to update it, and op=replace only after the user agrees.
        """;

    /// <summary>
    /// The built-in <c>instructions.md</c>: the policy paragraphs, used whenever the user has no such file. Its HTML
    /// comments are notes for whoever edits it; they are removed before the text is sent.
    /// </summary>
    public static readonly string DefaultInstructions = Join([UsingMemory, Recalling, Writing, WritingRecent, WritingPersona]);

    /// <summary>
    /// The general rules, which the code provides so they stay in place when the user replaces the core rules. They
    /// point at the memory skill, where each kind of memory is written.
    /// </summary>
    private static readonly string GeneralRules = $"""
        ## General rules
        - When a write tool refuses, do what its Next step says.
        - How to write each kind of memory in detail is in the `{MemorySkill.Name}` skill.
        """;

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
    /// The activities of the last seven days, one per line with their dates and projects, inside <c>recent_activity</c>
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
