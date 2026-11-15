# TacitMemory 测试规范

本文档定义测试接缝、测试环境与运行方式。

## 1. 三个测试接缝

测试只针对公开契约编写，不直接测试 `internal` 实现（读取 `internal` 类型的例外见第 3 节）。等待后台备份、向量补齐或整理判断写入缓存完成时，测试使用 `MemoryService` 的 internal 钩子 `BackupCompletion`、`VectorCompletion`、`JudgmentCompletion`，它们不属于公开契约。读取使用次数时测试使用 internal 的 `PageUsages()`（返回 internal 的 `PageUsage`）与 `ActivityUsages()`（按短码返回活动的次数），它们也不属于公开契约。让自动备份停在打包阶段时，测试在 `TacitOptions` 上设置 internal 钩子 `BeforeBackupPack`（打包前等待），见 `BackupRaceTests`。让重新加载打不开运行时，测试设置 `MemoryService` 的 internal 钩子 `OnOpening`（每次打开运行时先以配置调用它，从中抛出即打开失败），见 `ReloadTests`。

1. **`tests/TacitMemory.Core.Tests`：门面接缝**
   - 通过 `TacitMemory.Core` 的公开门面测试；
   - 每个测试使用独立的临时用户主目录、临时 `TACIT_HOME` 与临时记忆仓库（`TestHome`），真实文件、真实 `git`；密钥环境变量由 `TestHome.EnvironmentVariables` 注入，测试不读取开发机的真实环境变量；绑定只写入临时用户主目录，从不碰真实的客户端配置。
   - 测试类索引（每个测试类一行，按模块分组）：
     - `InitTests`：`init` 生成的文件与首个提交（`config.yaml` 只写 `language` 一行）、拒绝与重复执行、与已有配置冲突、语言推断、末尾的 Web 界面地址（取自 `server.port`）。
     - `ConfigTests`：配置的默认值（含主线简报上限 `limits.brief` 与别名最少个数的默认值；已删除的键（清单见 `docs/reference/configuration.md`）按未知键报错并点名键路径；`consolidation.hintItemLength` 与 `hintResidentLength` 的默认值与校验）、校验错误与定位、未初始化提示、`rules.md` 的生成与超限；模型映射只认 `provider`、`url`、`model`、`apiKey`，`endpoint` 等其他键按未知键报错并点名键路径；`provider` 缺省为通用协议，`cloudflare` 不区分大小写，其他值报错并列出取值；`url` 必须为 http 或 https 绝对地址、原样保存；`model` 为空报错；三个模型的密钥各自的环境变量优先、配置值与都没有；以及 `backup.webdav` 的校验；`server.host`：IP 地址文字（含 `0.0.0.0`、`127.0.0.2`、IPv6）保留，`127.0.0.1` 与不设相同，主机名、带端口、方括号与 `1` 这类写法报错并点名键路径。
     - `ConfigWriterTests`：配置写出器：读出再保存的配置加载后与原来相同；只写与默认不同的键，按配置参考的顺序，不带注释；校验不通过的配置被拒绝，点名全部出错的键路径，文件保持原样。
     - `ReloadTests`：重新加载：启动后保存的设置在重新加载后生效；需重启的项保留运行中的值；新配置打不开时以原配置继续；经保存打不开的配置使文件写回原样；保存保留文件中的密钥；重新加载期间到达的写入等它完成后执行；启动时间与额外监听的结果跨过重新加载保留；两种配置都打不开时服务报告无法继续。
     - `LocalizationTests`：中英文本表的完整性，以及 `init` 写出的标题是否属于对应语言（见第 3 节）。
     - `ShowTests`：`tacit show` 的输出：各段来源与长度、`--default` 内置文本、内置核心规则的三节顺序、标签随记忆语言；值得记标准的来源与条数，内置默认另存为文件后服务端说明不变；常驻区块各节的引导语；服务端说明的最近活动段不含主线列表；工具清单的标签取自已加载的配置语言，而非系统界面语言。
     - `CommandSkillTests`：两个记忆指令 skill 的成品：生成，frontmatter 含 `disable-model-invocation: true` 与简短的 `description`；服务启动写出全部 skill，内容不变时不重写；`tacit-remember` 不依赖记忆仓库；`tacit-consolidate` 指向资源 `tacit://consolidation-report`、`tacit serve` 与 `consolidate:`；主线条目（背景说明与近况两行）的示例、"只处理主线"的附加文字示例；中英两种记忆语言下的结构词与示例；`show remember` 与 `show consolidate` 的输出，以及 `--default` 被拒绝。
     - `ConsolidationReportTests`：整理报告（资源正文）：绝对路径与 `git -C` 用法、A、B 两部分及其类别的顺序与标题、无内容写 None.、限时的说明（头部）、上次整理的三种基准（`consolidate:`、`init:`、都没有）、缺少页面类型的格式问题不受基准限制、每类 20 项上限与剩余数量、取消；B 部分的画像证据（全部候选附作用域、归属与原话，原话注释只取基准以来新增的活动并按日期排列）、教训（只取基准以来变化的 `incident` 页）、变化过的页（路径、类型、缺失时的 `untyped`、大小、小节日期与使用次数，不附正文，未变的页不出现，变化后出现）、只有 1 条的领域；B 部分的主线提案材料（未成立的主线名附页数与活动数，专题中同前缀至少 3 页的组列出最长共有前缀与各页标识，不标注主线的活动只列基准以来的，合并后旧名字不再出现）。
     - `ConsolidationCandidateTests`：候选清理的三档（确定列为待清理并写明画像短码或拒绝列表条目，拿不准附提示且仍可确认，否定两者都不列）、选项只取画像与拒绝列表、无可比对时不提问、决策与嵌入模型不可用时的降级、阈值改变、判断缓存（内容不变不再调用、改阈值分档改变而不调用、重建索引后重新调用）。
     - `ConsolidationConflictTests`：互相冲突的画像条目：只判断上次整理后新增或改写的条目（含未提交的修改）、跨节、同一对只报一次、确定与拿不准两档标明档位、无其他条目时不提问、决策模型不可用时的降级。
     - `ConsolidationDuplicatePageTests`：疑似重复的页：只判断变过的页（含未提交的修改），未变的页对不再报；同一无序页对只报一次；确定与拿不准两档标明档位，否定不收；同主线没有其他页时不提问；删除的页不参与；没有 `consolidate:` 与 `init:` 提交时全部页都算变过；决策与嵌入模型不可用时的降级；限时到点的未检查数量。
     - `ConsolidationBudgetTests`：整理限时（到点照常返回并注明未检查数量、在途调用不取消且跑完写入缓存、未发出的判断不再发出；到点后仍有缓存答案的类别照常列出，前面类别耗尽预算不影响后面类别；候选的判断被截断时附"自行对照画像"的提醒）、工作池的并发上限、模型冷却后排队的提问不再发出，以及冷却结束后再次提问；推进限时的用例断言替身未收到被客户端中断的连接（`ModelDouble.AbandonedRequests` 为 0），避免断线的重试被计时器误触发后表现为多一次调用。
     - `FormatViolationTests`：整理报告的"违反格式或规则"：细节页的 YAML 头、别名最少个数、相关页、文件名、小节重名、非 UTF-8 文件、混用结构词（仅提示；小节标题（含领域、主线、触发、作用域与教训，两种语言各一例）、归属行、原话与拒绝标记、列表项中的改为与 instead）；汇总节的格式问题（无法识别的行、不存在的周号或月份、写错文件的周或月；周号按 ISO 周的年份与周数计，跨年的周按周四所在月）；长标题与长别名不再报告；各项修正后消失，每类 20 项上限。
     - `ConsolidationReminderTests`：整理提示：天数门槛（含 `consolidation.remindAfterDays`）、候选即出现、只有格式问题不出现、候选在运行中写入后的下一次取用出现、整理提交（含空提交）后消失、生成时不调用模型、仓库不可读时省略且服务不中断、`tacit show instructions` 含提示与否；四个触发条件各自独立生效（天数、未汇总的周、候选达到 `consolidation.remindCandidates`、主线为空），低于阈值的候选数不出现，只有格式问题不出现。
     - `BindingTests`：`connect` 与 `disconnect` 的门面规则：名称规范化与拒绝（逐例断言拒绝时不写任何文件）、同名异路径的拒绝、上下文文件的上一级目录与 skill 目录不存在时的拒绝、`~` 展开与绝对路径的记录、记录的四个字段、上下文文件的创建与删除（`createdContextFile`）、常驻区块的写入与逐字节恢复、客户端 MCP 配置的字节不变、缺失的名称与配置无效时的 `disconnect`；共用：两个名称共用 skill 目录与上下文文件时的单一常驻区块与三个链接、两种解绑顺序、`createdContextFile` 的继承、只共用上下文文件、Windows 上大小写不同的同一路径、一次渲染只返回一个结果、名称绑到其他路径时的拒绝。
     - `SkillLinkTests`：三个 skill 的目录链接：三个位置都空时全部建立；任一位置被占用（目录、文件或指向别处的链接）时整体拒绝，三个链接都不建；skill 目录不存在或是文件时拒绝且不写；重复 `connect` 补齐缺失的链接；后续步骤失败时回滚；`disconnect` 删除三个链接、报告已缺失的链接并保留别人的同名项；只有旧的记忆 skill 链接的绑定（Windows 上为真实目录联接）；skill 目录被两个客户端共用时的复用报告，以及两种解绑顺序下链接的保留与拆除。
     - `ServiceTests`：服务端说明的组成（不含主线列表）、语言部分、状态提示，以及启动与运行中的绑定渲染（共用的上下文文件只渲染一次）。
     - `StatusTests`：`tacit status` 的报告：服务运行中（地址、版本、运行时长）与未运行（含端口被其他程序占用，服务栏含 MCP 端点），三个辅助模型的可用、冷却中（含剩余秒数，冷却结束后回到可用）与未配置（与可用、冷却中并列），日志栏（日志位置；今天的警告与错误条数在运行、离线与配置无效三种报告中一致，没有文件时为 0，有错误时为问题；文件读不了时条数未知、说明原因并为问题），配置有误时的错误键路径与服务栏，宽松读取的端口与语言（`server.port` 可读而其他键有错、文件不存在、YAML 无法解析三种情况）；离线报告不含依赖服务的列，其辅助模型栏只列未配置的模型；服务栏的 Web 界面地址行，额外监听的行（绑定时的地址、失败时的问题级别与原因、端口被占与非本机地址两种原因），失败时记一条警告。
     - `StatusColumnsTests`：`tacit status` 的各栏（不依赖服务的部分）：绑定的四种坏情况（文件缺失、非 UTF-8、标记不完整或重复、三个 skill 链接中某一个缺失或指向别处），每种检查前后文件字节不变（不含 `.cache/`）；共用路径的问题只出现一次、挂在第一个引用它的绑定上，`SharedWith` 列出共用的其他绑定；上次整理的三种基准（初始提交、整理提交、从未整理），改名前的旧前缀提交不作为基准；待处理计数（含待汇总的周）与整理提示相同且不调模型；仓库历史不可读时其余栏照常；最近本地备份在三种前缀中取最新；密钥来源栏列出三个模型的密钥与 WebDAV 密码的来源（环境变量、配置文件或"未设置"，"未设置"不算错误、不上色，不显示值），未配置的模型即使设了环境变量也只写"未配置，不需要密钥"；服务运行与未运行时共用的栏内容一致；离线辅助模型栏只列未配置的模型，三个都已配置时省略，在线与离线中"未配置"的写法一致；客户端栏：无记录时一行说明，有记录时每个客户端一行（名称、日期、协议版本、能力、扩展，能力与扩展为空时都写"无"，不显示版本），服务未运行时同样显示。
     - `ClientRecordTests`：客户端能力记录的核心行为：首次见到写文件并记一条日志；同一天重复既不写文件也不记日志；协议版本变化或能力、扩展的并集变大再记一条，不变大不记；跨天只更新日期；名称（忽略大小写）为键，版本不同仍是同一条；能力与扩展取历次声明的并集；声明的文字去掉控制字符；服务重启后记录仍在；文件无法写入时记警告且调用照常返回；备份不含该文件。
     - `ResidentDocumentTests`：常驻区块外壳：`resident.md` 的替换、节的校验与拒绝（含已删除的节名）、运行中修改与删除；`workstreams` 节的内容与顺序（位于领域索引之后，活跃在前、按最近活动倒序，安静按名称排序）、活跃与安静的显示、未成立的名字与格式不全的条目不进入常驻区块、全部领域都列出、空节不出现、`resident.md` 为新节给出的引导语；主线文件与活动记录变化后重新渲染；经 `TestHome.Clock` 跨过 28 天窗口边界或本地午夜后重新渲染。
     - `PromptCheckTests`：不保存地校验提示词文档：没有文件时核心规则与值得记标准给出相同的固定部分合计、上限与最近活动的余量，值得记标准的条目数；常驻区块与写法约定给出自身的字数（CRLF 按 LF 计）；独占一行的注释不计入核心规则，值得记标准按排版后计（前言、续行与其他小节不计）；核心规则以运行中的值得记标准合计；超出固定部分时余量随之减少并报告拒绝；值得记标准两节都超出时各报一条；每类保存拒绝（核心规则超限、值得记标准超条数、常驻区块的未知节名与超长、写法约定的 frontmatter 与超长）都由校验以相同文字报告；校验不写文件。
     - `PromptProblemTests`：服务运行中改坏的提示词文档：读取得到原因、文本为文件现在的内容、服务端说明仍为上一版；固定部分合计超限时核心规则与值得记标准都标出；改正文件、在页面上保存、恢复默认后各自清除；状态报告只在有误时带出「提示词文档有误」一栏，每行为提醒级别。
     - `PromptSaveTests`：经服务保存与恢复提示词文档：首次保存以 LF 写文件且服务端说明立即采用；保存写法约定立即重新生成记忆 skill，保存常驻区块立即重新渲染，且防抖之后不再渲染第二次；违反规则（值得记标准超条数、常驻区块的未知节名、核心规则与运行中的值得记标准合计超出固定部分）时拒绝并指出文件，不写文件；文件在读取后被改过或已出现时不覆盖；按当前哈希保存时替换；恢复默认删除文件并立即生效，文件不存在时不变；内置值得记标准放不下长核心规则时拒绝恢复并保留文件；未知或大小写不同的文档名拒绝。
     - `PromptDocumentTests`：`prompts/` 下的 `rules.md` 与 `instructions.md`：`init` 不生成提示词文档、缺失 `rules.md` 时的内置值得记标准（中英）、运行中删除后回到内置默认、核心规则的替换、通用规则、HTML 注释的处理、备份包含 `prompts/`。
     - `LogTests`：写入的日志事件：三个写入工具与 `recall` 的事件含页面标识或短码、不含正文、原话与查询文本；决策模型最终失败的冷却警告；任何事件都不含三个模型的密钥；启动事件；通知同时显示并记录，自动备份事件；管理 API 调用除成功的 GET 外各记一条。`CapturingLoggerFactory` 保存全部事件供搜索。
     - `ServiceLogTests`：读回服务日志：有日志文件的日期（最新在前，其他文件不算）；每条的时间、级别与消息，续行并入前一条，第一条之前的行自成一条且没有时间，VRB、DBG、FTL 归入最近的三级之一；末尾没有换行的一行留到下次读；服务正写着的文件照样能读；`after` 之后的条目与总条数，`after` 不是非负整数被拒；今天没有文件时为空，其他日期没有文件时找不到，日期不是日历日期时被拒。
     - `MemorySkillTests`：记忆 skill 成品的生成、frontmatter、每种页面类型一例、活动的坏例与好例（中英两种记忆语言各一对）、`skill.md` 的校验与运行中更新；SKILL.md 被占用而替换被拒时，提示词的改动在占用结束后仍会应用。
     - `PageTests`：细节页的新建与读取、按短码修改小节与页头、`section` 误填短码的拒绝（含 `hash` 同填、大写或带空白的短码、同名小节按名字改写、不属于本页的短码样值照常新建）、`dates`、串行写入、页面标识校验、未知参数的拒绝（点名、与 op 问题一并列出、下一步、不写文件）。
     - `DuplicatePageTests`：新建细节页时的重复页判断与放行。
     - `CredentialTests`：凭据检测与 `confirm_no_secret`，含小节与页头写入，删除小节不需确认。
     - `RecallTests`：全文检索与索引同步：中文切分、筛选、结果呈现，索引重建与跳过非 UTF-8 页面；未知参数的拒绝（查询与读页两路，未知参数排在 query/page 问题之前）；小节标题后的最后修改日期（只有变化的小节更新）；使用统计不影响检索排序。
     - `RecallPersonaTests`：`recall` 的 `kind=persona` 与 `kind=rules`：整份画像的输出顺序（全局四节、领域块按标签、主线块按名称）、触发描述与条数、原话注释不输出、短码可用于 `replace` 与 `delete`、忽略 `query`、空画像与无领域时返回 `No relevant memory.`、未知领域的拒绝并列出已有标签、`domain` 只与 `rules` 搭配，以及与 `page`、`workstream`、`from`、`to` 同时出现的拒绝。
     - `WorkstreamBriefTests`：主线简报（`recall` 只给 `workstream`）：已成立的主线开头为背景说明与近期摘要（缺省时只有背景说明），未成立的名字不带这两行，分组与顺序（有效决定、已知坑、工作、实体、未分类），同日按使用次数排序，推翻记录与「已推翻」一行，未决事项，主线约定只取主线作用域，五段的先后，最近 3 条活动，最近一次周汇总（只取该主线活动所在周中最近一个有汇总的周），`from`、`to` 限定页面与活动，空主线返回 `No relevant memory.`，与 `kind` 同给被拒绝，每个列出的页计一次使用统计，页目录按最旧优先截断并注明省略
     - `VectorTests`：向量检索、嵌入模型不可用时的降级与补齐、模型或向量长度变化后的重建。
     - `ReindexTests`：`reindex` 的结果：重建前后同一查询的结果一致、每个单元都有向量；重建后判断缓存清空，整理重新调用决策模型；嵌入模型不可用时只重建全文并注明向量待补；嵌入模型未配置时只重建全文并注明未配置；非 UTF-8 文件列入结果；重建后小节日期取文件最近一次提交、未提交的文件取当天；开始、结束与结果写入日志。
     - `UsageStatsTests`：使用统计的计数规则（整页读取每个小节各加一、检索计入所显示的单元、页面级命中只计标题与别名单元）、最近返回日期随时钟前移、从未返回的页无计数与日期、编辑小节后计数保留、新增活动不改变同月其他活动的计数、`reindex` 清零。
     - `MaintenanceTests`：维护操作独占：维护期间到达的写入、`recall` 与服务端说明排队，完成后照常执行；两个维护操作依次执行。用挂起的嵌入替身让重建停在补齐阶段，等待的调用在短暂停顿后仍未完成，才断言它们被挡住。
     - `RerankTests`：reranker 精排的排序、过滤、不可用时的处理与恢复。
     - `ModelProtocolTests`：三个模型的请求经 `ModelDouble` 收到：每个模型请求其配置的完整 URL、不追加路径（路径与配置一致）；嵌入、reranker、决策模型各自的请求字段；有密钥时带 `Authorization: Bearer`，没有密钥时不带认证头；Cloudflare 的 reranker 请求体含 `query` 与 `contexts`、不含 `model`，`recall` 按 `id` 对回的分数排序与过滤；Cloudflare 决策请求与 System One 同体，响应取 `result` 后判断（新建重复页被识别）；`success` 为假、缺 `result` 或缺 `answers` 只调用一次并按不可用降级；`provider: cloudflare` 的嵌入请求与通用协议相同。
     - `ModelRetryTests`：辅助模型的请求内重试：429、5xx、网络错误与单次超时在第 2～5 次成功时模型仍可用；5 次都失败后冷却；401/403、400/404/422 与格式不对的响应只调用一次；可重试与不可重试的失败、退避与 `Retry-After`、20 秒总时限、冷却与冷却后的再次尝试，以及 `recall`、实时写入判断与整理判断经过重试。
     - `ProbeTests`：`tacit probe` 经门面：通用协议与 Cloudflare 的成功、4xx 带响应体且截断到 500 字符、5xx 只调用一次不重试、格式不对与 Cloudflare `success` 为假、超时（20 秒，经 `TestHome.Clock`）、网络错误、未配置不发请求、请求体只含内置文本、失败的探测不使服务中的模型进入冷却、回显的密钥被替换。
     - `UnconfiguredModelTests`（经门面）：三个模型都不配置或只配置部分时服务照常启动，写入、画像候选与检索照常，返回中不附模型说明；未配置的模型不发请求、不进入冷却，服务端说明只列冷却中的模型；整理对依赖未配置模型的类别标为"未检查（未配置）"且不提问；`tacit status` 的模型项为未配置；去掉嵌入模型后向量保留，加回同一模型名不重新嵌入，换模型名则丢弃并补齐；启动日志一行列出未配置的角色且不含地址与密钥。
     - `AccessPasswordTests`：访问密码的设置、校验、清除与修改（本机免当前密码，非本机须给出且计入限速），会话的签名与 30 天有效期，状态文件的内容与损坏时的处理；登录与修改的限速：暂停期内即使密码正确也拒绝，暂停结束后的写入清理计数（经可替换的时钟），计数表有上限且丢弃最旧的，同时到达的错误密码先计数后校验。
     - `BackupTests`：每日自动备份的生成、保留、关闭、明文密钥提示（配置中写了任一模型的明文 `apiKey` 或 WebDAV 明文密码即提示，环境变量覆盖时仍提示）与失败处理。
     - `BackupRaceTests`：自动备份在写入槽中打包：打包期间到达的写入排队等待，不与之争用文件。
     - `ManualBackupTests`：手动备份的位置与文件名（本地时间）、内容与清单（`kind`、`memoryHeadSubject`）、写入队列中排在其前的写入、不被自动清理、`--to` 创建目录、同秒重名拒绝且不覆盖、`keep: 0` 时照常、明文密钥提示、相对目录被拒。
     - `RestoreTests`：恢复的预览（备份信息与将被替换的内容，`--include-config` 时三项替换）、旧清单（无 `kind`、`memoryHeadSubject`）、无清单拒绝；清单与内容不符、条目位置越界（`..`、绝对路径、未列出的文件）各自拒绝且什么都不改、不生成保险备份；仅恢复记忆仓库；`--include-config` 替换配置与提示词并删除多余的提示词文件；`bindings.json` 永不恢复；恢复前先生成保险备份并记录日志；恢复后的检索、服务端说明与常驻区块反映备份内容；恢复回来的 `memory.path` 不同时的提示；换入失败时回滚（记忆仓库回到恢复前内容）；恢复期间到达的写入落在恢复后的仓库中；恢复回来的 `config.yaml` 不能加载（如含旧版 `apiKeyEnv`）时恢复照常完成、结果附错误说明，预览同样提示；恢复结束后记忆仓库内没有残留的 `.tacit-*` 暂存目录；`path` 与 `remote` 的来源校验、`remote` 只能预览的规则由门面拒绝。
     - `RestoreTests` 另含带配置的恢复：恢复回来的配置立即重新加载，并列出等待重启的项；恢复回来的配置无法加载时服务按原配置继续、不列等待重启的项，结果中说明出错的键路径；不带配置的恢复不重新加载。
     - `RemoteBackupTests`：云端备份经 `WebDavDouble`：PUT `.part` 后 MOVE 覆盖（`Overwrite: T`，基本认证头）；第二次上传替换远端内容；父目录缺失时 MKCOL 后重试一次；MOVE 不支持（405）时报错、远端旧文件不变、`.part` 被删；上传中途断开时远端旧文件不变；认证与连接失败的阶段；错误与日志不含密码；`remote.json` 的记录与 `status` 的云端行（不访问网络）；未配置时的提示；目录被拒；无用户名时不发认证；配置中的明文密码提示；本地 `backups/` 中不留副本。
     - `RemoteRestoreTests`：恢复云端备份经 `WebDavDouble`：预览读取远端文件、下载文件位于 `backups/.download/` 且不计入最近的本地备份；确认的下载恢复为远端内容、生成保险备份并删除下载文件；远端没有备份时拒绝、什么都不改、不留下载文件；认证失败拒绝且不泄露密码；非备份的远端文件拒绝、什么都不改、保留下载文件；未配置时的提示。
     - `RecentActivityTests`：新增活动与 `replaces` 改写、回复只有一行（附新短码）、写入不改动 `workstreams.md`；已删除的 `op`、`items`、`hash` 与其他未知参数按名拒绝，下一步指向 `tacit-consolidate`。
     - `SameTopicTests`：近期活动的同主题判断与退回。
     - `RecallActivityTests`：检索近期活动：`kind` 筛选、过滤、主线不入索引、同步。
     - `SummaryTests`：周汇总与月汇总：检索命中并标注是汇总及其周或月、跨年的周按 ISO 日期筛选、不进入页面与主线筛选、不进入同主题判断、写入新活动不破坏汇总节（字节不变，新日期标题位于汇总节之前）、手改后经写入同步。
     - `ConsolidationSummariesTests`：整理报告中的汇总材料：已结束、有活动但没有汇总行的周（跨月的周只列一次且汇总写入周四所在月的文件、只有部分天数的周、没有主线的活动归为"其他"、跨年的 ISO 周、进行中的周与已汇总的周不列）与月（附写入该月文件的周汇总）；主线材料只列已成立且在上次整理之后（含进行中的周）有标注活动的主线，附当前条目与这些活动，三种基准（`consolidate:`、`init:`、都没有）都正确；没有新活动的主线不列，没有已成立的主线时写明一行。只经门面读取报告，不直接读取 `SummaryLines` 或 `SummaryCalendar`。
     - `RecentSummaryInstructionTests`：服务端说明中的最近 7 天活动：`<recent_activity>` 标签包裹、不含短码与主线、排序、不设条数上限、超出 `TotalLimit` 时先截最早的活动（固定部分保持完整）、窗口内全部活动都列出、窗口随日期前移、手改活动记录后的刷新。
     - `PersonaAddTests`：画像 `add` 的校验、节标题补回、凭据、备份与常驻区块更新；未知参数的拒绝、缺 op 时的参数列表与 op 问题合并；作用域：领域与主线块的创建与放置（全局四节之后，领域在前、主线在后，各自按名称排序）、块内节的补回、新领域缺少或多行的触发描述被拒、`scope` 格式不合规被拒、写入不因条数或长度被拒（全局节、领域与领域个数都不设上限）、跨作用域的短码后缀。
     - `PersonaReplaceDeleteTests`：画像 `replace` 与 `delete` 的定位、校验与回显；按短码跨作用域定位，`replace` 保持原作用域，删除领域条目后块保留标题；拒绝时的短码不附整份画像，下一步指向 `recall kind=persona`。
     - `PersonaConflictTests`：画像写入时的规则冲突判断，以及决策模型或嵌入模型不可用时的降级；作用域：全局条目只与全局条目比较，领域条目与全局条目及同领域条目比较，`replace` 保持原作用域，冲突拒绝时指出所在领域。
     - `PersonaProposeTests`：候选 `propose` 的写入、校验与标题补回；作用域行（中英两套）的写入与不合规的拒绝，`trigger` 不适用，英文记忆的写法。
     - `SamePreferenceTests`：`propose` 的同一偏好判断、出现记录与拒绝列表；跨作用域：并入候选保留原作用域，选项标明作用域与归属节，问题原文逐字比对，画像与拒绝列表的领域条目也参与比较，无天数门槛的回复。
     - `PersonaIndexTests`：画像与候选不进入派生索引，无法识别的行与非 UTF-8 画像文件的状态提示。
     - `FormatChecksPersonaTests`：整理报告中画像、候选文件、活动记录与主线文件的格式问题（名字不合规、重名、缺背景说明、无法识别的行）：文件、行号与问题说明，修正后消失；作用域块的问题（缺触发行、块内无法识别的三级标题、域名不合规）；长度与条数不列入格式问题，超长的条目与触发行、条目过多的节或领域都不报告。
   - 未自动化：内置常驻区块逐字节不变，已用中英两种默认配置手工比对确认，不写入测试。
   - 未自动化：`serve` 日志文件的按天滚动与保留 14 份（由 Serilog 保证），在验收中手工确认。
