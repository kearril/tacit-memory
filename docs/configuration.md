# TacitMemory 配置参考

本文档说明 `TACIT_HOME`、环境变量、`config.yaml` 各配置项与 `prompts/` 提示词文档的格式和校验规则。日常修改建议直接使用 Web 界面的设置页与提示词页，本文供手动编辑文件时查阅。

## 1. `TACIT_HOME` 与环境变量

配置目录默认为 `~/.tacit`，可通过 `TACIT_HOME` 环境变量修改。所有环境变量均在进程启动时读取，修改后需重启 `tacit serve`。

| 用途 | 环境变量 |
| :--- | :--- |
| 指定配置目录 | `TACIT_HOME` |
| 提供密钥（非空时优先于 `config.yaml`） | `TACIT_EMBEDDING_API_KEY`、`TACIT_RERANKER_API_KEY`、`TACIT_DECISION_API_KEY`、`TACIT_WEBDAV_PASSWORD` |
| 关闭终端颜色与面板美化 | `NO_COLOR` |

## 2. `config.yaml`

`tacit init` 生成初始配置文件，Web 设置页保存时按固定键序整份重写（不保留注释，仅写出偏离默认值的项与 `language`）。

- 经设置页保存或带配置恢复后，服务在进程内重新加载立即生效；手动编辑文件需重启服务或在设置页保存后生效。
- 以下五项不随重新加载生效，需重启 `tacit serve`：`server.port`、`server.host`、`limits.page.minAliases`（设置页可改），以及 `memory.path`、`language`（设置页只读，须手动改文件）。
- 完整键结构：

```yaml
server:
  port: 18888
  host: 192.168.1.10
memory:
  path: ~/.tacit/memory
language: zh
machine: { name: …, description: … }
models:
  embedding: { provider: …, url: …, model: …, apiKey: … }
  reranker: { provider: …, url: …, model: …, apiKey: … }
  decision: { provider: …, url: …, model: …, apiKey: … }
limits:
  brief: 3000
  page: { minAliases: 2 }
consolidation: { remindAfterDays: 3, remindCandidates: 2, budgetSeconds: 20, maxConcurrency: 5, hintEntryLength: 200, hintResidentLength: 6000 }
decision:
  samePreference: { certain: …, unlikely: … }
  samePage: { certain: …, unlikely: … }
  sameTopic: { certain: …, unlikely: … }
  ruleConflict: { certain: …, unlikely: … }
retrieval: { candidates: 30, topK: 5, minRerankScore: … }
backup: { keep: 7, webdav: { url: …, username: …, password: …, file: tacit-backup.zip } }
sync: { remote: …, pullMinutes: 30 }
```

### `server` 与 `memory`

- `server.port`：监听端口，默认 `18888`（始终监听 `127.0.0.1`）。修改后需手动更新客户端的 MCP 端点配置。
- `server.host`：额外监听的单个 IP 地址（如 `192.168.1.10` 或 `0.0.0.0`），缺省不额外监听。仅接受纯 IP，不接受主机名、端口或 IPv6 方括号。设为非回环地址前须先通过本机 Web 界面设置访问密码，否则服务拒绝启动。
- `memory.path`：记忆仓库路径，默认 `TACIT_HOME/memory`。

> 访问密码的哈希与会话签名密钥保存在 `TACIT_HOME/access.json`，不属于配置文件，也不进入备份。

### `language` 与 `machine`

- `language`：取 `zh` 或 `en`，决定记忆内容与界面/CLI/日志语言（详见 [`design.md`](design.md)「语言划分」）。
- `machine.name`：本机标识（小写字母、数字与连字符，最长 32 字符）；省略时自动由主机名规整生成。修改后无需重启。
- `machine.description`：可选单行定位说明，显示在常驻区块机器名之后；不能含换行符，修改后无需重启。

### `models`

三个辅助模型均可省略（即未配置，按降级运行）。配置后可用 `tacit probe` 测试连通性。

- 每个模型仅接受 `provider`、`url`、`model`、`apiKey` 四个键，出现未知键会报错。
- 存在映射时 `url` 与 `model` 必填：`url` 须为完整 `http`/`https` 请求地址，程序原样请求、不自动追加路径。
- `provider` 省略时按通用协议请求（嵌入：OpenAI Embeddings；reranker：Cohere/Jina Rerank；决策：System One）；填 `cloudflare` 时选用 Cloudflare Workers AI 适配。
- `apiKey` 可选；设置时以 `Authorization: Bearer` 发送，未设时不发送认证头。

