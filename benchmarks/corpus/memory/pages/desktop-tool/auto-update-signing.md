---
title: 桌面端自动更新采用 Minisign 签名与延迟安装
aliases: [自动升级方案, 更新包验签机制, Tauri Updater 配置]
dates: { first: 2026-08-20, last: 2026-10-05 }
related: [desktop-tool/global-hotkey-wayland, _topics/ci-env-bootstrap]
type: decision
---

## 签名与发布链路
发布流水线使用 `TAURI_SIGNING_PRIVATE_KEY`（Minisign Ed25519 私钥）对各平台安装包生成 `.sig` 签名文件，并生成 `latest.json` 清单随 GitHub Releases 一并发布；对应公钥固化在 `tauri.conf.json` 中供客户端离线校验。

## 更新体验策略
仅在非计费网络下于后台静默拉取增量包并完成验签，等到用户下次主动关闭软件时再静默替换二进制文件，不在编辑中途强行弹窗或强制重载，避免打断当前工作流。
