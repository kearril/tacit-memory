# TacitMemory CLI 参考

本文档列出 `tacit` 每个命令的语法、选项、输出与退出码。命令背后的规则见 `docs/design.md`（绑定、备份与恢复、服务运行见第 12 节），配置见 `docs/reference/configuration.md`，日常运维的做法见 `docs/operations.md`。各命令的实现状况见 `docs/architecture.md`。

## 1. 命令一览

| 命令 | 作用 | 需要服务运行 |
| :--- | :--- | :--- |
| `tacit init [--language zh\|en] [--memory <路径>]` | 生成配置模板并创建记忆仓库 | 否 |
| `tacit serve` | 启动常驻服务 | — |
| `tacit connect --client <名称> --context <文件> --skills <目录>` | 绑定客户端 | 否 |
| `tacit disconnect --client <名称>` | 解除绑定 | 否 |
| `tacit show instructions\|resident\|skill [--default]`、`tacit show tools\|remember\|consolidate` | 查看 Agent 收到的内容 | 否 |
| `tacit status` | 查看服务、模型、绑定、整理、备份与密钥的状况 | 否 |
| `tacit probe [embedding\|reranker\|decision]` | 探测配置中的辅助模型 | 否 |
| `tacit reindex` | 全量重建派生索引 | 是 |
| `tacit backup [--to <目录>] [--remote]` | 立即生成手动备份或上传云端备份 | 是 |
| `tacit restore --from <文件>\|--remote [--include-config] [--yes]` | 从本地备份或云端备份恢复 | 是 |

需要服务运行的命令经管理 API（`127.0.0.1` 上的 `/api`）完成工作，无需停止服务；服务未运行时提示先执行 `tacit serve`，并以退出码 4 结束。

- `init` 在服务首次启动前创建记忆仓库，是唯一不经常驻服务写入记忆仓库的入口。`connect` 写客户端文件、客户端 skill 目录中的链接、`bindings.json` 与三个 skill 成品；`disconnect` 写客户端文件、链接与 `bindings.json`。两者加载配置时都会补写缺失的 `rules.md`，只有 `connect` 生成 skill。
- `show` 与 `init`、`connect` 一样直接调用门面，不读写常驻服务的状态。

## 2. 命令

### `tacit init`

生成配置 `TACIT_HOME/config.yaml`、`prompts/rules.md` 模板与记忆仓库。

- `--language zh|en`：记忆语言，默认取系统界面语言。
- `--memory <路径>`：记忆仓库的位置，默认 `TACIT_HOME/memory`。
- 记忆目录非空且不是 Git 仓库时拒绝初始化：改用空目录，或用 `--memory` 指向已有的记忆仓库。

### `tacit serve`

在前台启动常驻服务，控制台显示监听地址即已就绪，`Ctrl+C` 停止。未初始化、配置无效、提示词文档违反限制、端口被占用或向量扩展 `vec0.dll` 校验不通过时拒绝启动。终端输出同时写入日志（见 `docs/operations.md`「日志」）。

### `tacit connect`

```powershell
tacit connect --client omp --context ~/.omp/agent/AGENTS.md --skills ~/.omp/agent/skills
```

- `--client`：客户端的名称，只是标签：小写字母、数字与连字符，最多 32 个字符，大小写不同视为同一名称。
- `--context`：客户端在每个会话开始时读入的上下文文件。上一级目录必须已存在，文件本身可以不存在（不存在时创建）。
- `--skills`：客户端扫描 skill 的目录，必须已存在。
- 三个选项都必填，开头的 `~` 指用户目录。
- 绑定前严格加载配置：有错误时拒绝执行并列出出错的键路径；省略 `models` 不算错误。
- 成功后写入常驻区块，在 skill 目录中建立三个 skill 链接（`tacit-memory`、`tacit-remember`、`tacit-consolidate`），最后打印 MCP 端点（见 `docs/reference/mcp.md` 第 1 节）。检查顺序、拒绝条件与链接形式见 `docs/design.md` 第 12 节「客户端绑定」。
- 同名同路径重复执行是安全的：补齐缺失的链接，常驻区块按当前画像重写。升级后用同样的参数重新执行，即可补上新增的 skill 链接。

**共用**：两个客户端若读取同一个 skill 目录，可以都绑定到它，例如共用 `~/.agents/skills`：

```powershell
tacit connect --client omp --context ~/.omp/agent/AGENTS.md --skills ~/.agents/skills
tacit connect --client claude --context ~/.claude/CLAUDE.md --skills ~/.agents/skills
```

第二条命令会报告「skill 目录 … 已由 omp 使用」，复用已有的三个链接，不重复建立。两个客户端也可以共用同一个上下文文件（例如都读 `~/.agents/AGENTS.md`），文件中只有一个常驻区块。路径按完整路径比较，Windows 上忽略大小写；tacit 不解析目录联接与软链接，经两个不同入口指向同一目录会被当作两处。