### `limits`、`consolidation`、`decision` 与 `retrieval`

- `limits.brief`（默认 `3000`）：主线简报字符上限。
- `limits.page.minAliases`（默认 `2`）：细节页最少别名个数（不足时拒绝写入，修改后需重启）。
- `consolidation`：
  - `remindAfterDays`（默认 `3`）：距上次整理满该天数时提示整理。
  - `remindCandidates`（默认 `2`）：上次整理后有新出现记录的候选达该数量时提示整理。
  - `budgetSeconds`（默认 `20`）：单次整理等待模型判断的总时限。
  - `maxConcurrency`（默认 `5`）：模型判断并发上限。
  - `hintEntryLength`（默认 `200`）与 `hintResidentLength`（默认 `6000`）：整理报告篇幅提示阈值，仅提示不拒写。
- `decision`：四项判断各自的 `certain` / `unlikely` 阈值。内置默认值基于 TypeSafe `jev-latest` 标定；更换其他决策模型前请核对并调整。
- `retrieval`：`candidates`（默认 `30`，全文与向量各自候选数）、`topK`（默认 `5`，最终返回条数上限）、`minRerankScore`（默认 `0.20`，reranker 最低分阈值）。

### `backup` 与 `sync`

- `backup.keep`（默认 `7`）：自动备份保留份数，`0` 表示关闭自动备份。
- `backup.webdav`：云端备份配置（省略即不启用）：
  ```yaml
  backup:
    keep: 7
    webdav:
      url: https://<WebDAV 服务地址>/tacit/   # 目录地址，必填
      username: <用户名>                      # 可选；填写时须同时提供密码
      password: <密码>                        # 可选，建议改用环境变量 TACIT_WEBDAV_PASSWORD
      file: workstation.zip                   # 可选，默认 tacit-backup.zip，须以 .zip 结尾
  ```
- `sync.remote`：记忆仓库同步用的私有 Git 远端地址（SSH、HTTPS 或本地路径），省略即不同步。
- `sync.pullMinutes`（默认 `30`，范围 `0–1440`）：定时拉取间隔分钟数，`0` 关闭定时拉取。

## 3. 密钥来源与优先级

| 配置键 | 对应环境变量 | 何时必需 |
| :--- | :--- | :--- |
| `models.embedding.apiKey` | `TACIT_EMBEDDING_API_KEY` | 嵌入模型需要认证时 |
| `models.reranker.apiKey` | `TACIT_RERANKER_API_KEY` | Reranker 需要认证时 |
| `models.decision.apiKey` | `TACIT_DECISION_API_KEY` | 决策模型需要认证时 |
| `backup.webdav.password` | `TACIT_WEBDAV_PASSWORD` | 配置了 `backup.webdav.username` 时 |

环境变量非空时始终优先于配置文件中的明文值。若配置文件含明文密钥，备份时会给出安全提示；`tacit status` 只显示密钥来源，不输出明文。

## 4. 提示词文档（`prompts/`）

`TACIT_HOME/prompts/` 下可放置四份自定义文档（`tacit init` 仅建空目录，不预生成文件；删除文件即恢复内置默认）：

| 文件 | 作用 | 生效方式与校验规则 |
| :--- | :--- | :--- |
| `rules.md` | 值得记 / 不记标准 | 整份替换内置默认；仅识别 `## Worth recording` 与 `## Not recorded` 两节，各最多 10 条顶层 `- ` 列表项 |
| `instructions.md` | 核心规则（何时检索与写入） | 整份替换内置默认；独占一行的 HTML 注释下发前删去且不计长度；与 `rules.md` 共同计入服务端说明固定部分上限（3000 字符） |
| `resident.md` | 常驻区块外壳与各节引导语 | 整份替换内置默认；不超过 1500 字符；小节标题须从 `## red_lines`、`## working_rules`、`## preferences`、`## profile`、`## situational_rules`、`## workstreams` 中选取且不重复 |
| `skill.md` | 个人写法约定 | 作为 `## User conventions` 追加到 `tacit-memory` skill 末尾；不超过 3000 字符，不能以 `---` frontmatter 开头 |

- 推荐通过 Web 界面的「提示词」页编辑并实时查看额度；如需手动编辑并以内置默认为起点，可执行：
  ```bash
  tacit show instructions --default > ~/.tacit/prompts/instructions.md
  ```
- 启动时校验失败将拒绝启动；运行中手动改坏某个文件时，该文件沿用上一有效版本并在状态与日志中报错，其余文件不受影响。所有修改在下一次会话生效。
