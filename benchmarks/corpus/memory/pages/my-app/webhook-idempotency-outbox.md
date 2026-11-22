---
title: Webhook 派发采用事务发件箱与幂等键防重放
aliases: [Transactional Outbox 方案, Webhook 幂等投递, 事件外发防丢设计]
dates: { first: 2026-09-02, last: 2026-10-09 }
related: [my-app/job-scheduler-migration, my-app/async-retry-dead-letter, _topics/http-client-resilience]
type: decision
---

## 事务发件箱落盘
业务状态更新与外部 Webhook 通知不再在同一次 HTTP 请求中直接串行调用。状态变更在同一数据库事务内额外向 `outbox_messages` 表写入待投递事件（包含 `event_id`、`payload_json`、`target_url` 与 `status`），由后台中继协程使用 `SELECT ... FOR UPDATE SKIP LOCKED` 批量认领并投递，消除数据库已提交但外部调用失败造成的状态撕裂。

## 幂等键与防重放校验
每次出站请求统一附加 `Idempotency-Key`（固定为 `event_id`）、`X-Webhook-Timestamp`（UTC 秒级时间戳）与基于共享签密计算的 `X-Webhook-Signature` 请求头。下游接收方按 `Idempotency-Key` 做 24 小时幂等去重，并拒绝时间戳偏差超过 300 秒的请求，既容忍至少一次投递产生的网络重发，又阻断截获报文后的重放攻击。

## 权衡与取舍
相比直接引入外部消息中间件（如 RabbitMQ），基于现有关系库实现 Outbox 无需额外运维一套队列集群，且天然享有与业务表的原子事务保证；当出站吞吐增长到单表轮询成为瓶颈时，再将中继层替换为逻辑日志监听投递。
