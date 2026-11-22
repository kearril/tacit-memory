---
title: PostgreSQL WAL 归档与时间点恢复改造
aliases: [PG PITR 归档, WAL 连续备份, 数据库任意时间点回档]
dates: { first: 2026-09-10, last: 2026-10-18 }
related: [ops-infra/postgres-slow-query, ops-infra/cloud-snapshot-backup]
type: work
---

## 已落地架构
在 `build-node` 的 PostgreSQL 主库上启用 `archive_mode = on` 与 `wal_level = replica`，通过 `pgBackRest` 执行 `archive-push` 将每段 16MB WAL 日志持续推送至对象存储独立前缀，每日 UTC 02:30 制作一次全量或差异基础备份，将误删表或错误批量更新的恢复粒度从天级缩短到秒级。

## 时间点演练流程
恢复演练在隔离容器中进行：配置 `restore_command` 调用 `pgbackrest archive-get`，并在 `postgresql.auto.conf` 中指定 `recovery_target_time` 与 `recovery_target_action = 'promote'`，同时放置 `recovery.signal` 启动只读校验，确认目标时刻的数据行完整后再切出临时查询实例。

## Leftovers
- `archive_command` 连续失败超过 5 分钟时的告警规则尚未接入值班通知渠道，目前仅依赖监控面板手动巡检 `pg_stat_archiver.failed_count`。
- 尚缺一条月度自动化演练脚本，把恢复校验实例的拉起与表行数比对纳入定时任务。
