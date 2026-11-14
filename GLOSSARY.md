# TacitMemory

跨客户端通用的个人记忆系统：让 Agent 记得用户的偏好、最近在做的事，以及被提起时的具体细节。

每个术语括号内为英文名：给 Agent 看的英文文本、工具参数与代码类型名一律使用它。

## Language

### 记忆的三层

**画像**（Persona）：
关于用户本人的常驻记忆，依次分为红线、协作规则、偏好、档案四节，存于 `persona.md`。
_Avoid_: 人设、用户资料

**红线**（Red line）：
画像中违反后会造成不可挽回或严重后果、用户不接受的做法，每条都给出替代做法；只能由用户明确提出。
_Avoid_: 禁令、硬规则、底线

**协作规则**（Working rule）：
画像中约束 Agent 行为的部分，写成"情境 → 做法"；违反时用户会当场纠正。
_Avoid_: 偏好规则、指令

**偏好**（Preference）：
画像中描述用户喜欢什么、习惯怎样的部分，写成描述句而非指令；违反时用户不会纠正，但会觉得不对味。
_Avoid_: 喜好、风格设定、倾向

**档案**（Profile）：
画像中关于用户的客观事实，例如语言、系统环境、技术栈。
_Avoid_: 背景、个人信息

**近期活动**（Recent activity）：
用户最近在做什么、想通了什么的记录，包括活动记录与汇总。最近 7 天的活动渲染进服务端说明；主线的背景说明与近期摘要在常驻区块中。
_Avoid_: 时间线、日志、工作记录、近期摘要、常驻摘要

**背景说明**（Background）：
主线条目的第一行，写这件事是什么，基本不变，存于 `workstreams.md`。
_Avoid_: 主线描述、简介

**近期摘要**（Recent）：
主线条目的第二行，概括这件事近期处在什么阶段、往哪走，由整理时重写；写走向，不罗列做了哪些。活动比它新，两者不一致时以活动为准。
_Avoid_: 近况清单、进展列表、流水账

**未成立的主线名**（Unestablished workstream name）：
`pages/` 目录或活动的主线标注中出现、而 `workstreams.md` 中没有的主线名。写入、检索与主线简报照常，但不进入常驻区块，只作为整理报告的提案材料。
_Avoid_: 临时主线、草稿主线

**活动记录**（Activity record）：
按月存放、永久保留的全部活动，存于 `recent/YYYY-MM.md`。
_Avoid_: 完整日志、历史

**活动**（Activity）：
活动记录中的一条：写人在做什么、想通了什么，属于某一天，可标注所属主线。
_Avoid_: 活动条目、日志条目、事件

**汇总**（Summary）：
活动记录末尾 `## 汇总` 节中的一行，概括一个已完成的 ISO 周或自然月，由整理时的主会话写入。它是检索单元（标明是汇总），但不是活动：不取短码，不参与同主题判断与"最近 7 天"的列表。
_Avoid_: 摘要、总结、日报

**周汇总**（Weekly summary）：
`- 2026-W45：<一行>`，按 ISO 周（周一至周日）计，写在该周周四所在月份的文件里。
_Avoid_: 周报

**月汇总**（Monthly summary）：
`- 2026-11：<一行>`，按自然月计，写在该月的文件里。
_Avoid_: 月报

**同主题**（Same topic）：
同一天里关于同一件事的两条活动，包括这件事当天的推进；同主题的活动合并为一条，不论是否标注了同一所属主线。
_Avoid_: 重复活动、相似活动

**细节页**（Page）：
关于一件事或一个主题的 Markdown 页面，页内事实、决策、坑点写在一起，被提起时检索取出。
_Avoid_: 笔记、卡片、事件页、文档

**小节**（Section）：
细节页正文中以二级标题开始的一段，小节名由主会话决定，在页内唯一。检索以小节为单位返回内容。
_Avoid_: 段落、分块、chunk

