# TacitMemory

TacitMemory 是一个跨客户端的个人记忆系统。它把你的偏好、近况和过往问题保存为人类可读的 Markdown 文件（位于 Git 仓库中），并通过本机的 MCP 服务交给 Agent 使用。
画像随会话常驻注入上下文；近期活动与细节页由 Agent 按需检索。
目前已实现 omp 客户端的绑定。

## 前提

- **.NET 10 SDK**：用于构建。
- **Git**：命令行可直接调用；记忆仓库由 Git 管理。
- **操作系统**：已在 Windows x64 上验证。向量扩展 `vec0.dll` 仅附带 Windows x64 版本。
- **三个辅助模型**（必填，需自行准备，均为 HTTP 端点）：
  - 嵌入模型：OpenAI 兼容接口（`/embeddings`）；
  - reranker：Cohere/Jina 兼容接口（`/rerank`）；
  - 决策模型：TypeSafe 原生决策接口（`/systemone`），密钥见「密钥」一节。

`endpoint` 填写基础地址，程序会在其后追加上述路径。

## 构建

```powershell
dotnet publish src/TacitMemory.Cli -c Release -o <安装目录>
```

输出目录中的可执行文件为 `tacit.exe`，并附带 `vec0.dll`，两者须放在同一目录。把该目录加入 `PATH` 后即可在任意位置运行 `tacit`。CLI 未打包为 dotnet tool。

## 快速开始

1. **初始化**

   ```powershell
   tacit init
   ```

   生成配置 `~/.tacit/config.yaml` 与记忆仓库。语言默认取系统界面语言，可用 `--language zh|en` 指定；`--memory <路径>` 可指定记忆仓库位置。

2. **填写模型**：编辑 `config.yaml` 的 `models` 一节，填入三个模型的 `endpoint` 与 `model`；决策模型的密钥见「密钥」一节。

3. **绑定 omp**

   ```powershell
   tacit connect --client omp
   ```

   写入常驻区块、注册 MCP 服务并建立三个 skill 链接（`tacit-memory`、`tacit-remember`、`tacit-checkup`）。绑定前会校验配置，`models` 缺失时会报错。

4. **启动服务**（前台运行，建议单独开一个终端）

   ```powershell
   tacit serve
   ```

   控制台显示监听地址即已就绪；`Ctrl+C` 停止。

5. **验证**

   ```powershell
   tacit show resident
   tacit show skill
   ```

   随后开一个新的 omp 会话。绑定与提示词的修改在新会话中生效。

## 日常使用

- 会话开始时，omp 会读到常驻区块（画像）与服务端说明（含近期摘要）。Agent 在对话中按记忆规则自行检索与写入，无需手动操作。
- 记忆指令 `tacit-remember` 与 `tacit-checkup` 是 omp 的 skill，用 `/skill:` 触发需要 omp 开启 `skills.enableSkillCommands`（默认已开启）。升级后请重新执行 `tacit connect --client omp`，补上新增的 skill 链接；已有的链接保持不变。
- 想让 Agent 立即把本次会话写进记忆（例如压缩上下文或退出前），在 omp 中输入 `/skill:tacit-remember 附加要求`；附加要求可省略，写了就作为本次的重点或额外要求。`/skill:tacit-remember` 也可以写在句中触发。
- 整理记忆时在 omp 中输入 `/skill:tacit-checkup`（附加要求可省略）开始体检。体检会列出疑似重复的页、互相冲突的画像条目、可确认与待清理的画像候选、格式问题与过期的近期主线；Agent 直接修正机械问题，需要你拍板的逐项询问，最后在记忆仓库中以 `checkup:` 提交。距上次体检达到 `checkup.remindAfterDays` 天（默认 7 天），或有可确认的候选时，新会话的服务端说明会提示可以体检。
- 体检的时间与并发可在 `config.yaml` 的 `checkup` 一节调整：`budgetSeconds`（默认 20）是一次体检最多等模型的秒数，到点照常出报告，没算完的项注明"再运行一次 `tacit-checkup` 接着算"；`maxConcurrency`（默认 5）是同时进行的模型判断数。
- 记忆工具需要 `tacit serve` 运行。服务停止时工具不可用，但已写入的常驻区块仍然有效。
- 记忆仓库中的 Markdown 可直接编辑，服务会监听变化并同步索引。服务写入某个文件时，会连同该文件中未提交的手动修改一并提交；其余手动修改请在仓库中自行 `git commit`。
- 查看 Agent 收到的内容：`tacit show instructions`、`resident`、`skill`、`tools`、`remember`、`checkup`。每段都标明来源与长度。
- 索引损坏或换了嵌入模型时执行 `tacit reindex`（需服务运行）：全量重建全文、向量与判断缓存，等待完成后输出文件数、单元数、已有向量的单元数、耗时与跳过的非 UTF-8 文件；嵌入模型不可用时只重建全文，并注明向量将在模型恢复后补齐。执行期间记忆工具的调用排队等待。

