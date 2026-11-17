# TacitMemory 测试规范

本文档定义测试接缝、测试环境与运行方式。每个测试测什么，看测试类与测试名。

## 1. 三个测试接缝

测试只针对公开契约编写，不直接测试 `internal` 实现（例外见第 3 节）。

1. **`tests/TacitMemory.Core.Tests`：门面接缝**
   - 通过 `TacitMemory.Core` 的公开门面测试。
   - 每个测试有独立的临时用户主目录、`TACIT_HOME` 与记忆仓库（`TestHome`），用真实文件与真实 `git`。`init` 在每个测试进程里按语言做一次作模板，之后复制，因为 Windows 上启动 `git` 很慢。
   - 测试不读开发机的环境变量，绑定只写入临时用户主目录，从不碰真实的客户端配置。
2. **`tests/TacitMemory.Host.Tests`：MCP 接缝**
   - 在进程内以真实 HTTP 启动 `ServiceHost`，用 SDK 的 MCP 客户端调用。
   - 非本机连接用 `127.0.0.2` 模拟：测试经内部入口把「本机」收窄为只有 `127.0.0.1`。macOS 上需要先加回环别名，见第 5 节。
   - 工具的校验与拒绝规则由门面接缝覆盖，这里不重复。
3. **`tests/TacitMemory.Cli.Tests`：CLI 接缝**
   - 经 `CommandLine.RunAsync` 在进程内执行真实的命令管道；命令只经 `CliConsole` 读写控制台，这是 CLI 唯一的接缝。
   - 需要服务的命令另起进程内的真实主机，监听空闲端口。
   - `ConsoleApp` 的日志与 `Environment.ExitCode` 是静态状态，所有测试类加入 `CliCollection`，禁止并行；新测试类必须标注 `[Collection(CliCollection.Name)]`。

**等待后台工作**：测试调用 `MemoryService` 的 internal 入口 `SettledAsync`，它立即执行排着的去抖刷新，再等备份、向量补齐与整理判断结束。不轮询；去抖、重试与冷却都走测试时钟（`TestHome.Clock`），推进时间即到期。只有验证真实文件监听本身的少数测试才轮询。

**使用次数**经记忆页的公开视图读取，这些视图本身不计入。

**未自动化，在实机验收中确认**：内置常驻区块逐字节不变；日志文件按天滚动与保留；CLI 美化后的外观。

## 2. 模型与外部服务

测试不调用真实模型，也不访问网络。

- `ModelDouble`：进程内的 HTTP 替身，按用例返回确定的结果，并记录每个请求供断言。三种模型都可设为不可用，也可按次编排失败、断开与挂起，用来测试重试与降级。另有按 Cloudflare 形状应答的路由。
- 默认情况下，嵌入模型与 reranker 不可用，决策模型对一切选择题答「都不是」，所以不涉及模型的测试不会等网络；用到模型的测试传入自己的替身。
- `WebDavDouble`：进程内的 WebDAV 替身，测试云端备份。
- 形似凭据的样本在运行时拼接，源码里不出现完整的凭据格式。

## 3. 本地化完整性

两种语言写法齐全由编译期保证。本地化测试检查编译器看不到的部分：每项都非空、格式占位符一致、结构词在两种语言间互不重复、`init` 写出的标题属于该语言。这是直接读取 `internal` 类型的测试，因为文本表本身就是被检查的对象。

## 4. 提示词的验证

常驻区块、记忆规则、记忆 skill、记忆指令与工具说明的措辞不由自动测试固定，自动测试只检查结构与长度上限。改动以日常使用中的摩擦点为依据（该记没记、记错地方、参数用错、未经同意改写画像等），改后在真实客户端中按该情形手工复现：

- 用临时 `TACIT_HOME`。`connect` 会写客户端的上下文文件与 skill 链接，事先备份，结束后 `disconnect` 并校验恢复原样。
- 用较小的模型验收：提示词要在能力较弱的模型上也达到基本期望。
- 判断依据是可观察的结果：记忆仓库的文件与提交、会话中的工具调用与 skill 读取。
- 验收记忆指令时，附加文字写明「只读、不写入、不提交」，以免改动真实记忆。