**页面类型**（Page type）：
细节页页头中的 `type`，取 `incident`（排错）、`decision`（决定）、`entity`（实体）、`work`（工作单元）四种之一。新建时必须给出，`update_header` 可改。`work` 页的「遗留」小节（英文 Leftovers）与 `decision` 页的「推翻记录」小节（英文 Overturned）是服务识别的两个约定小节。
_Avoid_: 分类、标签、种类

**页头**（Header）：
细节页开头以 `---` 围起的 YAML 块，含 `title`、`aliases`、`dates`、`related`、`type`；服务只改写其中的顶层键，其余字节保持不变。
_Avoid_: 前言、元数据、front matter

**页面标识**（Page identifier / `PageId`）：
`<所属主线>/<slug>` 形式的细节页名称（专题为 `_topics/<slug>`），slug 为小写字母、数字与连字符，创建后不变；`related` 用它引用其他页。
_Avoid_: 路径、文件名

**专题**（Topic）：
不属于任何主线的细节页，存放在 `pages/_topics/`。
_Avoid_: 话题

**主线**（Workstream）：
一件持续的事的名字，统一为小写，由多个围绕它的会话聚合而成，不要求有代码仓库。只有 `workstreams.md` 中有条目的名字才是已成立的主线，由用户确认后成立；写入时不拦截新名字。细节页按它分目录（`pages/<主线>/`），活动以 `[主线名]` 标注。不包括 `_topics` 等以下划线开头的系统名与以点开头的目录。
_Avoid_: 项目、所属项目、代码仓库名

### 检索

**线索**（Cues）：
细节页 YAML 头中与检索有关的字段：`title` 与 `aliases` 构成页面级线索，供检索命中；`dates` 与所属主线用于筛选；`related` 不参与检索。
_Avoid_: 元数据、标签、索引字段

**别名**（Alias）：
线索中的 `aliases`：同一件事的口语说法、中英文名与别称。
_Avoid_: 关键词、标签、tags

**派生索引**（Derived index）：
从记忆仓库中的 Markdown 生成的全文与向量索引，存于 `.cache/`，可随时删除重建。
_Avoid_: 数据库、缓存库

**使用统计**（Usage statistics）：
派生索引中每个检索单元的返回次数与最近返回日期，由 `recall` 返回结果与整页读取计入，`reindex` 清空。只供主线简报排序与整理报告，检索排序不读取。
_Avoid_: 访问次数、点击数

**主线简报**（Workstream brief）：
`recall` 只给 `workstream` 时返回的该主线地图，依次为主线名（已成立时其后为背景说明与近期摘要）、主线约定（主线作用域的画像条目）、未决事项、页目录（按页面类型分组）、最近 3 条活动与最近一次周汇总；超出 `limits.brief` 时从最旧的页目录条目起截断。
_Avoid_: 项目简报、主线概览、主线摘要、主线页

**未决事项**（Open items）：
主线简报中汇总各 `work` 页「遗留」小节的部分，每条附页面标识。
_Avoid_: 待办清单、TODO

**短码**（Hash / `Short code`）：
服务返回条目或小节时附带在方括号里的内容哈希前 6 位，用于在后续调用的 `hash` 或 `replaces` 参数中引用它；内容一改即失效。
_Avoid_: 条目 ID、编号

### 写入

**主会话**（Main session）：
用户正在对话的那个 Agent 会话。所有需要判断的写入由它完成，整理时的汇总、主线与提案也由它写出。
_Avoid_: 主 Agent、当前模型

**主模型**（Main model）：
主会话所用的对话模型，负责写什么、怎么写这类需要理解与生成的工作。
_Avoid_: 大模型、LLM

**记忆规则**（Memory rules）：
规定主会话何时检索、何时写入记忆的规则，写在服务端说明中（核心规则与通用规则）；怎样写的细节在记忆 skill 中。
_Avoid_: 记忆提示词、系统规则

