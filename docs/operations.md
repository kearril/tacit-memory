# TacitMemory 运维指南

本文档涵盖日常运维操作：文件布局、备份恢复、多机 Git 同步、日志、索引重建、排障与发布升级。命令选项见 `tacit <命令> --help`，配置项见 [`configuration.md`](configuration.md)。

## 1. 数据布局

`TACIT_HOME` 默认为 `~/.tacit`：

```text
~/.tacit/
├── config.yaml      # 配置文件
├── prompts/         # 自定义提示词文档
├── skills/          # 服务生成的 skill（请勿手改）
├── bindings.json    # 客户端绑定记录
├── access.json      # 访问密码哈希与会话密钥（运行状态，不进备份）
├── clients.json     # 客户端能力记录（运行状态，不进备份）
├── sync.json        # 同步结果与暂停状态（运行状态，不进备份）
├── backups/         # 本地备份 zip 与 remote.json
├── logs/            # 按天滚动的服务日志（保留 14 天）
└── memory/          # 独立 Git 记忆仓库（.cache/ 为可重建的派生索引）
```

## 2. 备份与恢复

| 类型 | 触发时机 | 文件位置 | 清理策略 |
| :--- | :--- | :--- | :--- |
| 自动备份 | 每天首次写入后 | `backups/auto-YYYY-MM-DD.zip` | 保留最近 `backup.keep` 份（默认 7，`0` 关闭） |
| 手动备份 | `tacit backup` | `backups/manual-*.zip`（或 `--to` 指定目录） | 永不自动清理 |
| 保险备份 | 每次恢复前自动生成 | `backups/pre-restore-*.zip` | 永不自动清理 |
| 云端备份 | `tacit backup --remote` | WebDAV `backup.webdav.file`（默认 `tacit-backup.zip`） | 远端只留一份，原子替换 |

所有备份均包含记忆仓库（含 `.git`，不含 `.cache`）、`config.yaml`、`prompts/`、`bindings.json` 与 `manifest.json`，不含 `skills/` 与运行状态文件（`access.json`、`clients.json`、`sync.json`）。

- **云端备份**：支持任意提供 `PUT`、`MOVE`、`MKCOL`、`GET`、`DELETE` 的 WebDAV 服务。先上传 `.part` 再 `MOVE` 覆盖，服务不支持 `MOVE` 时直接报错，不会退回直接覆盖。
- **恢复**：运行 `tacit restore --from <zip>` 或 `tacit restore --remote`，先预览再确认，执行前自动创建保险备份。默认仅恢复记忆仓库，加 `--include-config` 一并恢复配置与提示词，永不恢复 `bindings.json`。恢复结束后自动重建索引。

## 3. 多机 Git 同步

多台机器共享同一份记忆时，配置私有 Git 远端（`sync.remote`）。只同步记忆仓库，配置、提示词、绑定与备份留在各自机器，每台机器应配置不同的 `machine.name`。

1. **准备私有远端**：在自建服务器/NAS 建空裸仓库（`git init --bare ~/tacit-memory.git`），或在托管平台建**空**的私有仓库（不要勾选生成 README）。
2. **第一台机器**：配置 `sync.remote` 后执行 `tacit sync`，推送初始历史。之后服务会在写入前后与定时自动同步。
3. **第二台机器**：执行 `tacit init --remote <地址>` 克隆记忆仓库并生成配置，随后照常 `tacit connect` 与 `tacit serve`。

**非交互认证**：后台同步禁用终端提示（SSH 启用 `BatchMode=yes`，单命令超时 60 秒）。建议：
- SSH：先在终端 `ssh <主机>` 接受主机指纹，并在 `~/.ssh/config` 中为该主机显式指定 `IdentityFile`（比依赖 `ssh-agent` 更适合 `launchd` / `systemd` / Windows 服务）。
- HTTPS：先在终端完成一次 `git fetch` 缓存凭据。
- 配置后执行 `tacit sync` 验证常驻服务能否实际完成认证。

