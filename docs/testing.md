# TacitMemory 测试规范

本文档定义测试接缝、测试环境与运行方式。

## 1. 三个测试接缝

测试只针对公开契约编写，不直接测试 `internal` 实现（读取 `internal` 类型的例外见第 3 节）。等待后台备份、向量补齐或整理判断写入缓存完成时，测试使用 `MemoryService` 的 internal 钩子 `BackupCompletion`、`VectorCompletion`、`JudgmentCompletion`，它们不属于公开契约。

1. **`tests/TacitMemory.Core.Tests`：门面接缝**
   - 通过 `TacitMemory.Core` 的公开门面测试；
   - 每个测试使用独立的临时用户主目录、临时 `TACIT_HOME` 与临时记忆仓库（`TestHome`），真实文件、真实 `git`；密钥环境变量由 `TestHome.EnvironmentVariables` 注入，测试不读取开发机的真实环境变量；绑定只写入临时用户主目录，从不碰真实的客户端配置。
   - 测试类索引（每个测试类一行，按模块分组）：
     - `InitTests`：`init` 生成的文件与首个提交、拒绝与重复执行、与已有配置冲突、语言推断。
     - `ConfigTests`：配置的默认值、校验错误与定位、未初始化提示、`rules.md` 的生成与超限；模型映射只认 `provider`、`url`、`model`、`apiKey`，`endpoint` 等其他键按未知键报错并点名键路径；`provider` 缺省为通用协议，`cloudflare` 不区分大小写，其他值报错并列出取值；`url` 必须为 http 或 https 绝对地址、原样保存；`model` 为空报错；三个模型的密钥各自的环境变量优先、配置值与都没有；以及 `backup.webdav` 的校验；已删除的 `apiKeyEnv`、`backup.remote` 与整理改名前的旧配置节按未知键报错；中英模板的 `url` 与密钥变量名。
     - `LocalizationTests`：中英文本表的完整性，以及 `init` 写出的标题是否属于对应语言（见第 3 节）。
     - `ShowTests`：`tacit show` 的输出：各段来源与长度、`--default` 内置文本、标签随记忆语言；工具清单的标签取自已加载的配置语言，而非系统界面语言。
     - `CommandSkillTests`：两个记忆指令 skill 的成品：生成，frontmatter 含 `disable-model-invocation: true` 与简短的 `description`；服务启动写出全部 skill，内容不变时不重写；`tacit-remember` 不依赖记忆仓库；`tacit-consolidate` 指向资源 `tacit://consolidation-report`、`tacit serve` 与 `consolidate:`；中英两种记忆语言下的结构词与示例；`show remember` 与 `show consolidate` 的输出，以及 `--default` 被拒绝。
     - `ConsolidationReportTests`：整理报告（资源正文）：绝对路径与 `git -C` 用法、六类、上次整理的三种基准（`consolidate:`、`init:`、都没有）、候选门槛、主线过期与不过期、每类 20 项上限、取消。
     - `ConsolidationCandidateTests`：候选清理的三档（确定列为待清理并写明画像短码或拒绝列表条目，拿不准附提示且仍可确认，否定两者都不列）、选项只取画像与拒绝列表、无可比对时不提问、决策与嵌入模型不可用时的降级、阈值改变、判断缓存（内容不变不再调用、改阈值分档改变而不调用、重建索引后重新调用）。
     - `ConsolidationConflictTests`：互相冲突的画像条目：只判断上次整理后新增或改写的条目（含未提交的修改）、跨节、同一对只报一次、确定与拿不准两档标明档位、无其他条目时不提问、决策模型不可用时的降级。
     - `ConsolidationDuplicatePageTests`：疑似重复的页：只判断变过的页（含未提交的修改），未变的页对不再报；同一无序页对只报一次；确定与拿不准两档标明档位，否定不收；同项目没有其他页时不提问；删除的页不参与；没有 `consolidate:` 与 `init:` 提交时全部页都算变过；决策与嵌入模型不可用时的降级；限时到点的未检查数量。
     - `ConsolidationBudgetTests`：整理限时（到点照常返回并注明未检查数量、在途调用不取消且跑完写入缓存、未发出的判断不再发出；到点后仍有缓存答案的类别照常列出，前面类别耗尽预算不影响后面类别；可确认候选的判断被截断时附"自行对照画像"的提醒）、工作池的并发上限、模型冷却后排队的提问不再发出，以及冷却结束后再次提问。
     - `FormatViolationTests`：整理报告的"违反格式或规则"：细节页的 YAML 头、标题与别名长度、相关页、文件名、小节重名、非 UTF-8 文件、混用结构词（仅提示；小节标题、归属行、原话与拒绝标记、列表项中的改为与 instead）；汇总节的格式问题（无法识别的行、不存在的周号或月份、写错文件的周或月；周号按 ISO 周的年份与周数计，跨年的周按周四所在月）；各项修正后消失，每类 20 项上限。
     - `ConsolidationReminderTests`：整理提示：天数门槛（含 `consolidation.remindAfterDays`）、可确认候选即出现、只有格式问题不出现、候选在运行中写入后的下一次取用出现、整理提交（含空提交）后消失、生成时不调用模型、仓库不可读时省略且服务不中断、`tacit show instructions` 含提示与否。
     - `BindingTests`：`connect` 与 `disconnect` 的门面规则：名称规范化与拒绝（逐例断言拒绝时不写任何文件）、同名异路径的拒绝、上下文文件的上一级目录与 skill 目录不存在时的拒绝、`~` 展开与绝对路径的记录、记录的四个字段、上下文文件的创建与删除（`createdContextFile`）、常驻区块的写入与逐字节恢复、`mcp.json` 的字节不变、缺失的名称与配置无效时的 `disconnect`；共用：两个名称共用 skill 目录与上下文文件时的单一常驻区块与三个链接、两种解绑顺序、`createdContextFile` 的继承、只共用上下文文件、Windows 上大小写不同的同一路径、一次渲染只返回一个结果、名称绑到其他路径时的拒绝。
     - `SkillLinkTests`：三个 skill 的目录链接：三个位置都空时全部建立；任一位置被占用（目录、文件或指向别处的链接）时整体拒绝，三个链接都不建；skill 目录不存在或是文件时拒绝且不写；重复 `connect` 补齐缺失的链接；后续步骤失败时回滚；`disconnect` 删除三个链接、报告已缺失的链接并保留别人的同名项；只有旧的记忆 skill 链接的绑定（Windows 上为真实目录联接）；skill 目录被两个客户端共用时的复用报告，以及两种解绑顺序下链接的保留与拆除。
     - `ServiceTests`：服务端说明的组成、语言与项目部分、状态提示，以及启动与运行中的绑定渲染（共用的上下文文件只渲染一次）。
     - `StatusTests`：`tacit status` 的报告：服务运行中（地址、版本、运行时长）与未运行（含端口被其他程序占用，服务栏含 MCP 端点），三个辅助模型的可用、冷却中（含剩余秒数，冷却结束后回到可用）与未配置（与可用、冷却中并列），日志位置，配置有误时的错误键路径与服务栏，宽松读取的端口与语言（`server.port` 可读而其他键有错、文件不存在、YAML 无法解析三种情况）；离线报告不含依赖服务的列，其辅助模型栏只列未配置的模型。
     - `StatusColumnsTests`：`tacit status` 的各栏（不依赖服务的部分）：绑定的四种坏情况（文件缺失、非 UTF-8、标记不完整或重复、三个 skill 链接中某一个缺失或指向别处），每种检查前后文件字节不变（不含 `.cache/`）；共用路径的问题只出现一次、挂在第一个引用它的绑定上，`SharedWith` 列出共用的其他绑定；上次整理的三种基准（初始提交、整理提交、从未整理），改名前的旧前缀提交不作为基准；待处理计数与整理提示相同且不调模型；仓库历史不可读时其余栏照常；最近本地备份在三种前缀中取最新；密钥来源栏列出三个模型的密钥与 WebDAV 密码的来源（环境变量、配置文件或"未设置"，"未设置"不算错误、不上色，不显示值），未配置的模型即使设了环境变量也只写"未配置，不需要密钥"；服务运行与未运行时共用的栏内容一致；离线辅助模型栏只列未配置的模型，三个都已配置时省略，在线与离线中"未配置"的写法一致。
     - `ResidentDocumentTests`：常驻区块外壳：`resident.md` 的替换、节的校验与拒绝、运行中修改与删除。
     - `PromptDocumentTests`：`prompts/` 下的 `rules.md` 与 `instructions.md`：核心规则的替换、通用规则、HTML 注释的处理、备份包含 `prompts/`。
     - `LogTests`：写入的日志事件：三个写入工具与 `recall` 的事件含页面标识或短码、不含正文、原话与查询文本；决策模型最终失败的冷却警告；任何事件都不含三个模型的密钥；启动事件；通知同时显示并记录，自动备份事件。`CapturingLoggerFactory` 保存全部事件供搜索。
     - `MemorySkillTests`：记忆 skill 成品的生成、frontmatter、`skill.md` 的校验与运行中更新。
     - `PageTests`：细节页的新建与读取、按短码修改小节与页头、`section` 误填短码的拒绝（含 `hash` 同填、大写或带空白的短码、同名小节按名字改写、不属于本页的短码样值照常新建）、`dates`、串行写入、页面标识校验、未知参数的拒绝（点名、与 op 问题一并列出、下一步、不写文件）。
     - `DuplicatePageTests`：新建细节页时的重复页判断与放行。
     - `CredentialTests`：凭据检测与 `confirm_no_secret`，含小节与页头写入，删除小节不需确认。
     - `RecallTests`：全文检索与索引同步：中文切分、筛选、结果呈现，索引重建与跳过非 UTF-8 页面；未知参数的拒绝（查询与读页两路，未知参数排在 query/page 问题之前）；小节标题后的最后修改日期（只有变化的小节更新）；使用统计不影响检索排序。
     - `VectorTests`：向量检索、嵌入模型不可用时的回退与补齐、模型或向量长度变化后的重建。
     - `ReindexTests`：`reindex` 的结果：重建前后同一查询的结果一致、每个单元都有向量；重建后判断缓存清空，整理重新调用决策模型；嵌入模型不可用时只重建全文并注明向量待补；嵌入模型未配置时只重建全文并注明未配置；非 UTF-8 文件列入结果；重建后小节日期取文件最近一次提交、未提交的文件取当天；开始、结束与结果写入日志。
     - `UsageStatsTests`：使用统计的计数规则（整页读取每个小节各加一、检索计入所显示的单元、页面级命中只计标题与别名单元）、最近返回日期随时钟前移、从未返回的页无计数与日期、`reindex` 清零。
     - `MaintenanceTests`：维护操作独占：维护期间到达的写入、`recall` 与服务端说明排队，完成后照常执行；两个维护操作依次执行。用挂起的嵌入替身让重建停在补齐阶段，等待的调用在短暂停顿后仍未完成，才断言它们被挡住。
     - `RerankTests`：reranker 精排的排序、过滤、不可用时的处理与恢复。
     - `ModelProtocolTests`：三个模型的请求经 `ModelDouble` 收到：每个模型请求其配置的完整 URL、不追加路径（路径与配置一致）；嵌入、reranker、决策模型各自的请求字段；有密钥时带 `Authorization: Bearer`，没有密钥时不带认证头；Cloudflare 的 reranker 请求体含 `query` 与 `contexts`、不含 `model`，`recall` 按 `id` 对回的分数排序与过滤；Cloudflare 决策请求与 System One 同体，响应取 `result` 后判断（新建重复页被识别）；`success` 为假、缺 `result` 或缺 `answers` 只调用一次并按不可用降级；`provider: cloudflare` 的嵌入请求与通用协议相同。
     - `ModelRetryTests`：辅助模型的请求内重试：429、5xx、网络错误与单次超时在第 2～5 次成功时模型仍可用；5 次都失败后冷却；401/403、400/404/422 与格式不对的响应只调用一次；可重试与不可重试的失败、退避与 `Retry-After`、20 秒总时限、冷却与冷却后的再次尝试，以及 `recall`、实时写入判断与整理判断经过重试。
     - `ProbeTests`：`tacit probe` 经门面：通用协议与 Cloudflare 的成功、4xx 带响应体且截断到 500 字符、5xx 只调用一次不重试、格式不对与 Cloudflare `success` 为假、超时（20 秒，经 `TestHome.Clock`）、网络错误、未配置不发请求、请求体只含内置文本、失败的探测不使服务中的模型进入冷却、回显的密钥被替换。
     - `UnconfiguredModelTests`（经门面）：三个模型都不配置或只配置部分时服务照常启动，写入、画像候选与检索照常，返回中不附模型说明；未配置的模型不发请求、不进入冷却，服务端说明只列冷却中的模型；整理对依赖未配置模型的类别标为"未检查（未配置）"且不提问；`tacit status` 的模型项为未配置；去掉嵌入模型后向量保留，加回同一模型名不重新嵌入，换模型名则丢弃并补齐；启动日志一行列出未配置的角色且不含地址与密钥。
     - `BackupTests`：每日自动备份的生成、保留、关闭、明文密钥提示（配置中写了任一模型的明文 `apiKey` 或 WebDAV 明文密码即提示，环境变量覆盖时仍提示）与失败处理。
     - `ManualBackupTests`：手动备份的位置与文件名（本地时间）、内容与清单（`kind`、`memoryHeadSubject`）、写入队列中排在其前的写入、不被自动清理、`--to` 创建目录、同秒重名拒绝且不覆盖、`keep: 0` 时照常、明文密钥提示、相对目录被拒。
     - `RestoreTests`：恢复的预览（备份信息与将被替换的内容，`--include-config` 时三项替换）、旧清单（无 `kind`、`memoryHeadSubject`）、无清单拒绝；清单与内容不符、条目位置越界（`..`、绝对路径、未列出的文件）各自拒绝且什么都不改、不生成保险备份；仅恢复记忆仓库；`--include-config` 替换配置与提示词并删除多余的提示词文件；`bindings.json` 永不恢复；恢复前先生成保险备份并记录日志；恢复后的检索、服务端说明与常驻区块反映备份内容；恢复回来的 `memory.path` 不同时的提示；换入失败时回滚（记忆仓库回到恢复前内容）；恢复期间到达的写入落在恢复后的仓库中；恢复回来的 `config.yaml` 不能加载（如含旧版 `apiKeyEnv`）时恢复照常完成、结果附错误说明，预览同样提示；恢复结束后记忆仓库内没有残留的 `.tacit-*` 暂存目录；`path` 与 `remote` 的来源校验、`remote` 只能预览的规则由门面拒绝。
     - `RemoteBackupTests`：云端备份经 `WebDavDouble`：PUT `.part` 后 MOVE 覆盖（`Overwrite: T`，基本认证头）；第二次上传替换远端内容；父目录缺失时 MKCOL 后重试一次；MOVE 不支持（405）时报错、远端旧文件不变、`.part` 被删；上传中途断开时远端旧文件不变；认证与连接失败的阶段；错误与日志不含密码；`remote.json` 的记录与 `status` 的云端行（不访问网络）；未配置时的提示；目录被拒；无用户名时不发认证；配置中的明文密码提示；本地 `backups/` 中不留副本。
     - `RemoteRestoreTests`：恢复云端备份经 `WebDavDouble`：预览读取远端文件、下载文件位于 `backups/.download/` 且不计入最近的本地备份；确认的下载恢复为远端内容、生成保险备份并删除下载文件；远端没有备份时拒绝、什么都不改、不留下载文件；认证失败拒绝且不泄露密码；非备份的远端文件拒绝、什么都不改、保留下载文件；未配置时的提示。
     - `RecentActivityTests`：新增活动与 `replaces` 改写、回复只有一行（附新短码）、写入不改动 `recent.md`；已删除的 `op`、`items`、`hash` 与其他未知参数按名拒绝，下一步指向 `tacit-consolidate`。
     - `SameTopicTests`：近期活动的同主题判断与退回。
     - `RecallActivityTests`：检索近期活动：`kind` 筛选、过滤、主线不入索引、同步。
     - `SummaryTests`：周汇总与月汇总：检索命中并标注是汇总及其周或月、跨年的周按 ISO 日期筛选、不进入页面与项目筛选、不进入同主题判断、写入新活动不破坏汇总节（字节不变，新日期标题位于汇总节之前）、手改后经写入同步；ISO 周边界（经 `SummaryLines.WeekOf`）；已完成且有活动、但还没有汇总的周与月（经 `SummaryCalendar.Unsummarized`）。
     - `RecentSummaryInstructionTests`：服务端说明中的近期摘要：内容、排序、截断、窗口与刷新。
     - `PersonaAddTests`：画像 `add` 的校验、节标题补回、凭据、备份与常驻区块更新；未知参数的拒绝、缺 op 时的参数列表与 op 问题合并。
     - `PersonaReplaceDeleteTests`：画像 `replace` 与 `delete` 的定位、校验与回显。
     - `PersonaConflictTests`：画像写入时的规则冲突判断，以及决策模型或嵌入模型不可用时的降级。
     - `PersonaProposeTests`：候选 `propose` 的写入、校验与标题补回。
     - `SamePreferenceTests`：`propose` 的同一偏好判断、出现记录与拒绝列表。
     - `PersonaIndexTests`：画像与候选不进入派生索引，无法识别的行与非 UTF-8 画像文件的状态提示。
     - `FormatChecksPersonaTests`：整理报告中画像、候选文件、活动记录与近期主线的格式问题：文件、行号与问题说明，修正后消失，两种超限的处理方式。
   - 未自动化：内置常驻区块逐字节不变，已用中英两种默认配置手工比对确认，不写入测试。
   - 未自动化：`serve` 日志文件的按天滚动与保留 14 份（由 Serilog 保证），在验收中手工确认。
