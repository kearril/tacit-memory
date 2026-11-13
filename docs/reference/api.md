# TacitMemory 管理 API 参考

本文档列出管理 API 的约定：地址、鉴权与防护、响应与错误格式，以及端点清单。它的使用者是 `tacit` 命令与 Web 界面。各操作背后的规则见 `docs/design.md`，Web 界面如何使用这些端点见 `docs/web.md`。

本文描述目标形态。表中的"状态"列标明是否已实现：**已实现**、**计划**。标为计划的部分，现行行为见 `docs/architecture.md` 第 5 节。

## 1. 地址与格式

| 项 | 值 | 状态 |
| :--- | :--- | :--- |
| 地址 | `http://<主机>:<端口>/api`，端口取自 `server.port`（默认 18888） | 已实现 |
| 监听 | 总是监听 `127.0.0.1`；`server.host` 给出的一个额外地址也监听，与 `server.port` 同一端口；额外地址绑定失败时只在回环上运行（见 `docs/reference/configuration.md`） | 已实现 |
| 请求与响应 | JSON，属性名为 camelCase，枚举为 camelCase 文本 | 已实现 |
| 接口描述 | `GET /api/openapi.json`，OpenAPI 文档；字段以它为准，本文不重复；仓库中的快照是 `web/openapi.json` | 已实现 |

同一个监听上还有 `/mcp`（见 `docs/reference/mcp.md`）与 `/ui`（Web 界面的静态文件，不需登录；`/` 重定向到 `/ui/`）。

## 2. 鉴权与防护

按连接来源区分、请求防护、会话与访问密码均已实现。

### 按连接来源区分

依据是 TCP 连接的对端地址，不看任何请求头。「本机」指对端为任一回环地址（`127.0.0.0/8`、`::1`，含映射到 IPv6 的回环地址）的连接。

| 对端地址 | `/api` | `/mcp` |
| :--- | :--- | :--- |
| 回环地址（本机） | 免登录 | 可用 |
| 其他 | 带有效会话时与回环连接相同；否则 401，错误名 `ApiUnauthorized`（会话端点除外，见第 4 节） | 403，错误名 `McpRemoteRefused`，会话也不放行 |

- `tacit` 命令从本机调用，不带任何凭据。经 SSH 端口转发进来的连接同样来自回环地址，免登录。
- 不支持反向代理转发到回环地址：经它进来的请求会被当作本机而免登录。
- 不提供 HTTPS；非回环连接上的访问密码与密钥以明文传输。

### 访问密码与会话

- 访问密码只有一个，没有用户名。它的哈希与会话签名密钥保存在 `TACIT_HOME` 下的访问状态文件 `access.json` 中，不在配置里，没有对应的环境变量，不进入备份（见 `docs/reference/configuration.md`）。哈希用 PBKDF2-SHA256（600000 次迭代，每次设置都换新的盐），比较为定长时间比较；最短 8 个字符，首尾空白原样计入。
- `server.host` 为非回环地址而没有访问密码时，启动服务被拒绝。
- 登录成功后服务发出会话 Cookie `tacit_session`：值为到期时间加上以访问状态文件中的密钥做的 HMAC-SHA256 签名，服务端不存会话清单；有效期从登录起固定 30 天，不随使用续期；属性为 `HttpOnly`、`SameSite=Strict`、`Path=/api`，不设 `Secure`（不提供 HTTPS）。会话在服务重启后仍有效。
- 退出登录只让当前浏览器的 Cookie 过期；被复制走的 Cookie 在修改访问密码之前仍然有效。
- 设置、修改或清除访问密码都换掉签名密钥，使全部已有会话失效。修改访问密码时，发起的非本机连接在同一响应中得到新的会话。
- 同一对端地址连续 5 次输错后，60 秒内该地址的登录请求返回 429，说明中给出剩余秒数；登录成功清零。计数只在内存中，服务重启即清零。未设访问密码时的登录请求同样计数。
- 登录后的权限与回环连接相同。
- 访问密码的设置与清除规则：本机连接设置或修改密码不需要旧密码；非本机连接修改须提供当前密码，不对时 401（`PasswordWrong`）。清除只接受本机连接，非本机 403（`PasswordClearRemoteRefused`）；运行中的配置里 `server.host` 为非回环地址时清除被拒绝，409（`PasswordClearRefused`）。

### 请求防护

只作用于 `/api`。

| 防护 | 规则 | 拒绝时 |
| :--- | :--- | :--- |
| Origin | 带 `Origin` 头时，须与请求自身的源（协议加 Host 头）相同；对所有连接生效 | 403 |
| Host | 仅本机连接：主机名须为 `127.0.0.1`、`localhost` 或 `[::1]`，端口不限；其他连接不检查 | 403 |
| Content-Type | `POST` 与 `PUT` 须为 `application/json` | 415 |

防护先于鉴权判定：其他设备的请求若同时违反防护，得到的是防护的拒绝，而不是 401。

