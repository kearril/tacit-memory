# TacitMemory 架构

本文档记录代码如何组织：程序集边界、模块分工、数据流与外部依赖。系统行为、接口与规则见 `docs/design.md`，术语见 `GLOSSARY.md`。代码结构变化时同步更新。

本文描述 v1 的目标结构。各表的"状态"列标明是否已实现：**已实现**、**部分**（尚未实现的部分写在括号里）、**计划**。

## 1. 总览

实线为已实现的部分，虚线为计划中的部分。

```mermaid
flowchart LR
    Client[客户端] -->|HTTP MCP /mcp| Mcp
    User[用户] -->|tacit 命令| Cli
    Cli -.->|管理 API /api| Admin
    subgraph Host["tacit serve（ASP.NET Core，127.0.0.1）"]
        Mcp[TacitMemory.Mcp<br>McpHost]
        Admin[管理 API]
    end
    Cli -->|init / connect / disconnect / show / serve| Facade
    Mcp --> Service
    Admin -.-> Facade
    subgraph Core["TacitMemory.Core"]
        Facade[门面 Tacit]
        Service[MemoryService]
        Writer[串行写入]
        Store[Markdown 读写]
        Index[派生索引与检索]
        Judge[判断]
        Inject[注入渲染]
        Checkup[体检报告]
        Backup[备份与恢复]
    end
    Facade --> Service
    Service --> Inject
    Service --> Writer
    Service --> Index
    Service --> Backup
    Writer --> Store
    Writer --> Judge
    Writer --> Index
    Store --> Repo[(记忆仓库<br>Markdown + Git)]
    Index --> Db[(.cache/index.db<br>SQLite)]
    Index --> Aux[嵌入模型 / reranker]
    Judge --> Decision[决策模型]
    Inject --> Files[客户端上下文文件]
```

## 2. 程序集

| 程序集 | 职责 | 依赖 |
| :--- | :--- | :--- |
| `TacitMemory.Core` | 全部记忆规则（模块见第 3 节）。只公开门面 `Tacit`、常驻服务的运行状态 `MemoryService` 与契约类型，其余为 `internal` | 无内部依赖 |
| `TacitMemory.Mcp` | MCP 主机 `McpHost`：在 `127.0.0.1` 上挂载 `/mcp`，把 `MemoryService` 的操作映射为 MCP 工具、prompt 与服务端说明，不含业务规则 | `Core` |
| `TacitMemory.Cli` | `tacit` 命令（可执行文件名 `tacit`）：`init`、`connect`、`disconnect`、`show` 直接调用门面；`show tools` 经 `TacitMemory.Mcp` 读取工具定义，交给门面 `ShowTools` 标注输出；`serve` 打开 `MemoryService` 并启动 `McpHost`；运维命令经管理 API 调用服务（计划） | `Core`、`Mcp` |

## 3. Core 内部模块