2. **`tests/TacitMemory.Mcp.Tests`：MCP 接缝**
   - 复用 `TestHome`，在进程内以真实 HTTP 启动 `McpHost`，用 SDK 的 MCP 客户端调用；
   - 测试类索引（每个测试类一行）：
     - `McpHostTests`：服务端说明下发、运行模式（默认的 `2026-07-28` 客户端与固定为 `2025-03-26`、`2025-06-18`、`2025-11-25` 的客户端都能连接、取得服务端说明并调用工具，`SessionId` 为空；原始 `initialize` 的响应不带 `Mcp-Session-Id`）、工具列表与标注、必填参数与取值（`required`、`enum`，含 `write_page` 的 `type` 四个取值）、`op` 取值外的调用到达服务后被拒绝、参数错误标记与三段返回、schema 之外的参数名经原始调用被点名（`write_page` 的 `content`、`recall` 的 `limit`，`write_recent` 的 `op`、`items`、`hash`）、各写入工具的往返读回、端口被占用时不启动；prompt 列表恒为空而不报错；资源列表只含 `tacit://consolidation-report`，读取得到含记忆仓库绝对路径与报告六类标题的文本，改名前的旧 URI 读取时报资源不存在；工具说明按配置填入（`itemLength`、`minAliases` 改为非默认值并重启后，说明出现新值、不再出现 100 与 "by default"），且与 `ToolCatalog` 的说明一致。
     - `ManagementApiTests`：管理 API 经真实 HTTP 访问：`/api/status` 的 JSON（camelCase，可读回为报告，密钥来源栏含三个模型的条目，辅助模型的未配置状态以 `unconfigured` 读回）；Origin、Host（仅 `127.0.0.1` 或 `localhost` 加本服务端口）与 POST 的 Content-Type（非 `application/json` 返回 415）三道防护及其错误体；`/mcp` 不受防护；一次 `/api` 调用产生一条含路由与状态码的日志事件；`POST /api/reindex` 的 JSON 往返（结果可读回，含跳过的文件与向量待补）；`POST /api/backup` 的 JSON 往返，`to` 为相对路径返回 400（`RequestInvalid`），同秒重名返回 409（`BackupExists`），云端目标未配置返回 409（`CloudBackupNotConfigured`）。
     - `ManagementErrorTests`：错误体为 JSON 且 `error` 取自 `TacitError`：`backup --to` 指向无法创建的路径返回 500（`FileSystemFailed`），错误说明含原因而不含异常类型名；恢复不存在的文件返回 400（`RequestInvalid`）；请求体读不出时返回 400，说明指出字段，不含解析器原文；`"remote"` 是云端目标的线上值，未配置时返回 409（`CloudBackupNotConfigured`）。
     - `RemoteBackupApiTests`：`POST /api/backup` 的云端目标经真实 HTTP 上传并返回结果 JSON；云端目标与 `to` 同时出现返回 400（`RequestInvalid`）；上传失败返回 500（`CloudBackupFailed`），错误体含 HTTP 状态码，不含密码。
     - `RestoreApiTests`：`POST /api/restore` 经真实 HTTP：`preview` 为真时只返回预览且不改动；执行返回结果 JSON；无法恢复的 zip 返回 409（`RestoreInvalid`）且错误体含 `message`；缺少 `path`、相对路径与缺少 `preview` 返回 400；非 JSON 的 Content-Type 返回 415。
     - `RemoteRestoreApiTests`：`POST /api/restore` 的 `remote` 预览经真实 HTTP 返回下载文件路径与种类为 `remote` 的预览 JSON；`remote` 与 `path` 同时出现、或执行阶段带 `remote` 返回 400（`RequestInvalid`）且不下载；远端没有备份返回 500（`CloudBackupFailed`），错误体不含密码。
     - `ToolCatalogTests`：`tacit show tools` 读取的工具定义，工具与参数的说明、参数类型，不含取消令牌；说明中的上限取自给定的 `LimitsConfig`，默认值与改动后的值都能填入。
   - 工具的校验与拒绝规则由门面接缝覆盖；两个记忆指令 skill 的正文由 `CommandSkillTests` 覆盖，整理报告的内容由 `ConsolidationReportTests` 覆盖。
