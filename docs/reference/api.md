# TacitMemory 管理 API 参考

本文档列出管理 API 的约定：地址、鉴权与防护、响应与错误格式，以及端点清单。它的使用者是 `tacit` 命令与 Web 界面。各操作背后的规则见 `docs/design.md`，Web 界面如何使用这些端点见 `docs/web.md`。

本文描述目标形态。表中的"状态"列标明是否已实现：**已实现**、**计划**。标为计划的部分，现行行为见 `docs/architecture.md` 第 5 节。

## 1. 地址与格式

| 项 | 值 | 状态 |
| :--- | :--- | :--- |
| 地址 | `http://<主机>:<端口>/api`，端口取自 `server.port`（默认 18888） | 已实现 |
| 监听 | 总是监听 `127.0.0.1`；`server.host` 给出的一个额外地址也监听，与 `server.port` 同一端口；额外地址绑定失败时只在回环上运行（见 `docs/reference/configuration.md`） | 已实现 |
| 请求与响应 | JSON，属性名为 camelCase，枚举为 camelCase 文本 | 已实现 |
| 接口描述 | `GET /api/openapi.json`，OpenAPI 文档；字段以它为准，本文不重复；仓库中的快照是 `web/openapi.json` | 已实现 |
| Web 界面 | `/ui` 下为静态文件，任何连接都不需登录：存在的文件原样返回；其他不带扩展名的路径返回入口页；带扩展名而找不到的路径 404；产物未构建时 `/ui` 下任何路径返回 404 与「Web 界面未构建」。`/` 重定向到 `/ui/`。入口页的缓存为 `no-store`，`assets/` 下的文件为 `immutable` 长期缓存，其余文件为 `no-cache` | 已实现 |

同一个监听上还有 `/mcp`（见 `docs/reference/mcp.md`）与 `/ui`（见上表）。

## 2. 鉴权与防护

按连接来源区分、请求防护、会话与访问密码均已实现。

### 按连接来源区分

依据是 TCP 连接的对端地址，不看任何请求头。「本机」指对端为任一回环地址（`127.0.0.0/8`、`::1`，含映射到 IPv6 的回环地址）的连接。

| 对端地址 | `/api` | `/mcp` |
| :--- | :--- | :--- |
| 回环地址（本机） | 免登录 | 可用 |
| 其他 | 带有效会话时与回环连接相同；否则 401，错误名 `ApiUnauthorized`（会话端点除外，见第 4 节） | 403，错误名 `McpRemoteRefused`，会话也不放行 |

- 其他设备只能访问 `/`（重定向到 `/ui/`）、`/ui` 之下与 `/api` 之下（后者按会话规则）；访问其他路径一律 403（`RemotePathRefused`）。路径按不区分大小写匹配，与路由的匹配一致。
- `tacit` 命令从本机调用，不带任何凭据。经 SSH 端口转发进来的连接同样来自回环地址，免登录。
- 不支持反向代理转发到回环地址：经它进来的请求会被当作本机而免登录。
- 不提供 HTTPS；其他设备的连接上的访问密码与密钥以明文传输。

### 访问密码与会话

