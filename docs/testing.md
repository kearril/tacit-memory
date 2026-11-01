# TacitMemory 测试规范

本文档定义测试接缝、测试环境与运行方式。

## 1. 两个测试接缝

测试只针对公开契约编写，不直接测试 `internal` 实现（读取 `internal` 类型的唯一例外见第 3 节）。等待后台备份、向量补齐或体检判断写入缓存完成时，测试使用 `MemoryService` 的 internal 钩子 `BackupCompletion`、`VectorCompletion`、`JudgmentCompletion`，它们不属于公开契约。

1. **`tests/TacitMemory.Core.Tests`：门面接缝**
   - 通过 `TacitMemory.Core` 的公开门面测试；
   - 每个测试使用独立的临时用户主目录、临时 `TACIT_HOME` 与临时记忆仓库（`TestHome`），真实文件、真实 `git`；绑定只写入临时用户主目录，从不碰真实的客户端配置。
   - 测试类索引（每个测试类一行，按模块分组）：
     - `InitTests`：`init` 生成的文件与首个提交、拒绝与重复执行、与已有配置冲突、语言推断。
     - `ConfigTests`：配置的默认值、校验错误与定位、未初始化提示，以及 `rules.md` 的生成与超限。
     - `LocalizationTests`：中英文本表的完整性，以及 `init` 写出的标题是否属于对应语言（见第 3 节）。
     - `ShowTests`：`tacit show` 的输出：各段来源与长度、`--default` 内置文本、记忆 skill 与 `remember` 数据、标签随记忆语言。
     - `RememberPromptTests`：`remember` prompt 的内容：当日活动与主线、`focus` 的有无、画像写法步骤。
     - `CheckupPromptTests`：`checkup` 指令文本：绝对路径与步骤、结构词对照表与示例（随记忆语言）、上次体检的三种基准（`checkup:`、`init:`、都没有）、候选门槛、主线过期与不过期、每类 20 项上限、取消。
     - `CheckupCandidateTests`：候选清理的三档（确定列为待清理并写明画像短码或拒绝列表条目，拿不准附提示且仍可确认，否定两者都不列）、选项只取画像与拒绝列表、无可比对时不提问、决策与嵌入模型不可用时的降级、阈值改变、判断缓存（内容不变不再调用、改阈值分档改变而不调用、重建索引后重新调用）。
     - `CheckupConflictTests`：互相冲突的画像条目：只判断上次体检后新增或改写的条目（含未提交的修改）、跨节、同一对只报一次、确定与拿不准两档标明档位、无其他条目时不提问、决策模型不可用时的降级。
     - `CheckupDuplicatePageTests`：疑似重复的页：只判断变过的页（含未提交的修改），未变的页对不再报；同一无序页对只报一次；确定与拿不准两档标明档位，否定不收；同项目没有其他页时不提问；删除的页不参与；没有 `checkup:` 与 `init:` 提交时全部页都算变过；决策与嵌入模型不可用时的降级；限时到点的未检查数量。
     - `CheckupBudgetTests`：体检限时（到点照常返回并注明未检查数量、在途调用不取消且跑完写入缓存、未发出的判断不再发出；到点后仍有缓存答案的类别照常列出，前面类别耗尽预算不影响后面类别；可确认候选的判断被截断时附"自行对照画像"的提醒）、工作池的并发上限、模型冷却后排队的提问不再发出，以及冷却结束后再次提问。
     - `FormatViolationTests`：体检报告的"违反格式或规则"：细节页的 YAML 头、标题与别名长度、相关页、文件名、小节重名、非 UTF-8 文件、混用结构词（仅提示；小节标题、归属行、原话与拒绝标记、列表项中的改为与 instead）；各项修正后消失，每类 20 项上限。
     - `CheckupReminderTests`：体检提示：天数门槛（含 `checkup.remindAfterDays`）、可确认候选即出现、只有格式问题不出现、候选在运行中写入后的下一次取用出现、体检提交（含空提交）后消失、生成时不调用模型、仓库不可读时省略且服务不中断、`tacit show instructions` 含提示与否。
     - `ModelRetryTests`：辅助模型请求内重试：429、5xx、网络错误与单次超时在第 2～5 次成功时模型仍可用；5 次都失败后冷却；401/403、400/404/422 与格式不对的响应只调用一次；`Retry-After` 被遵守；20 秒总时限用完即失败；缺密钥的决策模型不发起调用；`recall`、写入判断与体检判断都经过重试。
     - `BindingTests`：`connect` / `disconnect` 对 `AGENTS.md`、`mcp.json` 与常驻区块的写入、保留、拒绝与逐字节恢复。
     - `SkillLinkTests`：记忆 skill 的目录链接：建立、拒绝与回滚、`disconnect` 只删除链接（Windows 上为真实目录联接）。
     - `ServiceTests`：服务端说明的组成、语言与项目部分、状态提示，以及启动与运行中的绑定渲染。
     - `ResidentDocumentTests`：常驻区块外壳：`resident.md` 的替换、节的校验与拒绝、运行中修改与删除。
     - `PromptDocumentTests`：`prompts/` 下的 `rules.md` 与 `instructions.md`：核心规则的替换、通用规则、HTML 注释的处理、备份包含 `prompts/`。
     - `MemorySkillTests`：记忆 skill 成品的生成、frontmatter、`skill.md` 的校验与运行中更新。
     - `PageTests`：细节页的新建与读取、按短码修改小节与页头、`dates`、串行写入、页面标识校验。
     - `DuplicatePageTests`：新建细节页时的重复页判断与放行。
     - `CredentialTests`：凭据检测与 `confirm_no_secret`，含小节与页头写入，删除小节不需确认。
     - `RecallTests`：全文检索与索引同步：中文切分、筛选、结果呈现，索引重建与跳过非 UTF-8 页面。
     - `VectorTests`：向量检索、嵌入模型不可用时的回退与补齐、模型或向量长度变化后的重建。
     - `RerankTests`：reranker 精排的排序、过滤、不可用时的处理与恢复。
     - `ModelRetryTests`：辅助模型的请求内重试：可重试与不可重试的失败、退避与 `Retry-After`、20 秒总时限、冷却与冷却后的再次尝试、缺密钥的决策模型，以及 `recall` 与实时写入判断经过重试。
     - `BackupTests`：每日自动备份的生成、保留、关闭、明文密钥提示与失败处理。
     - `RecentActivityTests`：`add_activity` 与 `replaces` 改写、`update_mainline`、添加后回复中的主线与近期活动。
     - `SameTopicTests`：近期活动的同主题判断与退回。
     - `RecallActivityTests`：检索近期活动：`kind` 筛选、过滤、主线不入索引、同步。
     - `RecentSummaryInstructionTests`：服务端说明中的近期摘要：内容、排序、截断、窗口与刷新。
     - `PersonaAddTests`：画像 `add` 的校验、节标题补回、凭据、备份与常驻区块更新。
     - `PersonaReplaceDeleteTests`：画像 `replace` 与 `delete` 的定位、校验与回显。
     - `PersonaConflictTests`：画像写入时的规则冲突判断，以及决策模型或嵌入模型不可用时的降级。
     - `PersonaProposeTests`：候选 `propose` 的写入、校验与标题补回。
     - `SamePreferenceTests`：`propose` 的同一偏好判断、出现记录与拒绝列表。
     - `PersonaIndexTests`：画像与候选不进入派生索引，无法识别的行与非 UTF-8 画像文件的状态提示。
     - `FormatChecksPersonaTests`：体检报告中画像、候选文件、活动记录与近期主线的格式问题：文件、行号与问题说明，修正后消失，两种超限的处理方式。
   - 未自动化：内置常驻区块逐字节不变，已用中英两种默认配置手工比对确认，不写入测试。
