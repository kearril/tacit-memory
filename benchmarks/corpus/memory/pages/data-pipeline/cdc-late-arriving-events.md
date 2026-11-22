---
title: 跨天迟到 CDC 变更事件的脏分区重物化策略
aliases: [跨天迟到数据处理, CDC 回溯重物化, 脏分区刷新方案]
dates: { first: 2026-09-25, last: 2026-10-21 }
related: [data-pipeline/incremental-watermark, data-pipeline/dag-orchestrator-migration, data-pipeline/dq-validation-rules]
type: decision
---

## 问题背景
常规增量作业的 6 小时回溯窗口可以覆盖绝大多数移动端弱网延迟，但业务库通过 CDC 同步过来的历史订单状态订正和退款记录经常晚于原始创建日期 2 至 5 天到达。若直接忽略会导致财务对账口径失真，若每次都全量重算过去 7 天又会造成计算资源浪费。

## 脏分区标记与重物化
摄取层解析 CDC 变更记录的业务日期 `biz_date`，凡是落在已封板历史分区中的记录先按 `(biz_date, event_uuid)` 写入暂存补丁表，并向元数据库 `dirty_partitions` 表插入待刷新分区记录。编排器每日汇总阶段自动读取未处理的 `dirty_partitions`，仅针对受影响的历史分区执行局部 `MERGE` 与下游宽表重物化，完成后标记为 `clean`。

## 边界与约束
自动重物化窗口上限设为 14 天，避免异常上游回灌数月前数据瞬间拖垮夜间队列；超过 14 天的历史订正转入人工审批回填通道，通过编排器显式指定日期切片执行。
