---
title: Parquet 小文件泛滥导致 DuckDB 聚合查询性能骤降
aliases: [Parquet 小文件治理, 列式存储碎片合并, DuckDB 扫描变慢]
dates: { first: 2026-09-18, last: 2026-09-20 }
related: [data-pipeline/duckdb-parquet-engine, data-pipeline/dag-orchestrator-migration]
type: incident
---

## 故障现象
原本 18 秒完成的 30 天指标汇总任务在两周内逐步退化到 6 分钟以上，且 `build-node` 在读取分区目录阶段出现大量文件描述符打开开销与高额 I/O 等待。

## 根因定位
新接入的近实时微批落盘任务每 5 分钟向当日分区目录写入一批 Parquet 文件，单日产生近 300 个 40KB~80KB 的碎片文件，30 天累计接近 9000 个小文件。DuckDB 执行目录扫描时不得不逐个解析海量文件页脚元数据，且微小的 row group 导致列压缩率显著下降、无法利用统计信息跳过无关块。

## 治理与合并方案
新增每日 UTC 01:15 运行的分区压缩合并任务（Compaction）：将前一日分区内的碎片文件按 `event_ts` 排序后重写为目标 `128MB` 的 ZSTD 分片，写入临时目录校验总行数一致后执行原子替换并清理碎片；同时将微批落盘间隔放宽至 15 分钟以减少原始文件数。治理后 30 天聚合耗时回落至 15 秒。

## Lesson
高频微批写入列式分区必须配套定时 Compaction 合并机制；上线新的写入通道时除了监控单批吞吐，还要给每日分区文件数量与平均文件大小设立告警阈值。