## 5. 运行方式

默认只运行与当前变更直接相关的测试。全量测试、格式检查与前端检查由 GitHub Actions 运行（触发条件见 `docs/reference/operations.md`「发布」）。测试需要 `git` 在 `PATH` 中。

测试在 Windows、Linux 与 macOS 上都能运行。Host 与 Cli 的测试要从 `127.0.0.2` 发起连接：Windows 与 Linux 把整个 `127.0.0.0/8` 当作本机地址，macOS 默认只有 `127.0.0.1`，要先加上 `sudo ifconfig lo0 alias 127.0.0.2 up`（重启后失效，常驻需开机执行）。缺少它时，模拟非本机连接的测试会报出这条命令。模拟「文件被其他程序占用」的少数测试只在 Windows 上有意义，其他系统上直接返回。

```powershell
dotnet test tests/TacitMemory.Core.Tests                               # 门面接缝
dotnet test tests/TacitMemory.Host.Tests                                # MCP 接缝
dotnet test tests/TacitMemory.Cli.Tests                                # CLI 接缝
dotnet test tests/TacitMemory.Core.Tests --filter "FullyQualifiedName~InitTests"   # 单个测试类
dotnet test TacitMemory.slnx                                           # 全量
```

C# 格式由根目录 `.editorconfig` 规定，用 `dotnet format` 检查与修正，不放进编译：

```powershell
dotnet format --verify-no-changes TacitMemory.slnx   # 只检查
dotnet format TacitMemory.slnx                       # 就地修正
```

## 6. OpenAPI 快照

`web/openapi.json` 是管理 API 文档的快照，也是前端类型的生成源。`OpenApiTests` 把实际文档与快照比对。改了任一端点的请求或响应类型后，更新快照并重新生成前端类型：

```powershell
$env:TACIT_UPDATE_OPENAPI = "1"
dotnet test tests/TacitMemory.Host.Tests --filter "FullyQualifiedName~OpenApiTests"
Remove-Item Env:TACIT_UPDATE_OPENAPI
dotnet test tests/TacitMemory.Host.Tests --filter "FullyQualifiedName~OpenApiTests"
```

## 7. 前端检查

在 `web/` 下用 Bun 运行，不需要服务，也不需要 `dotnet`；`dotnet build` 与 `dotnet test` 也不需要 Bun。

```powershell
cd web
bun run check       # 类型检查、ESLint、Prettier 格式检查、Vitest 与构建依次运行
bun run format      # Prettier 就地格式化
```

- Vitest 只测不含界面的逻辑：地址、命令的影响面、表单模型、日期推算、格式化、时间线组装、Markdown 解析等纯函数，测试文件与被测模块放在一起。命令的影响面在 node 下用真实的 `QueryClient` 测。
- 不测样式，不做浏览器端到端测试。字典的键一致与写全由类型检查保证，吸顶条的条下边界由实机检查保证。
- 格式由 `web/.prettierrc.json` 规定（120 列，Tailwind 类名排序）；生成的 `openapi.json` 与 `src/api/schema.ts` 不经 Prettier。

## 8. 发行包的冒烟

`build/smoke.sh` 检查一个发布目录能否运行，覆盖「源码构建下测试通过、发行包里却少了东西」这类问题。发行包经过裁剪而测试跑在未裁剪的构建上，所以它也是裁剪有没有删掉用得到的东西的唯一检查。

```bash
build/smoke.sh <发布目录> [<期望的版本>]
```

需要 bash（macOS 自带的 3.2 即可，Windows 上用 Git Bash）、`curl` 与 `jq`。Windows 上的 `jq` 用 scoop 或 winget 安装。

它用临时 `TACIT_HOME` 与空闲端口启动服务，把 CLI、管理 API、四个 MCP 工具、重建索引、备份与恢复预览、Web 界面各走一遍，任何一项失败即以退出码 1 结束。发布工作流在打包后、创建 Release 前运行它。