2. **`tests/TacitMemory.Host.Tests`：MCP 接缝**
   - 复用 `TestHome`，在进程内以真实 HTTP 启动 `ServiceHost`，用 SDK 的 MCP 客户端调用；
   - 非本机连接的模拟：主机判定「本机」的规则默认为全部回环地址，测试经内部入口 `ServiceHost.TryStartAsync(…, isLocal)` 把它收窄为只有 `127.0.0.1`（`PeerClients.OnlyLoopbackIsLocal`）。`PeerClients.Local()` 从 `127.0.0.1` 连接，是本机；`PeerClients.Remote()` 把本地端点绑定到 `127.0.0.2` 再连接，是非本机。额外监听在测试中经 `server.host` 用 `127.0.0.2`；绑定失败的用例用文档地址 `192.0.2.1`，它不是本机的地址。主机项目对 `TacitMemory.Host.Tests` 开放内部成员，只为这一入口及其探测参数 `probe`（用于模拟探测之后才被占用的额外地址），以及默认的本机判定规则 `ServiceHost.IsLoopback`（直接检查各种写法的回环地址）。
   - 测试类索引（每个测试类一行）：
     - `ServiceHostTests`：服务端说明下发（含 `<recent_activity>`，不含主线列表）、运行模式（默认的 `2026-07-28` 客户端与固定为 `2025-03-26`、`2025-06-18`、`2025-11-25` 的客户端都能连接、取得服务端说明并调用工具，`SessionId` 为空；原始 `initialize` 的响应不带 `Mcp-Session-Id`）、`write_persona` 的 `scope` 与 `trigger` 经 MCP 写入领域块、工具列表与标注、必填参数与取值（`required`、`enum`，含 `write_page` 的 `type` 四个取值）、缺 `op` 与 `op` 取值外的调用到达服务后被拒绝、参数错误标记与三段返回、schema 之外的参数名经原始调用被点名（`write_page` 的 `content`、`recall` 的 `limit`，`write_recent` 的 `op`、`items`、`hash`）、各写入工具的往返读回、端口被占用时不启动；prompt 列表恒为空而不报错；资源列表只含 `tacit://consolidation-report`，读取得到含记忆仓库绝对路径与报告两部分与各类别标题的文本，改名前的旧 URI 读取时报资源不存在；工具说明按配置填入（`minAliases` 改为非默认值并重启后，说明出现新值、不再出现 "by default"，说明中不出现字数与条数），且与 `ToolCatalog` 的说明一致；`recall` 的 `kind` 四个取值（`pages`、`activities`、`rules`、`persona`）与 `domain` 参数。
     - `ManagementApiTests`：管理 API 经真实 HTTP 访问：`/api/status` 的 JSON（camelCase，可读回为报告，密钥来源栏含三个模型的条目，辅助模型的未配置状态以 `unconfigured` 读回）；Origin（须与请求自身同源）、本机连接的 Host（`127.0.0.1`、`localhost` 或 `[::1]`，端口不限）与 POST、PUT 的 Content-Type（非 `application/json` 返回 415）三道防护及其错误体；`/mcp` 对本机不受防护；除成功的 GET 外，一次 `/api` 调用产生一条含路由与状态码的日志事件；状态报告的日志栏带今天的警告与错误条数；`POST /api/reindex` 的 JSON 往返（结果可读回，含跳过的文件与向量待补）；`POST /api/backup` 的 JSON 往返，`to` 为相对路径返回 400（`RequestInvalid`），同秒重名返回 409（`BackupExists`），云端目标未配置返回 409（`CloudBackupNotConfigured`）；客户端能力记录：经 SDK 客户端以给定的名称、版本、能力与扩展连接（2026-07-28 与旧版本的 `initialize`），`/api/status` 的 `clients` 含该客户端与其能力、扩展；同一客户端重复连接只记一条日志事件，能力或扩展的并集变大后再记一条；服务重启后记录仍在。
     - `ConnectionGuardTests`：非本机连接（`127.0.0.2`）、未登录：`/api` 下除会话端点外各端点 401（错误体 `ApiUnauthorized`），会话端点可用；防护先于 401（Origin 不同源 403、POST 与 PUT 非 JSON 415）；非本机连接的 Host 不检查；`/mcp` 对非本机 403（`McpRemoteRefused`），对本机不拒绝；本机连接免登录；401 的调用也记一条日志；路径的字母大小写不影响判定（`/API/status`、`/MCP` 等同样拒绝，且 `reindex` 不执行）；原始请求行的各种写法（尾斜杠、重复斜杠、百分号编码、`..` 段、`/apix`）非本机都得不到 API 或 MCP 的应答；非本机访问 `/`、`/ui`、`/api` 之外的路径 403（`RemotePathRefused`）；本机的 Origin、Host 与 Content-Type 防护同样不因大小写而被跳过；默认的本机判定规则认 `127.0.0.0/8`、`::1` 与映射到 IPv6 的回环地址（含 `::ffff:127.0.0.5`），其他地址不认。
     - `AccessApiTests`：会话与访问密码经真实 HTTP，非本机连接（`127.0.0.2`，不保存 Cookie，每个请求只带测试指定的 Cookie）：`GET /api/session` 的字段与可用性；登录的 401 与 200，Cookie 的属性（`HttpOnly`、`SameSite=Strict`、`Path=/api`、无 `Secure`）；登录后 `/api` 与 `openapi.json` 放行、`/mcp` 仍 403；错误密码、5 次后 429、剩余秒数、60 秒后恢复、成功清零、无密码时也计数、限速按地址（本机不受影响）、修改密码时错误的当前密码同样计数并暂停、429 带 `Retry-After`（剩余秒数）；退出（Cookie 过期、被复制的 Cookie 仍有效直到改密码）；30 天过期（不随使用续期）；改动的 Cookie 被拒；防护先于会话；重启后会话仍有效；设置与修改（本机无需旧密码、非本机须当前密码、修改后旧会话失效、发起者得到新会话、本机修改结束所有远程会话）；清除（非本机 403 且密码保留、非本机未登录 401、本机清除后登录失败、清除结束旧会话）；日志中不含密码与 Cookie 值。
     - `ListenerTests`：未设 `server.host` 时只在 `127.0.0.1` 应答；设了 `server.host` 而没有访问密码时启动被拒绝（`ConfigInvalid`，说明含 `server.host`），`0.0.0.0` 同样；清除密码时 409（本机）与 403（非本机，即使带有会话）；设了则两个地址同一端口都应答（端口 0 时两者同端口）；`0.0.0.0` 与回环并存；额外地址绑定失败或端口已被占用时仍在回环上启动，状态报告标明原因；额外地址在探测之后才被占用时去掉额外监听重试一次，状态报告标明端口被占用；`127.0.0.1` 与不设相同；回环端口被占时仍不启动，包括 `server.host` 为 `0.0.0.0` 的情况。
     - `ManagementErrorTests`：错误体为 JSON 且 `error` 取自 `TacitError`：`backup --to` 指向无法创建的路径返回 500（`FileSystemFailed`），错误说明含原因而不含异常类型名；恢复不存在的文件返回 400（`RequestInvalid`）；请求体读不出时返回 400，说明指出字段，不含解析器原文；`"remote"` 是云端目标的线上值，未配置时返回 409（`CloudBackupNotConfigured`）。
     - `RemoteBackupApiTests`：`POST /api/backup` 的云端目标经真实 HTTP 上传并返回结果 JSON；云端目标与 `to` 同时出现返回 400（`RequestInvalid`）；上传失败返回 500（`CloudBackupFailed`），错误体含 HTTP 状态码，不含密码。
     - `OpenApiTests`：`GET /api/openapi.json` 经真实 HTTP 返回文档，位于 `/api` 之下并受 Origin 防护；登录限速的 429 声明 `Retry-After` 头；`GET /api/backups` 与 `POST /api/probe` 的响应分别声明为 `BackupList` 与 `ProbeReport`；服务日志的条目级别声明为三个值；快照一致性（见第 6 节）。
     - `ActivityApiTests`：`GET /api/activity` 经真实 HTTP：每条活动按其主线标签计数一次，没有标签的计入未归属，跨月文件累计；窗口从本周之前 52 周的周一开始，窗口之前的月文件不读；不是 UTF-8 的月文件被跳过并点名，其余照常返回；整理日取窗口内的 `consolidate:` 提交，历史读不了时整理日为空、计数照常；没有活动记录时返回空窗口；非本机未登录 401。
     - `BackupListApiTests`：`GET /api/backups` 经真实 HTTP：列出自动、手动、保险三种本地备份（完整路径、时间、大小），新的在前，其他文件不列；种类在线上为 camelCase；没有 `backups/` 时列表为空、云端未配置；配置了云端时给出上次尝试且不访问网络；`remote.json` 读不了时没有上次尝试；非本机未登录 401。
     - `PromptApiTests`：`/api/prompts` 经真实 HTTP：没有文件时四份文档按顺序给出内置文本、不带哈希（写法约定为空）；有文件时给出原文与哈希；文档名在线上为小写；每份文档带长度信息；check 返回长度且不写文件，违反规则时 200 并带全部错误，未知文档名 404；运行中改坏文件后 `GET /api/prompts` 与 `GET /api/status` 都带回原因；`PUT` 的首次保存（空哈希，CRLF 写成 LF）、违反规则的 400（`PromptRejected`，问题指出文件，不写文件）、哈希过期或文件已出现的 409（`PromptChanged`）；`DELETE` 回到内置文本并删除文件；未知或大小写不同的文档名 404；非本机未登录 401。
     - `LogApiTests`：`/api/logs` 经真实 HTTP：日期清单（最新在前，带今天）；一天的条目带时间、级别与消息；`after` 之后只给新条目，不是非负整数时 400；今天没有文件时为空，其他日期没有文件时 404，日期不对时 400；非本机未登录 401。
     - `ProbeApiTests`：`POST /api/probe` 经真实 HTTP：按顺序探测三个模型，耗时以毫秒给出；某个模型失败只体现在结果中，请求本身成功；探测读取服务启动后保存的配置；保存的配置无法加载时返回 409（`ConfigInvalid`）；非本机未登录 401。
     - `RestoreApiTests`：`POST /api/restore` 经真实 HTTP：`preview` 为真时只返回预览且不改动；执行返回结果 JSON；无法恢复的 zip 返回 409（`RestoreInvalid`）且错误体含 `message`；缺少 `path`、相对路径与缺少 `preview` 返回 400；非 JSON 的 Content-Type 返回 415。
     - `RemoteRestoreApiTests`：`POST /api/restore` 的 `remote` 预览经真实 HTTP 返回下载文件路径与种类为 `remote` 的预览 JSON；`remote` 与 `path` 同时出现、或执行阶段带 `remote` 返回 400（`RequestInvalid`）且不下载；远端没有备份返回 500（`CloudBackupFailed`），错误体不含密码。
     - `ConfigApiTests`：`/api/config` 经真实 HTTP：每一项的文件值、运行中的值与默认值；保存的设置立即生效、无需重启；校验不通过时返回全部问题且文件不变；基于已被改动的文件的保存被拒绝、改动保留；需重启的项保存后连同运行中的值一并报告；只读项不能从页面修改；额外监听地址要先设访问密码；手动编辑过的文件与运行中的值并列给出；只给出各密钥的来源而不给值，单独请求时给值（环境变量优先）；密钥省略即保留、给值即写入、`null` 即删除，非密钥的键不能按密钥改；关掉的模型从文件与运行中的服务中移除；不认识的 `provider` 在它自己的键上报错；没有会话的其他设备被拒绝。
     - `BindingsApiTests`：`/api/bindings` 经真实 HTTP：绑定时展开用户主目录并给出要注册的 MCP 服务，绑定清单给出同一个 MCP 服务；拒绝时给出与命令相同的原因且什么都不改；不合规则的客户端名称为 400；列表说明绑定哪里有问题；解除绑定，未绑定的名称为 404；没有会话的其他设备被拒绝。
     - `WebInterfaceTests`：`/ui` 的托管（测试用自己的目录代替构建产物）：构建产物原样返回，`assets/` 下的文件长期缓存、入口页为 `no-store`；不带扩展名的路径返回入口页，带扩展名而找不到的路径 404 且不返回入口页；路径越出 `web/` 目录的请求不读取任何文件；产物未构建时返回「Web 界面未构建」；`/` 重定向到 `/ui/`；非本机连接无需登录即可打开界面，大小写不同的前缀同样适用。
     - `ToolCatalogTests`：`tacit show tools` 读取的工具定义，工具与参数的说明、参数类型，不含取消令牌；说明中的别名最少个数取自给定的 `LimitsConfig`，默认值与改动后的值都能填入；说明中不出现已删除的上限数字。
   - 工具的校验与拒绝规则由门面接缝覆盖；两个记忆指令 skill 的正文由 `CommandSkillTests` 覆盖，整理报告的内容由 `ConsolidationReportTests` 覆盖。