**值得记标准**（Recording criteria）：
记忆规则中判断哪些内容应写入、哪些不记的条目。内置一份默认，按记忆语言给出；用户放置 `prompts/rules.md` 时整份替换它。
_Avoid_: 记忆触发器、写入条件

**提示词文档**（Prompt documents）：
`TACIT_HOME/prompts/` 下用户可放置的四份文档：`rules.md`（值得记标准）、`instructions.md`（核心规则）与 `resident.md`（常驻区块的外壳与各节引导语）存在时整份替换各自的内置默认，`skill.md`（写法约定）接在记忆 skill 末尾。文件存在即用户自定义，不存在即使用内置默认；服务不会生成这些文件。启动时校验；运行中修改时，`resident.md` 的变化经去抖后重渲染常驻区块，`skill.md` 的变化经去抖后重新生成记忆 skill，其余文档下一个会话生效。
_Avoid_: 提示词文件、配置文件

**记忆 skill**（Memory skill）：
常驻服务生成的 skill（`TACIT_HOME/skills/tacit-memory/SKILL.md`），承载各类记忆的详细写法；会话开始时主会话只看到它的名称与描述，写入前读取正文。
_Avoid_: 记忆文档、写入指南

**写法约定**（Writing conventions）：
用户在 `prompts/skill.md` 中写下的写法习惯，作为 `## User conventions` 一节接在记忆 skill 末尾；只能补充，不能改动系统部分。
_Avoid_: 用户 skill、自定义规则

**记忆语言**（Memory language）：
记忆内容的书写语言，也是面向人的文字所用语言；由配置 `language` 决定，并作为语言规范注入服务端说明。
_Avoid_: 界面语言、locale

**语言规范**（Language norms）：
服务端说明中规定记忆用什么语言书写的一段：记忆内容使用记忆语言，用户原话、代码、路径、命令与专有名词保持原样。

**结构词**（Structure words）：
记忆文件与 `prompts/rules.md` 中构成格式的固定词，例如节名、`归属：`、`原话`、`改为`（英文 `instead`）；按记忆语言写入，解析时识别所有支持语言的写法。
_Avoid_: 关键字、标签

**判断**（Judgment）：
常驻服务在写入前与整理时交给决策模型回答的固定问题，例如两页是否重复、两条是否同一偏好；结果按阈值分为确定、拿不准与否定三档处理。
_Avoid_: 分类、裁决

**凭据检测**（Credential check）：
每次写入前检查本次写入的文本是否含密码、密钥、令牌等凭据：只用内置正则匹配常见格式，不调用决策模型；命中即拒绝，主会话与用户确认后可带 `confirm_no_secret` 重新提交。
_Avoid_: 密钥扫描、脱敏、过滤

**记忆指令**（Memory command）：
只能由用户手动触发的两个 skill：`tacit-remember`（让主会话立即归纳并写入）与 `tacit-consolidate`（开始整理）。用户可在触发时附上自己的要求，与正文一同送达。与记忆 skill 不同，主会话不会自行读取它们。
_Avoid_: 手动 skill、MCP prompt、斜杠命令

**画像条目**（Persona entry）：
画像某一节中的一条：一个顶层列表项及其续行，原话注释另行保存；服务按短码引用它。
_Avoid_: 画像项、item

**作用域**（Scope）：
画像条目适用的范围，取值为全局（`global`）、主线（`workstream:<名>`）或领域（`domain:<标签>`）。全局条目在前四节中，主线与领域条目在其后的作用域块中；主线条目进入**主线简报**，领域条目经**领域索引**与 `recall kind=rules` 使用。
_Avoid_: 范围、层级

**领域**（Domain）：
只在某类情境下成立的一组画像条目所归属的标签，每个领域是 `persona.md` 中的一个作用域块；其触发描述决定它何时适用（见**触发描述**）。
_Avoid_: 主题、分类

