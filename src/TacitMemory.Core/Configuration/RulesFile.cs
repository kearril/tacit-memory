using System.Text;
using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Configuration;

/// <summary>
/// <c>rules.md</c>: the "worth recording" and "not recorded" sections, each a list of top-level <c>- </c> items.
/// Headings are recognized in every language; other sections and continuation lines are ignored. Without the file the
/// built-in criteria apply, in the memory language.
/// </summary>
internal static class RulesFile
{
    public const int MaxItemsPerSection = 10;

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly Localized[] DefaultWorthRecording =
    [
        new("用户纠正或打断 Agent", "The user corrects or interrupts the agent"),
        new("方案拍板，保留用户原话", "A plan is decided; keep the user's own words"),
        new("查明根因的排错，含试过但无效的做法", "A root cause found, with what was tried and failed"),
        new("新的环境事实", "New facts about the user's environment"),
        new("用户明确要求记下", "The user asks to remember something"),
    ];

    private static readonly Localized[] DefaultNotRecorded =
    [
        new("常规的写代码、改错字、看日志", "Routine coding, typo fixes and log reading"),
        new("能从代码、仓库文档、配置中查到的内容", "What code, repository docs or configuration already show"),
        new("临时问答、一次性查询和闲聊，没有结论时", "Quick questions, one-off lookups and small talk that reach no conclusion"),
    ];

    /// <summary>The built-in criteria in the memory language, used whenever <c>rules.md</c> is absent.</summary>
    public static RecordingCriteria Default(MemoryLanguage language) => new(
        [.. DefaultWorthRecording.Select(item => item.In(language))],
        [.. DefaultNotRecorded.Select(item => item.In(language))]);

    /// <summary>Criteria written as a <c>rules.md</c> file, with the headings in the memory language and LF line endings.</summary>
    public static string Compose(RecordingCriteria criteria, MemoryLanguage language) =>
        $"## {StructureWords.WorthRecording.In(language)}\n{Items(criteria.WorthRecording)}\n" +
        $"## {StructureWords.NotRecorded.In(language)}\n{Items(criteria.NotRecorded)}";

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
        .. new[] { (StructureWords.WorthRecording, criteria.WorthRecording), (StructureWords.NotRecorded, criteria.NotRecorded) }
            .Where(section => section.Item2.Count > MaxItemsPerSection)
            .Select(section => Messages.RulesTooMany.Format(
                language, path, section.Item1.In(language), section.Item2.Count, MaxItemsPerSection, section.Item2.Count - MaxItemsPerSection)),
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

    private static bool Is(Localized word, string heading) =>
        string.Equals(heading, word.Zh, StringComparison.Ordinal) || string.Equals(heading, word.En, StringComparison.OrdinalIgnoreCase);
}
