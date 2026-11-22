# Changelog

All user-visible changes are recorded here starting from `1.0.0`. When a `v*` tag is pushed, the release workflow extracts the matching version section as the GitHub Release notes.

Format: each release has a `## <version> - <date>` heading (without the `v` prefix), with English above and Chinese below. Upcoming changes live under `## Unreleased`.

## Unreleased

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