| 模块 | 职责 | 状态 |
| :--- | :--- | :--- |
| 门面 | 对外的唯一入口 `Tacit`；`OpenService` 返回 `MemoryService`，持有配置、记忆工具、服务端说明缓存与文件监听；`CheckupPromptAsync` 生成 `checkup` 指令文本 | 已实现 |
| 初始化 | 生成 `config.yaml` 与 `prompts/rules.md` 模板，创建记忆仓库骨架 | 已实现 |
| 配置 | 逐项读取并校验 `config.yaml`，一次报告全部出错的键路径；读取并校验 `prompts/` 下的提示词文档（规则见 `docs/design.md` 第 12 节）；校验服务端说明的固定部分长度 | 已实现 |
| 串行写入 | 所有写入经 `MemoryService` 的同一把写入锁依次进行；每次写入：校验 → 凭据检测 → 判断 → 改文件 → Git 提交 → 同步索引 → 必要时补齐向量、重渲染常驻区块、自动备份 | 已实现 |
| 近期活动 | 写入活动（`add_activity`、`replaces`）与近期主线（`update_mainline`）：按日定位标题并保留其余字节、计算活动短码与版本短码；写入后的回复附主线与近期活动 | 已实现 |
| 画像写入 | `write_persona` 的四个操作：参数按 `op` 校验，经校验与判断（画像冲突、同一偏好）后按行修改画像或候选文件，经串行队列写入并提交；画像变化时重渲染常驻区块。行为见 `docs/design.md` 第 6 节 | 已实现 |
| 细节页写入 | `write_page` 的四个操作（`create`、`upsert_section`、`delete_section`、`update_header`）：参数按 `op` 校验，凭据检测，小节以短码定位，只改写本次文件并提交，写入后同步该页的索引 | 已实现 |
| Markdown 读写 | 解析各文件格式（识别所有语言的结构词）、按行定位修改、计算短码；解析时同时返回格式问题（文件、位置、说明），由体检报告生成时当场使用；解析 `candidates.md` 的候选与拒绝列表及其出现记录，并按行追加出现记录 | 已实现 |
| 派生索引与检索 | SQLite FTS5 与 `vec0` 建表与增量同步；索引按来源（细节页、活动记录、画像与候选文件）组织，单元携带类型、项目与日期；画像每条与每个候选各为一个单元，供判断按来源取嵌入近邻（写入前后与手动编辑后同步），检索永不返回；中文两字切分；RRF 合并；reranker 精排；索引版本校验与重建；`judgment_cache` 表保存体检判断的概率，键为提问内容的哈希，三档在读取时按阈值计算 | 已实现 |
| 判断 | 各判断的问题模板（英文）与候选筛选；三档阈值与内置默认值；凭据正则；降级 | 已实现 |
| 决策模型接口 | 与模型无关的三种题型（选择、打分、真伪）；TypeSafe Jev 适配器，密钥取自 `apiKey` 或 `apiKeyEnv` | 已实现 |
| 辅助模型客户端 | 以 `HttpClient` 调用三个模型，三者共用 `ModelClient` 的请求内重试与冷却（数值与判定规则见 `docs/design.md` 第 4 节）；状态提示只按冷却判定；缺密钥的决策模型启动即不可用 | 已实现 |
| 注入渲染 | 常驻区块渲染与标记校验；按段落组装服务端说明（开场、核心规则（`instructions.md` 或内置默认）、通用规则、语言规范、值得记标准、近期摘要、状态提示、已有项目、体检提示），执行长度上限与截断顺序；生成记忆 skill（`skills/tacit-memory/SKILL.md`，`skill.md` 接在末尾，内容不变不写） | 已实现 |
| 客户端适配 | 定位上下文文件、注册与注销 MCP、建与删 skill 链接（`Clients/SkillLink`：Windows 用目录联接，其他平台用软链接；删除时只删链接本身）、`bindings.json` 读写 | 部分（缺 Claude Code 与 `--file`） |
| 体检报告 | 现场计算报告（`Checkup/`）：上次体检基准（最近的 `checkup:` 提交，否则 `init:`，都没有则从未体检）；疑似重复的页与互相冲突的画像条目经判断工作池与限时会话取得答案（`JudgmentPool`、`JudgmentSession`）；可确认与待清理的画像候选；违反格式或规则（`FormatViolations`，生成报告时重新解析全部记忆文件）；过期的近期主线。六类的计算方式、限时与缓存规则见 `docs/design.md` 第 9 节。`CheckupReport` 按报告顺序组装各类，无问题的类别写 None.；体检提示（`CheckupReminder`）复用其中不调模型的部分 | 已实现 |
| 备份与恢复 | 每日自动备份的 zip 打包与保留份数；解包、远端推送与恢复（恢复时暂停写入队列）未实现 | 部分（缺手动备份、恢复与远端推送） |
| Git | 经 `SimpleExec` 调用 `git` 命令行：初始化、按文件提交、查询提交时间、推送 | 部分（缺推送） |
| 文件同步 | 运行时以 `FileSystemWatcher` 监听记忆仓库与 `TACIT_HOME/prompts/` 目录，去抖后按哈希同步索引、使服务端说明缓存失效或重渲染常驻区块；提示词文档逐个文件重读，校验与保留上一版的规则见设计文档第 12 节；监听出错或 `prompts/` 被删除时，下一次重读重建监听并重新生成文件；启动时按哈希增量同步索引；服务自身写入后立即同步，随后的监听事件因哈希相同不再处理；后台的索引同步与常驻区块重渲染与服务自身的写入串行执行（写入以替换文件的方式完成，后台读取中的文件会使替换失败），后台读取以允许他人写入的方式打开文件，外部编辑器或手动修改不因此失败 | 已实现 |
| 本地化 | 面向人的短文本与结构词：每一项以 `Localized(Zh, En)` 一并定义，缺一种写法即无法编译；面向 Agent 的英文文本只有一份 | 已实现 |
| 查看 | `tacit show`：读取与常驻服务相同的来源，输出服务端说明、常驻区块、记忆 skill、工具说明或 `remember` 提示词，每一部分标明来源与长度；`--default` 输出内置文本；不需要运行中的服务 | 已实现 |