## 3. 响应与错误

错误体为 `{"error": "<错误名>", "message": "<记忆语言的说明>"}`，`message` 可直接显示。

| 状态码 | 含义 | 状态 |
| :--- | :--- | :--- |
| 200 | 成功 | 已实现 |
| 400 | 请求不被接受：请求体无法读取、缺少必填成员、取值无效；访问密码少于 8 个字符（`PasswordTooShort`） | 已实现 |
| 401 | 其他设备的连接未登录或会话失效访问 `/api`（`ApiUnauthorized`）；登录密码不对（`PasswordWrong`）；修改密码时当前密码不对 | 已实现 |
| 403 | 被请求防护拒绝；其他设备的连接访问 `/mcp`；其他设备的连接清除访问密码 | 已实现 |
| 409 | 记忆或配置不处于该操作接受的状态，用户可以修正；`server.host` 为非回环地址时清除访问密码（`PasswordClearRefused`） | 已实现 |
| 415 | Content-Type 不是 `application/json` | 已实现 |
| 429 | 登录被限速（`LoginLimited`） | 已实现 |
| 500 | 环境失败（`git`、向量扩展、云端服务、文件系统）或服务意外失败 | 已实现 |

校验配置或提示词文档不通过时返回 409，错误体另带出错位置的清单（配置为键路径），供界面标到对应字段。

每次调用记一条日志（方法、路径、状态码、耗时），不含请求体与密钥。

## 4. 端点

| 端点 | 作用 | 状态 |
| :--- | :--- | :--- |
| **会话与访问密码** | | |
| `GET /api/session` | 当前连接是否来自回环（`local`）、是否已登录（`loggedIn`）、是否已设访问密码（`passwordSet`），以及记忆语言（`language`）与服务版本（`version`）；不需登录 | 已实现 |
| `POST /api/session` | 以访问密码登录：`{password}`，成功则设会话 Cookie，返回同上的会话状态；不需登录 | 已实现 |
| `DELETE /api/session` | 退出登录：只让当前浏览器的 Cookie 过期；无会话时也成功；不需登录 | 已实现 |
| `PUT /api/access-password` | 设置或修改访问密码：`{password, current}`，`current` 为非本机连接必填；成功后返回会话状态，非本机连接同时得到新的会话 Cookie。非本机连接未登录时 401 | 已实现 |
| `DELETE /api/access-password` | 清除访问密码：只接受本机连接；`server.host` 为非回环地址时 409 | 已实现 |
| **接口描述** | | |
| `GET /api/openapi.json` | OpenAPI 文档，描述本节各端点的请求、成功与错误响应（非本机连接须已登录） | 已实现 |
| **状态与维护** | | |
| `GET /api/status` | 状态报告 | 已实现 |
| `POST /api/reindex` | 重建派生索引（维护操作） | 已实现 |
| `POST /api/backup` | 手动备份或云端备份：`{target, to}` | 已实现 |
| `POST /api/restore` | 预览或执行恢复：`{path, remote, config, preview}` | 已实现 |
| `GET /api/backups` | `backups/` 下的备份清单（种类、时间、大小）与云端备份记录 | 计划 |
| **配置** | | |
| `GET /api/config` | 各项的当前值、默认值、是否需重启才生效；密钥只给来源，不给值 | 计划 |
| `PUT /api/config/<节>` | 校验并保存一节，随后在进程内重新加载 | 计划 |
| `GET /api/config/secrets/<键>` | 单个密钥的值 | 计划 |
| `POST /api/probe` | 探测已配置的辅助模型 | 计划 |
| **绑定** | | |
| `GET /api/bindings` | 绑定清单与各自的检查结果 | 计划 |
| `POST /api/bindings` | 新增绑定：客户端名称、上下文文件、skill 目录 | 计划 |
| `DELETE /api/bindings/<客户端名称>` | 解除绑定 | 计划 |
| **提示词文档** | | |
| `GET /api/prompts` | 四份文档的当前内容、是否为内置默认、长度与上限 | 计划 |
| `PUT /api/prompts/<文档名>` | 校验并保存 | 计划 |
| `DELETE /api/prompts/<文档名>` | 删除文件，回到内置默认 | 计划 |
| **日志** | | |
| `GET /api/logs` | 有日志的日期 | 计划 |
| `GET /api/logs/<日期>` | 某天的日志 | 计划 |
| `GET /api/logs/stream` | 实时推送新日志（Server-Sent Events） | 计划 |

已实现端点的补充说明：

- `POST /api/backup`：`target` 为 `local` 时 `to` 可给出主机上的绝对目录，省略则写到 `backups/`；`target` 为 `remote` 时执行云端备份。
- `POST /api/restore`：`path` 与 `remote` 二选一。`preview` 为真时只返回预览（`remote` 时先下载云端备份），为假时执行，且 `remote` 不能用于执行；`config` 为真时一并恢复 `config.yaml` 与 `prompts/`。校验在核心中完成。
