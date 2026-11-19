# TacitMemory 运维

本文档说明日常运维怎么做：文件在哪、怎么备份与恢复、日志看哪里、出了问题怎么排查、怎么安装与升级。命令的完整选项见 `tacit <命令> --help`，配置项见 `docs/reference/configuration.md`。

## 1. 文件在哪

`TACIT_HOME` 默认为用户目录下的 `.tacit`，可用同名环境变量改变位置。

```text
~/.tacit/
├── config.yaml                    # 配置
├── prompts/                       # 可编辑的提示词文档
├── skills/                        # 生成的 skill：tacit-memory、tacit-remember、tacit-consolidate，请勿手改
├── bindings.json                  # 已绑定客户端的记录
├── access.json                    # 访问状态：访问密码的哈希与会话签名密钥（不是配置；不进入备份；删除即未设访问密码）
├── clients.json                   # 连接过服务的客户端能力记录（按名称一条，能力与扩展为历次声明的并集；停止服务后删除即重置；运行状态，不进入备份）
├── backups/                       # 备份（zip）：auto-、manual-、pre-restore- 三种；remote.json 记录云端备份的最近一次结果
├── logs/                          # serve 的日志（见第 3 节）
└── memory/                        # 记忆仓库（Git），memory.path 可改
    ├── persona.md、candidates.md、workstreams.md、recent/、pages/
    └── .cache/                    # 派生索引，可删除，由文件重建
```

绑定所给的两个路径会被改动（只改 TacitMemory 自己的部分）：

- 上下文文件（`connect --context`）：起止标记之间的常驻区块；
- skill 目录（`connect --skills`）下的 `tacit-memory`、`tacit-remember`、`tacit-consolidate`：指向 `TACIT_HOME/skills/` 下同名 skill 的三个链接。

## 2. 备份与恢复

| 种类 | 何时生成 | 位置 | 清理 |
| :--- | :--- | :--- | :--- |
| 自动 | 每天第一次写入后 | `backups/auto-YYYY-MM-DD.zip` | 保留最近 `backup.keep` 份（默认 7；`0` 关闭自动备份） |
| 手动 | `tacit backup` | `backups/manual-*.zip`，或 `--to` 指定的目录 | 永不自动清理 |
| 保险 | 每次恢复前 | `backups/pre-restore-*.zip` | 不清理 |
| 云端 | `tacit backup --remote` | 远端 `backup.webdav.file`（默认 `tacit-backup.zip`），只保留一份 | 每次替换 |

所有备份都含记忆仓库（含 `.git`，不含 `.cache`）、`config.yaml`、`prompts/`、`bindings.json` 与清单。生成的 skill 不进入备份。访问状态文件 `access.json` 也不进入备份，恢复不会改变访问密码。

**云端备份**经通用 WebDAV 协议上传，任何支持 `PUT`、`MOVE`、`MKCOL`、`GET` 与 `DELETE` 的服务都可以用（`GET` 用于云端恢复，`DELETE` 只用于清理失败留下的 `.part` 文件）。在 `config.yaml` 中填写 `backup.webdav`（见 `docs/reference/configuration.md`「`backup`」）后执行 `tacit backup --remote`。远端不支持 `MOVE` 时报错，不会退回为直接覆盖。上传与恢复都只在执行命令时发生；上传不在本地留副本。

**恢复**：`tacit restore --from <zip>` 从本地备份恢复，`tacit restore --remote` 从云端备份恢复；先看预览，确认后执行，恢复前自动生成保险备份。默认只恢复记忆仓库，加 `--include-config` 一并恢复 `config.yaml` 与 `prompts/`；`bindings.json` 永不恢复。恢复期间记忆工具的调用排队等待，结束后索引全量重建。`restore --remote` 先把云端备份下载到 `backups/.download/<文件名>`，恢复成功后删除，失败则保留以便排查。

完整规则见 `docs/design.md`「运行」中的「备份与恢复」。

## 3. 日志

日志写入 `TACIT_HOME/logs/tacit-YYYYMMDD.log`，只有 `serve` 写入，内容与终端输出相同：按天一份，保留最近 14 份，级别从 Information 起，格式为「时间 级别 消息」；ASP.NET Core 与 MCP SDK 的日志只写警告。管理 API 的每次请求记一条（方法、路径、状态码与耗时），成功的 GET 请求除外：它们不改动任何东西，Web 界面的自动刷新又会让它们占满日志。消息语言随记忆语言。

记录的事件：