3. **`tests/TacitMemory.Cli.Tests`：CLI 接缝**
   - 经 `CommandLine.RunAsync` 在进程内执行真实的命令管道（参数解析、用法检查、执行与退出码）。`CliHarness` 注入 `TestHome` 的 `TacitOptions`（临时用户主目录与临时 `TACIT_HOME`）、捕获 stdout 与 stderr 的写入器、可编排的 stdin，以及 stdout、stderr、stdin 三个终端标志与 `NO_COLOR` 设置；命令只经 `CliConsole` 读写控制台，这是 CLI 唯一的接缝。
   - 需要服务的命令另起进程内的真实主机：`MemoryService` 加 `ServiceHost`，与 `ServiceHostTests` 同样的启动方式，监听一个空闲端口（写入测试配置的 `server.port`）。“端口被其他程序占用”用一个非 tacit 的 HTTP 监听模拟，“端口被占用”的 `serve` 用 `TcpListener` 占住端口。
   - `ConsoleApp.Log`、`ConsoleApp.LogError` 与 `Environment.ExitCode` 是静态状态，因此该项目的测试类都加入 `CliCollection` 集合，禁止并行；新增的测试类必须标注 `[Collection(CliCollection.Name)]`。
   - 测试类索引：
     - `UsageErrorTests`：用法错误为 2，说明只出现在 stderr（未知命令、`show` 缺子命令或子命令未知、`connect` 缺 `--client`、`--context` 或 `--skills`、名称不合规、非法 `--language`、未知选项、`backup` 的 `--to` 与 `--remote` 冲突、`restore` 的 `--from` 与 `--remote` 不恰好一个、`show tools` 等不接受的 `--default`、`probe` 的未知子目标与多余参数）；空路径选项（`restore --from ""`、`backup --to` 为空白、`init --memory ""`）为一行说明的用法错误；用法错误先于配置检查；枚举取值不区分大小写，命令词与子命令也不区分大小写。
     - `ConfigRefusalTests`：配置无法加载（`endpoint` 键，即未知键，导致 3 且说明含键路径；残留的 `candidates` 节同样导致 3 并点名键路径）时 `connect`、`status`、`backup`、`reindex`、`show`（含 `show tools`）为 3；缺少模型密钥与省略整个 `models` 段都不再导致配置无法加载，`connect` 为 0；`disconnect` 与 `show … --default` 照常为 0。
     - `ServiceExitCodeTests`：服务未运行时 `reindex`、`backup`、`restore`、`status` 为 4，说明提示先执行 `tacit serve`；端口被非 tacit 的 HTTP 监听占用时 `status` 为 4；服务运行时 `status` 为 0，纯文本输出无控制字符；服务拒绝 `status` 的报告时为 1、拒绝原因只在 stderr；服务拒绝 `restore` 等请求时同为 1；`backup` 不给选项时写到 `TACIT_HOME/backups/`；`status` 的 Web 界面地址行，服务运行时额外监听的地址行与绑定失败的原因行；服务运行中改坏提示词文档后 `status` 列出「提示词文档有误」一行并退出 3，无误时不出现该栏。
     - `HelpAndOutputTests`：`-h`、`--help`、`connect --help`、`probe --help`、裸命令与 `--version` 为 0；`connect` 只绑定临时用户主目录；`show … --default` 的 stdout 只有内置文本；`show tools` 的 stdout 含配置填入的别名最少个数，且不含字数与条数上限；总览的退出码表只出现一次，总览中出现 `probe`；命令与子命令的词不区分大小写；命令帮助不露出 `[Default` 与可空标记 `?>`。
     - `BindingCommandTests`：`connect` 缺任一选项为 2 且不写任何文件；成功时 stdout 含 MCP 端点 URL；名称不合规为 2；`disconnect` 只需 `--client`、路径取自记录；名称不存在为 1。
     - `RestoreConfirmationTests`：`restore` 的确认（stdin 非终端且无 `--yes` 为 2 且什么都不恢复；终端输入非 `y` 为 1；`--yes` 与 `-y` 为 0）；旧的 `--config` 为用法错误，`--include-config` 可用。
     - `AccessPasswordCommandTests`：设了访问密码后 `status`、`reindex`、`backup`（备份不含 `access.json`）、`restore` 照常返回 0 且不带凭据；`serve` 在 `server.host` 为 `127.0.0.1` 以外的地址而没有访问密码时退出 3 并说明键路径。
     - `ServeTests`：`serve` 端口被占为 1、配置无效为 3、取消令牌（Ctrl+C）为 0；额外监听的地址行在启动提示中（回环地址之后）；额外地址绑定失败时给出警告且服务照常运行；`serve` 写进当天日志的行为「时间 级别 消息」，并能经门面按同一格式读回。
     - `ProbeCommandTests`：`tacit probe` 的退出码 0（全部成功，含大小写不同的子目标）、1（失败；明确指定的模型未配置）、3（配置无效）；不带参数时跳过未配置的模型且为 0；服务未运行、记忆仓库未初始化时照常；输出不含密钥值；纯文本无控制字符；报告随记忆语言。
     - `OutputStylingTests`：不需要服务的 `status` 与 `connect`：stdout 不是终端，或设置了 `NO_COLOR` 时为纯文本、不含控制字符；终端中的美化与纯文本显示相同的文字（含辅助模型未配置的离线 `status`）；`status` 的日志栏在终端与管道中都显示今天的警告与错误条数；终端 stderr 下失败的等待仍为 4；`ClientsColumnCliTests`：`status` 的客户端栏在服务未运行（记录文件中的控制字符不出现在纯文本中）与运行时都显示。
     - `StyledRestoreAndWaitTests`：需要服务的 `restore` 预览（纯文本、`NO_COLOR` 与终端中的美化文字相同），`reindex` 与 `restore --yes` 的转圈只出现在终端 stderr 且 stdout 仍为纯文本，stdout 与 stderr 各自判断。
   - 未自动化：美化后的外观（面板、颜色、转圈）在实机验收中目测。