3. **`tests/TacitMemory.Cli.Tests`：CLI 接缝**
   - 经 `CommandLine.RunAsync` 在进程内执行真实的命令管道（参数解析、用法检查、执行与退出码）。`CliHarness` 注入 `TestHome` 的 `TacitOptions`（临时用户主目录与临时 `TACIT_HOME`）、捕获 stdout 与 stderr 的写入器、可编排的 stdin，以及 stdout、stderr、stdin 三个终端标志与 `NO_COLOR` 设置；命令只经 `CliConsole` 读写控制台，这是 CLI 唯一的接缝。
   - 需要服务的命令另起进程内的真实主机：`MemoryService` 加 `McpHost`，与 `McpHostTests` 同样的启动方式，监听一个空闲端口（写入测试配置的 `server.port`）。“端口被其他程序占用”用一个非 tacit 的 HTTP 监听模拟，“端口被占用”的 `serve` 用 `TcpListener` 占住端口。
   - `ConsoleApp.Log`、`ConsoleApp.LogError` 与 `Environment.ExitCode` 是静态状态，因此该项目的测试类都加入 `CliCollection` 集合，禁止并行；新增的测试类必须标注 `[Collection(CliCollection.Name)]`。
   - 测试类索引：
     - `UsageErrorTests`：用法错误为 2，说明只出现在 stderr（未知命令、`show` 缺子命令或子命令未知、`connect` 缺 `--client`、`--context` 或 `--skills`、名称不合规、非法 `--language`、未知选项、`backup` 的 `--to` 与 `--remote` 冲突、`restore` 的 `--from` 与 `--remote` 不恰好一个、`show tools` 等不接受的 `--default`、`probe` 的未知子目标与多余参数）；空路径选项（`restore --from ""`、`backup --to` 为空白、`init --memory ""`）为一行说明的用法错误；用法错误先于配置检查；枚举取值不区分大小写，命令词与子命令也不区分大小写。
     - `ConfigRefusalTests`：配置无法加载（`endpoint` 键，即未知键，导致 3 且说明含键路径）时 `connect`、`status`、`backup`、`reindex`、`show`（含 `show tools`）为 3；缺少模型密钥与省略整个 `models` 段都不再导致配置无法加载，`connect` 为 0；`disconnect` 与 `show … --default` 照常为 0。
     - `ServiceExitCodeTests`：服务未运行时 `reindex`、`backup`、`restore`、`status` 为 4，说明提示先执行 `tacit serve`；端口被非 tacit 的 HTTP 监听占用时 `status` 为 4；服务运行时 `status` 为 0，纯文本输出无控制字符；服务拒绝 `status` 的报告时为 1、拒绝原因只在 stderr；服务拒绝 `restore` 等请求时同为 1；`backup` 不给选项时写到 `TACIT_HOME/backups/`。
     - `HelpAndOutputTests`：`-h`、`--help`、`connect --help`、`probe --help`、裸命令与 `--version` 为 0；`connect` 只绑定临时用户主目录；`show … --default` 的 stdout 只有内置文本；`show tools` 的 stdout 含配置填入的上限；总览的退出码表只出现一次，总览中出现 `probe`；命令与子命令的词不区分大小写；命令帮助不露出 `[Default` 与可空标记 `?>`。
     - `BindingCommandTests`：`connect` 缺任一选项为 2 且不写任何文件；成功时 stdout 含 MCP 端点 URL；名称不合规为 2；`disconnect` 只需 `--client`、路径取自记录；名称不存在为 1。
     - `RestoreConfirmationTests`：`restore` 的确认（stdin 非终端且无 `--yes` 为 2 且什么都不恢复；终端输入非 `y` 为 1；`--yes` 与 `-y` 为 0）；旧的 `--config` 为用法错误，`--include-config` 可用。
     - `ServeTests`：`serve` 端口被占为 1、配置无效为 3、取消令牌（Ctrl+C）为 0。
     - `ProbeCommandTests`：`tacit probe` 的退出码 0（全部成功，含大小写不同的子目标）、1（失败；明确指定的模型未配置）、3（配置无效）；不带参数时跳过未配置的模型且为 0；服务未运行、记忆仓库未初始化时照常；输出不含密钥值；纯文本无控制字符；报告随记忆语言。
     - `OutputStylingTests`：不需要服务的 `status` 与 `connect`：stdout 不是终端，或设置了 `NO_COLOR` 时为纯文本、不含控制字符；终端中的美化与纯文本显示相同的文字（含辅助模型未配置的离线 `status`）；终端 stderr 下失败的等待仍为 4。
     - `StyledRestoreAndWaitTests`：需要服务的 `restore` 预览（纯文本、`NO_COLOR` 与终端中的美化文字相同），`reindex` 与 `restore --yes` 的转圈只出现在终端 stderr 且 stdout 仍为纯文本，stdout 与 stderr 各自判断。
   - 未自动化：美化后的外观（面板、颜色、转圈）在实机验收中目测。

