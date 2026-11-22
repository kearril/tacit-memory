---
title: 增量水位线与回补任务改造
aliases: [ETL 断点续跑, watermark 进度追踪, 迟到数据重算]
dates: { first: 2026-09-22, last: 2026-10-24 }
related: [data-pipeline/duckdb-parquet-engine, ops-infra/postgres-slow-query]
type: work
---

## 进度状态表设计
在元数据库新增 `pipeline_cursors` 表，记录 `job_name`、`last_committed_ts` 与 `batch_checksum`；每批次写完目标分区文件后先通过临时目录做原子重命名，再在同一事务内推进游标，确保下游永远不会读到写了一半的残缺文件。

## 迟到窗口处理
移动端埋点常有弱网延迟上报，批处理作业引入 6 小时回溯窗口，按 `event_uuid` 执行 `MERGE` 去重写入，既吸收延迟到达的记录又避免重复计入日活指标。

## Leftovers
- 命令行参数 `--reprocess-range` 目前仅支持按整日粒度重跑，尚未支持按小时切片局部重算。
- 连续重试超限后落入死信清单的批次明细，还未接入每日告警邮件汇总。