## 2. 模型依赖

测试不调用真实模型，也不访问网络。`ModelDouble` 是进程内的 HTTP 替身，按用例返回确定的结果。配置中每个模型的 `url` 是替身上的完整地址（`EmbeddingUrl`、`RerankUrl`、`DecisionUrl`，分别为 `/v1/embeddings`、`/v1/rerank` 与 `/v1/systemone`，路由不变）：`/v1/embeddings` 默认返回文本相邻字对的散列向量（措辞相近即相近），也可按用例指定任意文本的向量；`/v1/systemone` 按用例给出选择题各选项的概率（默认选"都不是"）；`/v1/rerank` 默认给每个文本 1.0 分，可按用例指定分数。每个请求都被记录：`EmbeddingRequests`、`RerankRequests`、`DecisionRequests` 各按顺序列出收到的 `ReceivedRequest`，含请求路径（`Path`）、`Authorization` 头（没有则为 `null`）与 JSON 正文（`Body`），用于断言认证头与字段；`LastDecision` 取决策模型的最近一次。`ModelsYaml(embeddingModel, decisionKey, embeddingKey, rerankerKey)` 写出指向替身的 `models` 一节，只在给出密钥时写出对应的 `apiKey`。每个模型都可设为不可用（立即返回 404，这是不重试的客户端错误，因此不涉及重试的测试不会等待退避）。`ModelDouble.ScriptEmbedding`、`ScriptRerank`、`ScriptDecision` 按次编排某个模型的下几次请求：`ModelReply.Status(code, retryAfterSeconds)` 返回状态码（可带 `Retry-After`），`ModelReply.Body` 返回指定正文，`ModelReply.Drop()` 断开连接（客户端看到网络错误），`ModelReply.Hang()` 挂起直到客户端放弃；脚本用完后模型按用例设定的方式回答。`EmbeddingRequestsReached(n)` 等待嵌入模型收到第 n 个请求，用于确定地拨动挂起的请求。`TestHome.InitReady` 默认让所有模型指向一个共享替身：嵌入模型与 reranker 不可用，决策模型对一切选择题答"都不是"，因此不涉及模型的测试不会等待网络，写入也不会被判为重复页；用到模型的测试传入自己的替身。测试中形似凭据的样本在运行时拼接，源码里不出现完整的凭据格式。

