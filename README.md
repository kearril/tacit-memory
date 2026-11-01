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
  - 决策模型：TypeSafe 原生决策接口（`/systemone`），密钥可写在配置中，或放在环境变量里由 `apiKeyEnv` 指定。

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

2. **填写模型**：编辑 `config.yaml` 的 `models` 一节，填入三个模型的 `endpoint` 与 `model`。决策模型的密钥二选一：`apiKeyEnv: <环境变量名>`，或 `apiKey: <密钥>`。

3. **绑定 omp**

   ```powershell
   tacit connect --client omp
   ```

   写入常驻区块、注册 MCP 服务并建立 skill 链接。绑定前会校验配置，`models` 缺失时会报错。

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
- 想让 Agent 立即把本次会话写进记忆（例如压缩上下文或退出前），在 omp 中输入 `/tacit:remember`；要指定重点时写成 `/tacit:remember focus=<重点>`（omp 只认 `名=值` 写法，直接跟文字会被丢弃）。
- 整理记忆时在 omp 中输入 `/tacit:checkup`（或让 Agent 执行记忆指令 `checkup`）开始体检。体检会列出疑似重复的页、互相冲突的画像条目、可确认与待清理的画像候选、格式问题与过期的近期主线；Agent 直接修正机械问题，需要你拍板的逐项询问，最后在记忆仓库中以 `checkup:` 提交。距上次体检达到 `checkup.remindAfterDays` 天（默认 7 天），或有可确认的候选时，新会话的服务端说明会提示可以体检。
- 体检的时间与并发可在 `config.yaml` 的 `checkup` 一节调整：`budgetSeconds`（默认 20）是一次体检最多等模型的秒数，到点照常出报告，没算完的项注明"再执行一次 `checkup` 接着算"；`maxConcurrency`（默认 5）是同时进行的模型判断数。
- 记忆工具需要 `tacit serve` 运行。服务停止时工具不可用，但已写入的常驻区块仍然有效。
- 记忆仓库中的 Markdown 可直接编辑，服务会监听变化并同步索引。服务写入某个文件时，会连同该文件中未提交的手动修改一并提交；其余手动修改请在仓库中自行 `git commit`。
- 查看 Agent 收到的内容：`tacit show instructions`、`resident`、`skill`、`tools`、`remember`。每段都标明来源与长度。

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

## 解绑

```powershell
tacit disconnect --client omp
```

移除常驻区块、`mcp.json` 中的 `tacit` 项与 skill 链接。只删除链接本身，记忆 skill 文件与记忆仓库都保留。
只删除常驻区块不能停止维护：下次渲染时会重新追加。要停止维护，请执行解绑。

## 文件在哪

`TACIT_HOME` 默认为用户目录下的 `.tacit`，可用同名环境变量改变位置。

```text
~/.tacit/
├── config.yaml                    # 配置
├── prompts/                       # 可编辑的提示词文档
├── skills/tacit-memory/SKILL.md   # 生成的记忆 skill，请勿手改
├── bindings.json                  # 已绑定客户端的记录
├── backups/                       # 每日自动备份（zip）
└── memory/                        # 记忆仓库（Git），memory.path 可改
    ├── persona.md、candidates.md、recent.md、recent/、pages/
    └── .cache/                    # 派生索引，可删除，由文件重建
```

omp 绑定后还会改动以下位置（只改 TacitMemory 自己的部分）：

- `~/.omp/agent/AGENTS.md`：起止标记之间的常驻区块；
- `~/.omp/agent/mcp.json`：`tacit` 一项；
- `~/.omp/agent/skills/tacit-memory`：指向记忆 skill 的链接。

**备份**：每天第一次写入后自动生成一份 zip，包含记忆仓库、配置、提示词、`bindings.json` 与清单，保留最近 7 份（`backup.keep` 可改，`0` 关闭）。配置中若含明文密钥，备份时会提示。目前没有恢复命令，需手动解压备份。

## 排障

错误信息输出到控制台（stderr），命令以退出码 1 结束。`serve` 的运行信息输出在其所在终端；目前没有独立的日志文件。

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
