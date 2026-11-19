# TacitMemory

TacitMemory 是一个跨客户端的个人记忆系统。它将用户的偏好、近期活动与历史记录以人类可读的 Markdown 文件保存在独立 Git 仓库中，并通过本机 HTTP MCP 服务提供给支持 MCP 与 skill 的 Agent 客户端使用。

其中，画像、领域索引与主线随会话常驻注入上下文；细节页与历史活动由 Agent 按需检索。

## 前置要求

- **Git**：命令行可直接调用，用于管理记忆仓库。
- **操作系统**：支持 Windows x64、macOS（Apple Silicon）与 Linux x64（glibc），均提供独立发行包。
- **辅助模型**（可选，均通过 HTTP 接口接入；未配置时服务正常运行，相关功能按降级规则跳过或回退）：
  - 嵌入模型：OpenAI Embeddings 协议；
  - Reranker：Cohere / Jina Rerank 协议；
  - 决策模型：System One 协议（如 TypeSafe 原生决策接口）。

  内置提供方适配目前支持 Cloudflare Workers AI（reranker、决策模型），API 密钥为可选项。内置判断阈值基于 TypeSafe `jev-latest` 实测标定；更换其他决策模型前，请参阅 [`docs/reference/configuration.md`](docs/reference/configuration.md)「`decision` 与 `retrieval`」。

## 安装

从 GitHub Releases 下载对应平台的发行包并解压至空目录。发行包为自包含构建，无需预先安装 .NET 运行时。

| 平台 | 发行包 |
| :--- | :--- |
| Windows x64 | `tacit-<版本>-win-x64.zip` |
| macOS（Apple Silicon） | `tacit-<版本>-osx-arm64.tar.gz` |
| Linux x64 | `tacit-<版本>-linux-x64.tar.gz` |

可执行文件（`tacit.exe` 或 `tacit`）、SQLite 原生库（`e_sqlite3.dll`、`libe_sqlite3.dylib` 或 `libe_sqlite3.so`）与 `web/` 目录须位于同一目录。将该目录加入 `PATH`，或在 `PATH` 目录中建立指向可执行文件的符号链接后，即可在任意目录运行 `tacit`：

```bash
mkdir -p <安装目录> && tar -xzf tacit-<版本>-osx-arm64.tar.gz -C <安装目录>
ln -s <安装目录>/tacit ~/.local/bin/tacit
```

macOS 发行包仅采用 ad-hoc 签名，未经 Apple 公证。通过浏览器下载时会附加隔离属性（quarantine），Gatekeeper 将阻止运行其中的 `tacit` 与 `libe_sqlite3.dylib`；使用 `gh release download` 或 `curl -LO` 下载则不会附加该属性。若已触发拦截，可在解压后执行：

```bash
xattr -dr com.apple.quarantine <安装目录>
```

校验下载文件与升级步骤请参阅 [`docs/reference/operations.md`](docs/reference/operations.md)「发布」。

### 从源码构建

需要 .NET 11 SDK、Bun（用于构建 Web 界面）以及完整的 Git 历史（版本号从 Git tag 推导）：

```bash
dotnet publish src/TacitMemory.Cli -c Release -o <安装目录>
```

上述命令生成依赖框架的产物，运行环境须预装 ASP.NET Core 11 运行时。构建自包含单文件发行包及跳过 Web 界面构建的方法，请参阅 [`docs/reference/operations.md`](docs/reference/operations.md)「发布」。CLI 未发布为 dotnet tool。

## 快速开始

1. **初始化配置与记忆仓库**

   ```bash
   tacit init
   ```

   该命令生成 `~/.tacit/config.yaml` 与记忆仓库。交互式终端下会提示选择记忆语言（`zh` 或 `en`）；在脚本或非交互环境中执行时，请显式传入 `--language zh` 或 `--language en`。

2. **配置辅助模型（可选）**

   编辑 `~/.tacit/config.yaml` 的 `models` 节，填写所需模型的 `url`（完整请求地址）与 `model`；未填写的模型视为未配置，可后续随时补充。配置格式请参阅 [`docs/reference/configuration.md`](docs/reference/configuration.md)，配置完成后可执行 `tacit probe` 验证连通性。