2. **`tests/TacitMemory.Mcp.Tests`：MCP 接缝**
   - 复用 `TestHome`，在进程内以真实 HTTP 启动 `McpHost`，用 SDK 的 MCP 客户端调用；
   - 测试类索引（每个测试类一行）：
     - `McpHostTests`：服务端说明下发、工具列表与标注、参数错误标记与三段返回、各写入工具的往返读回、端口被占用时不启动；`remember` 与 `checkup` 两个 prompt 的列表（`checkup` 无参数）与取回文本（含记忆仓库的绝对路径与报告的六类标题）。
     - `ToolCatalogTests`：`tacit show tools` 读取的工具定义，工具与参数的说明、参数类型，不含取消令牌。
   - 工具的校验与拒绝规则由门面接缝覆盖；`remember` prompt 的内容由 `RememberPromptTests` 覆盖，`checkup` prompt 的内容由 `CheckupPromptTests` 覆盖。

## 2. 模型依赖

测试不调用真实模型，也不访问网络。`ModelDouble` 是进程内的 HTTP 替身，按用例返回确定的结果：`/v1/embeddings` 默认返回文本相邻字对的散列向量（措辞相近即相近），也可按用例指定任意文本的向量；`/v1/systemone` 按用例给出选择题各选项的概率（默认选"都不是"），并记录请求次数与最近一次请求；`/v1/rerank` 默认给每个文本 1.0 分，可按用例指定分数。每个模型都可设为不可用（立即返回 404，这是不重试的客户端错误，因此不涉及重试的测试不会等待退避）。`ModelDouble.ScriptEmbedding`、`ScriptRerank`、`ScriptDecision` 按次编排某个模型的下几次请求：`ModelReply.Status(code, retryAfterSeconds)` 返回状态码（可带 `Retry-After`），`ModelReply.Body` 返回指定正文，`ModelReply.Drop()` 断开连接（客户端看到网络错误），`ModelReply.Hang()` 挂起直到客户端放弃；脚本用完后模型按用例设定的方式回答。`EmbeddingRequestsReached(n)` 等待嵌入模型收到第 n 个请求，用于确定地拨动挂起的请求。`TestHome.InitReady` 默认让所有模型指向一个共享替身：嵌入模型与 reranker 不可用，决策模型对一切选择题答"都不是"，因此不涉及模型的测试不会等待网络，写入也不会被判为重复页；用到模型的测试传入自己的替身。测试中形似凭据的样本在运行时拼接，源码里不出现完整的凭据格式。

