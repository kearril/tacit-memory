---
title: 数据流水线迁至轻量 DAG 编排器并支持分区级回填
aliases: [现行 ETL 编排方案, DAG 依赖调度与回填, 替代 crontab 的流水线编排]
dates: { first: 2026-09-15, last: 2026-09-28 }
related: [data-pipeline/crontab-shell-runner, data-pipeline/incremental-watermark, data-pipeline/parquet-small-files-compaction, data-pipeline/cdc-late-arriving-events]
type: decision
---

## 现行编排架构
正式下线早期宿主机 `crontab` 与 shell 串行脚本，在 `build-node` 部署基于 Python 的轻量 DAG 编排器（资产声明式依赖图 + SQLite 元数据状态库），将抽取、Schema 契约校验、Parquet Compaction、指标聚合与宽表物化建模为显式有向无环图。上游节点校验失败时自动阻断下游执行，保留节点级日志与单步断点续跑能力。

## 分区级回填与幂等约束
编排器以 UTC 日期分区 `dt=YYYY-MM-DD` 为一等调度单元，支持在界面与命令行按 `--partition-range 2026-09-01..2026-09-07` 并发发起历史回填，并限制最大并发回填数为 2 以保护磁盘 I/O。每个资产节点强制遵循先写临时目录再原子替换分区的幂等约定，保证任意日期切片重跑均可安全覆盖且不污染其他日期。

## 权衡与取舍
调研阶段排除了重量级集群编排平台（如完整部署 Airflow + Celery + 独立消息队列），因当前数据规模集中在单台 `build-node` 运行，轻量单进程编排器既能提供依赖拓扑、可视化状态与分区回填，又能把常驻内存开销控制在 300MB 以内。
