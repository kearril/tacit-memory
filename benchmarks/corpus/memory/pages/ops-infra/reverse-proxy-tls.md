---
title: 反向代理内部证书链缺失导致客户端校验失败
aliases: [Caddy 证书链报错, TLS 握手失败, 内网 HTTPS 证书异常]
dates: { first: 2026-09-11, last: 2026-09-13 }
related: [ops-infra/container-compose-layout, my-app/rbac-audit-architecture]
type: incident
---

## 现象
命令行工具与新开 Chromium 实例访问测试网关 `https://staging.example.internal` 时均抛出 `ERR_CERT_AUTHORITY_INVALID` 拒绝建立连接，仅带 `-k` 跳过校验的 `curl` 请求可以通。

## 根因分析
切换为自定义证书配置 `tls /etc/certs/leaf.pem /etc/certs/leaf.key` 后，Caddy 只下发了终端叶证书而遗漏中间 CA 证书。`workstation` 上的常用浏览器因本地 AIA 缓存掩盖了问题，导致干净容器和 CLI 客户端第一时间报错。

## 修复步骤
按终端证书在前、中间证书在后的顺序拼接生成 `fullchain.pem`，更新 Caddyfile 指向完整链并平滑重载；随后在无缓存的临时容器中执行 `openssl s_client -showcerts -connect staging.example.internal:443` 确认返回完整两级证书且校验返回码为 0。

## Lesson
手动指定 TLS 证书文件时必须交付包含中间 CA 的完整链 `fullchain.pem`，验收阶段务必使用干净容器里的 `openssl s_client` 验证信任链，不能仅凭日常浏览器能打开就判定正常。