重试的等待是 `TestHome.Clock` 上的计时器：每次尝试的超时与每段退避各创建一个，`Advance` 只向前拨动时间，`Elapse` 拨动并触发到期的计时器，`NextTimerAsync(时长)` 等待客户端创建该时长的计时器并认领它。按时长匹配而不按序号匹配，因为写入会触发后台的向量补齐，它也会创建计时器。模型失败后的冷却期同样用 `TestHome.Clock` 的 `Advance` 跨过。

## 3. 本地化完整性

两种写法齐全由编译期保证（见 `docs/architecture.md` 第 3 节）。`TacitMemory.Core.Tests` 中的本地化测试检查编译器看不到的部分：每项两种写法都非空、格式占位符一致；结构词在两种语言之间互不重复；`init` 写出的每个标题都是该语言的结构词。这是唯一直接读取 `internal` 类型的测试：文本表本身就是被检查的对象，没有公开契约可以替代。

## 4. 提示词的验证

常驻区块外壳、记忆规则、记忆 skill 与工具说明的措辞不由自动测试固定（自动测试只检查结构与长度上限）。改动以日常使用中出现的摩擦点为依据：Agent 该记没记、记错了地方、参数用错、未经同意改写画像等。改后在真实 omp 中按该摩擦点的情形手工复现验收：

- 使用临时 `TACIT_HOME`；`connect` 会写入用户的 `~/.omp/agent/AGENTS.md`、`mcp.json` 与 skill 链接，事先备份这两个文件，结束后 `disconnect`，恢复并校验 SHA-256，确认 skill 链接已删除；
- 不停止 NSSM 中的 `llama-swap`；
- 判断依据是可观察的结果：记忆仓库的文件与提交，以及 omp 会话记录中的工具调用与 skill 读取。omp 经 `write` 工具以 `xd://mcp__tacit_<工具名>` 调用 tacit 工具，参数以 JSON 放在 `content` 中。

## 5. 运行方式

默认只运行与当前变更直接相关的测试，不自动运行全量测试。测试使用真实 `git`，需要它在 `PATH` 中。

```powershell
dotnet test tests/TacitMemory.Core.Tests                               # 门面接缝
dotnet test tests/TacitMemory.Mcp.Tests                                # MCP 接缝
dotnet test tests/TacitMemory.Core.Tests --filter "FullyQualifiedName~InitTests"   # 单个测试类
dotnet test TacitMemory.slnx                                           # 全量
```