`HoldEmbeddings()` 挂起所有嵌入请求，直到 `ReleaseEmbeddings()`，用于让重建停在向量补齐阶段，再拨动它。

Cloudflare 格式的路由：`CloudflareRerankUrl`（`/ai/run/@cf/baai/bge-reranker-base`）与 `CloudflareDecisionUrl`（`/ai/run/clef-flash`）按 Cloudflare 的形状应答，计数与记录同样计入 `RerankRequests` 与 `DecisionRequests`，分数与概率仍由 `Score` 与 `Choose` 指定。请求体含 `contexts` 时答 `{result: {response: [{id, score}]}, success, errors, messages}`，请求体含 `questions` 时答 `{result: {model, answers}, success, errors, messages}`。`CloudflareReportsFailure = true` 时两条路由都答 `success: false` 且无 `result`。`ModelsYaml` 另有 `embeddingCloudflare`、`rerankerCloudflare`、`decisionCloudflare` 三个参数，为真时写出 `provider: cloudflare` 并把对应模型的 `url` 指向上述 Cloudflare 路由。

`WebDavDouble` 是进程内的 WebDAV 替身，与 `ModelDouble` 同样用 Kestrel 实现，`TacitMemory.Core.Tests` 与 `TacitMemory.Host.Tests` 两个项目都链接它。支持 PUT、MOVE、MKCOL、GET 与 DELETE；集合只在建立后存在，父目录缺失的 PUT 与 MKCOL 返回 409。`RequireBasic` 要求 HTTP 基本认证；`MoveSupported = false` 让 MOVE 返回 405（不支持原子替换的服务）；`Answer(方法, 状态码)` 让某个方法固定返回指定状态码；`DropNextPutAfter(n)` 让下一次 PUT 读完 n 字节后断开连接；`Requests` 按序记录每个请求的方法、路径、Authorization、Destination 与 Overwrite；`FileAt(路径)` 读取服务上保存的文件。替身只监听 `127.0.0.1` 的临时端口，测试不访问任何真实的 WebDAV 服务。

