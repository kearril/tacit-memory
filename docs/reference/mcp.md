# TacitMemory MCP 接口参考

本文档列出常驻服务对 Agent 暴露的接口：端点、MCP 机制、工具的参数与返回，以及资源。各操作背后的规则见 `docs/design.md`（写入见第 6 节，检索见第 7 节，服务端说明见第 8 节，整理见第 9 节）；工具说明等发给模型的文字负责什么见 `docs/prompts.md`，其原文以代码为准，用 `tacit show tools` 查看。

## 1. 端点

| 项 | 值 |
| :--- | :--- |
| 端点 | `http://127.0.0.1:<端口>/mcp`，端口取自 `server.port`（默认 18888） |
| 传输方式 | Streamable HTTP |
| 建议的服务名 | `tacit` |
| 鉴权 | 无；只监听 `127.0.0.1`，本机视为可信边界 |

`tacit connect` 最后打印端点。tacit 不读写任何客户端的 MCP 配置，由用户在客户端中自行配置（omp 的写法见 `README.md`「快速开始」）；改了 `server.port` 也须自行更新客户端中的配置。服务未运行时记忆工具不可用，已写入的常驻区块仍然有效。

## 2. 机制

常驻服务以无状态方式提供三种 MCP 机制：工具、资源与服务端说明（内容见 `docs/design.md` 第 8 节「服务端说明」）。

- 不提供 prompt：`prompts/list` 恒答空列表，`prompts/get` 一律报错，因为 omp 不看能力声明、每次都请求 prompt 列表，请求失败就丢弃服务器。
- 不使用资源模板、订阅与 `list_changed` 通知，也不使用 completions、elicitation、sampling、roots 与 logging。

## 3. 工具

| 工具 | `op` | 必填参数 |
| :--- | :--- | :--- |
| `recall` | — | `query` 或 `page`；可选 `project`、`from`、`to`、`kind` |
| `write_page` | `create` | `page`、`title`、`aliases`、`sections`；可选 `related` |
| | `upsert_section` | `page`、`section`、`body`；替换时加 `hash` |
| | `delete_section` | `page`、`section`、`hash` |
| | `update_header` | `page`；`title`、`add_aliases`、`related` 至少一项 |
| `write_recent` | — | `text`；可选 `project`、`source`、`replaces`、`confirm_no_secret` |
| `write_persona` | `add` | `persona_section`、`text`、`source`；可选 `confirm_no_secret` |
| | `replace` | `hash`、`text`、`source`；可选 `confirm_no_secret` |
| | `delete` | `hash` |
| | `propose` | `persona_section`、`text`、`source` |

- `recall` 各参数的含义见 `docs/design.md` 第 7 节。
- `persona_section` 取值为 `red_lines`、`working_rules`、`preferences`、`profile`；`write_page` 的 `section` 为小节名。写入凭据检测未通过时（`write_page`、`write_recent`、`write_persona`），可加 `confirm_no_secret` 重新提交。`write_recent` 只写活动，没有 `op`；近期主线不经它写入，见 `docs/design.md` 第 6 节。
- 长度与条数上限取自 `config.yaml` 的 `limits`（见 `docs/reference/configuration.md`），工具说明中的数字随配置填入。
- 标注：`recall` 只读、幂等、不访问外部世界，并请求客户端首轮即加载；三个写入工具非只读、有破坏性、非幂等、不访问外部世界。

### 参数校验

- 参数结构扁平，由服务按 `op` 校验。
- `replace`、`delete` 缺短码或短码与任何画像条目都不符时，不按缺参数处理，而是拒绝并返回整份画像：按节列出，每节标明条数与上限，每条附短码；`delete` 不接受 `text`、`source`、`confirm_no_secret`，`replace` 不接受 `persona_section`。
- 参数名不在工具 schema 中的（未知参数）一律拒绝，每个未知参数一条 `Unknown parameter <名>.`，排在最前；同一次调用中缺 `op`、`op` 无效、放错 `op` 的已知参数一并列出，只拒绝一次。判断不看值：键在、名字不在 schema 中即为未知，值为 `null` 或空串也算；已知参数值为 `null` 仍按放错 `op` 的规则视为未给出。下一步：`op` 有效时列出该 op 的参数（`write_page` 另列 `page`）；`op` 缺失或无效时列出 `op` 的取值与工具的全部参数；`recall` 列出全部参数；`write_recent` 没有 `op`，下一步列出它的全部参数，并加一句 "The mainline is maintained by the tacit-consolidate skill."（`op`、`items`、`hash` 即属此类）。拒绝不写文件、不提交，经调用记录路径记入日志，日志不含参数值。

### 返回与拒绝

- 返回 Markdown 风格的纯文本，不返回结构化数据。
- 参数缺失、短码过期、超出上限或违反规则时标记错误，按"拒绝 / 下一步 / 相关内容"三段返回，"下一步"是主会话可直接执行的动作。同主题活动被退回、候选并入已有候选、提议命中拒绝列表或画像属于正常分支，不标记错误。

## 4. 资源

资源只有一个：

| URI | 名称 | 类型 | 内容 |
| :--- | :--- | :--- | :--- |
| `tacit://consolidation-report` | `consolidation-report` | `text/markdown` | 读取时现场生成的整理报告（`MemoryService.ConsolidationReportAsync`），内容见 `docs/design.md` 第 9 节 |

资源描述写明只在运行 `tacit-consolidate` skill 时读取。