## 4. 数据流

### 写入

1. MCP 工具调用进入常驻服务 `MemoryService`，按 `op` 校验参数，排入串行队列；
2. 校验上限与必填线索；凭据检测；核对短码；
3. 需要时由决策模型判断（重复页取嵌入近邻作候选，同主题取当天的活动，画像冲突取跨四节的嵌入近邻，同一偏好取候选、拒绝列表与画像的嵌入近邻）；
4. 按行修改 Markdown 文件；
5. 用 `git` 只提交本次写入的文件；
6. 同步受影响的索引条目（写入后立即同步，随后的文件监听事件因哈希相同而跳过）；
7. 画像变化时重新渲染常驻区块；当天首次写入后触发自动备份。

调用 `git` 一律加 `-c core.quotepath=off` 并以 UTF-8 读取输出，避免中文路径被转义或乱码。

### 检索

1. 按单元自身的项目与日期筛选（普通 SQL）；单元挂在来源下，项目与日期逐单元保存；画像与候选的单元由筛选排除。
2. 全文：FTS5（unicode61）。中文切分由 `IndexText` 在写入与查询前完成，匹配规则见 `docs/design.md` 第 7 节；按 BM25 排序。
3. 向量：`vec0`（余弦距离），单元与筛选规则同全文；缺向量的单元按批补齐：启动与监听同步后在后台进行，每次写入后与模型恢复后的第一次检索时则在本次调用中完成。
4. 两路各取候选，RRF 合并后交 reranker，按阈值过滤并截取（数量见 `docs/design.md` 第 7 节）。

### 体检

- `checkup` 经 `MemoryService.CheckupPromptAsync` 进入体检报告模块：先读取记忆仓库的提交历史（上次体检的基准），再由各类别的生成函数读取 `candidates.md`、`recent.md` 的最近提交等，组装报告。报告计算不写记忆仓库的文件；判断的概率写入派生索引的判断缓存。修改由主会话在记忆仓库中完成。
- 判断工作池（`Checkup/JudgmentPool`）由服务共享，持有并发上限与判断缓存；每次体检的 `JudgmentSession` 负责限时：各类别读取仓库、提交全部判断之后才开始计时，读取与提交不计入预算。到点时尚未取得并发槽位的判断不再发出；已取得槽位的判断照常准备问题并先读缓存，只有缓存未命中且未到点才调用决策模型；在途的调用跑完后写入缓存。三类判断都经此接缝：问题由类别准备（嵌入、检索近邻），回答由工作池取得（缓存或决策模型），三档由类别用自己的阈值划分。
- 指令文本的结构（开头、步骤、结构词对照表与示例、报告）见 `docs/prompts.md` 第 5 节（`checkup`）。

### 注入

