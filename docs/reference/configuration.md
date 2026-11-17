# TacitMemory 配置参考

本文档列出用户能配置的全部内容：`TACIT_HOME`、环境变量、`config.yaml` 与 `prompts/` 下的提示词文档。各配置项控制的行为见 `docs/design.md`，`TACIT_HOME` 下的文件布局见 `docs/reference/operations.md`。日常修改用 Web 界面的设置页即可，每一项都附解说；本文是手改文件时的参考。

## 1. `TACIT_HOME` 与环境变量

配置目录为 `TACIT_HOME`（默认 `~/.tacit`），包含 `config.yaml`、`prompts/`（提示词文档）、`skills/`（生成的 skill）、`bindings.json`、`backups/` 与 `logs/`。

环境变量只有三种用途：

| 用途 | 环境变量 |
| :--- | :--- |
| 指定配置目录 | `TACIT_HOME` |
| 提供密钥 | `TACIT_EMBEDDING_API_KEY`、`TACIT_RERANKER_API_KEY`、`TACIT_DECISION_API_KEY`、`TACIT_WEBDAV_PASSWORD`（见第 3 节） |
| 关闭终端美化 | `NO_COLOR`（终端里的颜色与面板都关掉，输出纯文本） |

环境变量在进程启动时读取，修改后需重新打开终端并重启 `tacit serve`。

## 2. `config.yaml`

`config.yaml` 由同一个写出器整份生成：`tacit init` 写出初始文件，此后经设置页保存时整份重写。写出的文件键顺序固定、不带注释，只含与默认值不同的项；`language` 总是写出。`tacit init` 写出的文件只有一行 `language: zh`（指定了 `--memory` 时另有 `memory.path`）。经设置页保存后，或从备份一并恢复 `config.yaml` 后，服务在进程内重新加载，新配置立即生效；手动修改文件后，在设置页保存任何改动或重启 `tacit serve` 才生效。五项例外不随重新加载生效，保持运行中的值直到重启：`server.port`、`server.host`、`limits.page.minAliases`（设置页可改，重启后生效），以及 `memory.path`、`language`（设置页只读，只能在文件中修改）。`tacit probe` 每次直接读取文件。

全部的键（`…` 处取值见下文各节）：

```yaml
server:
  port: 18888
  host: 192.168.1.10
memory:
  path: ~/.tacit/memory
language: zh
models:
  embedding: { provider: …, url: …, model: …, apiKey: … }
  reranker: { provider: …, url: …, model: …, apiKey: … }
  decision: { provider: …, url: …, model: …, apiKey: … }
limits:
  brief: 3000
  page: { minAliases: 2 }
consolidation: { remindAfterDays: 7, remindCandidates: 5, budgetSeconds: 20, maxConcurrency: 5, hintItemLength: 200, hintResidentLength: 6000 }
decision:
  samePreference: { certain: …, unlikely: … }
  samePage: { certain: …, unlikely: … }
  sameTopic: { certain: …, unlikely: … }
  ruleConflict: { certain: …, unlikely: … }
retrieval: { candidates: 30, topK: 5, minRerankScore: … }
backup: { keep: 7, webdav: { url: …, username: …, password: … } }
```

- 除 `models` 外各项均有默认值；`models` 中省略的模型即未配置（见 `docs/design.md`「辅助模型」中的「降级」）。判断阈值与 `minRerankScore` 的默认值内置。向量维度在启动时由嵌入模型的返回确定。
- 加载出错时列出全部出错的键路径。`init`、`disconnect` 与带 `--default` 的 `show` 不严格加载配置，其余命令遇到无效配置以退出码 3 结束。

### `server` 与 `memory`

- `server.port`：MCP 端点、管理 API 与 Web 界面监听的端口，默认 18888。服务总是监听 `127.0.0.1` 上的这个端口。修改后需自行更新客户端中的 MCP 配置（见 `docs/reference/mcp.md`）。
- `server.host`：额外监听的一个 IP 地址，缺省为没有。只接受 IP 地址文字，如 `192.168.1.10` 或 `0.0.0.0`（表示所有地址）；主机名、带端口的写法与 IPv6 的方括号写法都报错，报错中给出键路径 `server.host`。写 `127.0.0.1` 与不写相同。它与 `server.port` 用同一端口，只有其他设备能连上；本机连接的行为不变。额外地址绑定失败时服务仍只在回环地址上运行，状态报告标明原因；额外地址在探测之后才被占用时，服务去掉额外监听重试一次，不再重试额外地址。`0.0.0.0` 在 Windows 上与回环地址同时监听，在其他系统上只监听所有地址（回环端口仍须空闲）。修改后需重启服务。
- `memory.path`：记忆仓库的位置，默认为 `TACIT_HOME/memory`。

