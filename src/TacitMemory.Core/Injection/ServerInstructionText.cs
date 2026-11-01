using System.Globalization;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Recent;

namespace TacitMemory.Core.Injection;

/// <summary>
/// The server instructions, assembled from parts. Fixed parts (opening, memory rules, language norms, recording
/// criteria) are never cut and must fit <see cref="FixedLimit"/>; the status part is short and always whole. The
/// recent summary and the project list are cut to fit <see cref="TotalLimit"/>: the oldest activities go first, then
/// project names from the end, and the list vanishes when none fits.
/// </summary>
internal static class ServerInstructionText
{
    public const int TotalLimit = 4000;

    public const int FixedLimit = 2500;

    private const string Opening = """
        TacitMemory is the user's personal memory across sessions. The user's persona is already in your context inside <user_memory>; recent activity is summarized under ## Recent below; details live in pages you reach with `recall`.
        """;

    private const string UsingMemory = """
        ## Using memory
        - Keep what you know from memory apart from what you infer from the persona; never present a guess as something you remember.
        - Do not bring up past events the user has not mentioned.
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
        - When `write_page` refuses, do what its "Next step" says.
        """;

    private const string WritingRecent = """
        ## Writing activities and mainline
        - Write an activity when a main matter concludes; one per session, and later progress uses `replaces`.
        - Describe what the person is doing or concluded, not files changed; set `project` for a project.
        - Change the mainline only for a lasting new direction or a clearly ended one.
        - When the user describes their situation, mention recent context in half a sentence.
        """;

    private const string WritingPersona = """
        ## Writing persona
        - Propose a habit, preference or working rule the user states, even in passing, with write_persona op=propose and its section; it becomes a candidate.
        - Use op=add only when the user explicitly asks you to remember; only the user sets a red line.
        - If the user's words contradict a persona entry, follow them, propose nothing, ask whether to update it, and op=replace only after the user agrees.
        """;

    /// <summary>The fixed parts, which every session gets in full.</summary>
    public static string Fixed(MemoryLanguage language, RecordingCriteria criteria) => Join(
    [
        Opening,
        UsingMemory,
        Recalling,
        Writing,
        WritingRecent,
        WritingPersona,
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
    /// <param name="skippedFiles">Pages, activity record files and persona files left out of the index because they are not UTF-8.</param>
    /// <param name="recent">The mainline and the last seven days of activities.</param>
    /// <param name="projects">The project names listed under <c>## Projects</c>, in the order they are listed.</param>
    public static string Build(
        MemoryLanguage language,
        RecordingCriteria criteria,
        IReadOnlyList<string> unavailableModels,
        IReadOnlyList<string> skippedFiles,
        RecentSummary recent,
        IReadOnlyList<string> projects)
    {
        var fixedText = Fixed(language, criteria);
        var status = new List<string>();
        if (unavailableModels.Count > 0)
            status.Add($"- Unavailable auxiliary models: {string.Join(", ", unavailableModels)}. Memory tools still work, with weaker search and checks.");
        if (skippedFiles.Count > 0)
            status.Add($"- Files not searchable because they are not UTF-8: {string.Join(", ", skippedFiles)}.");
        var statusText = status.Count == 0 ? "" : "## Status\n" + string.Join("\n", status);

        // Newest first, so the oldest activity is the last one and goes first.
        var activities = recent.Week.ToList();
        var kept = projects.ToList();
        while (true)
        {
            var text = Join([
                fixedText,
                Summary(recent.Mainline, activities),
                statusText,
                kept.Count == 0 ? "" : "## Projects\n" + string.Join(", ", kept),
            ]);
            if (text.Length <= TotalLimit)
                return text;
            if (activities.Count > 0)
                activities.RemoveAt(activities.Count - 1);
            else if (kept.Count > 0)
                kept.RemoveAt(kept.Count - 1);
            else
                return text;
        }
    }

    /// <summary>
    /// The project names the instructions list: the directories under <c>pages/</c> that do not start with an
    /// underscore or a dot, together with every project label in the activity log; sorted by name, each once.
    /// </summary>
    public static IReadOnlyList<string> Projects(string memoryPath, IEnumerable<string> activityProjects)
    {
        var pages = Path.Combine(memoryPath, "pages");
        IEnumerable<string> directories = Directory.Exists(pages)
            ? Directory.EnumerateDirectories(pages).Select(Path.GetFileName).OfType<string>()
            : Enumerable.Empty<string>();
        return directories
            .Where(name => !name.StartsWith('_') && !name.StartsWith('.'))
            .Concat(activityProjects)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The recent summary: the mainline without codes, then the activities with their dates and projects.</summary>
    private static string Summary(IReadOnlyList<string> mainline, IReadOnlyList<RecentFile.Activity> activities)
    {
        if (mainline.Count == 0 && activities.Count == 0)
            return "";

        var lines = new List<string> { "## Recent" };
        if (mainline.Count > 0)
        {
            lines.Add("Mainline:");
            lines.AddRange(mainline.Select(item => "- " + item));
        }
        if (activities.Count > 0)
        {
            lines.Add("Activities from the last 7 days, newest first:");
            lines.AddRange(activities.Select(activity =>
                $"- {activity.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} {activity.Labeled}"));
        }
        return string.Join("\n", lines);
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