### `tacit disconnect`

```powershell
tacit disconnect --client omp
```

- 只需名称，路径取自绑定记录。
- 移除常驻区块与三个 skill 链接：只删指向 tacit 的链接本身，skill 文件与记忆仓库都保留，同名的其他条目不动。上下文文件若是 `connect` 创建的且变空，则一并删除。
- 另一个绑定仍在使用同一上下文文件或同一 skill 目录时，两者都原样保留，并报告「仍被 … 使用，保留」；最后一个绑定解除时才拆除。
- 文件或链接已不在时逐项报告缺失，照常删除记录，以 0 结束；名称不存在时以 1 结束。只宽松读取语言，配置无效时照常工作。
- tacit 不改动 MCP 配置，客户端中的 `tacit` 条目需自行删除。
- 只删除常驻区块不能停止维护：下次渲染时会重新追加。要停止维护，请执行解绑。

### `tacit show`

查看 Agent 收到的内容，每一部分标明来源（代码、内置默认或用户文件）与长度。输出不含运行时状态（调用失败的辅助模型、非 UTF-8 文件）。

| 子命令 | 输出 |
| :--- | :--- |
| `instructions` | 服务端说明 |
| `resident` | 常驻区块 |
| `skill` | 记忆 skill 的成品 |
| `tools` | 工具说明；先严格加载配置，再由 CLI 经 `TacitMemory.Mcp` 读取，说明中的上限取自该配置 |
| `remember`、`consolidate` | 对应记忆指令 skill 的成品（不含整理报告） |

`--default` 输出内置默认文本（`skill` 只输出系统部分），只用于 `instructions`、`resident` 与 `skill`；`remember`、`consolidate` 与 `tools` 不接受。

### `tacit status`

任何问题先看它。列出：

- 服务：地址、版本、运行时长；未运行时写明"服务未运行"。服务栏始终给出 MCP 端点（`http://127.0.0.1:<端口>/mcp`）。
- 辅助模型：可用、冷却（含剩余秒数）或未配置。
- 客户端：每个记录过的客户端一行，最近连接者在前：名称与版本、最近连接日期、协议版本、声明的扩展（无则写"无"）；没有记录时写明"尚未有客户端连接过服务"。服务运行与未运行时都显示（记录见 `docs/design.md` 第 12 节「客户端能力记录」）。
- 绑定：上下文文件是否存在且为 UTF-8、起止标记完整且唯一、三个 skill 链接是否都指向 tacit，缺哪个就指出哪个。每条绑定注明与之共用路径的其他绑定；共用路径只检查一次，问题挂在第一个引用它的绑定上。
- 上次整理：日期与距今天数，从未整理时写明。
- 待处理问题：可确认的候选、格式问题、主线是否为空、待汇总的周，口径与整理提示相同。
- 最近本地备份（三种前缀中最新的一份）与云端备份（`backups/remote.json` 记录的最近一次结果，不访问网络）。
- 日志位置。
- 密钥来源：环境变量或配置文件，不显示值；未配置的模型或 WebDAV 写明"未配置，不需要密钥"。

不访问网络，不调用模型。

服务未运行时省略服务运行时数据，辅助模型栏只列未配置的模型（三个都已配置时省略该栏），其余各栏照常。`config.yaml` 无法加载时（有错误、文件不存在或记忆仓库未初始化）列出原因，有错误时列出全部错误的键路径；此时仍宽松读取 `server.port`（读不出、文件不存在或 YAML 无法解析时为 18888）与 `language`（读不出时取系统界面语言）探测服务：服务在运行则并入它的各栏，未运行或端口被他人占用则在服务栏注明。

退出码：

- 配置无效为 3，即使服务拒绝报告，拒绝原因也只写入 stderr。
- 配置有效时只看服务：应答并给出报告为 0；服务拒绝给出报告，或离线报告读取遇到 IO 或权限错误，为 1，原因写入 stderr，stdout 不输出报告；无应答或端口被其他程序占用为 4。不因模型冷却或待处理问题而变化。
- 多余参数为用法错误 2。

### `tacit probe`

对 `config.yaml` 中配置的模型各发一次真实请求，检查它能否正常使用。不带参数时依次探测三个模型，未配置的显示"未配置，跳过"。

- 不需要服务运行，也不需要记忆仓库已初始化；读取的是 `config.yaml`（运行中的服务仍用启动时的配置，修改后需重启）。
- 请求为内置的英文文本，不发送记忆内容。请求规则见 `docs/design.md` 第 4 节「探测」。
- 成功只输出"成功"；失败输出 HTTP 状态码、响应体前 500 个字符与耗时，网络错误与超时输出类别与耗时。
- 退出码：全部成功为 0；有模型失败，或明确指定的模型未配置为 1；参数非法为 2；`config.yaml` 无效为 3。