## 2. 模型依赖

测试不调用真实模型，也不访问网络。`ModelDouble` 是进程内的 HTTP 替身，按用例返回确定的结果。配置中每个模型的 `url` 是替身上的完整地址（`EmbeddingUrl`、`RerankUrl`、`DecisionUrl`，分别为 `/v1/embeddings`、`/v1/rerank` 与 `/v1/systemone`，路由不变）：`/v1/embeddings` 默认返回文本相邻字对的散列向量（措辞相近即相近），也可按用例指定任意文本的向量；`/v1/systemone` 按用例给出选择题各选项的概率（默认选"都不是"）；`/v1/rerank` 默认给每个文本 1.0 分，可按用例指定分数。每个请求都被记录：`EmbeddingRequests`、`RerankRequests`、`DecisionRequests` 各按顺序列出收到的 `ReceivedRequest`，含请求路径（`Path`）、`Authorization` 头（没有则为 `null`）与 JSON 正文（`Body`），用于断言认证头与字段；`LastDecision` 取决策模型的最近一次。`ModelsYaml(embeddingModel, decisionKey, embeddingKey, rerankerKey)` 写出指向替身的 `models` 一节，只在给出密钥时写出对应的 `apiKey`。每个模型都可设为不可用（立即返回 404，这是不重试的客户端错误，因此不涉及重试的测试不会等待退避）。`ModelDouble.ScriptEmbedding`、`ScriptRerank`、`ScriptDecision` 按次编排某个模型的下几次请求：`ModelReply.Status(code, retryAfterSeconds)` 返回状态码（可带 `Retry-After`），`ModelReply.Body` 返回指定正文，`ModelReply.Drop()` 断开连接（客户端看到网络错误），`ModelReply.Hang()` 挂起直到客户端放弃；脚本用完后模型按用例设定的方式回答。`EmbeddingRequestsReached(n)` 等待嵌入模型收到第 n 个请求，用于确定地拨动挂起的请求。`TestHome.InitReady` 默认让所有模型指向一个共享替身：嵌入模型与 reranker 不可用，决策模型对一切选择题答"都不是"，因此不涉及模型的测试不会等待网络，写入也不会被判为重复页；用到模型的测试传入自己的替身。测试中形似凭据的样本在运行时拼接，源码里不出现完整的凭据格式。

