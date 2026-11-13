# TacitMemory

TacitMemory 是一个跨客户端的个人记忆系统。它把你的偏好、近况和过往问题保存为人类可读的 Markdown 文件（位于 Git 仓库中），并通过本机的 MCP 服务交给 Agent 使用。
画像、领域索引与主线随会话常驻注入上下文；细节页与活动记录由 Agent 按需检索。

## 前提

- **.NET 10 SDK**：用于构建。
- **Bun**：仅 `dotnet publish` 构建 Web 界面时需要，详见 [`docs/operations.md`](docs/operations.md) 第 6 节。
- **Git**：命令行可直接调用；记忆仓库由 Git 管理。
- **操作系统**：已在 Windows x64 上验证。向量扩展 `vec0.dll` 仅附带 Windows x64 版本。
- **三个辅助模型**（可选，均为 HTTP 端点，需自行准备；不配置的模型即未配置，服务照常运行，依赖它的功能按降级规则处理）：
  - 嵌入模型：OpenAI Embeddings 协议；
  - reranker：Cohere/Jina Rerank 协议；
  - 决策模型：System One 协议（TypeSafe 的原生决策接口即其一个服务）。

  目前支持 Cloudflare Workers AI 的提供方适配（reranker、决策模型）。密钥可不配。内置的判断阈值是用某一个决策模型实测标定的，换用其他决策模型前请先读 [`docs/reference/configuration.md`](docs/reference/configuration.md)「`decision` 与 `retrieval`」。

## 构建

```powershell
dotnet publish src/TacitMemory.Cli -c Release -o <安装目录>
```

输出目录中的可执行文件为 `tacit.exe`，并附带 `vec0.dll` 与 Web 界面的 `web/` 子目录，它们须放在同一目录。把该目录加入 `PATH` 后即可在任意位置运行 `tacit`。CLI 未打包为 dotnet tool。

发布的前提与不构建 Web 界面的方法见 [`docs/operations.md`](docs/operations.md) 第 6 节。

## 快速开始

1. **初始化**

   ```powershell
   tacit init
   ```

   生成配置 `~/.tacit/config.yaml` 与记忆仓库。

2. **填写模型**（可选）：编辑 `config.yaml` 的 `models` 一节，填入要用的模型的 `url`（完整请求地址）与 `model`；不写的模型即未配置，可以之后再补。写法见 [`docs/reference/configuration.md`](docs/reference/configuration.md)，填好后可用 `tacit probe` 检查。

3. **绑定客户端**

   ```powershell
   tacit connect --client <名称> --context <上下文文件> --skills <skill 目录>
   ```

   例如客户端的上下文文件是 `~/.my-agent/AGENTS.md`、skill 目录是 `~/.my-agent/skills` 时：

   ```powershell
   tacit connect --client my-agent --context ~/.my-agent/AGENTS.md --skills ~/.my-agent/skills
   ```

   `--client` 是给这个客户端起的名称（小写字母、数字与连字符，最多 32 个字符），`--context` 是客户端在每个会话开始时读入的上下文文件，`--skills` 是它扫描 skill 的目录；这两个路径在哪里，见你的客户端的文档。绑定会写入常驻区块并建立三个 skill 链接，然后打印 MCP 端点、传输方式与建议的服务名。tacit 不改写任何客户端的 MCP 配置，需要按打印的内容在客户端的 MCP 配置中自行加入这个 HTTP 端点，地址形如 `http://127.0.0.1:<端口>/mcp`；配置的位置与写法取决于客户端。

4. **启动服务**（前台运行，建议单独开一个终端）

   ```powershell
   tacit serve
   ```

   控制台显示监听地址即已就绪；`Ctrl+C` 停止。服务运行时，浏览器打开 `http://127.0.0.1:<端口>/`（端口默认 18888）即进入 Web 界面，本机打开不需要登录。

5. **验证**

   ```powershell
   tacit status
   tacit show resident
   ```

   然后开一个新的会话。绑定与提示词的修改在新会话中生效。

