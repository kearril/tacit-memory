using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Setup;

/// <summary>The files <c>init</c> writes, in the memory language. All text uses LF line endings.</summary>
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
    ];

    public static string Rules(MemoryLanguage language) =>
        $"## {StructureWords.WorthRecording.In(language)}\n{Items(DefaultWorthRecording, language)}\n" +
        $"## {StructureWords.NotRecorded.In(language)}\n{Items(DefaultNotRecorded, language)}";

    public static string Persona(MemoryLanguage language) =>
        Sections(language, StructureWords.RedLines, StructureWords.WorkingRules, StructureWords.Preferences, StructureWords.Profile);

    public static string Candidates(MemoryLanguage language) =>
        Sections(language, StructureWords.Candidates, StructureWords.RejectionList);

    public static string Recent(MemoryLanguage language) => Sections(language, StructureWords.Mainline);

    public const string MemoryGitignore = ".cache/\n";

    /// <summary>
    /// The commented <c>config.yaml</c>. <paramref name="memoryPath"/> is written only when the user chose one;
    /// otherwise the default stays a comment.
    /// </summary>
    public static string Config(MemoryLanguage language, string? memoryPath)
    {
        var memory = memoryPath is null ? ConfigDefaultMemory.In(language) : $"  path: {YamlQuote(memoryPath)}";
        return ConfigTemplate.In(language).ReplaceLineEndings("\n").Replace("{memory}", memory).Replace("{language}", language.Code());
    }

    private static string Items(IEnumerable<Localized> items, MemoryLanguage language) =>
        string.Concat(items.Select(item => $"- {item.In(language)}\n"));

    private static string Sections(MemoryLanguage language, params Localized[] headings) =>
        string.Join("\n", headings.Select(heading => $"## {heading.In(language)}\n"));

    private static string YamlQuote(string value) => "'" + value.Replace("'", "''") + "'";

    private static readonly Localized ConfigDefaultMemory = new(
        "  # path: <路径>             # 记忆仓库位置；默认为 TACIT_HOME 下的 memory",
        "  # path: <path>             # memory repository location; defaults to memory under TACIT_HOME");

    private static readonly Localized ConfigTemplate = new(
        """
        # TacitMemory 配置。除 models 外各项均可省略，省略时取注释中的默认值。

        server:
          port: 18888                 # 常驻服务在 127.0.0.1 上监听的端口

        memory:
        {memory}
        language: {language}                  # 记忆语言：zh 或 en

        # 辅助模型，必填。
        models:
          embedding:                  # OpenAI 兼容的 /v1/embeddings
            endpoint: ''
            model: ''
          reranker:                   # Cohere/Jina 兼容的 /v1/rerank
            endpoint: ''
            model: ''
          decision:                   # TypeSafe 的 /v1/systemone
            endpoint: ''
            model: ''
            apiKeyEnv: ''             # 存放密钥的环境变量名；也可改写 apiKey: <密钥>，二者只能选一个

        limits:
          persona:                    # 画像各节的条数上限
            redLines: 5
            workingRules: 20
            preferences: 15
            profile: 10
          itemLength: 100             # 每条最多字数，不含来源注释
          mainline: 5                 # 近期主线最多条数
          page:
            titleLength: 50           # 细节页标题最多字数
            aliasLength: 30           # 每个别名最多字数
            minAliases: 2             # 别名最少个数

        candidates:
          minDays: 2                  # 画像候选在不同日期出现满几天后可确认

        checkup:
          remindAfterDays: 7          # 距上次体检多少天后提示
          mainlineStaleDays: 14       # 近期主线多少天未更新视为过期

        # 各判断的上下阈值，默认值内置；需要时取消注释并修改。
        # decision:
        #   samePreference: { certain: …, unlikely: … }
        #   samePage:       { certain: …, unlikely: … }
        #   sameTopic:      { certain: …, unlikely: … }
        #   ruleConflict:   { certain: …, unlikely: … }

        retrieval:
          candidates: 30              # 全文与向量各取的候选数
          topK: 5                     # 最多返回条数
          # minRerankScore: …         # reranker 分数下限，默认值内置

        backup:
          keep: 7                     # 自动备份保留份数，0 表示关闭
          # remote: <Git 远端>        # 配置后每次自动备份后推送记忆仓库

        """,
        """
        # TacitMemory configuration. Everything except models may be left out; the defaults are in the comments.

        server:
          port: 18888                 # port the memory service listens on at 127.0.0.1

        memory:
        {memory}
        language: {language}                  # memory language: zh or en

        # Auxiliary models; required.
        models:
          embedding:                  # OpenAI-compatible /v1/embeddings
            endpoint: ''
            model: ''
          reranker:                   # Cohere/Jina-compatible /v1/rerank
            endpoint: ''
            model: ''
          decision:                   # TypeSafe /v1/systemone
            endpoint: ''
            model: ''
            apiKeyEnv: ''             # environment variable holding the key; or write apiKey: <key> instead, not both

        limits:
          persona:                    # item limits of the persona sections
            redLines: 5
            workingRules: 20
            preferences: 15
            profile: 10
          itemLength: 100             # max characters per item, source comment excluded
          mainline: 5                 # max mainline items
          page:
            titleLength: 50           # max characters in a page title
            aliasLength: 30           # max characters per alias
            minAliases: 2             # min number of aliases

        candidates:
          minDays: 2                  # distinct days a persona candidate must appear on before it can be confirmed

        checkup:
          remindAfterDays: 7          # days after the last checkup before reminding
          mainlineStaleDays: 14       # days without update after which the mainline counts as stale

        # Upper and lower thresholds of each judgment; built-in defaults. Uncomment to change.
        # decision:
        #   samePreference: { certain: …, unlikely: … }
        #   samePage:       { certain: …, unlikely: … }
        #   sameTopic:      { certain: …, unlikely: … }
        #   ruleConflict:   { certain: …, unlikely: … }

        retrieval:
          candidates: 30              # candidates taken from full-text and vector search each
          topK: 5                     # max results returned
          # minRerankScore: …         # lowest reranker score kept; built-in default

        backup:
          keep: 7                     # automatic backups kept; 0 turns them off
          # remote: <Git remote>      # if set, the memory repository is pushed after each automatic backup

        """);
}
