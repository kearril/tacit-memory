using System.Text;
using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Configuration;

/// <summary>
/// <c>rules.md</c>: the "worth recording" and "not recorded" sections, each a list of top-level <c>- </c> items.
/// The headings are structure words; other sections and continuation lines are ignored. Without the file the built-in
/// criteria apply. They are English, as everything only the agent reads is.
/// </summary>
internal static class RulesFile
{
    public const int MaxItemsPerSection = 10;

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>The built-in criteria, used whenever <c>rules.md</c> is absent.</summary>
    public static readonly RecordingCriteria Default = new(
        [
            "The user corrects or interrupts the agent",
            "A plan is decided; keep the user's own words",
            "A root cause found, with what was tried and failed",
            "New facts about the user's environment",
            "The user asks to remember something",
        ],
        [
            "Routine coding, typo fixes and log reading",
            "What code, repository docs or configuration already show",
            "Quick questions, one-off lookups and small talk that reach no conclusion",
        ]);

    /// <summary>Criteria written as a <c>rules.md</c> file, with LF line endings.</summary>
    public static string Compose(RecordingCriteria criteria) =>
        $"## {StructureWords.WorthRecording}\n{Items(criteria.WorthRecording)}\n" +
        $"## {StructureWords.NotRecorded}\n{Items(criteria.NotRecorded)}";

    /// <summary>The criteria in <c>rules.md</c>, or <c>null</c> when the file is absent and the built-in criteria apply.</summary>
    public static RecordingCriteria? Load(string path, MemoryLanguage language) =>
        File.Exists(path) ? Parse(File.ReadAllText(path, Utf8), path, language) : null;

    /// <summary>The criteria in a text written as <c>rules.md</c>; <paramref name="path"/> names the file in a refusal.</summary>
    /// <exception cref="TacitException">A section has more than <see cref="MaxItemsPerSection"/> items.</exception>
    public static RecordingCriteria Parse(string text, string path, MemoryLanguage language)
    {
        var criteria = Read(text);
        var problems = Problems(criteria, path, language);
        return problems.Count == 0 ? criteria : throw new TacitException(TacitError.PromptInvalid, problems[0]);
    }

    /// <summary>The refusal of each section of <paramref name="criteria"/> with more than <see cref="MaxItemsPerSection"/> items.</summary>
    public static IReadOnlyList<string> Problems(RecordingCriteria criteria, string path, MemoryLanguage language) =>
    [
        .. new[] { (Heading: StructureWords.WorthRecording, Items: criteria.WorthRecording), (Heading: StructureWords.NotRecorded, Items: criteria.NotRecorded) }
            .Where(section => section.Items.Count > MaxItemsPerSection)
            .Select(section => Messages.RulesTooMany.Format(
                language, path, section.Heading, section.Items.Count, MaxItemsPerSection, section.Items.Count - MaxItemsPerSection)),
    ];

    /// <summary>The criteria in a text written as <c>rules.md</c>, however many items each section has.</summary>
    public static RecordingCriteria Read(string text)
    {
        var worth = new List<string>();
        var notRecorded = new List<string>();
        List<string>? section = null;
        foreach (var line in text.ReplaceLineEndings("\n").Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                var heading = line[3..].Trim();
                section = Is(StructureWords.WorthRecording, heading) ? worth
                    : Is(StructureWords.NotRecorded, heading) ? notRecorded
                    : null;
            }
            else if (section is not null && line.StartsWith("- ", StringComparison.Ordinal) && line[2..].Trim() is { Length: > 0 } item)
            {
                section.Add(item);
            }
        }

        return new RecordingCriteria(worth, notRecorded);
    }

    private static string Items(IEnumerable<string> items) => string.Concat(items.Select(item => $"- {item}\n"));

    private static bool Is(string word, string heading) => string.Equals(heading, word, StringComparison.OrdinalIgnoreCase);
}