- 访问密码只有一个，没有用户名。它的哈希与会话签名密钥保存在 `TACIT_HOME` 下的访问状态文件 `access.json` 中，不在配置里，没有对应的环境变量，不进入备份（见 `docs/reference/configuration.md`）。哈希用 PBKDF2-SHA256（600000 次迭代，每次设置都换新的盐），比较为定长时间比较；最短 8 个字符，首尾空白原样计入。
- `server.host` 为 `127.0.0.1` 以外的地址而没有访问密码时，启动服务被拒绝。
- 登录成功后服务发出会话 Cookie `tacit_session`：值为到期时间加上以访问状态文件中的密钥做的 HMAC-SHA256 签名，服务端不存会话清单；有效期从登录起固定 30 天，不随使用续期；属性为 `HttpOnly`、`SameSite=Strict`、`Path=/api`，不设 `Secure`（不提供 HTTPS）。会话在服务重启后仍有效。
- 退出登录只让当前浏览器的 Cookie 过期；被复制走的 Cookie 在修改访问密码之前仍然有效。
- 设置、修改或清除访问密码都换掉签名密钥，使全部已有会话失效。修改访问密码时，发起的非本机连接在同一响应中得到新的会话。
- 同一对端地址连续 5 次输错后，60 秒内该地址的登录请求返回 429，说明中给出剩余秒数；登录成功清零。429 的响应带 `Retry-After` 头，值为剩余秒数。计数只在内存中，服务重启即清零。未设访问密码时的登录请求同样计数。计数表只保留暂停中的地址与最近一次输错不足 60 秒的地址，最多 1024 个，超出时丢弃最旧的。
- 登录后的权限与回环连接相同。
- 访问密码的设置与清除规则：本机连接设置或修改密码不需要旧密码；非本机连接修改须提供当前密码，不对时 401（`PasswordWrong`）；错误的当前密码与登录计入同一地址的计数，暂停期内即使当前密码正确也返回 429（`LoginLimited`）。清除只接受本机连接，非本机 403（`PasswordClearRemoteRefused`）；运行中的配置里 `server.host` 为 `127.0.0.1` 以外的地址时清除被拒绝，409（`PasswordClearRefused`）。

### 请求防护

只作用于 `/api`。

| 防护 | 规则 | 拒绝时 |
| :--- | :--- | :--- |
| Origin | 带 `Origin` 头时，须与请求自身的源（协议加 Host 头）相同；对所有连接生效 | 403 |
| Host | 仅本机连接：主机名须为 `127.0.0.1`、`localhost` 或 `[::1]`，端口不限；其他连接不检查 | 403 |
| Content-Type | `POST` 与 `PUT` 须为 `application/json` | 415 |

防护先于鉴权判定：其他设备的请求若同时违反防护，得到的是防护的拒绝，而不是 401。

## 3. 响应与错误

错误体为 `{"error": "<错误名>", "message": "<记忆语言的说明>"}`，`message` 可直接显示；校验不通过时另带 `problems`。

| 状态码 | 含义 | 状态 |
| :--- | :--- | :--- |
| 200 | 成功 | 已实现 |
| 400 | 请求不被接受：请求体无法读取、缺少必填成员、取值无效；访问密码少于 8 个字符（`PasswordTooShort`）；配置校验不通过（`ConfigRejected`） | 已实现 |
| 401 | 其他设备的连接未登录或会话失效访问 `/api`（`ApiUnauthorized`）；访问密码不对（`PasswordWrong`）；修改密码时当前密码不对 | 已实现 |
| 403 | 被请求防护拒绝；其他设备的连接访问 `/mcp`；其他设备的连接访问 `/`、`/ui`、`/api` 之外的路径（`RemotePathRefused`）；其他设备的连接清除访问密码 | 已实现 |
| 404 | 请求的对象不存在：不是密钥的键路径（`SecretNotFound`）、没有该绑定（`BindingNotFound`） | 已实现 |
| 409 | 记忆或配置不处于该操作接受的状态，用户可以修正；`server.host` 为 `127.0.0.1` 以外的地址时清除访问密码（`PasswordClearRefused`）；保存配置时 `config.yaml` 已在读取之后被改过（`ConfigChanged`） | 已实现 |
| 415 | Content-Type 不是 `application/json` | 已实现 |
| 429 | 登录或修改访问密码被限速（`LoginLimited`），带 `Retry-After` 头（剩余秒数） | 已实现 |
| 500 | 环境失败（`git`、向量扩展、云端服务、文件系统）或服务意外失败；保存的配置在重新加载时打不开（`ReloadFailed`） | 已实现 |

校验配置或提示词文档不通过时返回 400，错误体的 `problems` 逐条给出出错位置与说明（`{path, message}`，配置为键路径，如 `retrieval.topK`），一次给出全部问题，供界面标到对应字段。

调用记一条日志（方法、路径、状态码、耗时），不含请求体与密钥；成功的读取不记：成功的 `GET` 与成功的 `POST /api/recall/trace`。检索追踪被拒或失败时照常记录，但只有路径与状态码，查询文本从不写入日志。

## 4. 端点