`HoldEmbeddings()` 挂起所有嵌入请求，直到 `ReleaseEmbeddings()`，用于让重建停在向量补齐阶段，再拨动它。

Cloudflare 格式的路由：`CloudflareRerankUrl`（`/ai/run/@cf/baai/bge-reranker-base`）与 `CloudflareDecisionUrl`（`/ai/run/clef-flash`）按 Cloudflare 的形状应答，计数与记录同样计入 `RerankRequests` 与 `DecisionRequests`，分数与概率仍由 `Score` 与 `Choose` 指定。请求体含 `contexts` 时答 `{result: {response: [{id, score}]}, success, errors, messages}`，请求体含 `questions` 时答 `{result: {model, answers}, success, errors, messages}`。`CloudflareReportsFailure = true` 时两条路由都答 `success: false` 且无 `result`。`ModelsYaml` 另有 `embeddingCloudflare`、`rerankerCloudflare`、`decisionCloudflare` 三个参数，为真时写出 `provider: cloudflare` 并把对应模型的 `url` 指向上述 Cloudflare 路由。

`WebDavDouble` 是进程内的 WebDAV 替身，与 `ModelDouble` 同样用 Kestrel 实现，`TacitMemory.Core.Tests` 与 `TacitMemory.Mcp.Tests` 两个项目都链接它。支持 PUT、MOVE、MKCOL、GET 与 DELETE；集合只在建立后存在，父目录缺失的 PUT 与 MKCOL 返回 409。`RequireBasic` 要求 HTTP 基本认证；`MoveSupported = false` 让 MOVE 返回 405（不支持原子替换的服务）；`Answer(方法, 状态码)` 让某个方法固定返回指定状态码；`DropNextPutAfter(n)` 让下一次 PUT 读完 n 字节后断开连接；`Requests` 按序记录每个请求的方法、路径、Authorization、Destination 与 Overwrite；`FileAt(路径)` 读取服务上保存的文件。替身只监听 `127.0.0.1` 的临时端口，测试不访问任何真实的 WebDAV 服务。