3. **绑定客户端**

   ```bash
   tacit connect --client <名称> --context <上下文文件> --skills <skill 目录>
   ```

   例如，当客户端的全局上下文文件为 `~/.my-agent/AGENTS.md`、skill 目录为 `~/.my-agent/skills` 时：

   ```bash
   tacit connect --client my-agent --context ~/.my-agent/AGENTS.md --skills ~/.my-agent/skills
   ```

   - `--client`：客户端标识名称（小写字母、数字与连字符，最多 32 个字符）；
   - `--context`：客户端在每个会话开始时读取的上下文文件路径；
   - `--skills`：客户端扫描 skill 的目录路径。

   `tacit connect` 会在上下文文件中写入常驻区块、在 skill 目录建立三个 skill 链接，并输出 MCP 端点、传输方式与建议服务名。`tacit` 不修改任何客户端的 MCP 配置文件，请根据输出信息在客户端配置中手动添加 HTTP MCP 端点（形如 `http://127.0.0.1:<端口>/mcp`）。

4. **启动常驻服务**

   ```bash
   tacit serve
   ```

   服务默认在前台运行（建议在独立终端或通过系统服务管理器托管运行）。输出监听地址即表示就绪，按 `Ctrl+C` 停止。服务运行期间，可在浏览器访问 `http://127.0.0.1:<端口>/`（默认端口 `18888`）使用 Web 界面，本机访问免登录。

5. **验证运行状态**

   ```bash
   tacit status
   tacit show resident
   ```

   完成后新建客户端会话即可生效；所有绑定与提示词文档的修改均在新会话中生效。

## 跨设备访问 Web 界面

1. 在本机打开 Web 界面，进入「设置 → 访问」设置访问密码。
2. 在 `config.yaml` 的 `server` 节配置额外监听地址 `host`（例如内网或虚拟专网 IP，或 `0.0.0.0`），重启 `tacit serve`。可通过 `tacit status` 查看全部监听地址。
3. 在其他设备访问 `http://<地址>:<端口>/` 并使用访问密码登录。

`/mcp` 端点始终仅接受本机回环连接。服务本身不提供 HTTPS，访问密码及配置页面密钥将以明文传输，请仅在可信网络（如加密虚拟专网）中开放额外监听。鉴权规则请参阅 [`docs/design.md`](docs/design.md)「访问与鉴权」。

## 日常使用

- **自动注入与按需检索**：会话开始时，客户端会加载常驻区块（画像、领域索引与主线）以及服务端说明（含核心规则与最近 7 天活动）。对话过程中，Agent 根据记忆规则自动调用 `recall` 检索及 `write_*` 工具写入（需保持 `tacit serve` 运行）。
- **读取主线简报**：处理特定主线时，Agent 会仅传入 `workstream` 参数调用 `recall` 获取主线简报（包含主线约定、未决事项、按类型分组的页目录、近期活动与最近一次周汇总），也可由用户主动指令触发（例如「先读取 `my-app` 的主线简报」）。
- **手动触发记录（`tacit-remember`）**：在压缩上下文或结束会话前，可通过客户端触发 `tacit-remember` skill 立即归纳并写入本次会话内容，支持附加重点说明。
- **定期整理（`tacit-consolidate`）**：当服务端说明出现整理提示时，可触发 `tacit-consolidate` skill 执行整理（支持附加范围限定说明）。整理分为两步：Agent 先自动修正格式、清理冗余候选、生成周/月汇总并更新主线近况；随后将需人工裁决的事项（主线提案、画像提案、领域规则、页面重组、冲突条目、领域卫生与篇幅提示）整理为编号清单，用户通过一次回复批量确认或调整，最终以 `consolidate:` 提交至记忆仓库。
- **版本升级后同步 skill**：新增或更新内置 skill 后，请使用相同参数重新执行 `tacit connect` 以补全或更新 skill 链接。
- **直接编辑记忆文件**：记忆仓库中的 Markdown 文件支持直接手动编辑，服务会监听文件变更并自动同步派生索引。服务执行写入时，若目标文件存在未提交的手动修改，会一并纳入本次提交；其余文件的手动修改请自行执行 `git commit`。
- **自定义提示词文档**：如需调整常驻区块引导语、值得记标准或核心规则，可编辑 `~/.tacit/prompts/` 下的对应文件，详情请参阅 [`docs/reference/configuration.md`](docs/reference/configuration.md)「提示词文档」。

## 文档导航

建议从 [`docs/overview.md`](docs/overview.md) 开始阅读，其中包含系统概览与完整文档索引。

## 许可

TacitMemory 采用 [Apache License 2.0](LICENSE) 开源许可发布。发行包中附带的第三方组件及其许可声明见包内 `THIRD-PARTY-NOTICES.txt`。
