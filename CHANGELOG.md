# Changelog

All user-visible changes are recorded here starting from `1.0.0`. When a `v*` tag is pushed, the release workflow extracts the matching version section as the GitHub Release notes.

Format: each release has a `## <version> - <date>` heading (without the `v` prefix), with English above and Chinese below. Upcoming changes live under `## Unreleased`.

## Unreleased

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

- **结构化 Markdown 记忆**：基于 `~/.tacit/memory` 纯文本 Git 仓库，分层维护画像、工作流、主题页与按月活动记录。
- **跨客户端 MCP 服务**：单个本地常驻 HTTP 服务支持多客户端并发读写，内置主流客户端一键绑定与 Skill 生成。
- **混合检索与整理**：结合全文、向量与重排的本地混合检索，配合辅助模型判断与定期整理流程。
- **Web 管理界面**：内置仪表盘、检索追踪、记忆浏览、提示词编辑与可视化配置页。
- **备份与多机同步**：支持本地自动备份、WebDAV 远端备份以及基于 Git 的多机记忆同步。
- **三平台预编译包**：提供 Windows（`win-x64`）、macOS（`osx-arm64`）与 Linux（`linux-x64`）自包含发行包，附带 SHA-256 校验与构建来源证明。