**触发描述**（Trigger）：
领域块下的一行，说明何时该领域适用，例如"改代理、分流或测速时"。只写情境，不写做法；它出现在**领域索引**中。
_Avoid_: 适用条件、触发器

**领域索引**（Domain index）：
常驻区块中每个领域一行的索引：标签、触发描述与条数，全部领域都列出。Agent 据此判断当前任务是否命中某个领域，命中后用 `recall kind=rules domain=<标签>` 取该领域的规则。
_Avoid_: 领域列表、领域目录

**画像候选**（Persona candidate）：
主会话观察到、尚未经用户确认的可能偏好或规则，存于 `candidates.md`。带作用域（缺省为全局），是整理时的证据，没有出现天数门槛。
_Avoid_: 待审区、建议、候选偏好

**出现记录**（Occurrence）：
画像候选下的一行，记下某一天用户的原话；整理时可据此看出现过几天，但出现天数不再作为确认门槛。
_Avoid_: 记录项、引用

**画像证据**（Persona evidence）：
整理报告 B 部分的一类：全部画像候选，附作用域、归属、出现记录与原话，供判断是否提升为画像；不受上次整理的基准限制。

**拒绝列表**（Rejection list）：
用户拒绝过的画像候选；相近的候选不再提议。
_Avoid_: 黑名单

### 运行

**记忆仓库**（Memory repository）：
存放全部记忆文件的独立 Git 仓库。
_Avoid_: 存储、数据目录、store

**常驻服务**（Memory service）：
`tacit serve` 启动的单一 HTTP MCP 服务；对话中的写入都经由它完成。
_Avoid_: 后端、守护进程、server

**管理 API**（Management API）：
常驻服务在 `/api` 下提供的 HTTP 接口，供 `tacit` 命令（及 Web 界面）调用，例如 `GET /api/status`；与 `/mcp` 同在服务的监听地址上。它先以三道防护拒绝外部网页与 DNS 重绑定（Origin 须与请求自身同源，本机连接的 Host 须为回环名，POST 与 PUT 的 Content-Type 须为 JSON），再按连接来源区分：本机连接免登录，其他设备的连接须持有登录得到的会话，否则 401。
_Avoid_: 运维接口、控制面板、admin

**Web 界面**（Web UI）：
常驻服务托管的网页，供用户在浏览器中查看状态报告与日志、修改配置与提示词文档、管理绑定并执行备份、恢复与重建索引；它只调用管理 API，语言即记忆语言。
_Avoid_: 控制面板、后台、admin、前端（指产品时）

**访问密码**（Access password）：
从本机以外的设备打开 Web 界面时须输入的唯一一个密码；没有用户名与账号。本机上的访问不需要它。
_Avoid_: 登录密码、口令、管理员密码、token

**绑定预设**（Binding preset）：
Web 界面中为常见客户端预填的一组上下文文件与 skill 目录路径；选中后仍按普通的绑定提交，常驻服务不区分绑定是否来自预设。
_Avoid_: 客户端预设、客户端模板、适配

**维护操作**（Maintenance operation）：
独占记忆仓库与派生索引的操作。执行期间，其他读写排队等待，完成后照常执行（范围见 `docs/design.md` 第 12 节「服务运行」）。`tacit reindex` 与 `tacit restore` 都是维护操作。
_Avoid_: 运维操作、独占操作、锁定操作

**重新加载**（Reload）：
经设置页保存配置后，常驻服务不退出进程、以新配置重开自身的维护操作；失败时以原配置重开，配置文件也保持原样。`server.port`、`server.host`、`limits.page.minAliases`、`memory.path` 与 `language` 不随它生效，要等重启 `tacit serve`。
_Avoid_: 热更新、热重载、重启（指它时）

**自动备份**（Automatic backup）：
每天第一次写入后生成的备份 zip，文件名为 `auto-YYYY-MM-DD.zip`，内容与手动备份相同；只有自动备份会被清理，保留最近 `backup.keep` 份（`0` 关闭）。
_Avoid_: 定时备份、每日备份

