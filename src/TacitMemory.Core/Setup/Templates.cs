using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Setup;

/// <summary>The files <c>init</c> writes besides <c>config.yaml</c>, in the memory language. All text uses LF line endings.</summary>
internal static class Templates
{
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

    public static string Rules(MemoryLanguage language) =>
        $"## {StructureWords.WorthRecording.In(language)}\n{Items(DefaultWorthRecording, language)}\n" +
        $"## {StructureWords.NotRecorded.In(language)}\n{Items(DefaultNotRecorded, language)}";

    public static string Persona(MemoryLanguage language) =>
        Sections(language, StructureWords.RedLines, StructureWords.WorkingRules, StructureWords.Preferences, StructureWords.Profile);

    public static string Candidates(MemoryLanguage language) =>
        Sections(language, StructureWords.Candidates, StructureWords.RejectionList);

    public static string Workstreams(MemoryLanguage language) => Sections(language, StructureWords.Workstream);

    public const string MemoryGitignore = ".cache/\n";

    private static string Items(IEnumerable<Localized> items, MemoryLanguage language) =>
        string.Concat(items.Select(item => $"- {item.In(language)}\n"));

    private static string Sections(MemoryLanguage language, params Localized[] headings) =>
        string.Join("\n", headings.Select(heading => $"## {heading.In(language)}\n"));
}