| 端点 | 作用 | 状态 |
| :--- | :--- | :--- |
| **会话与访问密码** | | |
| `GET /api/session` | 当前连接是否来自回环（`local`）、是否已登录（`loggedIn`）、是否已设访问密码（`passwordSet`），以及记忆语言（`language`）与服务版本（`version`）；不需登录 | 已实现 |
| `POST /api/session` | 以访问密码登录：`{password}`，成功则设会话 Cookie，返回同上的会话状态；不需登录 | 已实现 |
| `DELETE /api/session` | 退出登录：只让当前浏览器的 Cookie 过期；无会话时也成功；不需登录 | 已实现 |
| `PUT /api/access-password` | 设置或修改访问密码：`{password, current}`，`current` 为非本机连接必填；成功后返回会话状态，非本机连接同时得到新的会话 Cookie。非本机连接未登录时 401 | 已实现 |
| `DELETE /api/access-password` | 清除访问密码：只接受本机连接；`server.host` 为 `127.0.0.1` 以外的地址时 409 | 已实现 |
| **接口描述** | | |
| `GET /api/openapi.json` | OpenAPI 文档，描述本节各端点的请求、成功与错误响应（非本机连接须已登录） | 已实现 |
| **状态与维护** | | |
| `GET /api/status` | 状态报告；服务正沿用某份提示词文档的上一版时带 `prompts`：`items` 为 `{name, problem}`，无误时省略 | 已实现 |
| `GET /api/activity` | 近一年活动记录的每日条数（按主线与未归属）、整理日、读不了的月份文件 | 已实现 |
| `POST /api/reindex` | 重建派生索引（维护操作） | 已实现 |
| `POST /api/backup` | 手动备份或云端备份：`{target, to}` | 已实现 |
| `POST /api/restore` | 预览或执行恢复：`{path, remote, config, preview}` | 已实现 |
| `GET /api/backups` | `backups/` 下的本地备份清单（种类、时间、大小）、是否配置了云端备份与上一次云端备份的记录 | 已实现 |
| **配置** | | |
| `GET /api/config` | 文件中的值、运行中的值、默认值、各键的取值范围与是否需重启才生效，以及文件的哈希；密钥不给值 | 已实现 |
| `PUT /api/config` | `{hash, config}`：校验并写出整份 `config.yaml`，随后重新加载 | 已实现 |
| `GET /api/config/secrets/<键路径>` | 单个密钥当前生效的值 | 已实现 |
| `POST /api/probe` | 按已保存的 `config.yaml` 探测三个辅助模型，规则同 `tacit probe` | 已实现 |
| **绑定** | | |
| `GET /api/bindings` | 绑定清单与各自的检查结果 | 已实现 |
| `POST /api/bindings` | 新增绑定：`{client, contextFile, skillsFolder}` | 已实现 |
| `DELETE /api/bindings/<客户端名称>` | 解除绑定 | 已实现 |
| **提示词文档** | | |
| `GET /api/prompts` | 四份文档（`instructions`、`rules`、`resident`、`skill`）各自的名称、文本（文件的原文，文件不存在时为内置默认文本）、是否为内置默认、文件的哈希（文件不存在时为空）、长度信息 `measure`（见 check）与 `problem`（文件有误、服务正沿用上一版时的原因，否则为空）；另有 `residentSections`：`resident.md` 接受的节名，按常驻区块的顺序 | 已实现 |
| `POST /api/prompts/<文档名>/check` | 请求体 `{text}`，不写文件：返回 `{measure, problems}`。`measure` 为 `{length, limit, activityRoom, items}`：`resident`、`skill` 为该文本的字符数与上限；`instructions`、`rules` 为与另一份运行中的文档合计的服务端说明固定部分长度与上限 3000，`activityRoom` 为总长上限 4000 减去它（可为负）；`rules` 的 `items` 为两节的条目数与上限 10。长度按下发后的文本计。`problems` 为保存时会给出的全部拒绝，文本违反规则时仍为 200。未知文档名 404 | 已实现 |
| `PUT /api/prompts/<文档名>` | 请求体 `{hash, text}`：按读取时的哈希（文件不存在时为空）比对，校验后以 LF 换行写文件并立即生效；返回 `{document, notices}`（新状态与常驻区块渲染失败的说明）。校验不通过 400（`PromptRejected`，`problems` 列出全部错误）；文件已在别处改动 409（`PromptChanged`）；未知文档名 404（`PromptUnknown`） | 已实现 |
| `DELETE /api/prompts/<文档名>` | 删除文件，回到内置默认并立即生效，返回同上；文件不存在时直接返回当前状态；内置文本放不下时 400 并保留文件；未知文档名 404 | 已实现 |
| **记忆** | | |
| `GET /api/memory/persona` | 画像与画像候选，从文件现读，不计入使用统计，不改动任何文件；文件不存在时为空 | 已实现 |
| `GET /api/memory/workstreams` | 已成立的主线与未成立的主线名两组，从文件现读，不计入使用统计 | 已实现 |
| `GET /api/memory/workstreams/<名>` | 一条主线的结构化简报，取数与排序同 `recall` 的主线简报，不截断、不计入使用统计。既未成立、也没有细节页或活动时 404（`MemoryNotFound`） | 已实现 |
| `GET /api/memory/activity` | 有活动文件的月份（最新在前）与服务所在机器的当月，不计入使用统计 | 已实现 |
| `GET /api/memory/activity/<yyyy-MM>` | 某月的活动按天分组，附该月的汇总与文件的路径和格式问题；不计入使用统计。月份不是 `yyyy-MM` 时 400（`RequestInvalid`）；当月没有文件时为空，其他月份没有文件时 404（`MemoryNotFound`） | 已实现 |
| `GET /api/memory/pages` | 全部细节页的清单，从文件现读，不计入使用统计 | 已实现 |
| `GET /api/memory/pages/<主线>/<slug>` | 一个细节页：页头、文件、小节与各自的修改日期和召回次数；不计入使用统计。页面不存在 404（`MemoryNotFound`），页面标识不合规 400（`RequestInvalid`） | 已实现 |
| `POST /api/recall/trace` | 检索追踪：执行一次与 `recall` 等价的检索，逐阶段返回每个检索单元的名次、分数与去向；不计入使用统计，不改动任何文件。请求体 `{query, workstream, from, to, kind, minRerankScore, topK, candidates}`，除 `query` 外均可省略 | 已实现 |
| **日志** | | |
| `GET /api/logs` | `{today, dates}`：服务所在机器的今天，与有日志文件的日期（`yyyy-MM-dd`，最新在前）；今天可能还没有文件 | 已实现 |
| `GET /api/logs/<日期>` | `{date, today, entries, next}`：某天的服务日志，从旧到新。`entries` 每项 `{time, level, message}`：`time` 为带时区偏移的 ISO 8601（文件开头不带时间的行为 `null`），`level` 为 `information`、`warning`、`error`，`message` 含该条之后的续行（多条原因、异常堆栈），以换行连接；文件末尾尚未写完的一行不返回。`next` 为文件当前的总条数。查询参数 `after`（缺省 0）只返回第 `after` 条之后的条目，供页面跟随时取增量：文件只追加，条目的序号不变；`after` 大于现有条数时返回空的 `entries`，`next` 仍为现有条数；`after` 不是非负整数时 400（`RequestInvalid`）。日期不是 `yyyy-MM-dd` 的日历日期时 400（`RequestInvalid`）；今天没有文件时返回空的 `entries`，其他日期没有文件时 404（`LogDateNotFound`） | 已实现 |