- 启动（版本、端口、记忆仓库路径）、监听地址、端口被占用；
- 常驻区块渲染结果、提示词重载、索引同步失败、辅助模型最终失败与冷却；
- 管理 API 每次调用（方法、路由、状态码、耗时）、记忆工具每次调用（工具、`op`、页面标识或短码、结果、耗时）；`recall` 另记方式（`brief`、`page`、`persona`、`rules` 或 `search`）与给出的筛选条件，搜索还记查询原话（空白折成一个空格）与返回的结果数，供以后统计查不到的比例、重放真实查询；
- 维护操作的开始、结束与失败（耗时）；索引重建的结果（文件、单元、向量、跳过的非 UTF-8 文件）与向量待补；
- 自动、手动与保险备份的写入与失败，明文密钥提示；云端备份的上传、下载与失败，以及 `remote.json` 写入失败；
- 恢复的开始、完成、回退与回退失败，以及临时目录未能删除；
- 客户端能力记录的更新（首次见到某个客户端，或其协议版本变化、能力与扩展的并集变大；日志一行仍带客户端自报的版本），以及 `clients.json` 无法写入或无法读取。

不记录记忆正文、原话、提示词内容、密钥与 WebDAV 密码；查询文本只有 `recall` 搜索的查询原话例外，随日志保存 14 天。

## 4. 重建索引

派生索引（`memory/.cache/`）损坏或换了嵌入模型时执行 `tacit reindex`（需服务运行）。嵌入模型名或向量维度变化时服务也会自动丢弃旧向量并补齐，`reindex` 用于需要立即全量重建的情况。

换了决策模型后，也建议执行一次 `tacit reindex`。整理的判断缓存不随模型变化而失效（缓存键不含模型名，见 `docs/design.md`「整理」），内容没变的提问会沿用旧模型的答案；`reindex` 会清空判断缓存，之后的整理全部改问新模型。对话中实时写入的判断不读缓存，不受影响。

## 5. 定时提醒整理

用 `tacit consolidation`（需服务运行）或管理 API 的 `GET /api/consolidation`（本机免登录）询问是否该整理，接口的规则见 `docs/design.md`「整理」，触发条件见其「注入」。定时任务须在本机运行：管理 API 对其他设备要求登录，云端运行的定时任务连不到本机服务。

配置定时任务：在仪表盘「索引与整理」卡片上复制配置指令，粘贴给本机的 Agent，由它用自己环境的定时功能配好每天一次的检查（运行时间由它询问用户）。

## 6. 排障

所有命令的退出码一致：

| 码 | 含义 |
| :--- | :--- |
| 0 | 成功；`status` 中表示服务在运行 |
| 1 | 运行失败：服务拒绝、IO 或权限错误、端口被占、`restore` 未确认、`probe` 有模型失败 |
| 2 | 用法错误：未知命令或选项、缺参数、参数值非法 |
| 3 | 配置无效：`config.yaml` 缺失或有错、记忆仓库未初始化、提示词文档违反限制 |
| 4 | 服务未运行（需要服务的命令：`consolidation`、`reindex`、`backup`、`restore`） |

设了环境变量 `NO_COLOR`，或输出不是终端时，命令输出不带颜色与面板的纯文本。

- **任何问题先看 `tacit status`**。
- **找不到 `config.yaml`**：先执行 `tacit init`。
- **升级后报未知键**：报错点名的键已删除（清单见 `docs/reference/configuration.md`），从 `config.yaml` 中删去后重启。升级前绑定的 skill 目录里可能还留着旧的 `tacit-checkup` 链接（已改名为 `tacit-consolidate`），它指向已不再生成的目录；`disconnect` 不删除它，需手动删除。
- **配置错误**：报告会列出全部出错的键路径，逐项修正后重试。
- **忘记访问密码**：访问密码只能在本机重设或清除，忘记时在本机处理。先在 `config.yaml` 中去掉 `server.host` 并重启服务（否则清除会被拒绝）；然后在本机调用 `DELETE /api/access-password` 清除，或停止服务后删除 `TACIT_HOME/access.json`。清除后再在本机设置新的访问密码，并把 `server.host` 写回、重启。
- **`serve` 拒绝启动，提示需要访问密码**：`server.host` 为 `127.0.0.1` 以外的地址而没有访问密码。按上一条先去掉 `server.host` 启动，在本机设置访问密码，再写回 `server.host`。
- **端口被占用**：`serve` 无法启动。修改 `server.port`（默认 18888）后，需自行更新客户端中的 MCP 配置，指向新的端点（`tacit connect` 打印的地址）；tacit 不再替你改写它。
- **提示词超限**：启动被拒绝，报告会指出文件与超出的字符数。精简超限的文件后重启即可（各文件的上限见 `docs/reference/configuration.md`「提示词文档」，固定部分的上限见 `docs/design.md`「注入」）。
- **`init` 被拒绝**：记忆目录非空且不是 Git 仓库时拒绝初始化。改用空目录，或用 `--memory` 指向已有的记忆仓库。
- **辅助模型连不上**：先运行 `tacit probe`，据此检查 `url`（须为完整请求地址，路径与协议要求的一致）、`model`、`provider` 与密钥。服务不会因此退出：写入照常完成，判断视为"拿不准"，检索按降级规则处理（嵌入模型不可用时只走全文，详见 `docs/design.md`「辅助模型」）。环境变量在进程启动时读取，修改后需重新打开终端并重启 `serve`；`tacit probe` 读取的是 `config.yaml`，不需要服务运行。
- **客户端中记忆工具调用失败**：确认 `tacit serve` 正在运行，并且是绑定之后开的新会话。
- **自动备份失败**：只会提示，不影响已完成的写入，下一次写入时再次尝试。

