---
title: 跨服务 JSON 结构化日志字段与链路追踪约定
aliases: [结构化日志规范, 统一 JSON 日志字段, 全仓日志格式标准]
dates: { first: 2026-08-02, last: 2026-09-30 }
related: [my-app/rbac-audit-architecture, ops-infra/container-compose-layout]
type: entity
---

## 必选字段与命名约定
所有服务端进程与批处理任务统一向 `stdout` 输出单行 JSON（NDJSON）结构化日志，禁止混杂多行自由文本堆栈。每条记录必须包含以下小写下划线键名：`ts`（UTC ISO-8601 毫秒时间戳）、`level`（`debug`/`info`/`warn`/`error`）、`service`、`env`、`msg` 与 `trace_id`；涉及异步作业或请求上下文时补充 `job_id`、`actor_id` 与 `duration_ms`。

## 链路串联与脱敏规则
入口网关为未携带追踪头的外部请求生成 32 位十六进制 `trace_id`，通过 `X-Trace-Id` 向下游服务、Webhook 出站及审计日志透传。日志序列化层默认启用字段黑名单过滤，凡键名匹配凭据、签名或 Cookie 字段的值统一替换为 `[REDACTED]`，严禁将原始请求头或完整授权载荷直接打印进日志流。

## 采集与保留配套
容器运行时按 `ops-infra/container-compose-layout` 的 `json-file` 轮转限制做本地防爆盘兜底，长期保留与审计检索则由业务侧独立的 JSONL 审计流及集中采集代理负责，二者通过 `trace_id` 精确关联。
