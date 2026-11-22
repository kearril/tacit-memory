---
title: 异步任务指数退避重试与死信队列治理
aliases: [作业重试策略, 死信队列重放, exponential backoff 改造]
dates: { first: 2026-09-14, last: 2026-10-19 }
related: [my-app/job-scheduler-migration, my-app/cron-persistence-work, my-app/webhook-idempotency-outbox, _topics/http-client-resilience]
type: work
---

## 退避策略与错误分级
将作业执行失败拆分为可重试瞬态错误（网络超时、上游 `502`/`503`/`429`）与不可重试永久错误（参数校验失败、`400`/`403`）。瞬态错误按带抖动的指数退避（基础间隔 5 秒、倍率 2、全抖动随机偏移、单次上限 10 分钟）计算 `next_retry_utc`，最多重试 6 次，避免上游恢复瞬间被齐步重放流量再次击垮。

## 死信归档与人工重放
重试耗尽或命中永久错误的作业从活跃表移入 `dead_letter_jobs`，完整保留最后一次堆栈摘要、`retry_attempts` 计数与原始输入载荷；管理后台提供按批次筛选与单条重放端点 `/api/admin/dlq/{id}/requeue`，重放时会重置重试计数并记录操作人审计日志。

## Leftovers
- 死信队列按错误类型聚类批量重放功能尚未实现，目前只能逐条重放或通过运维脚本批量回灌。
- 超过 30 天未处理的死信记录尚缺自动冷归档与清理任务。
