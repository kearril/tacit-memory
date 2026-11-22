---
title: 任务调度引擎从轮询改为事件驱动
aliases: [调度器改造, SyncScheduler 选型, 任务编排定时机制]
dates: { first: 2026-08-12, last: 2026-09-20 }
related: [my-app/frontend-build-oom, my-app/cron-persistence-work, _topics/sqlite-wal-tuning]
type: decision
---

## 背景与方案
原版本每 5 秒轮询一次任务队列，空闲时也会持续唤醒数据库并造成最高数秒的派发延迟。新实现引入基于 `System.Threading.Channels` 的 `SyncScheduler`，挂在 `BackgroundService` 下按事件唤醒工作协程；通过配置项 `MaxConcurrency` 限制并发执行数，并在收到 `SIGTERM` 时先停止接新任务、等待在途作业优雅排空。

## 权衡与取舍
调研阶段对比过 Quartz.NET 与 Hangfire，两者都需要维护额外的多表元数据与后台心跳线程，对单节点嵌入式部署偏重，且排查锁竞争成本较高。因此一期选择自研轻量内存通道调度，聚焦低延迟与零外部依赖。

## Overturned
纯内存计时器仅适合即时与一次性作业。实测宿主机休眠或进程重启后，周期性 Cron 计划会出现时间漂移与漏跑。后续由 `my-app/cron-persistence-work` 补充数据库持久化触发时间与补偿策略，本页中的周期调度内存方案已废止，仅保留 `SyncScheduler` 负责实时派发。
