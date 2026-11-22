---
title: 早期桌面端在本地 JSON 配置文件明文保存同步令牌
aliases: [旧版明文令牌存储, config.json 凭证方案, 桌面端早期鉴权配置]
dates: { first: 2026-07-14, last: 2026-07-20 }
related: [desktop-tool/os-keychain-storage, desktop-tool/auto-update-signing]
type: decision
---

## 早期实现方式
桌面端初版开发周期紧张，为快速打通多端后台同步功能，直接将同步服务刷新令牌与用户配置一同序列化存放在用户目录的 `config.json` 中，仅在 Unix 平台写入后调用 `chmod 600` 限制同机其他非特权用户读取。

## 安全隐患与过渡方式
安全复审指出明文配置文件极易被用户误打包进 dotfiles 同步仓库、云盘备份或排障日志附带外泄。九月切换至系统钥匙串期间，新版本客户端启动时自动检测 `config.json` 中是否残留旧令牌字段：若存在则迁移写入系统安全存储、校验读回一致后立即从 JSON 中擦除该字段并覆写落盘。

## Overturned
九月中旬已由 `desktop-tool/os-keychain-storage` 正式取代。客户端现行版本禁止再向 `config.json` 写入任何认证令牌或密钥，旧版明文迁移钩子计划在稳定运行两个大版本后清理。