### `tacit reindex`

全量重建派生索引（全文、向量、判断缓存）：删除并重建索引文件，按文件重新同步，等待向量补齐后输出文件数、单元数、已有向量的单元数、跳过的非 UTF-8 文件与耗时。嵌入模型未配置时只重建全文并注明嵌入模型未配置；嵌入模型不可用时只重建全文并注明向量待补。它是维护操作，期间记忆工具的调用排队等待。

### `tacit backup`

- 不给选项时立即在 `TACIT_HOME/backups/` 生成手动备份 `manual-YYYY-MM-DD-HHmmss.zip`，不被自动清理。
- `--to <目录>`：写到指定目录，不存在时创建。
- `--remote`：现场生成一份备份，上传到云端，替换远端唯一的 `tacit-backup.zip`。
- `--to` 与 `--remote` 不能同时使用。

备份内容与云端上传的规则见 `docs/design.md` 第 12 节「备份与恢复」。

### `tacit restore`

```powershell
tacit restore --from <zip> [--include-config] [--yes]
tacit restore --remote [--include-config] [--yes]
```

- `--from` 与 `--remote` 恰好给一个；`--remote` 先下载云端备份，确认的即恢复的文件。
- 先显示预览：备份的生成时间、tacit 版本、种类（自动、手动、保险或云端）、记忆仓库 HEAD 的提交号与说明、文件数，以及将被替换的内容。
- 输入 `y` 才开始，其余输入一律取消（退出码 1）；`--yes`（短写 `-y`）跳过确认。标准输入不是终端且未带 `--yes` 时，显示预览后以退出码 2 结束，不挂起等待，也不恢复。
- 默认只恢复记忆仓库；`--include-config` 一并恢复 `config.yaml` 与 `prompts/`。`bindings.json` 永不恢复，绑定请重新执行 `tacit connect`。
- 结果给出保险备份 `pre-restore-*.zip` 的路径、恢复后的 HEAD 与索引重建的数量。恢复回来的 `config.yaml` 需要重启 `tacit serve` 才生效。

执行顺序、拒绝条件与失败时的回退见 `docs/design.md` 第 12 节「备份与恢复」。

## 3. 退出码

所有命令一致（`status` 与 `probe` 的取值见各自一节）：

| 码 | 含义 |
| :--- | :--- |
| 0 | 成功；`status` 中表示服务在运行 |
| 1 | 运行失败：服务拒绝、IO 或权限错误、`serve` 端口被占、`restore` 未确认，`probe` 有模型失败或明确指定的模型未配置 |
| 2 | 用法错误：未知命令或选项、缺参数、参数值非法、互斥选项冲突、非终端下 `restore` 未带 `--yes` |
| 3 | 配置无效：`config.yaml` 缺失或有错、记忆仓库未初始化、提示词文档违反限制。只有严格加载配置的命令会返回：`serve`、`connect`、`status`、`reindex`、`backup`、`restore`、`probe`（`probe` 不要求记忆仓库，只因 `config.yaml` 而返回），以及不带 `--default` 的 `show instructions`、`show resident`、`show skill`、`show remember`、`show consolidate`、`show tools`；`init`、`disconnect` 与带 `--default` 的 `show` 不会返回 |
| 4 | 服务未运行，含端口被其他程序占用 |

## 4. 输出

结果写 stdout；错误与确认提示写 stderr。

### 输出美化

美化只在终端中进行，且每一路（stdout、stderr）各自判断：该路是终端且未设置环境变量 `NO_COLOR`（非空）时美化，否则输出与美化前逐字相同的纯文本，不含任何控制字符。美化只改变排版与颜色，不新增任何文字，中英两种记忆语言下的内容一致。

- `status`：每栏的标题与行放进面板。颜色由各栏已有的结构化字段决定：服务正常为绿色、未运行为黄色；模型可用为绿色、冷却中为黄色、未配置不上色；绑定无问题为绿色、有问题为红色；待处理的计数均为零且主线未过期为绿色，有计数或主线过期为黄色，无法读取为红色；云端备份最近一次成功为绿色、失败为红色；客户端栏不上色；配置栏总是红色。
- `restore` 预览：`Report` 放进面板，将被替换的条目高亮。
- 等待转圈：`reindex`、`backup --remote` 与 `restore` 的执行阶段，stderr 为终端时显示转圈与已用时间，不显示百分比，结束即清除。
- `connect`：端点三行（端点、传输方式、建议的服务名）放进面板，其余照常。
- `probe`：每行按结果上色：成功为绿色，失败与明确指定却未配置的为红色；未配置而跳过的行不上色。