重试的等待是 `TestHome.Clock` 上的计时器：每次尝试的超时与每段退避各创建一个，`Advance` 只向前拨动时间，`Elapse` 拨动并触发到期的计时器，`NextTimerAsync(时长)` 等待客户端创建该时长的计时器并认领它。按时长匹配而不按序号匹配，因为写入会触发后台的向量补齐，它也会创建计时器。模型失败后的冷却期同样用 `TestHome.Clock` 的 `Advance` 跨过。

## 3. 本地化完整性

两种写法齐全由编译期保证（见 `docs/architecture.md` 第 3 节）。`TacitMemory.Core.Tests` 中的本地化测试检查编译器看不到的部分：每项两种写法都非空、格式占位符一致；结构词在两种语言之间互不重复；`init` 写出的每个标题都是该语言的结构词。这是直接读取 `internal` 类型的测试：文本表本身就是被检查的对象，没有公开契约可以替代。ISO 周边界与未汇总列表经门面由 `ConsolidationSummariesTests` 覆盖，不直接读取 `SummaryLines` 与 `SummaryCalendar`。

## 4. 提示词的验证

常驻区块外壳、记忆规则、记忆 skill、两个记忆指令与工具说明的措辞不由自动测试固定（自动测试只检查结构与长度上限）。改动以日常使用中出现的摩擦点为依据：Agent 该记没记、记错了地方、参数用错、未经同意改写画像等。改后在真实客户端中按该摩擦点的情形手工复现验收：