**手动备份**（Manual backup）：
用户随时执行 `tacit backup` 生成的备份 zip，文件名为 `manual-YYYY-MM-DD-HHmmss.zip`，内容与自动备份相同；不计入 `backup.keep`，永不被自动清理。
_Avoid_: 临时备份、快照

**保险备份**（Safety backup）：
恢复前自动生成的 `backups/pre-restore-YYYY-MM-DD-HHmmss.zip`，内容是恢复前的完整状态；不计入 `backup.keep`，不清理、不上传。恢复中途失败时记忆仓库由回退逻辑撤销，不依赖它；恢复成功后，它是找回恢复前状态的途径。
_Avoid_: 恢复备份、回滚点

**云端备份**（Cloud backup）：
用户执行 `tacit backup --remote` 时现场生成的一份备份，经 WebDAV 上传为 `backup.webdav.url` 目录下唯一的 `tacit-backup.zip`，每次原子替换远端那一份；只在执行命令时上传，本地不留副本。
_Avoid_: 远程备份、同步、推送
代码中以 `Cloud` 命名：`BackupTarget.Cloud`、`CloudBackupRecord`、`TacitError.CloudBackupFailed`、`TacitError.CloudBackupNotConfigured`。命令行 `--remote` 与管理 API 的 `target: "remote"`、`remote: true` 是规格定义的线上值，保持 `remote`，与代码名的对应只在此处说明。

**恢复**（Restore）：
用备份 zip 替换记忆仓库的操作：先预览，确认后执行，执行前先生成保险备份；加 `--include-config` 时一并替换 `config.yaml` 与 `prompts/`。`tacit restore --remote` 先下载云端备份，之后走同一流程。
_Avoid_: 还原、导入

**门面**（Facade）：
`TacitMemory.Core` 对外的唯一入口，类型名为 `Tacit`；CLI 经由它调用记忆规则，并由它打开常驻服务 `MemoryService`，MCP 工具经该服务调用记忆规则。
_Avoid_: 服务层、API、管理器

**服务端说明**（Server instructions）：
由常驻服务生成、在客户端连接时注入系统提示的文本，依次包含开场、核心规则（`instructions.md` 或内置默认）、通用规则、语言规范、值得记标准、最近 7 天的活动、状态提示与整理提示；所依据的记忆或提示词文档变化后重新生成。
_Avoid_: 系统提示、服务描述

**状态提示**（Status hints）：
服务端说明中列出冷却中的辅助模型与因不是 UTF-8 而被跳过的文件（细节页、活动记录、画像与候选文件）的一段，节标题为 `## Status`；无问题时省略。
_Avoid_: 健康检查、告警

**状态报告**（Status report / `StatusReport`）：
`tacit status` 与管理 API `GET /api/status` 输出的报告，依次由配置、服务、辅助模型、客户端、绑定、上次整理、待处理问题、备份、日志与密钥来源各栏组成；服务未运行时省略服务的运行数据，辅助模型栏只列未配置的模型（全部已配置时省略该栏）。
_Avoid_: 健康报告、状态输出

**辅助模型**（Auxiliary models）：
常驻服务调用的三个模型的统称：嵌入模型、reranker 与决策模型。与主模型相对。每个都可以不配置。
_Avoid_: 常驻模型、小模型

**降级**（Degraded mode）：
某个辅助模型未配置或不可用时常驻服务照常工作的方式：缺哪个模型就跳过它负责的步骤，不拒绝写入。
_Avoid_: 故障模式、回退

**不可用**（Unavailable）：
辅助模型已配置，但请求经重试后仍失败、正在冷却的状态；与未配置不同，它是故障。
_Avoid_: 掉线、挂了、未配置

