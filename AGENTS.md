# AGENTS.md

`TacitMemory` 是一个跨客户端通用的个人记忆系统（.NET 11）。

## 1. 文档

| 文档 | 回答的问题 | 动手前读 |
| :--- | :--- | :--- |
| `docs/design.md` | 系统做什么：记忆分层、读写检索整理规则、Web 界面与鉴权边界 | 改动任何记忆行为、规则或界面功能 |
| `docs/decisions.md` | 当前有效的设计决定、理由与没选的做法 | 改动会推翻或绕开某个决定时 |
| `docs/configuration.md` | 环境变量、`config.yaml` 配置项与提示词文档规则 | 改动配置项或提示词校验 |
| `docs/memory-files.md` | 记忆仓库目录结构与 Markdown 文件格式 | 改动文件读写或解析格式 |
| `docs/operations.md` | 备份恢复、Git 同步、日志、排障与发布 | 改动运维行为或发布脚本 |
| `docs/development.md` | 代码地图、程序集边界、测试接缝与运行方式 | 改动模块职责、接口依赖或编写测试 |

管理 API 的端点与字段以 `/api/openapi.json`（快照 `web/openapi.json`）为准，命令用法以 `tacit --help` 为准，MCP 工具说明以 `tacit show tools` 为准，不另写文档。

**何时更新文档**：在合入 `main` 前同步更新（可随代码同一提交，或单独提交 `docs:`）：

- 用户可见的行为、配置、格式或运维步骤变了 → `docs/design.md`、`docs/configuration.md`、`docs/memory-files.md` 或 `docs/operations.md`；
- 做了新的设计决定或推翻了旧决定 → 就地改写 `docs/decisions.md`，不保留取代链；
- 模块边界、数据流、依赖或测试方式变了 → `docs/development.md`。

文档只留代码里重建不出来的东西（为什么、规则、对用户的承诺、地图），不抄录实现步骤与完整字段清单；同一内容只写在一处，其他地方按标题引用。

## 2. 开发与提交规范

- **深模块边界**：`TacitMemory.Core` 仅公开门面 `Tacit`、`MemoryService` 及其子门面、契约类型与 `Localized`，其余保持 `internal`；`TacitMemory.Host` 与 `TacitMemory.Cli` 只调用 `Core` 公开接口，`Cli` 引用 `Host` 仅用于挂载端点、工具目录、管理 API 契约与宿主输出文字（`HostMessages`）。
- **语言约定**：代码注释使用英文；`README.md` 为英文，`README.zh-CN.md` 与 `docs/` 使用简体中文，`CHANGELOG.md` 采用中英双语。
- **公开文案文风**：公开文档、界面 i18n 与内置提示词采用客观、克制的书面技术语言，避免口语感叹或私有工作流黑话；判断三档统一写作「确定 / 不确定 / 否定」。
- **示例去个人化**：公开文档、源码注释、内置示例与测试夹具不得包含个人主机名、内网地址、私人路径或个人项目名，统一使用通用虚构示例（如 `my-app`、`db-migration`、`dev-box`、`workstation`）。
- **前端浏览器支持**：Web 界面以现代桌面浏览器（Baseline Newly Available）为目标，不引入重量级 polyfill。
- **提交与合并**：遵循 **Conventional Commits**（`<type>(<scope>): <summary>`），提交信息须自解释，不得包含内部追踪编号或本地文档路径；特性分支合入 `main` 采用 `git merge --no-ff`，合并提交信息写作 `chore(merge): <summary>`。
