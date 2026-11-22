# TacitMemory 记忆文件格式

记忆仓库中的所有文件都是可直接编辑的 Markdown。服务写入时仅定位修改目标行，尽量保留其余字节；手动编辑出现格式问题时服务照常读取，并在整理报告中提示修正。

## 1. 目录结构

```text
<记忆仓库>/
├── .gitignore              # 仅含 .cache/
├── persona.md              # 画像
├── candidates.md           # 画像候选与拒绝列表
├── workstreams.md          # 已成立主线的背景说明与近期摘要
├── recent/YYYY-MM.md       # 按月活动记录，末尾可有 ## Summary
├── pages/
│   ├── <主线>/<slug>.md    # 细节页，目录名即所属主线
│   └── _topics/<slug>.md   # 专题页（不属于任何主线）
└── .cache/                 # 派生索引，不进 Git
```

## 2. 通用约定

- **编码与换行**：新建文件为 UTF-8 无 BOM、LF 换行；修改已有文件保留原 BOM 与换行风格。列表项用 `- `，缩进两个空格。
- **标题与日期**：文件不写一级标题，从 `##` 开始分节；日期格式 `YYYY-MM-DD`（本地日期）。
- **结构词固定英文**：节名、`Belongs to:`、`Scope:`、`Trigger:`、`Background:`、`Recent:`、`quote`、`on` 等格式结构词一律英文（大小写不限），与 `language` 配置无关；正文内容按记忆语言书写。
- **来源注释**：紧跟在条目下一行的缩进 HTML 注释：
  - 画像：`<!-- 2026-10-25 quote「请优先使用标准日志格式」 -->`（原话可省）
  - 活动：`<!-- on dev-box quote「…」 -->`（日期由当日标题给出，`on <机器名>` 与 `quote` 均可缺其一）
- **主线名规则**：统一小写，仅允许 `[a-z0-9._-]`；`_` 开头为系统保留名（如 `_topics`），`.` 开头不算主线。
- **短码**：服务返回条目或小节时附带内容哈希前 6 位（如 `a3f9c2`），不写入文件；内容一改即失效。

## 3. 画像（`persona.md`）

前四节放全局条目，其后依次为领域块（`## Domain: <标签>`）与主线块（`## Workstream: <名>`），块内按同样的四节分三级标题：

```markdown
## Red lines
- 不要自行发布版本或推送到公开仓库；改为先请用户确认（理由：影响外部，无法撤回）
  <!-- 2026-10-25 quote「发布前必须问我」 -->

## Working rules
- 需求不清时 → 先追问确认，再给出完整方案
  <!-- 2026-10-20 quote「先问清楚再动手」 -->

## Preferences
- 沟通：先给结论，再列依据

## Profile
- 环境：Linux x64，常用 Shell 为 zsh

## Domain: db-migration
Trigger: 编写或修改数据库迁移脚本时
### Working rules
- 修改生产表结构 → 保持向后兼容，先加列再迁移数据

## Workstream: my-app
### Working rules
- 发版 → 先跑全量测试
```

各节推荐句式（软性建议，服务不强制拦截）：

| 节 | 推荐句式 |
| :--- | :--- |
| `Red lines`（红线） | `不要 <行为>；改为 <替代做法>`（可附理由） |
| `Working rules`（协作规则） | `<情境>时 → <做法>`（肯定句） |
| `Preferences`（偏好） | `<维度>：<描述>` |
| `Profile`（档案） | `<字段>：<事实>` |

## 4. 画像候选（`candidates.md`）

```markdown
## Candidates

### 沟通：先给结论，再列依据
Belongs to: Preferences
- 2026-10-28 quote「先说结论」
- 2026-10-30 quote「直接说结果」

## Rejection list

### 每次提交前都运行全量测试
Belongs to: Working rules
- 2026-10-27 quote「跑一下全部测试」
- 2026-10-29 rejected quote「不用，只跑相关的」
```

- `Belongs to:` 之后可附 `Scope: domain:<标签>` 或 `Scope: workstream:<名>`，缺省为全局。
- 候选全部作为整理证据，不设出现天数门槛；被拒绝的条目移入拒绝列表，相近候选不再重复提议。

## 5. 主线（`workstreams.md`）

```markdown
## Workstream
### my-app
Background: 本地开发环境与部署脚手架工具。
Recent: 核心模块已完成稳定版本发布，当前在进行代码与文档整理。
```

- 文件中有条目的名字即为已成立的主线，由整理阶段经用户确认后直接编辑维护。
- `Background:` 必填（基本不变）；`Recent:` 可选（整理时重写）。活动记录比近期摘要新，不一致时以活动为准。

## 6. 活动记录（`recent/YYYY-MM.md`）

```markdown
## 2026-10-31
- [my-app] 决策模型定为只做判断，去重与修正交给整理
  <!-- on dev-box quote「决策模型只负责选择题判断，归并交由整理完成」 -->
- 明确了画像中需包含用户协作倾向，而不仅限于硬性规范
  <!-- on dev-box -->

## Summary
- 2026-W45：本周完成了核心架构与记忆结构调整
- 2026-11：十一月主要推进记忆系统重构与文档整理
```

- 每天一个二级标题，属于主线的条目以 `[主线名]` 开头；机器标记 `on <机器名>` 由服务自动写入。
- `## Summary` 保持在文件末尾，由整理阶段写入：周汇总按 ISO 周计（写入该周周四所在月文件），月汇总按自然月计。汇总行参与检索，但不取短码、不进入最近 7 天活动列表。

## 7. 细节页（`pages/<主线>/<slug>.md`）

```markdown
---
title: 数据库升级后扩展丢失
aliases: [全文检索插件失效, 升级后搜索不到中文]
dates: { first: 2026-10-21, last: 2026-10-25 }
related: [my-app/backup-script, _topics/pgroonga]
supersedes: [my-app/legacy-search-decision]
type: incident
---

## 现象
……

## Lesson
……
```

| 页头字段 | 说明 |
| :--- | :--- |
| `title` | 必填，页面标题 |
| `aliases` | 至少 2 个，覆盖口语说法、中英文名与别称 |
| `dates` | 首次与最近一次经服务写入的日期（由服务维护） |
| `related` | 关联页面的页面标识（`<主线>/<slug>`），指向不存在的页时拒绝写入 |
| `supersedes` | 可选，当前页取代的旧页面标识列表；目标须存在且不能形成环，缺省不写出 |
| `type` | 必填，取 `incident`、`decision`、`entity`、`work` 之一 |

- `slug` 匹配 `^[a-z0-9]+(-[a-z0-9]+)*$`，主线内唯一，创建后不改名。
- 正文按 `##` 分小节，页内小节名不可重名。服务按英文名识别三个约定小节：`work` 页的 `Leftovers`、`decision` 页的 `Overturned`、`incident` 页的 `Lesson`；其余小节名自由按记忆语言命名。

## 8. Git 提交与容错

- **提交约定**：服务写入自动提交单个文件（作者 `tacit <tacit@localhost>`），提交信息为 `<区域>: <动作> <对象>`；完整整理提交以 `consolidate:` 开头，局部整理以 `consolidate(partial):` 开头。
- **同步属性**：开启同步时，服务将 `* text=auto eol=lf` 及 `recent/*.md`、`candidates.md` 的 `merge=union` 规则写入本机 `.git/info/attributes`（不提交）。
- **容错读取**：手改导致 YAML 头解析失败、缺少 `type`、日期格式错误或无法识别的节时，服务尽量继续索引与渲染，并在整理报告的格式问题中列出；非 UTF-8 文件会被跳过并在服务端说明中提示。