## 7. 发布

### 发行包

每个版本在 GitHub Releases 上有三个发行包，各附一个 `.sha256`：`tacit-<版本>-win-x64.zip`、`tacit-<版本>-osx-arm64.tar.gz` 与 `tacit-<版本>-linux-x64.tar.gz`。发行包是自包含的：解压即可运行，不需要预装 .NET。可执行文件（Windows 上为 `tacit.exe`，其他平台为 `tacit`）是单个文件，内含运行时与全部托管依赖（框架中用不到的部分在发布时裁掉，整体压缩）；旁边是按路径加载的原生库 SQLite（Windows 上为 `e_sqlite3.dll`，macOS 上为 `libe_sqlite3.dylib`，Linux 上为 `libe_sqlite3.so`），以及 `web/`、tacit 自己的 pdb、`LICENSE`、`NOTICE` 与 `THIRD-PARTY-NOTICES.txt`。tar.gz 保留可执行位。macOS 包的可执行文件带 ad-hoc 签名，未经公证。

校验下载（与 `.sha256` 文件中的值比对）：

```bash
sha256sum -c tacit-<版本>-<平台>.<扩展名>.sha256        # Linux、Git Bash
shasum -a 256 -c tacit-<版本>-osx-arm64.tar.gz.sha256   # macOS
```

安装与升级都是"放进一个干净的目录"，数据目录 `~/.tacit` 不受影响：

1. 停止服务（由托管工具运行时，从托管工具停止）；
2. 清空安装目录。不要把新版本解压到旧文件上：同名文件会被覆盖，但旧版本独有的文件会留下；
3. 把发行包解压到安装目录；
4. 启动服务，用 `tacit status` 确认版本。

### 版本号

版本号由带 `v` 前缀的 git tag 决定（`v0.1.0` 对应 `0.1.0`）。从源码构建、且当前提交不在 tag 上时，版本为预发布版本，如 `0.1.1-alpha.0.3`：最近的 tag 之后的第 3 个提交。构建需要完整的 git 历史，浅克隆得到的版本是 `0.0.0-alpha.0.N`。

### 打包与发布流程

推送到 `main` 时，GitHub Actions 在 Linux 运行器上运行格式检查、全量测试与前端检查，只改文档的推送不运行，也可以手动触发；其他分支的推送不运行。推送 `v*` tag 后，先在 Linux 与 Windows 上各运行一遍全量测试（Windows 上测试频繁启动 git 进程，所以先关闭运行器上的实时扫描，运行器用完即弃），再在三个平台各自的运行器上用 `build/package.sh` 打包、用 `build/smoke.sh` 冒烟。mac 包必须在 macOS 上构建，只有在那里构建时可执行文件才会被签名。三个包都通过冒烟后，由一个作业创建 Release，附全部发行包与各自的 SHA-256，不会出现只含部分平台的 Release；手动触发发布工作流时只打包与冒烟，产物作为工作流构件保留，不创建 Release。tag 带预发布后缀（如 `v0.2.0-rc.1`）时 Release 标为预发布。发布说明可选：`.github/release-notes/<tag>.md` 存在时作为 Release 正文。

在本机打包（需要 .NET 11 SDK、Bun、`jq` 与 bash；Windows 上用 Git Bash）。包要在它所针对的平台上打：macOS 只在本机构建时给可执行文件签名，Apple 芯片不运行未签名的程序。

```bash
build/package.sh <win-x64|osx-arm64|linux-x64> <输出目录>   # 输出目录会先被清空
build/smoke.sh <输出目录>/tacit
```

冒烟检查什么见 `docs/dev/testing.md`「发行包的冒烟」。

发行包把 .NET 运行时固定在构建时的补丁版本，运行时的安全补丁随 tacit 的新版本生效。

### 从源码构建

`dotnet publish src/TacitMemory.Cli` 默认得到依赖框架的产物，运行时需要预装 ASP.NET Core 11。它需要 Bun，它在 `web/` 下按锁文件安装依赖并构建 Web 界面，产物复制到输出目录的 `web/` 子目录。服务只读取这个目录，不写入。找不到 Bun 时发布失败。`dotnet build` 与 `dotnet test` 不构建 Web 界面，也不需要 Bun。

- 不需要 Web 界面时，发布加上 `/p:TacitSkipWebBuild=true`：不需要 Bun，输出目录没有 `web/`，访问 `/ui` 得到「Web 界面未构建」，服务的其余部分照常。
- 发布先删除输出目录下的 `web/`，旧版本的前端资源不会留下；输出目录中的其他文件只覆盖同名文件，不删除。需要完全干净的输出时，发布前删除输出目录。