- `MemoryService` 生成服务端说明后缓存（`ServerInstructionsAsync`，同一时间只生成一次）。以下变化使缓存失效：`persona.md` 与 `candidates.md`、`pages/` 下的任何文件、`recent/` 下的文件与近期主线、记忆仓库的提交（`.git` 下的 `HEAD`、`refs/`、`logs/` 与 `packed-refs`，含空的体检提交）、`prompts/` 下的提示词文档重新载入，以及本服务写入页面、画像或活动之后。每次取用时比对状态（不可用的模型、被跳过的文件、当天日期），不同则重新生成。体检提示在生成时由 `CheckupReminder` 读取仓库现状，不调用模型。无状态模式下 SDK 每个请求都要求给出服务端说明，状态相同时直接返回缓存。
- 记忆 skill：`Skills/MemorySkill` 在服务启动时与 `prompts/` 变化后生成 `TACIT_HOME/skills/tacit-memory/SKILL.md`；门面 `GenerateSkill` 不依赖运行中的服务。成品只取决于代码与 `prompts/skill.md`，不读取记忆仓库，因此记忆变化不会重写它。
- `connect` 绑定时同样先生成记忆 skill，再建立指向它的链接；链接的建立与拒绝规则见 `docs/design.md` 第 12 节。
- `persona.md` 或 `resident.md` 变化后（文件监听事件去抖 300 ms），注入渲染重写所有已绑定文件中的常驻区块。渲染彼此串行；失败只在控制台提示，不中断服务。

## 5. 进程与传输

- `serve` 进程中的 ASP.NET Core 主机只监听 `127.0.0.1:<port>`，不做鉴权。
- MCP 端点挂载于 `/mcp`，使用 SDK 的无状态模式（`HttpServerSessionMode.Stateless`）。工具由 `MemoryTools` 声明，参数与标注随工具定义，调用直接转交 `MemoryService`。prompt 由 `MemoryPrompts` 声明（`remember` 与 `checkup`，均无必填参数），文本由 `MemoryService` 现场生成。列表与取回请求都由 `WithPrompts` 注册处理：omp 不看服务器声明的能力，照样请求 prompt 列表，请求失败就丢弃这个服务器，因此列表端点必须始终可用。端口被占用时 `McpHost` 不启动，由 CLI 报告。
- 管理 API（计划）挂载于 `/api`，供 `status`、`reindex`、`backup`、`restore` 使用，将来的 Web UI 也走这里；这些命令目前尚未实现。

## 6. 外部依赖

| 用途 | 选用 | 状态 |
| :--- | :--- | :--- |
| HTTP MCP 服务 | `ModelContextProtocol.AspNetCore` | 已引入 |
| YAML（页头、配置） | `YamlDotNet`（表示模型）；`config.yaml` 由 Core 逐项读取，不接入框架配置系统 | 已引入 |
| CLI | `ConsoleAppFramework` | 已引入 |
| 调用 `git` | `SimpleExec`（只用 `ReadAsync`：不回显命令，可指定编码与取消，非零退出码抛出带输出的异常） | 已引入 |
| 索引驱动 | `Microsoft.Data.Sqlite`（关闭连接池，以便删除重建索引文件） | 已引入 |
| 向量 | `sqlite-vec` 官方发布的 Windows x64 可加载扩展 `vec0.dll`（v0.1.9），连同上游 LICENSE 存于仓库 `lib/sqlite-vec/`，构建时复制到输出目录；每次加载前校验代码中记录的 SHA-256，不符则拒绝启动；不使用 NuGet 上的第三方包 | 已引入 |
| 查询与映射 | `Dapper` | 已引入 |
| 嵌入 | `HttpClient`，调用 OpenAI 兼容的 `/embeddings`；请求与响应只有几个字段，不为此引入 SDK | 已引入 |
| reranker | `HttpClient` 类型化客户端，调用 Cohere/Jina 兼容的 `/v1/rerank` | 已引入 |
| 决策模型 | `HttpClient`，调用 TypeSafe 的 `/systemone` | 已引入 |
| 重试与超时 | 辅助模型请求内重试（规则见上表辅助模型客户端行与 `docs/design.md` 第 4 节）；每次尝试设超时，并受一次请求 20 秒总时限截断 | 已实现 |

依赖原则：一个依赖能解决实际问题或简化逻辑就引入，避开停更或附带额外条款的包。不引入中文分词库、表结构迁移库与 EF Core（与 FTS5、`vec0` 虚拟表不合）。Markdown 只按行识别 `##` 小节与代码围栏，不引入 Markdown 解析库：按行修改要求原样保留其余字节，语法树帮不上。版本以各项目文件为准；尚未引入的依赖在引入时锁定版本。