重试的等待是 `TestHome.Clock` 上的计时器：每次尝试的超时与每段退避各创建一个，`Advance` 只向前拨动时间，`Elapse` 拨动并触发到期的计时器，`NextTimerAsync(时长)` 等待客户端创建该时长的计时器并认领它。按时长匹配而不按序号匹配，因为写入会触发后台的向量补齐，它也会创建计时器。模型失败后的冷却期同样用 `TestHome.Clock` 的 `Advance` 跨过。

## 3. 本地化完整性

两种写法齐全由编译期保证（见 `docs/architecture.md` 第 3 节）。`TacitMemory.Core.Tests` 中的本地化测试检查编译器看不到的部分：每项两种写法都非空、格式占位符一致；结构词在两种语言之间互不重复；`init` 写出的每个标题都是该语言的结构词。这是直接读取 `internal` 类型的测试：文本表本身就是被检查的对象，没有公开契约可以替代。`SummaryTests` 中的 ISO 周边界与未汇总列表同样直接读取 `SummaryLines` 与 `SummaryCalendar`，因为这两个查询尚无公开入口；工单 11、12 把它们接入整理报告后，应改为经门面测试并删去这里的直接读取。

## 4. 提示词的验证

常驻区块外壳、记忆规则、记忆 skill、两个记忆指令与工具说明的措辞不由自动测试固定（自动测试只检查结构与长度上限）。改动以日常使用中出现的摩擦点为依据：Agent 该记没记、记错了地方、参数用错、未经同意改写画像等。改后在真实 omp 中按该摩擦点的情形手工复现验收：

