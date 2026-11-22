---
title: 数据质量校验规则与空值阈值基线
aliases: [DQ 检查清单, 指标口径监控, 异常数据拦截标准]
dates: { first: 2026-08-29, last: 2026-10-11 }
related: [data-pipeline/schema-drift-incident, data-pipeline/incremental-watermark]
type: entity
---

## 必检规则
每个日分区发布前执行三项硬性检查：`event_uuid` 主键无重复、事件时间戳不得超前当前系统时间 5 分钟以上、`region_code` 必须落在预定义地区白名单枚举内，任一项不通过即阻断发布。

## 波动告警阈值
当日总行数较过去 7 天滚动中位数跌落超过 30% 时触发 P1 级别告警并暂停覆盖下游宽表；`user_segment` 字段空值比例超过 2% 时发送预警通知但不阻断主流程。