已实现端点的补充说明：

- `GET /api/activity`：窗口为本周（周一开始，按服务的本地日期）及之前 52 周，截至今天，`from` 与 `to` 给出起止日期。`days` 只列出有活动的日子，每天给出各主线的条数（`workstreams`，按写下时的主线标注，多者在前）与未归属的条数（`unlabeled`）；日期标题下每条活动计一次，续行、来源注释与汇总节不计。`consolidationDays` 是窗口内有整理提交的日子，判定与状态报告的「上次整理」相同。某个月份文件不是有效 UTF-8 或读不了时跳过并列入 `skippedFiles`；记忆仓库的历史读不了时 `historyUnreadable` 为真、整理日为空。两者都不使请求失败。只读，不等待维护操作。

- `GET /api/memory/persona`：返回 `personaFile` 与 `candidatesFile`（`{path, problems}`：文件的完整路径，与整理报告对该文件列出的格式问题，每条为 `{line, location, kind, description}`，`line` 为所在行号，问题不在某一行时为空）；`global`（四节依次为 `redLines`、`workingRules`、`preferences`、`profile`，没有条目时也列出，其后是标题不是这四节的部分，`section` 为空）；`domains` 与 `workstreams`（按标签与名称排序，每块为 `{name, line, trigger, sections}`，`trigger` 只有领域有）。每节为 `{section, heading, entries}`，每条为 `{line, code, text, lines, sources}`：`code` 为 `write_persona` 所用的短码，不是条目的行为空；`lines` 为不是注释的续行（去掉首尾空白）；`sources` 为来源注释读出的来源，每条为 `{date, quote}`，没写日期或原话的一项为空；其他注释两边都不列。`candidates` 与 `rejectionList` 每条为 `{title, section, scope, occurrences}`，`scope` 为 `global`、`workstream:<名>` 或 `domain:<标签>`，出现记录为 `{date, quote, rejected}`。
- `GET /api/memory/workstreams`：返回 `{established, unestablished, file}`，每项为 `{name, background, recent, pages, lastActivity}`。`established` 按 `workstreams.md` 中的顺序；`unestablished` 是细节页文件夹或活动标注用到、但没有成立的主线名，按名称排序，`background` 与 `recent` 为空。`pages` 为该主线文件夹中的页数，`lastActivity` 为最近一条标注该主线的活动的日期，没有时为空。`file` 为 `workstreams.md` 的 `{path, problems}`，同画像；没有背景说明的主线不算成立，不在 `established` 中，问题落在它的标题行。
- `GET /api/memory/workstreams/<名>`：返回 `{name, established, background, recent, conventions, openItems, groups, overturned, activities, summary}`。`conventions` 为画像中该主线作用域的各节，形状同 `GET /api/memory/persona` 的节；`openItems` 每项为 `{page, lines}`，即工作页「遗留」一节的非空行；`groups` 按决策、故障、工作、实体的顺序，没有有效类型的页 `type` 为空、排在最后，每组的 `pages` 为 `{id, title, aliases, changed, uses, lastUse}`，排序同简报，被推翻的决定不在其中而列入 `overturned`；`activities` 为最近的活动 `{date, code, text}`，条数同简报；`summary` 为该主线有活动的最近一周的汇总 `{label, first, last, text}`，没有时为空。
- `GET /api/memory/activity`：返回 `{months, current}`：`months` 为 `recent/` 下月份文件的月份（`yyyy-MM`），最新在前；`current` 为服务所在机器今天的月份，可能还没有文件。
- `GET /api/memory/activity/<yyyy-MM>`：返回 `{month, file, days, summaries}`。`file` 为 `{path, problems}`，同画像；`days` 只列有活动的日子，从旧到新，每天为 `{date, activities}`，活动按文件顺序，每条为 `{code, workstream, text, lines, sources, uses}`：`code` 为 `write_recent` 替换时所用的短码，`workstream` 为主线标注（没有时为空），`text` 不含标注，`lines` 与 `sources` 为其下缩进的各行，分法同画像条目，`uses` 为 `recall` 返回它的次数（从未返回或派生索引尚未收录时为 0）。`summaries` 为该月文件「汇总」一节中的汇总，按文件顺序，每条为 `{kind, label, first, last, text}`，`kind` 为 `week` 或 `month`。文件有格式问题时照常返回能读出的部分；不在日期标题下的条目不列出。
- `GET /api/memory/pages`：`pages` 按页面标识排序，每项为 `{id, workstream, title, aliases, type, writtenType, first, last, changed, uses}`：专题的 `workstream` 为 `_topics`；`type` 为 `incident`、`decision`、`entity`、`work` 之一，页头没写或写了别的值时为空；`writtenType` 只在页头写了这四种以外的值时给出原文，否则为空；`first`、`last` 为页头的首末日期；`changed` 为各小节最近的修改日期，没有时取 `last`；`uses` 为页面各单元被 `recall` 返回的次数之和。非 UTF-8 的页也列出，标题为 slug。
- `GET /api/memory/pages/<主线>/<slug>`：返回 `{id, workstream, title, aliases, type, writtenType, first, last, related, file, uses, sections}`：`type` 与 `writtenType` 同清单；`related` 每项为 `{id, exists}`；`file` 为 `{path, problems}`，同画像；`sections` 按文件顺序，每节为 `{name, code, changed, uses, body, sources}`，`body` 为不含标题的 Markdown 原文，`sources` 为其中的来源注释（独占一行的与行内的都算，同一条只列一次），每条为 `{comment, source}`：`comment` 为注释原文（不含两侧空白），`source` 为读出的 `{date, quote}`；界面按注释原文查找，查不到的注释不显示。非 UTF-8 时没有小节、标题为 slug，`problems` 写明原因；页头读不出时标题为 slug，小节照常返回。
- `POST /api/recall/trace`：`workstream`、`from`、`to`、`kind` 的含义与校验同 `recall` 带 `query` 的检索（`kind` 只接受 `pages`、`activities`；`workstream` 不校验写法，与检索一样只用于筛选）；`minRerankScore`、`topK`、`candidates` 临时替换 `config.yaml` 中 `retrieval` 的同名值，范围与配置相同。参数有误时 400（`RequestInvalid`），`problems` 逐条给出参数名与说明，一次给出全部问题。返回 `settings`（三个值本次所用的 `value` 与配置中的 `configured`，未写时为内置默认）、`fullText`（`phrase` 短语匹配、`substring` 单字子串匹配、`none` 没有可检索的文本）、`embedding` 与 `reranker`（`{configured, answered, coolingDown, milliseconds}`：是否配置、是否作答、是否因冷却被跳过、耗时）、`counts`（全文与向量各返回的单元数、融合后的单元数、送 reranker 的数量、进入分组的数量、结果数）、`results`（与 `recall` 相同的分组与顺序：`number`、`kind` 为 `page`、`activity` 或 `summary`，页面带 `page`、`title` 与显示的 `sections`，活动带 `date`、`code` 与 `text`，汇总带 `date`、`period` 与 `text`）、`text`（Agent 从 `recall` 收到的原文，逐字相同）、`notes`（`recall` 附在原文末尾的降级说明，逐字相同，没有降级时为空）与 `units`（两路返回的全部单元，按融合名次：`kind` 为 `section`、`clue`（标题与别名，界面称线索）、`activity` 或 `summary`，`source`、`section`、`code`、`date`、`workstream`、`text`，`fullTextRank` 与 `bm25`（单字子串匹配时没有 BM25 值）、`vectorRank` 与 `distance`、`rrf` 与 `fusedRank`、`rerankScore`，`fate` 为 `shown`、`beyondTopK`、`belowThreshold`、`beyondCandidates` 或 `unreadable`（结果在前 topK 内、但页面已读不出），`result` 为所在结果的序号）。与 `recall` 共用辅助模型的冷却状态：追踪中失败的模型进入冷却，冷却中的模型在追踪中同样被跳过。
- `GET /api/backups`：`local` 只列出按备份命名的文件（`auto-*`、`manual-*`、`pre-restore-*`，种类为 `auto`、`manual`、`preRestore`），按文件的最后写入时间从新到旧；`backups/` 不存在时为空。`remoteConfigured` 表示配置了 `backup.webdav`；`lastRemote` 取自 `backups/remote.json`，没有记录或读不了时为 `null`。不联网，不列出云端的文件，不等待维护操作。
- `GET /api/config`：`file` 为 `config.yaml` 中写下的值，未写的项为 `null`；`running` 为运行中的服务所用的值，需重启的项在重启前与文件不同；`defaults` 为各项未写时的取值，含内置的判断阈值；`keys` 逐键给出键路径 `path`（枚举 `ConfigKeyPath`，OpenAPI 文档列出全部取值）、`min`、`max`、可选值 `choices`、需重启 `restart`、只读 `readOnly`（`memory.path`、`language`）与密钥 `secret`。密钥（`apiKey`、`password`）在三者中都为 `null`；`secrets` 逐个给出四个密钥的键路径、来源（`environment`、`configFile`，或未设置时为 `null`）与对应的环境变量名。`config.yaml` 无法加载时（如被手动改坏，或从备份恢复回来就有错误）409（`ConfigInvalid`），`problems` 逐个给出出错的键路径与原因；服务仍按上次加载的配置运行。只读，不等待维护操作。
- `GET /api/config/secrets/<键路径>`：键路径为 `models.<模型>.apiKey` 或 `backup.webdav.password`，返回 `{value}`：环境变量的值，否则 `config.yaml` 中的值，都没有时为 `null`。不是密钥的键路径 404（`SecretNotFound`）。日志只记路径，不记值。
- `PUT /api/config`：`hash` 取自 `GET` 的结果，与文件当前的哈希不符时 409（`ConfigChanged`），文件不动。`config` 为完整的配置，写出时只写与默认不同的项，键顺序固定，不带注释；其中的密钥被忽略。`secrets` 可省略，按键路径给出要改的密钥：字符串写入文件，`null` 从文件中删除；没给出的密钥沿用文件中原有的值。所属的模型或云端备份没有配置时，对应的密钥随之不写；不是密钥的键路径 400。校验与 `tacit serve` 读取配置相同，不通过时 400 并带 `problems`，文件不动。写出后作为维护操作重新加载，等待进行中的维护操作；需重启的项（`server.port`、`server.host`、`limits.page.minAliases`）保存后仍按原值运行。只读项（`memory.path`、`language`）与文件中的值不同时 400；`server.host` 为 `127.0.0.1` 以外的地址而没有访问密码时 400，问题落在 `server.host`。新配置打不开时把文件写回原样，服务按原配置继续运行，返回 500（`ReloadFailed`）。成功时返回新的哈希与 `restart`：文件中与运行中不同的需重启的项，每项给出键路径、运行中的值与文件中的值（`{path, running, saved}`，都是文字）。
- `POST /api/probe`：每次重新读取 `config.yaml`，与运行中的服务所用的配置无关；未配置的模型记为跳过，不发请求。某个模型失败不使请求失败，结果逐个给出（`outcome`、`failure`、`statusCode`、响应体前 500 个字符、`elapsedMilliseconds`）；`config.yaml` 读不了时 409。不改变模型的健康状态，不等待维护操作。
- `GET /api/bindings`：返回 `{items, mcp}`。`items` 与状态报告绑定栏的 `items` 相同，逐个给出客户端名称、两个路径、问题（`problem`，正常时为 `null`）、有问题的 skill 链接与共用路径的其他绑定；`mcp` 为已绑定的客户端要注册的 MCP 服务（`url`、`transport`、`name`），与绑定成功时返回的相同。不等待维护操作。
- `POST /api/bindings`：规则同 `tacit connect`，路径开头的 `~` 由服务展开为主机上用户的主目录；被拒绝时不改动任何文件，`message` 与命令行给出的原因相同：客户端名称不合规则 400（`ClientNameInvalid`），目录不存在、名称已绑定到其他路径、skill 链接名被占用等 409。成功时返回 `{client, lines, mcp}`：`lines` 为做了什么的摘要，`mcp` 为要在客户端中注册的 MCP 服务（`url`、`transport`、`name`）。
- `DELETE /api/bindings/<客户端名称>`：规则同 `tacit disconnect`，名称不区分大小写；没有该绑定时 404（`BindingNotFound`）。返回 `{client, lines, mcp: null}`。
- 两者与服务渲染常驻块互斥，不等待维护操作。
- `POST /api/backup`：`target` 为 `local` 时 `to` 可给出主机上的绝对目录，省略则写到 `backups/`；`target` 为 `remote` 时执行云端备份。
- `POST /api/restore`：`path` 与 `remote` 二选一。`preview` 为真时只返回预览（`remote` 时先下载云端备份），为假时执行，且 `remote` 不能用于执行；`config` 为真时一并恢复 `config.yaml` 与 `prompts/`，恢复后服务按恢复回来的配置重新加载（规则同 `PUT /api/config` 后的重新加载）；结果的 `restart` 列出需重启才生效、与运行中不同的项（`{path, running, saved}`），不带配置或配置不能加载时为空，`notices` 说明是否已重新加载。校验在核心中完成。