**决策模型**（Decision model）：
只做判断、不生成文本的辅助模型：回答选择题，给出每个选项的概率。
_Avoid_: 判断模型、分类器、裁判模型

**通用协议**（Generic protocol）：
一类辅助模型的公开请求格式，凡是兼容它的服务都能直接接入：嵌入为 OpenAI Embeddings，reranker 为 Cohere/Jina Rerank，决策模型为 System One。
_Avoid_: 标准接口、OpenAI 兼容（泛指时）

**提供方适配**（Provider adapter）：
针对某一家模型提供方、格式不兼容通用协议的单独接入，例如 Cloudflare Workers AI；只为少数提供方提供。
_Avoid_: 厂家适配、供应商插件、format

**探测**（Probe）：
用户以 `tacit probe` 对 `config.yaml` 中配置的辅助模型发出的一次真实请求，只回答它能否正常使用；不经常驻服务，不影响冷却。
_Avoid_: 测试、健康检查、ping

**整理**（Consolidation）：
用户定期执行 `tacit-consolidate` 开启的 Agent 会话：主模型依据整理报告写入周与月汇总、重写主线的近况，起草提案（含主线提案）并以一张编号清单交用户一次确认，最后以 `consolidate:` 开头提交；上次整理即最近一次这样的提交。整理取代旧的体检，服务不调用生成模型。
_Avoid_: 体检、checkup、维护、清理

**整理报告**（Consolidation report）：
常驻服务整理记忆时列出的材料，分 A、B 两部分。A 部分由主会话直接处理：违反格式或规则之处、待清理的画像候选、待汇总的周与月、主线材料（已成立主线的当前条目与其新活动）。B 部分为主会话起草提案的材料与需要用户拍板的项：主线提案材料（未成立的主线名、专题中同前缀的页、不标注主线的活动）、画像证据、教训、变化过的页、疑似重复的页、互相冲突的画像条目、领域卫生与篇幅提示。
_Avoid_: 诊断、lint 结果

**待清理的画像候选**（Redundant candidate）：
与画像中某条或拒绝列表中某条是同一偏好的画像候选；整理中直接删除，不再交用户确认。
_Avoid_: 重复候选、过期候选

**客户端**（Client）：
读取上下文文件与 skill 目录、并经 MCP 连接常驻服务的程序。TacitMemory 不区分客户端种类，只以绑定时用户给出的名称（客户端名称）指代它。
_Avoid_: harness、宿主、IDE、Agent（指程序时）

**上下文文件**（Context file）：
客户端在每个会话开始时读入的全局说明文件；常驻区块写在其中。
_Avoid_: 配置文件、提示词文件

**skill 目录**（Skills folder）：
客户端扫描 skill 的目录，例如 `~/.agents/skills`；绑定在其中放置指向记忆 skill 与两个记忆指令的链接。可被多个客户端共用。
_Avoid_: 插件目录、技能库

**常驻区块**（Resident block）：
常驻服务在客户端全局上下文文件中维护的一段内容，由起止标记界定，包含全局画像、领域索引与主线一节（活跃的主线显示背景说明与近期摘要，安静的只显示背景说明），并声明自身是可能过时、可被稀释的记忆，而非强制规则；外壳与四节引导语可由 `prompts/resident.md` 整份替换。
_Avoid_: 受管区块、注入区、常驻区

**绑定**（Binding）：
一个客户端名称、一个上下文文件与一个 skill 目录的组合：常驻服务在该上下文文件中维护常驻区块，并在该 skill 目录中保持三个链接。MCP 的注册由用户在客户端中自行完成。多个绑定可以共用同一个上下文文件或 skill 目录。绑定记录存于 `bindings.json`。
_Avoid_: 安装、注册、接入、适配

