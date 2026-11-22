---
title: 桌面端认证令牌接入系统钥匙串并保留无头降级机制
aliases: [系统钥匙串存储, OS Keychain 改造, 现行桌面端密钥存储]
dates: { first: 2026-09-08, last: 2026-09-22 }
related: [desktop-tool/plaintext-config-migration, desktop-tool/tauri-ipc-locking]
type: decision
---

## 跨平台安全存储方案
废弃早期在 `config.json` 中明文存放同步令牌的做法，通过 Rust `keyring` crate 统一对接三大桌面平台原生安全存储：macOS Keychain、Windows Credential Manager 以及 Linux 桌面下的 Secret Service（`org.freedesktop.secrets`），条目服务名固定为 `desktop-tool-sync`，彻底将凭据与普通可读配置解耦。

## 兼容迁移与无头环境降级
首次升级启动时若发现旧版 `config.json` 残留明文令牌，自动导入当前平台钥匙串并在确认写入成功后擦除原 JSON 字段。在极简 Linux 窗口管理器或未运行 `gnome-keyring` / `kwallet` 的无头环境中，客户端不直接崩溃，而是降级为由 Argon2id 派生密钥加密的本地 `credentials.enc` 文件并在状态栏显示解锁提示，同时支持通过环境变量由外部凭据助手注入。

## 实现细节
钥匙串读写在部分 Linux 发行版上可能触发桌面解锁弹窗而阻塞调用线程，因此所有凭据访问均放在 `tokio::task::spawn_blocking` 中执行，并设置 10 秒超时防止拖死主界面初始化。
