---
title: 控制台权限模型与操作审计设计
aliases: [RBAC 权限体系, 控制台角色与审计日志, 鉴权与令牌架构]
dates: { first: 2026-08-22, last: 2026-10-08 }
related: [ops-infra/reverse-proxy-tls, my-app/cron-persistence-work]
type: entity
---

## 角色与权限矩阵
系统内置 `Viewer`、`Operator`、`Admin` 三类角色，按资源作用域拆分为 `jobs:read`、`jobs:trigger` 与 `system:config`。只读成员仅可查看作业状态与日志，操作员可手动触发与重试任务，管理员才允许修改全局配置和轮换密钥。

## 会话与令牌生命周期
短生命周期 Access Token 有效期 15 分钟，采用 HMAC-SHA256 签名；Refresh Token 放入 `HttpOnly`、`SameSite=Strict` 的安全 Cookie，有效期 7 天。每次刷新都会签发新令牌并作废旧令牌，若检测到已作废令牌被重放则立即撤销该会话族。

## 密钥轮换流程
签名验证支持双密钥平滑过渡窗口，配置中通过 `signing_keys` 数组按 `kid` 索引。主签名密钥每 90 天轮换一次，旧密钥在切换后保留 24 小时用于校验仍在有效期内的历史令牌，窗口结束后自动下线。

## 审计日志落盘格式
关键写操作以追加方式写入 `logs/audit-YYYY-MM.jsonl`，每行一条结构化记录，固定包含 `actor_id`、`action`、`target_id`、`result`、`client_ip` 与 `trace_id` 字段，便于后续按链路追踪或导入离线分析。

## 限流与防暴力破解
登录端点 `/api/auth/login` 按来源 IP 启用滑动窗口限流，每 5 分钟最多允许 10 次失败尝试。触发阈值后接口返回 `429 Too Many Requests` 并附带 `Retry-After` 响应头，防止字典撞库与凭据爆破。

## 外部集成令牌
面向脚本与 CI 调用的个人访问令牌统一带有 `pat_live_` 前缀，生成时仅展示一次，服务端数据库只保存 SHA-256 摘要值。令牌创建时必须绑定最小权限范围（如仅只读或仅允许触发指定作业），不支持无限制通配。