**客户端能力记录**（Client record）：
服务对每个连接过的客户端（按名称，忽略大小写）保存的一条记录：名称、最近连接的日期、最近一次连接的协议版本、声明的能力与扩展；能力与扩展各是该客户端历次声明的并集，只增不减，要重置就停止服务后删除 `clients.json`。不保存版本。存于 `clients.json`，属运行状态而非记忆，不进入备份；`tacit status` 的客户端栏显示它，只观察，不改变任何行为。
_Avoid_: 客户端记录、客户端状态、客户端日志、客户端缓存

**常驻**（Resident）：
会话开始时就在上下文中的进入方式。与之相对的是**按需**（On-demand）：由 Agent 主动检索取得。
_Avoid_: Push、Pull、预加载

### MCP 接口

客户端经常驻服务的 MCP 工具读写记忆；工具名、`op` 取值与参数名一律使用英文，下列各条只说明它们对应的概念。整理报告另以 MCP 资源提供，只在 `tacit-consolidate` 中读取。

**检索工具**（`recall`）：
按 `query` 检索，或以 `page`（页面标识）读取整页，二者取一；只给 `workstream` 时返回主线简报。`kind=persona` 读整份画像，`kind=rules` 读领域规则（`domain` 指定领域），两者都不检索，每条附短码。结果以小节为单位返回，每节附短码。
_Avoid_: 搜索、search

**写页工具**（`write_page`）：
增改细节页。`op` 取 `create`（新建页，须带 `type`）、`upsert_section`（新增或改写小节）、`delete_section`（删除小节）、`update_header`（改页头的标题、类型、别名或相关页）。
_Avoid_: 编辑文档、写文档

**近期写入工具**（`write_recent`）：
记录活动，或以 `replaces` 改写当天的活动。没有 `op`；`text` 必填。主线不经它写入，由整理直接编辑 `workstreams.md`。
_Avoid_: 写日志、日志工具

**画像写入工具**（`write_persona`）：
改动画像。`op` 取 `add`（追加到画像节末尾；可带 `scope` 指定作用域，新领域须带 `trigger`）、`propose`（记为画像候选，可带 `scope`，不直接进入画像；命中拒绝列表或已在画像中的不记录）、`replace`（按短码原地改写，保持原作用域）与 `delete`（按短码删除）。
_Avoid_: 修改人设、更新画像

**操作**（`op`）：
写入工具用于分派的参数，取值见上面各工具。
_Avoid_: action、类型

**画像节参数**（`persona_section`）：
`add` 与 `propose` 写入的节，取值为 `red_lines`（红线）、`working_rules`（协作规则）、`preferences`（偏好）与 `profile`（档案）。
_Avoid_: 分类、节名

**原话**（`source`）：
用户本人的原话，一行，保存为画像条目或活动下的注释；`write_persona` 的 `add`、`replace`、`propose` 必填。
_Avoid_: 引文、出处

**替换标识**（`replaces`）：
今日某条活动的短码；传入时原地改写该活动而非新增，只匹配今天的活动。
_Avoid_: 覆盖、更新标识

**短码参数**（`hash`）：
按工具与操作指代两种短码之一：小节短码（`write_page` 的 `upsert_section` 改写已有小节、`delete_section`）或画像条目短码（`write_persona` 的 `replace`、`delete`）。
_Avoid_: 哈希、ID

**主线参数**（`workstream`）：
所属主线的名称，统一小写；`recall` 中用于筛选，`write_page` 中构成页面标识的第一段（专题用 `_topics`），`write_recent` 中为活动标注。
_Avoid_: 项目参数、仓库名、标签

**类型参数**（`kind`）：
`recall` 的取值：`pages`（只检索细节页）、`activities`（只检索活动，含汇总）、`rules`（领域规则）或 `persona`（整份画像）；缺省时两者都检索。后两者不检索，每条附短码。
_Avoid_: 分类、类别

**确认无密钥**（`confirm_no_secret`）：
仅在写入因疑似密钥被拒、且已与用户确认不含密钥后传入 `true`，跳过凭据检测。
_Avoid_: 忽略检测、跳过检查
