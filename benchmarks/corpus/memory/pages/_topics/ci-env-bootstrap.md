---
title: 跨平台构建环境初始化脚本规范
aliases: [CI 环境引导脚本, 本地开发工具链配置, env.ps1 与 bootstrap.sh]
dates: { first: 2026-08-18, last: 2026-10-16 }
related: [my-app/frontend-build-oom, desktop-tool/auto-update-signing]
type: entity
---

## Windows 与 Unix 脚本约定
Windows 开发机通过 PowerShell 7 加载 `scripts/env.ps1` 在当前会话内固定 `DOTNET_ROOT`、`CARGO_HOME` 与 Node 工具路径，不污染系统全局 PATH；Linux 与 macOS 节点统一执行 `scripts/bootstrap.sh` 并在首行启用 `set -euo pipefail` 保证错误即时中断。

## 依赖锁定与缓存键
前端构建任务一律执行 `pnpm install --frozen-lockfile`，Rust 构建强制附加 `--locked` 参数禁止隐式升级锁文件；CI 缓存键按操作系统、CPU 架构以及 `pnpm-lock.yaml`、`Cargo.lock`、`Directory.Packages.props` 哈希联合生成。