`server.host` 为 `127.0.0.1` 以外的地址时，服务只有在已设访问密码时才会启动；否则启动被拒绝，报告中给出键路径 `server.host` 与补救办法。访问密码不在配置中，见下文「访问状态文件」。

### 访问状态文件（不是配置）

访问密码的哈希与会话签名密钥保存在 `TACIT_HOME/access.json`，属运行状态：

- 它不是配置：`config.yaml` 中没有访问密码的键，没有对应的环境变量，没有命令行子命令；`tacit init` 不生成它；备份不含它，恢复不改变它。
- 文件不存在即「未设访问密码」。文件损坏时同样按未设访问密码对待，并记一条警告。
- 访问密码只经管理 API 设置、修改与清除（见 `docs/design.md`「访问与鉴权」）。

### `language`

`language` 取 `zh` 或 `en`，决定记忆语言；缺省规则与各类文字的语言划分见 `docs/design.md`「语言」。

### `models`

三个辅助模型各按一种通用协议请求（嵌入模型为 OpenAI Embeddings，reranker 为 Cohere/Jina Rerank，决策模型为 System One），协议与提供方适配的行为见 `docs/design.md`「辅助模型」。

- `models` 段或某个模型的映射缺失即为未配置，不报错。映射中只认 `provider`、`url`、`model`、`apiKey`，`endpoint` 等其他键按未知键报错。
- 映射存在时 `url` 与 `model` 必填且不得为空。`url` 须为 http 或 https 绝对地址，是完整的请求地址：程序原样保存、原样请求，不追加路径，也不补或删末尾的 `/`。
- `provider` 可省略（即通用协议），写 `cloudflare`（不区分大小写）选用 Cloudflare Workers AI 的适配；其他值按配置错误报告，并列出可用取值。已配置的模型的 `model` 都必填，包括 Cloudflare 的 reranker（它的模型名在 URL 中，`model` 只用于显示）。
- `apiKey` 可省略，见第 3 节。

配置后可用 `tacit probe` 检查模型能否正常使用。

### `limits`

写入不设长度与条数上限（见 `docs/design.md`「写入」中的「写入规则」）；简洁靠提示词引导，过长的内容由整理报告提示，由用户决定是否精简。

- `limits.brief`：主线简报的字符上限，默认 3000；超出时只截短页目录（见 `docs/design.md`「检索」）。
- `limits.page.minAliases`：细节页别名的最少个数，默认 2，不足时拒绝写入。

工具说明中的别名最少个数取自 `limits.page.minAliases`，改配置后需重启服务。

### `consolidation`

- `remindAfterDays`（默认 7）：距上次整理达到该天数时，服务端说明出现整理提示。四个触发条件见 `docs/design.md`「注入」。
- `remindCandidates`（默认 5）：候选达到该个数时，服务端说明出现整理提示。
- `budgetSeconds`（默认 20）：一次整理最多等模型的秒数，到点照常出报告，没算完的项注明"再运行一次 `tacit-consolidate` 接着算"。
- `maxConcurrency`（默认 5）：同时进行的模型判断数，由服务内的多次整理共享。
- `hintItemLength`（默认 200）：画像条目或上次整理以来新增的活动超过该字符数时，整理报告的"篇幅提示"列出它的位置与长度。只提示，不拒绝写入。
- `hintResidentLength`（默认 6000）：常驻区块总长超过该字符数时，整理报告的"篇幅提示"给出一行说明。只提示，不拒绝写入。

计时与缓存规则见 `docs/design.md`「整理」。

### `decision` 与 `retrieval`

- `decision`：四种判断各自的上下阈值（`certain`、`unlikely`），三档的划分见 `docs/design.md`「辅助模型」中的「判断」。内置默认值是用某一个决策模型实测标定的；换用其他决策模型后可能不再适用，可能把不重复的内容判为重复而拒绝写入，或漏掉该拦下的重复与冲突，需自行核对并调整。
- `retrieval`：`candidates` 为全文与向量两路各取的候选数，`topK` 为返回条数，`minRerankScore` 为 reranker 分数阈值（见 `docs/design.md`「检索」）。

### `backup`

- `backup.keep`：自动备份保留的份数，默认 7；`0` 只关闭自动备份。
- `backup.webdav`：云端备份的 WebDAV 目录，不写即不使用云端备份。

```yaml
backup:
  keep: 7
  webdav:
    url: https://<WebDAV 服务地址>/tacit/   # 目录；末尾的 / 可省略
    username: 你的用户名                   # 可省略，省略时不发送认证
    password: 你的密码                     # 可省略，改设环境变量 TACIT_WEBDAV_PASSWORD
```

