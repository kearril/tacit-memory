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
        # TacitMemory 配置。各项均可省略，省略时取注释中的默认值。

        server:
          port: 18888                 # 常驻服务在 127.0.0.1 上监听的端口

        memory:
        {memory}
        language: {language}                  # 记忆语言：zh 或 en

        # 辅助模型，均可省略：省略的模型即未配置，服务照常运行、按降级工作。三个模型的 url 都是完整请求地址，程序原样请求、不追加路径；
        # apiKey 可省略，有密钥时以 Authorization: Bearer 发送。
        # models:
        #   embedding:                  # 通用协议：OpenAI Embeddings
        #     provider: ''              # 可省略；cloudflare 与通用协议相同
        #     url: ''                   # 完整请求地址
        #     model: ''
        #     apiKey: ''                # 可省略，改设环境变量 TACIT_EMBEDDING_API_KEY（两处都有时以环境变量为准）
        #   reranker:                   # 通用协议：Cohere/Jina Rerank
        #     provider: ''              # 可省略；cloudflare 为 Cloudflare Workers AI 的格式
        #     url: ''                   # 完整请求地址
        #     model: ''
        #     apiKey: ''                # 可省略，改设环境变量 TACIT_RERANKER_API_KEY（两处都有时以环境变量为准）
        #   decision:                   # 通用协议：System One
        #     provider: ''              # 可省略；cloudflare 为 Cloudflare Workers AI 的格式
        #     url: ''                   # 完整请求地址
        #     model: ''
        #     apiKey: ''                # 可省略，改设环境变量 TACIT_DECISION_API_KEY（两处都有时以环境变量为准）

        limits:
          persona:                    # 画像各节的条数上限
            redLines: 5
            workingRules: 15
            preferences: 10
            profile: 10
          itemLength: 200             # 每条最多字数，不含来源注释
          mainline: 5                 # 近期主线最多条数
          page:
            titleLength: 50           # 细节页标题最多字数
            aliasLength: 30           # 每个别名最多字数
            minAliases: 2             # 别名最少个数
          domains: 10                 # 领域最多个数
          domainEntries: 10           # 每个领域最多条数
          triggerLength: 60           # 领域的触发描述最多字数
          brief: 3000                 # 主线简报最多字数，超出时只截短页目录

        consolidation:
          remindAfterDays: 7          # 距上次整理多少天后提示
          remindCandidates: 5         # 候选达到多少个时提示
          budgetSeconds: 20           # 一次整理最多等模型多少秒，到点照常出报告
          maxConcurrency: 5           # 一次整理同时进行的模型判断数

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
          # 云端备份（WebDAV）的地址与账号；password 可省略，改设环境变量 TACIT_WEBDAV_PASSWORD
          # webdav: { url: https://…/tacit/, username: …, password: … }

        """,
        """
        # TacitMemory configuration. Everything may be left out; the defaults are in the comments.

        server:
          port: 18888                 # port the memory service listens on at 127.0.0.1

        memory:
        {memory}
        language: {language}                  # memory language: zh or en

        # Auxiliary models, all optional: a model left out is unconfigured, and the service runs without it, degraded. Each url is
        # the complete request address, sent as written, with no path added. apiKey may be left out; when set, it is sent as a Bearer token.
        # models:
        #   embedding:                  # OpenAI Embeddings protocol
        #     provider: ''              # may be left out; cloudflare is the same as the generic protocol
        #     url: ''                   # complete request address
        #     model: ''
        #     apiKey: ''                # may be left out: set the environment variable TACIT_EMBEDDING_API_KEY instead (the variable wins if both are set)
        #   reranker:                   # Cohere/Jina Rerank protocol
        #     provider: ''              # may be left out; cloudflare is Cloudflare Workers AI's format
        #     url: ''                   # complete request address
        #     model: ''
        #     apiKey: ''                # may be left out: set the environment variable TACIT_RERANKER_API_KEY instead (the variable wins if both are set)
        #   decision:                   # System One protocol
        #     provider: ''              # may be left out; cloudflare is Cloudflare Workers AI's format
        #     url: ''                   # complete request address
        #     model: ''
        #     apiKey: ''                # may be left out: set the environment variable TACIT_DECISION_API_KEY instead (the variable wins if both are set)

        limits:
          persona:                    # item limits of the persona sections
            redLines: 5
            workingRules: 15
            preferences: 10
            profile: 10
          itemLength: 200             # max characters per item, source comment excluded
          mainline: 5                 # max mainline items
          page:
            titleLength: 50           # max characters in a page title
            aliasLength: 30           # max characters per alias
            minAliases: 2             # min number of aliases
          domains: 10                 # max number of domains
          domainEntries: 10           # max entries per domain
          triggerLength: 60           # max characters in a domain's trigger
          brief: 3000                 # max characters in a workstream brief; only its page index is shortened

        consolidation:
          remindAfterDays: 7          # days after the last consolidation before reminding
          remindCandidates: 5         # candidates at which to remind
          budgetSeconds: 20           # seconds one consolidation waits for the models before it reports what it has
          maxConcurrency: 5           # model judgments one consolidation runs at once

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
          # cloud backup (WebDAV): address and account; password may be left out and set in TACIT_WEBDAV_PASSWORD instead
          # webdav: { url: https://…/tacit/, username: …, password: … }

        """);
}
