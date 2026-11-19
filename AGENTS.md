# AGENTS.md

`TacitMemory` 是一个跨客户端通用的个人记忆系统（.NET 11）。

## 1. 文档

| 文档 | 读者 | 回答的问题 | 动手前读 |
| :--- | :--- | :--- | :--- |
| `docs/overview.md` | 维护者 | 它是什么、用户看到什么、整体怎么搭 | 第一次接触项目 |
| `docs/design.md` | 维护者 | 系统做什么：记忆规则与用户可见的行为（含 Web 界面、访问与鉴权） | 改动任何记忆行为、规则或界面功能 |
| `docs/decisions.md` | 维护者 | 当前有效的设计决定、理由与没选的做法 | 改动会推翻或绕开某个决定时 |
| `docs/reference/` | 维护者 | 记忆文件格式、配置项、MCP 接口、运维与发布 | 改动对应的格式、配置、工具或运维步骤 |
| `docs/dev/architecture.md` | 开发者 | 代码地图：程序集、模块、数据流、依赖原则 | 改动模块职责、公开接口或依赖 |
| `docs/dev/testing.md` | 开发者 | 测试接缝与运行方式 | 编写或运行测试 |
| `docs/dev/prompts.md` | 开发者 | 发给模型的每段文字负责什么、放在哪个环节 | 改动提示词、工具说明或判断问题 |
| `docs/dev/ui-design.md` | 开发者 | 界面的视觉原则、令牌与组件约定 | 编写界面代码 |
| `GLOSSARY.md` | 全部 | 术语与应避免的同义词 | 命名公开类型、配置项、工具与参数 |

管理 API 的端点与字段以 `/api/openapi.json`（快照 `web/openapi.json`）为准，命令的用法以 `tacit --help` 为准，不另写文档。

**何时更新文档**：只在以下几种变化时更新，在合入 `main` 前补齐（可以随代码同一提交，也可以在同一分支上单独提交 `docs:`）：

- 用户可见的行为或规则变了 → `docs/design.md` 或 `docs/reference/`；
- 做了新的设计决定，或推翻了旧的 → `docs/decisions.md`，就地改写，不保留取代链；
- 模块边界、数据流或依赖变了 → `docs/dev/architecture.md`；
- 测试接缝、提示词职责、界面约定变了 → `docs/dev/` 中对应的一份。

不把看代码就能知道的细节写进文档：实现步骤、字段清单、每个组件的外观、每个测试测什么。文档只留代码里重建不出来的东西：为什么、规则、对用户的承诺、地图。同一内容只写在一处，其他地方引用；引用其他文档时写标题而不写节号。已收入 `GLOSSARY.md` 的术语直接使用。

## 2. Agent 开发与代码规范

- **深模块边界**：`TacitMemory.Core` 仅公开核心门面接口与契约类型（以及双语文字的类型 `Localized`，供宿主写自己的文字），内部实现保持 `internal`；`TacitMemory.Host` 与 `TacitMemory.Cli` 只调用 `Core` 的公开接口，`Cli` 另外引用 `Host`，仅用于挂载端点、读取工具说明、管理 API 的请求体与 JSON 约定，以及宿主的输出文字（`HostMessages`）。
- **语言约定**：代码注释使用英文；仓库文档（`README.md`、`docs/`、`GLOSSARY.md`）使用中文。
- **公开文案文风**：公开文档、界面 i18n 与内置提示词采用客观、克制的书面技术语言，避免口语感叹、第一人称日记体或私有工作流黑话；三态判断统一使用 `GLOSSARY.md` 中的「确定 / 不确定 / 否定」。
- **示例去个人化**：公开文档、源码注释、内置示例与测试夹具不得包含个人主机名、内网地址、私人路径或个人项目名称，统一使用通用虚构示例（如 `my-app`、`db-migration`、`dev-box`、`workstation`）。
- **前端浏览器支持**：Web 界面以现代桌面浏览器（Baseline Newly Available）为目标，不额外引入重量级 polyfill。
- **提交规范**：遵循 **Conventional Commits**（`<type>(<scope>): <summary>`，如 `feat:`、`fix:`、`refactor:`、`test:`、`docs:`、`build:`）。
- **提交信息要求**：`main` 及所有将合入 `main` 的分支（含子代理工作分支），提交信息应自解释，不得包含仅在本地或外部追踪系统有效的内部编号，亦不得使用带编号的分支名。
- **合并方式**：特性分支合入 `main` 采用普通合并（`git merge --no-ff`），保留分支原有提交；合并提交信息统一写作 `chore(merge): <summary>`，概括合入内容（如 `chore(merge): bring recent activity into main`）。
