# TacitMemory 运维

本文档说明日常运维怎么做：文件在哪、怎么备份与恢复、日志看哪里、出了问题怎么排查。命令的完整选项见 `docs/reference/cli.md`，配置项见 `docs/reference/configuration.md`。

## 1. 文件在哪

`TACIT_HOME` 默认为用户目录下的 `.tacit`，可用同名环境变量改变位置。

```text
~/.tacit/
├── config.yaml                    # 配置
├── prompts/                       # 可编辑的提示词文档
├── skills/                        # 生成的 skill：tacit-memory、tacit-remember、tacit-consolidate，请勿手改
├── bindings.json                  # 已绑定客户端的记录
├── clients.json                   # 连接过服务的客户端记录（运行状态，不进入备份）
├── backups/                       # 备份（zip）：auto-、manual-、pre-restore- 三种；remote.json 记录云端备份的最近一次结果
├── logs/                          # serve 的日志（见第 3 节）
└── memory/                        # 记忆仓库（Git），memory.path 可改
    ├── persona.md、candidates.md、recent.md、recent/、pages/
    └── .cache/                    # 派生索引，可删除，由文件重建
```

绑定所给的两个路径会被改动（只改 TacitMemory 自己的部分）：

- 上下文文件（`connect --context`；omp 为 `~/.omp/agent/AGENTS.md`）：起止标记之间的常驻区块；
- skill 目录（`connect --skills`；omp 为 `~/.omp/agent/skills`）下的 `tacit-memory`、`tacit-remember`、`tacit-consolidate`：指向 `TACIT_HOME/skills/` 下同名 skill 的三个链接。

## 2. 备份与恢复

| 种类 | 何时生成 | 位置 | 清理 |
| :--- | :--- | :--- | :--- |
| 自动 | 每天第一次写入后 | `backups/auto-YYYY-MM-DD.zip` | 保留最近 `backup.keep` 份（默认 7；`0` 关闭自动备份） |
| 手动 | `tacit backup` | `backups/manual-*.zip`，或 `--to` 指定的目录 | 永不自动清理 |
| 保险 | 每次恢复前 | `backups/pre-restore-*.zip` | 不清理 |
| 云端 | `tacit backup --remote` | 远端 `tacit-backup.zip`，只保留一份 | 每次替换 |

所有备份都含记忆仓库（含 `.git`，不含 `.cache`）、`config.yaml`、`prompts/`、`bindings.json` 与清单。生成的 skill 不进入备份。

**云端备份**经通用 WebDAV 协议上传，任何支持 `PUT`、`MOVE`、`MKCOL`、`GET` 与 `DELETE` 的服务都可以用（`GET` 用于云端恢复，`DELETE` 只用于清理失败留下的 `.part` 文件）。在 `config.yaml` 中填写 `backup.webdav`（见 `docs/reference/configuration.md`「`backup`」）后执行 `tacit backup --remote`。远端不支持 `MOVE` 时报错，不会退回为直接覆盖。上传与恢复都只在执行命令时发生；上传不在本地留副本。

**恢复**：`tacit restore --from <zip>` 从本地备份恢复，`tacit restore --remote` 从云端备份恢复；先看预览，确认后执行，恢复前自动生成保险备份。默认只恢复记忆仓库，加 `--include-config` 一并恢复 `config.yaml` 与 `prompts/`；`bindings.json` 永不恢复。恢复期间记忆工具的调用排队等待，结束后索引全量重建。`restore --remote` 先把云端备份下载到 `backups/.download/tacit-backup.zip`，恢复成功后删除，失败则保留以便排查。

完整规则见 `docs/design.md` 第 12 节「备份与恢复」。

## 3. 日志

日志写入 `TACIT_HOME/logs/tacit-YYYYMMDD.log`，只有 `serve` 写入，内容与终端输出相同：按天一份，保留最近 14 份，级别从 Information 起，格式为「时间 级别 消息」；ASP.NET Core 与 MCP SDK 的日志只写警告。消息语言随记忆语言。

记录的事件：

- 启动（版本、端口、记忆仓库路径）、监听地址、端口被占用；
- 常驻区块渲染结果、提示词重载、索引同步失败、辅助模型最终失败与冷却；
- 管理 API 每次调用（方法、路由、状态码、耗时）、记忆工具每次调用（工具、`op`、页面标识或短码、结果、耗时）；
- 维护操作的开始、结束与失败（耗时）；索引重建的结果（文件、单元、向量、跳过的非 UTF-8 文件）与向量待补；
- 自动、手动与保险备份的写入与失败，明文密钥提示；云端备份的上传、下载与失败，以及 `remote.json` 写入失败；
- 恢复的开始、完成、回退与回退失败，以及临时目录未能删除；
- 客户端记录的更新（首次见到某个客户端，或其协议版本、能力、扩展变化），以及 `clients.json` 无法写入或无法读取。

不记录记忆正文、原话、查询文本、提示词内容、密钥与 WebDAV 密码。

## 4. 重建索引

派生索引（`memory/.cache/`）损坏或换了嵌入模型时执行 `tacit reindex`（需服务运行）。嵌入模型名或向量维度变化时服务也会自动丢弃旧向量并补齐，`reindex` 用于需要立即全量重建的情况。

换了决策模型后，也建议执行一次 `tacit reindex`。整理的判断缓存不随模型变化而失效（缓存键不含模型名，见 `docs/design.md` 第 9 节），内容没变的提问会沿用旧模型的答案；`reindex` 会清空判断缓存，之后的整理全部改问新模型。对话中实时写入的判断不读缓存，不受影响。

## 5. 排障

各命令的退出码见 `docs/reference/cli.md`「退出码」。

- **任何问题先看 `tacit status`**（各栏与退出码见 `docs/reference/cli.md`「`tacit status`」）。
- **找不到 `config.yaml`**：先执行 `tacit init`。
- **配置错误**：报告会列出全部出错的键路径，逐项修正后重试。
- **端口被占用**：`serve` 无法启动。修改 `server.port`（默认 18888）后，需自行更新客户端中的 MCP 配置，指向新的端点（`tacit connect` 打印的地址）；tacit 不再替你改写它。
- **提示词超限**：启动被拒绝，报告会指出文件与超出的字符数。精简超限的文件后重启即可（各文件的上限见 `docs/reference/configuration.md` 第 4 节，固定部分的上限见 `docs/design.md` 第 8 节）。
- **`init` 被拒绝**：记忆目录非空且不是 Git 仓库时拒绝初始化。改用空目录，或用 `--memory` 指向已有的记忆仓库。
- **辅助模型连不上**：先运行 `tacit probe`，据此检查 `url`（须为完整请求地址，路径与协议要求的一致）、`model`、`provider` 与密钥。服务不会因此退出：写入照常完成，判断视为"拿不准"，检索按降级规则处理（嵌入模型不可用时只走全文，详见 `docs/design.md` 第 4 节）。环境变量在进程启动时读取，修改后需重新打开终端并重启 `serve`；`tacit probe` 读取的是 `config.yaml`，不需要服务运行。
- **向量扩展无法加载**：启动时会校验 `vec0.dll` 的存在与哈希，不符即拒绝启动。确认它与 `tacit.exe` 位于同一目录，且未被替换。
- **omp 中记忆工具调用失败**：确认 `tacit serve` 正在运行，并且是绑定之后开的新会话。
- **自动备份失败**：只会提示，不影响已完成的写入，下一次写入时再次尝试。