- 使用临时 `TACIT_HOME`；`connect` 会写入用户的 `~/.omp/agent/AGENTS.md` 与三个 skill 链接，事先备份 `AGENTS.md`，结束后 `disconnect`，恢复并校验 SHA-256，确认三个 skill 链接已删除；
- 不停止 NSSM 中的 `llama-swap`；
- 判断依据是可观察的结果：记忆仓库的文件与提交，以及 omp 会话记录中的工具调用与 skill 读取。omp 经 `write` 工具以 `xd://mcp__tacit_<工具名>` 调用 tacit 工具，参数以 JSON 放在 `content` 中。
- 两个记忆指令（实机）：`/skill:tacit-remember 附加文字` 与句中触发时，命令前后的文字都到达 Agent；`/skill:tacit-consolidate` 后 Agent 以 `read tacit://consolidation-report` 读到资源；会话开始的 skill 列表中只有 `tacit-memory`，普通对话中 Agent 不自行调用两个记忆指令。用 `omp --model haiku` 验收（交互会话或 `-p` 均可），附加文字写明"只读、不写入、不提交"以免改动真实记忆。

## 5. 运行方式

默认只运行与当前变更直接相关的测试，不自动运行全量测试。测试使用真实 `git`，需要它在 `PATH` 中。

```powershell
dotnet test tests/TacitMemory.Core.Tests                               # 门面接缝
dotnet test tests/TacitMemory.Mcp.Tests                                # MCP 接缝
dotnet test tests/TacitMemory.Cli.Tests                                # CLI 接缝
dotnet test tests/TacitMemory.Core.Tests --filter "FullyQualifiedName~InitTests"   # 单个测试类
dotnet test TacitMemory.slnx                                           # 全量
```