## 从其他设备访问 Web 界面

1. 在本机的 Web 界面「设置 → 访问」中设置访问密码。
2. 在 `config.yaml` 的 `server` 一节写 `host`（如 Tailscale 地址，或 `0.0.0.0` 表示所有网络接口），然后重启 `tacit serve`。`tacit status` 的服务栏会给出额外监听的地址。
3. 在另一台设备上打开 `http://<该地址>:<端口>/`，输入访问密码登录。

MCP 端点仍只接受本机连接。连接不加密，访问密码以明文传输，请只在可信的网络（如 Tailscale）中开放。规则见 [`docs/reference/api.md`](docs/reference/api.md) 第 2 节。

## 日常使用

- 会话开始时，客户端会读到常驻区块（画像、领域索引与主线）与服务端说明（含最近 7 天的活动）。Agent 在对话中按记忆规则自行检索与写入，无需手动操作。记忆工具需要 `tacit serve` 运行。
- 开始处理某个主线时，Agent 会先调用 `recall` 只给 `workstream` 读取该主线的主线简报：主线约定、未决事项、按类型分组的页目录、最近的活动与最近一次周汇总。也可以手动让它这样做，例如"先看一下 my-app 的主线简报"。
- 想让 Agent 立即把本次会话写进记忆（例如压缩上下文或退出前），按客户端触发 skill 的方式触发 `tacit-remember`，并可附上附加要求；附加要求可省略，写了就作为本次的重点或额外要求。
- 定期按客户端触发 skill 的方式触发 `tacit-consolidate`（附加要求可省略）开始整理。整理分两步：Agent 先直接处理格式问题、待清理的候选、待汇总的周与月，并重写主线的近况；然后把需要你拍板的事项（主线提案、画像提案、领域规则、页面重组、冲突条目、领域卫生、篇幅提示）编成一张编号清单，你用一次回复决定（如"1 2 确认，3 拒绝，4 改成主线作用域"）。最后在记忆仓库中以 `consolidate:` 提交。到了该整理的时候，新会话的服务端说明会提示。
- 两个记忆指令是 skill，触发方式取决于客户端。升级后请用同样的参数重新执行 `tacit connect`，补上新增的 skill 链接。
- 记忆仓库中的 Markdown 可直接编辑，服务会监听变化并同步索引。服务写入某个文件时，会连同该文件中未提交的手动修改一并提交；其余手动修改请在仓库中自行 `git commit`。
- 想调整 Agent 收到的规则与常驻区块的措辞，编辑 `~/.tacit/prompts/` 下的提示词文档，见 [`docs/reference/configuration.md`](docs/reference/configuration.md)「提示词文档」。

## 文档

| 文档 | 内容 |
| :--- | :--- |
| [`docs/reference/cli.md`](docs/reference/cli.md) | 每个命令的语法、选项、输出与退出码 |
| [`docs/reference/configuration.md`](docs/reference/configuration.md) | `config.yaml`、环境变量、密钥与提示词文档 |
| [`docs/reference/mcp.md`](docs/reference/mcp.md) | MCP 端点、工具参数、返回格式与资源 |
| [`docs/reference/api.md`](docs/reference/api.md) | 管理 API 的端点、访问规则与错误 |
| [`docs/web.md`](docs/web.md) | Web 界面的页面与流程 |
| [`docs/operations.md`](docs/operations.md) | 文件位置、备份与恢复、日志、排障 |
| [`docs/design.md`](docs/design.md) | 记忆如何分层、写入、检索与注入 |
| [`docs/prompts.md`](docs/prompts.md) | 发给模型的每段文字负责什么 |
| [`docs/architecture.md`](docs/architecture.md) | 代码结构与各模块的实现状况 |
| [`docs/testing.md`](docs/testing.md) | 测试方式 |
| [`GLOSSARY.md`](GLOSSARY.md) | 术语 |