- 使用临时 `TACIT_HOME`；`connect` 会写入客户端的上下文文件与三个 skill 链接，事先备份上下文文件，结束后 `disconnect`，恢复并校验 SHA-256，确认三个 skill 链接已删除；
- 保持辅助模型的服务运行，验收期间不要停掉它；
- 用较小的模型验收：提示词要在能力较弱的模型上也达到基本期望；
- 判断依据是可观察的结果：记忆仓库的文件与提交，以及会话记录中的工具调用与 skill 读取。
- 两个记忆指令（实机）：触发 `tacit-remember` 时，触发语前后的文字都到达 Agent；触发 `tacit-consolidate` 后 Agent 读到资源 `tacit://consolidation-report`；会话开始的 skill 列表中只有 `tacit-memory`，普通对话中 Agent 不自行调用两个记忆指令。验收时附加文字写明"只读、不写入、不提交"以免改动真实记忆。

## 5. 运行方式

默认只运行与当前变更直接相关的测试，不自动运行全量测试。全量测试、C# 格式检查与第 7 节的前端检查由 GitHub Actions 运行，触发条件见 `docs/operations.md` 第 6 节。测试使用真实 `git`，需要它在 `PATH` 中。

```powershell
dotnet test tests/TacitMemory.Core.Tests                               # 门面接缝
dotnet test tests/TacitMemory.Host.Tests                                # MCP 接缝
dotnet test tests/TacitMemory.Cli.Tests                                # CLI 接缝
dotnet test tests/TacitMemory.Core.Tests --filter "FullyQualifiedName~InitTests"   # 单个测试类
dotnet test TacitMemory.slnx                                           # 全量
```