**冲突与选边**：
- 发生 rebase 冲突时自动同步暂停，按 `tacit status` 或整理报告给出的步骤在记忆仓库中 `pull --rebase`、解决冲突、继续 rebase，然后运行 `tacit sync` 恢复。
- 远端历史不相关或刚恢复备份后同步暂停，需运行 `tacit sync --use-local`（以本机为准覆盖远端）或 `tacit sync --use-remote`（先自动做手动备份，再重置到远端并重建索引），均需先预览确认。

## 4. 日志、索引与定时提醒

- **服务日志**：写入 `TACIT_HOME/logs/tacit-YYYYMMDD.log`，仅 `tacit serve` 写入，按天一份保留 14 天。记录启动、工具调用（含 `recall` 查询词与耗时）、管理 API 变更请求、备份/恢复/同步结果与异常；不记录记忆正文、原话、提示词内容与密钥。
- **重建索引**：派生索引损坏、更换嵌入模型或更换决策模型（清空整理判断缓存）后，执行 `tacit reindex`。
- **定时提醒整理**：可通过 `tacit consolidation` 或本机 `GET /api/consolidation` 查询是否该整理。在 Web 仪表盘「索引与整理」卡片可一键复制给本机 Agent 的定时任务配置指令。

## 5. 排障速查

通用退出码：`0` 成功，`1` 运行失败，`2` 用法错误，`3` 配置无效，`4` 服务未运行。

- **定位入口**：任何异常先跑 `tacit status`。
- **升级后报未知配置键**：删除 `config.yaml` 中已移除的旧键后重启服务。
- **忘记访问密码 / `serve` 因缺少密码拒绝启动**：先从 `config.yaml` 移除 `server.host` 并重启服务；本机调用 `DELETE /api/access-password` 或停服删除 `TACIT_HOME/access.json` 清除密码，重设后再恢复 `server.host`。
- **辅助模型连不上**：执行 `tacit probe` 检查 `url`（完整请求地址）、`model`、`provider` 与密钥。修改环境变量后须重启终端与服务。
- **客户端调用工具失败**：确认 `tacit serve` 正在运行，且在绑定后开启了新会话。
- **提示词超限**：按报错指出的文件与超出字符数精简后重启。

## 6. 升级与发布

### 校验与升级发行包

校验下载文件（另可用 `gh attestation verify <发行包> --repo kearril/tacit-memory` 验证构建来源）：

```bash
sha256sum -c tacit-<版本>-<平台>.<扩展名>.sha256        # Linux / Git Bash
shasum -a 256 -c tacit-<版本>-osx-arm64.tar.gz.sha256   # macOS
```

升级步骤（不影响 `~/.tacit` 数据目录）：

1. 停止当前运行的 `tacit serve`；
2. **清空安装目录**（避免残留旧版文件）；
3. 将新版发行包解压至安装目录；
4. 启动服务并执行 `tacit status` 核对版本。

### 本地打包与源码构建

版本号由带 `v` 前缀的 Git tag 推导（需完整 Git 历史）。本机打包自包含发行包需要 .NET 11 SDK、Bun、`jq` 与 bash，并在目标平台本机执行（macOS 仅在原生构建时生成 ad-hoc 签名）：

```bash
build/package.sh <win-x64|osx-arm64|linux-x64> <输出目录>
build/smoke.sh <输出目录>/tacit
```

若仅从源码发布依赖框架版本（需预装 ASP.NET Core 11 运行时）：

```bash
dotnet publish src/TacitMemory.Cli -c Release -o <输出目录>
```

不需要 Web 界面时可加 `/p:TacitSkipWebBuild=true`（跳过 Bun 构建，访问 `/ui` 将提示未构建）。推送 `v*` tag 至 GitHub 时，发布工作流会自动在三平台完成测试、打包、冒烟、来源证明并截取 `CHANGELOG.md` 对应章节创建 Release。