- `backup.webdav.url` 必填，须为 http 或 https 地址，末尾补 `/`。
- 写了 `username` 时必须有密码（配置中或环境变量中），见第 3 节。

远端须支持的 WebDAV 方法与上传规则见 `docs/design.md`「运行」中的「备份与恢复」，操作步骤见 `docs/reference/operations.md`。

## 3. 密钥

每个密钥有固定的环境变量名，环境变量非空时优先，否则使用配置中的明文值，两处都没有即为未设置：

| 配置键 | 环境变量 | 何时必需 |
| :--- | :--- | :--- |
| `models.embedding.apiKey` | `TACIT_EMBEDDING_API_KEY` | 嵌入模型需要密钥时 |
| `models.reranker.apiKey` | `TACIT_RERANKER_API_KEY` | reranker 需要密钥时 |
| `models.decision.apiKey` | `TACIT_DECISION_API_KEY` | 决策模型需要密钥时 |
| `backup.webdav.password` | `TACIT_WEBDAV_PASSWORD` | 配置了 `backup.webdav.username` 时 |

- 辅助模型没有密钥不是错误：配置了密钥的请求带 `Authorization: Bearer`，没有则不带认证头。
- WebDAV 只有写了 `username` 时密码必填；两处都没有时加载配置报错，同时指出键路径与环境变量名。
- 配置中写了明文密钥时，备份会提示妥善保管备份文件。`tacit status` 显示每个密钥的来源（环境变量或配置文件），不显示值。

## 4. 提示词文档

提示词文档位于 `TACIT_HOME/prompts/`：

| 文件 | 作用 | 行为 |
| :--- | :--- | :--- |
| `rules.md` | 值得记 / 不记的标准 | 存在时整份替换内置默认（内置默认按记忆语言给出） |
| `instructions.md` | 核心规则（何时检索、何时写） | 存在时整份替换内置默认 |
| `resident.md` | 常驻区块的外壳与各节引导语 | 存在时整份替换内置默认 |
| `skill.md` | 个人写法约定 | 接在记忆 skill 末尾 |

四份文档都不会自动生成：`tacit init` 只创建空的 `prompts/` 目录，文件存在即用户自定义，删除文件即回到内置默认。

修改有两种入口，规则相同：

- **Web 界面的提示词页**（`docs/design.md`「Web 界面」）：以内置默认为起点编辑，保存前由服务校验，不通过则文件不变；通过后立即生效，「恢复默认」即删除文件。
- **手动改文件**：服务监听 `prompts/`，保存后重新加载；出错的文件沿用上一版并提示（见下）。需要以内置默认为起点时，在 PowerShell 7 中执行（输出为 UTF-8 无 BOM；`rules`、`resident` 同理）：

```powershell
tacit show instructions --default > ~/.tacit/prompts/instructions.md
```

各段文字的职责见 `docs/dev/prompts.md`。各文件在启动与运行中保存时按以下规则检查，长度上限均以字符计：

- **核心规则**（`instructions.md`）：独占一行的 HTML 注释（`<!-- -->`）下发前删去，不计入长度；与文字同行的注释原样下发。它与 `rules.md` 共同计入服务端说明固定部分的长度上限（见 `docs/design.md`「注入」中的「服务端说明」）。
- **值得记标准**（`rules.md`）：「值得记」与「不记」两节各最多 10 条，条目为顶层 `- ` 列表项；节标题按任一支持的语言识别，其他小节与续行忽略。
- **常驻区块**（`resident.md`）：全文不超过 1500 字符，换行按 LF 计，末尾空行也计入。开头为外壳正文，之后以 `## red_lines`、`## working_rules`、`## preferences`、`## profile`、`## situational_rules`、`## workstreams` 分节，小节标题须与名称完全一致（领域索引与主线两节的引导语写在对应标题下，标题与数据仍由服务生成）。出现其他小节标题或同一小节出现两次时拒绝启动，并指出文件与行号。缺少某一节时，该节只显示条目，不显示引导语；生成节在无内容时整节不出现。
- **记忆 skill**（`skill.md`）：不超过 3000 字符，不能以 frontmatter（开头的 `---` 部分）起始。
- 以上文档均不检查语言，可用任一语言书写。
- 启动时违反限制则拒绝启动，报告指出文件与超出的字符数。运行中保存时逐个文件检查：某文件出错则只它沿用上一版，其余文件照常生效；原因写入日志，并在提示词页、仪表盘的待处理与 `tacit status` 中列出，直到文件改正或回到内置默认。`rules.md` 与 `instructions.md` 合计超过固定部分上限时两者都沿用上一版。
- 修改在下一个会话生效。
