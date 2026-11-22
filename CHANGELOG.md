# Changelog

All user-visible changes are recorded here starting from `1.0.0`. When a `v*` tag is pushed, the release workflow extracts the matching version section as the GitHub Release notes.

Format: each release has a `## <version> - <date>` heading (without the `v` prefix), with English above and Chinese below. Upcoming changes live under `## Unreleased`.

## Unreleased

## 1.1.0 - 2026-11-22

- **Explicit page evolution (`supersedes`) and 1-hop graph recall**: Detail pages now support an optional `supersedes` header field via `write_page` (`create` / `update_header`) with cycle checks and consolidation format validation. Superseded pages are automatically treated as overturned in retrieval and workstream briefs, and `recall` performs bounded 1-hop expansion along `supersedes` and `related` links while surfacing status headers (`overturned -> <replacement>`) and concise `Links:` hints.
- **Better ranking for current decisions and detail pages**: Active replacements rank ahead of overturned decisions (even on legacy-phrased queries) without hiding historical rationale when queried directly. General searches prioritize detail pages over older single-line activities with gentle recency decay on activities only; explicit `kind=activities` searches remain unaffected.
- **Two-layer adaptive gating**: Raised the default `retrieval.minRerankScore` floor from `0.001` to `0.20` and added adaptive `topK` truncation (relative-to-best score cutoff and activity slot caps in general searches) so focused queries return concise results and out-of-corpus queries abstain cleanly.
- **Transparent Recall Trace & page evolution UI**: Recall Trace now displays graph-expansion badges, overturned states, raw vs. adjusted final scores, and adaptive cutoff fates (`BelowRelativeFloor`, `BeyondActivityCap`); detail page views now show `Superseded by`, `Supersedes`, and `Related` links alongside an overturned badge.
- **Expanded 92-query benchmark suite**: Added `current-state`, `activity-vs-page`, and `graph-hop` stress queries and `supersedes` chains, raising 92-query `Qwen3-0.6B` reference results from 94.7% → 100.0% Recall@5, 0.885 → 0.967 MRR, and 0% → 52.9% negative abstention.

---

- **显式页面演进（`supersedes`）与 1-hop 图扩展检索**：细节页新增可选 `supersedes` 页头字段，`write_page`（`create` / `update_header`）支持写入并校验存在性与无环约束，整理报告自动检查失效或成环引用；被取代的页面在检索与主线简报中自动视为已推翻，`recall` 沿 `supersedes` 与 `related` 执行有界 1-hop 扩展，并在结果中标注状态（`overturned -> <替代页>`）与精简 `Links:` 提示。
- **现行决定与细节页优先排序**：活跃替代页在现状提问与旧术语查询中自动排在已推翻旧页之前，直接追问历史原因时旧页仍可完整召回；通用检索中细节页优先于单行活动，仅对活动施加温和时间衰减（细节页零衰减），显式 `kind=activities` 检索不受影响。
- **两层自适应门控**：`retrieval.minRerankScore` 默认值由 `0.001` 提高至 `0.20`，并引入相对最佳分截断与通用检索活动槽位上限，减少无关尾项与低分误召回。
- **透明检索追踪与演进浏览界面**：Web 检索追踪新增图扩展来源徽标、已推翻标注、原始重排分与最终分对照及新增截断去向；细节页新增「被以下页面取代」「取代的页面」链接分组与已推翻徽标。
- **92 条查询基准扩展与提升**：扩充现状、活动与页面竞争及跨页关联压力查询并在内置语料中接入 `supersedes` 链，本地 `Qwen3-0.6B` 组合实测 Recall@5 由 94.7% 升至 100.0%，MRR 由 0.885 升至 0.967，负例拒答率由 0% 升至 52.9%。

## 1.0.1 - 2026-11-22

- **Faster local vector search & indexing**: Streamed zero-copy vector scanning with SIMD cosine distance, bounded top-K selection, and batched full-index sync—cutting 10,000-unit vector search latency by ~4× (to ~7.7 ms P50) and per-query allocations from ~46 MB to ~33 KB.
- **Automated package manager releases**: Added release workflow automation to update Homebrew tap and Scoop bucket on stable tags.
- **Retrieval & performance benchmarks**: Added an in-repo benchmark harness (`recall` and `perf`) with a 38-page / 78-query built-in corpus and benchmark guide (`docs/benchmarks.md`).
- **Documentation**: Updated README and upgrade guides covering Homebrew/Scoop installation, auxiliary model setups, and benchmark reference numbers.

---

- **本地向量检索与索引同步提速**：改用零拷贝流式向量扫描、SIMD 余弦距离、有界 Top-K 选择与批量事务同步；在 10,000 单元规模下，向量检索 P50 耗时降低约 4 倍（至 ~7.7 ms），单次查询内存分配由 ~46 MB 降至 ~33 KB。
- **自动更新包管理器渠道**：正式版 tag 发布时自动更新 Homebrew tap 与 Scoop bucket。
- **检索与性能基准套件**：新增仓库内基准工具（`recall` 与 `perf`）、内置 38 页 / 78 查询脱敏语料与基准指南（`docs/benchmarks.md`）。
- **文档完善**：补充 Homebrew / Scoop 安装与升级说明、辅助模型推荐方案及性能基准参考数据。

## 1.0.0 - 2026-11-21

First public release of TacitMemory, a local-first personal memory system for AI coding clients over MCP.

- **Structured Markdown memory**: Plain-text Git repository under `~/.tacit/memory` with persona, workstreams, topic pages, and monthly activity logs.
- **Cross-client MCP service**: Single local HTTP server supporting multiple AI coding clients concurrently, with one-command binding presets and generated skills.
- **Hybrid retrieval and consolidation**: Local hybrid search combining full-text, vector, and rerank scoring, plus LLM-assisted judgment and consolidation workflows.
- **Web UI**: Built-in management console with dashboard, recall trace inspection, memory browser, prompt editor, and settings.
- **Backup and multi-machine sync**: Automated local backups, optional WebDAV remote backups, and Git-based multi-machine memory synchronization.
- **Prebuilt binaries**: Self-contained releases for Windows (`win-x64`), macOS (`osx-arm64`), and Linux (`linux-x64`) with SHA-256 checksums and build provenance attestations.

---

TacitMemory 首个正式公开版本，面向 AI 编码客户端的本地优先、白盒个人记忆系统。

- **结构化 Markdown 记忆**：基于 `~/.tacit/memory` 纯文本 Git 仓库，分层维护画像、主线、细节页与按月活动记录。
- **跨客户端 MCP 服务**：单个本地常驻 HTTP 服务支持多客户端并发读写，内置主流客户端一键绑定与 Skill 生成。
- **混合检索与整理**：结合全文、向量与重排的本地混合检索，配合辅助模型判断与定期整理流程。
- **Web 管理界面**：内置仪表盘、检索追踪、记忆浏览、提示词编辑与可视化配置页。
- **备份与多机同步**：支持本地自动备份、WebDAV 远端备份以及基于 Git 的多机记忆同步。
- **三平台预编译包**：提供 Windows（`win-x64`）、macOS（`osx-arm64`）与 Linux（`linux-x64`）自包含发行包，附带 SHA-256 校验与构建来源证明。