### 修改提示词

提示词文档位于 `TACIT_HOME/prompts/`：

| 文件 | 作用 | 行为 |
| :--- | :--- | :--- |
| `rules.md` | 值得记 / 不记的标准 | 缺失时按记忆语言重新生成 |
| `instructions.md` | 核心规则（何时检索、何时写） | 存在时整份替换内置默认 |
| `resident.md` | 常驻区块的外壳与四节引导语 | 存在时整份替换内置默认 |
| `skill.md` | 个人写法约定 | 接在记忆 skill 末尾 |

需要以内置默认为起点时，在 PowerShell 7 中执行（输出为 UTF-8 无 BOM）：

```powershell
tacit show instructions --default > ~/.tacit/prompts/instructions.md
```

长度与格式的限制见 [`docs/design.md`](docs/design.md) 第 12 节；各段文字的职责见 [`docs/prompts.md`](docs/prompts.md)。

## 运维

服务运行时，下列命令经管理 API（`127.0.0.1` 上的 `/api`）完成工作，无需停止服务；服务未运行时，`reindex`、`backup`、`restore` 会提示先执行 `tacit serve` 并以退出码 4 结束。

- **`tacit status`**：列出服务（运行与否、地址、版本、已运行时长）、辅助模型（可用或冷却中）、绑定、上次体检、待处理问题、备份、日志目录与各密钥的来源（环境变量或配置文件，不显示值）。服务未运行时仍输出不依赖服务的部分；服务栏始终给出 MCP 端点。`config.yaml` 无法加载时仍列出全部错误，并按 `server.port` 能读出的端口（读不出时为 18888）探测服务：服务在运行则同时显示它的报告，退出码仍为 3。
- **`tacit reindex`**：全量重建派生索引，见上文。
- **`tacit backup`**：立即在 `TACIT_HOME/backups/` 生成手动备份 `manual-YYYY-MM-DD-HHmmss.zip`，不被自动清理。`--to <目录>` 写到指定目录（不存在时创建）。`--remote` 现场生成一份备份，上传到云端，替换远端唯一的 `tacit-backup.zip`；`--to` 与 `--remote` 不能同时使用。备份内容为记忆仓库（含 `.git`，不含 `.cache`）、`config.yaml`、`prompts/`、`bindings.json` 与清单。
- **`tacit restore --from <zip>`** 与 **`tacit restore --remote`**（两者恰好给一个）：先显示备份的生成时间、tacit 版本、种类（自动、手动、保险或云端）、记忆仓库 HEAD 的提交号与说明、文件数，以及将被替换的内容；输入 `y` 才开始，`--yes`（`-y`）跳过确认。标准输入不是终端且未带 `--yes` 时，显示预览后以退出码 2 结束，不恢复。恢复前会先生成保险备份 `pre-restore-*.zip`，路径会显示在结果中。默认只恢复记忆仓库；加 `--include-config` 时一并恢复 `config.yaml` 与 `prompts/`，`bindings.json` 永不恢复（绑定请重新执行 `tacit connect`）。恢复期间记忆工具的调用排队等待，结束后索引全量重建。恢复回来的 `config.yaml` 需要重启 `tacit serve` 才生效。

### 云端备份

云端备份经通用 WebDAV 协议上传，任何支持 `PUT`、`MOVE`、`MKCOL`、`GET` 与 `DELETE` 的服务都可以用，远端只保留一份（`GET` 用于云端恢复，`DELETE` 只用于清理失败留下的 `.part` 文件）。在 `config.yaml` 中填写：

```yaml
backup:
  keep: 7
  webdav:
    url: https://example.com/dav/tacit/   # 目录；末尾的 / 可省略
    username: 你的用户名                   # 可省略，省略时不发送认证
    password: 你的密码                     # 可省略，改设环境变量 TACIT_WEBDAV_PASSWORD
```

写了 `username` 时必须有密码（配置中或环境变量中）。远端不支持 `MOVE` 时报错，不会退回为直接覆盖。上传与恢复都只在执行命令时发生。上传不在本地留副本；`restore --remote` 先把云端备份下载到 `backups/.download/tacit-backup.zip`，恢复成功后删除，失败则保留以便排查。

### 密钥

每个密钥有固定的环境变量名，环境变量非空时优先，否则使用配置中的明文值：

| 配置键 | 环境变量 | 何时必需 |
| :--- | :--- | :--- |
| `models.decision.apiKey` | `TACIT_DECISION_API_KEY` | 总是 |
| `backup.webdav.password` | `TACIT_WEBDAV_PASSWORD` | 配置了 `backup.webdav.username` 时 |

两处都没有时加载配置会报错，并同时指出键路径与环境变量名。配置中写了明文密钥时，备份会提示妥善保管备份文件。

### 日志