C# 的格式由根目录 `.editorconfig` 规定（文件级命名空间、`System` 的 using 在前且不分组、简短的初始化器与匿名对象可以一行写多个成员），用 SDK 自带的 `dotnet format` 检查与修正，不放进编译：

```powershell
dotnet format --verify-no-changes TacitMemory.slnx   # 只检查，有不合格式的文件时失败
dotnet format TacitMemory.slnx                       # 就地修正
```

## 6. OpenAPI 快照

管理 API 的文档快照位于仓库的 `web/openapi.json`，格式固定（两空格缩进、键顺序与生成时一致、末尾一个换行）。`OpenApiTests` 把 `GET /api/openapi.json` 的实际文档与快照比对，不一致即失败。快照的唯一用途是前端类型的生成源，改动任一端点的请求或响应类型后，需要更新快照并重新生成前端类型。

更新方式：设置环境变量 `TACIT_UPDATE_OPENAPI`，运行该测试即改写快照，不再比对；之后不设该变量再运行一次，确认通过。

```powershell
$env:TACIT_UPDATE_OPENAPI = "1"
dotnet test tests/TacitMemory.Host.Tests --filter "FullyQualifiedName~OpenApiTests"
Remove-Item Env:TACIT_UPDATE_OPENAPI
dotnet test tests/TacitMemory.Host.Tests --filter "FullyQualifiedName~OpenApiTests"
```

## 7. 前端检查

前端检查在 `web/` 下用 Bun 运行，不需要运行中的服务，也不需要 `dotnet`。`dotnet build` 与 `dotnet test` 不要求 Bun，两者互不依赖。

```powershell
cd web
bun run typecheck   # tsc --noEmit，含中英字典键一致的检查
bun run lint        # ESLint
bun run test        # Vitest
bun run format:check  # Prettier 只检查格式
bun run format      # Prettier 就地格式化
bun run build       # Vite 构建到 web/dist/
bun run check       # 类型检查、ESLint、格式检查、Vitest 与构建依次运行
```

格式由 `web/.prettierrc.json` 规定：120 列，其余取 Prettier 默认；`prettier-plugin-tailwindcss` 按 Tailwind 的顺序排列类名（包括 `cn`、`cva` 中的字符串），主题与自定义工具类从 `src/styles/index.css` 读取。生成的 `openapi.json` 与 `src/api/schema.ts` 不经 Prettier。缩进、换行符与文件末尾换行由根目录 `.editorconfig` 统一。

Vitest 只测有逻辑的部分：接口调用层（`src/api/client.test.ts`）对 `ApiError` 的取值、会话缺失的 401 信号的发出与退订（其他 401 不发出）、无正文或非 JSON 应答的处理；登录后回跳地址的判断（`src/app/return-target.test.ts`，含拒绝站外地址）；登录表单的状态（`src/pages/login/login-attempt.test.ts`，429 锁住提交直到密码被改动）；错误说明的取用（`src/i18n/messages.test.ts`）。仪表盘的纯函数：时长、倒计时、大小与日期的格式（`src/pages/dashboard/format.test.ts`），热力图的窗口、周列、月份标签、深浅与统计（`heatmap.test.ts`），绑定与客户端合并为每个客户端一行（`agents.test.ts`），待处理的生成与去向（`todos.test.ts`，含每份有误的提示词文档一条、链接到该文档）。设置页的表单模型（`src/pages/settings/form.test.ts`）：草稿、改动、恢复默认、请求体（等于默认的项省略、阈值成对写出、清空即默认）、问题的定位与各节计数、待重启与运行值不一致的项、超出范围与需重启的计数、密钥的改写与清除；绑定预设（`bindings.test.ts`）。提示词页的地址与文档名的互转（`src/pages/prompts/names.test.ts`，未知或大小写不同的名称不算）；编辑状态（`editor.test.ts`）：内置默认在开始自定义前只读、开始后不改动也可保存，写法约定直接可编辑，已自定义时可恢复默认，文件有误时标出且仍可编辑；长度信息的显示文字（`measure.test.ts`）：自身上限的字数、固定部分与最近活动余量、两节条目数（满额不算超出）、超出时的标记与余量为负时的说法。日志页的日期（`src/pages/logs/day.test.ts`）：地址中的日期只认日历日期，今天不写进地址，日期清单含今天且最新在前，时钟取文件中写下的时间；筛选（`filter.test.ts`）：地址与筛选的互转（未知级别忽略、全开时省略），级别与关键字（不分大小写、按字面、含续行）的过滤、各级条数与高亮的切分；跟随（`follow.test.ts`）：增量的追加、跨日的判断、贴底的容差与新条目中通过筛选的条数。不测样式，不做浏览器端到端测试。字典的键一致由类型检查保证，不另写测试。

## 8. 发行包的冒烟

`build/smoke.ps1` 检查一个发布目录能否运行，覆盖"测试在源码构建下通过、发行包里却少了东西"这类问题。它不属于三个测试接缝：作用于打好的产物，以独立进程运行。

```powershell
./build/smoke.ps1 -PublishDir <发布目录> [-ExpectedVersion 0.1.0]
```

使用临时的 `TACIT_HOME` 与空闲端口，不配置辅助模型：运行 `--version`（给了期望版本时必须一致）与 `init`，后台启动 `serve`，检查 `/api/status` 报告服务运行且版本与可执行文件一致、`/mcp` 的 `tools/list` 恰为四个记忆工具、`/ui/` 返回 Web 界面。任何一项失败以退出码 1 结束，并输出服务的标准输出与日志；无论成败都停止服务、删除临时目录。发布工作流在打包后、创建 Release 前运行它，期望版本取自 tag。
