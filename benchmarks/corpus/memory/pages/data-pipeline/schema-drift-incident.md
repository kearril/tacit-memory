---
title: 上游字段类型变更引发 Parquet 合并报错
aliases: [字段漂移事故, ETL 类型冲突, DuckDB Binder Error]
dates: { first: 2026-10-09, last: 2026-10-10 }
related: [data-pipeline/duckdb-parquet-engine, data-pipeline/dq-validation-rules]
type: incident
---

## 事故经过
夜间定时汇总作业中途失败，日志报 `Binder Error: Cannot cast string 'N/A' to DOUBLE`。排查发现上游遥测接口在未通知的情况下将 `latency_ms` 字段从整数改为了可空字符串，且超时样本直接返回字面量 `'N/A'`。

## 止损与修复
在进入主分区前增加了基于 Pandera 的显式 Schema 契约校验层，仅在校验通过后启用 `union_by_name=true` 合并历史分区；格式异常记录隔离写入 `deadletter/dt=2026-10-09/`，可解析的数字字符串统一用 `TRY_CAST` 转为浮点型。

## Lesson
严禁未经契约校验就直接将外部 JSON 落盘进核心分区；所有外部数据源接入必须先过显式 Schema 检查，异常行分流到死信目录并触发告警，既不能静默丢数也不能让单批脏数据拖垮整晚汇总。
