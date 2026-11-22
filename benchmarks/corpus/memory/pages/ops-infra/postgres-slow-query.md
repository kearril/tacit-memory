---
title: PostgreSQL 连接池耗尽与慢查询积压
aliases: [PG 慢 SQL 排查, 数据库连接打满, 数据库响应雪崩]
dates: { first: 2026-10-03, last: 2026-10-06 }
related: [ops-infra/cloud-snapshot-backup, data-pipeline/incremental-watermark]
type: incident
---

## 现象
早高峰期间接口 P99 延迟从 40ms 飙升到 8.5s，应用日志大量抛出 `NpgsqlException: The connection pool has been exhausted`，健康检查随之超时。

## 诊断过程
开启 `pg_stat_statements` 按 `total_exec_time` 倒序排查，发现 `event_logs` 表已有 1200 万行，高频按租户与时间倒序查询因缺少联合索引触发全表顺序扫描。同时发现 `work_mem` 仍保持默认的 `4MB`，排序阶段频繁落盘执行外部归并，把连接长时间占满。

## 处理措施
使用 `CREATE INDEX CONCURRENTLY idx_event_logs_tenant_created ON event_logs (tenant_id, created_at DESC)` 在线建索引避免锁写；将实例 `work_mem` 调高至 `32MB`，并为应用数据库角色配置 `statement_timeout = '15s'` 切断失控长查询。

## Lesson
面向时间范围检索的大表上线前必须核对联合索引与 `EXPLAIN (ANALYZE, BUFFERS)` 执行计划；生产数据库默认启用 `pg_stat_statements` 并给应用账号设 `statement_timeout`，避免个别慢 SQL 拖垮整个连接池。