`serve` 在终端输出的内容同时写入 `TACIT_HOME/logs/tacit-YYYYMMDD.log`，每天一份，保留最近 14 份。记录的事件见 [`docs/design.md`](docs/design.md) 第 12 节「运行」。不记录记忆正文、原话、查询文本、提示词内容与任何密钥。

## 解绑

```powershell
tacit disconnect --client omp
```

移除常驻区块、`mcp.json` 中的 `tacit` 项与三个 skill 链接（只删指向 tacit 的链接）。只删除链接本身，skill 文件与记忆仓库都保留；同名的其他条目不动。
只删除常驻区块不能停止维护：下次渲染时会重新追加。要停止维护，请执行解绑。

## 文件在哪

`TACIT_HOME` 默认为用户目录下的 `.tacit`，可用同名环境变量改变位置。

```text
~/.tacit/
├── config.yaml                    # 配置
├── prompts/                       # 可编辑的提示词文档
├── skills/                        # 生成的 skill：tacit-memory、tacit-remember、tacit-checkup，请勿手改
├── bindings.json                  # 已绑定客户端的记录
├── backups/                       # 备份（zip）：auto-、manual-、pre-restore- 三种；remote.json 记录云端备份的最近一次结果
├── logs/                          # serve 的日志（见「日志」）
└── memory/                        # 记忆仓库（Git），memory.path 可改
    ├── persona.md、candidates.md、recent.md、recent/、pages/
    └── .cache/                    # 派生索引，可删除，由文件重建
```

omp 绑定后还会改动以下位置（只改 TacitMemory 自己的部分）：

- `~/.omp/agent/AGENTS.md`：起止标记之间的常驻区块；
- `~/.omp/agent/mcp.json`：`tacit` 一项；
- `~/.omp/agent/skills/` 下的 `tacit-memory`、`tacit-remember`、`tacit-checkup`：指向 `TACIT_HOME/skills/` 下同名 skill 的三个链接。

**备份与恢复**见上文「运维」一节。

## 排障

结果输出到 stdout，错误与确认提示输出到 stderr。命令的退出码如下，`tacit status` 同样遵循这一表：

| 码 | 含义 |
| :--- | :--- |
| 0 | 成功；`status` 中表示服务在运行 |
| 1 | 运行失败：服务拒绝、IO 或权限错误、`serve` 端口被占、`restore` 未确认 |
| 2 | 用法错误：未知命令或选项、缺参数、参数值非法、互斥选项冲突，或非终端下 `restore` 未带 `--yes` |
| 3 | 配置无效：`config.yaml` 缺失或有错、记忆仓库未初始化、提示词文档违反限制 |
| 4 | 服务未运行，含端口被其他程序占用 |

`serve` 的日志见「日志」一节。

- **任何问题先看 `tacit status`**（各栏见「运维」）：服务在运行时退出码 0，服务未运行或端口被其他程序占用时 4，`config.yaml` 无法加载时 3（有错误时列出全部出错的键路径；文件不存在或记忆仓库未初始化时说明缺少什么；此时仍探测服务，服务在运行时一并显示其报告）。
- **找不到 `config.yaml`**：先执行 `tacit init`。
- **配置错误**：报告会列出全部出错的键路径，逐项修正后重试。
- **端口被占用**：`serve` 无法启动。修改 `server.port`（默认 18888），然后重新执行 `tacit connect --client omp`，以更新 omp 中的 MCP 地址。
- **提示词超限**：启动被拒绝，报告会指出文件与超出的字符数。固定部分、`resident.md` 与 `skill.md` 各有上限，精简该文件后重启即可（详见 `docs/design.md` 第 12 节）。
- **`init` 被拒绝**：记忆目录非空且不是 Git 仓库时拒绝初始化。改用空目录，或用 `--memory` 指向已有的记忆仓库。
- **辅助模型连不上**：服务不会因此退出。写入照常完成，判断视为"拿不准"，检索按降级规则处理（嵌入模型不可用时只走全文，详见 `docs/design.md` 第 4 节）。检查 `endpoint`、`model` 与密钥环境变量；环境变量在进程启动时读取，修改后需重新打开终端并重启 `serve`。
- **向量扩展无法加载**：启动时会校验 `vec0.dll` 的存在与哈希，不符即拒绝启动。确认它与 `tacit.exe` 位于同一目录，且未被替换。
- **omp 中记忆工具调用失败**：确认 `tacit serve` 正在运行，并且是绑定之后开的新会话。
- **自动备份失败**：只会提示，不影响已完成的写入，下一次写入时再次尝试。

## 更多

- [`docs/design.md`](docs/design.md)：记忆如何分层、写入、检索，以及配置与上限
- [`docs/prompts.md`](docs/prompts.md)：发给模型的每段文字负责什么
- [`docs/architecture.md`](docs/architecture.md)：代码结构与各模块的实现状况
- [`GLOSSARY.md`](GLOSSARY.md)：术语
- [`docs/testing.md`](docs/testing.md)：测试方式
