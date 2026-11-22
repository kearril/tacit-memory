---
title: 离线聚合引擎选用 DuckDB 搭配 Parquet 分区
aliases: [ETL 存储选型, DuckDB 替代 Pandas, 列式分区方案]
dates: { first: 2026-08-15, last: 2026-09-10 }
related: [data-pipeline/incremental-watermark, data-pipeline/schema-drift-incident]
type: decision
---

## 选型依据
原有 Python 脚本使用 Pandas 加载 30 天全量明细做分组聚合时，内存峰值超过 14GB，经常在 `build-node` 上触发内核 OOM Killer。改用 DuckDB 对 Hive 分区目录 `dt=YYYY-MM-DD/part-*.parquet` 执行流式 SQL 聚合后，同样数据量耗时 18 秒且内存占用稳定在 1.5GB 以下。

## 目录与压缩约定
导出文件统一采用 ZSTD 压缩等级 3，单个 row group 目标大小控制在 `128MB`，目录按 UTC 自然日 `dt` 分区，避免产生过多几 KB 级别的小文件拖慢元数据扫描。

## 注意事项
本地 `.duckdb` 目录文件同一时刻只允许单进程写入；下游只读分析任务应直接读取 Parquet 分区文件，或在连接本地库时显式传入 `read_only=True` 防止文件锁冲突。
