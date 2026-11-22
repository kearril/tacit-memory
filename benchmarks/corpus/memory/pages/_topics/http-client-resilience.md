---
title: 跨服务 HTTP 客户端超时、连接复用与熔断降级约定
aliases: [HTTP 客户端韧性配置, SocketsHttpHandler 调优, 出站请求熔断规范]
dates: { first: 2026-08-14, last: 2026-10-07 }
related: [my-app/webhook-idempotency-outbox, my-app/async-retry-dead-letter]
type: entity
---

## 连接池与超时分层
禁止在每次请求中临时创建并销毁 `HttpClient` 导致端口耗尽（TIME_WAIT 堆积）。.NET 服务统一经 `IHttpClientFactory` 复用底层 `SocketsHttpHandler`，并将 `PooledConnectionLifetime` 设为 5 分钟以感知内部 DNS 变更。超时拆分为三层：`ConnectTimeout` 为 3 秒、单次尝试响应超时 10 秒、含重试的总调用预算 30 秒，防止上游黑洞挂死工作线程。

## 重试边界与熔断保护
透明自动重试仅允许用于天然幂等请求（`GET`/`HEAD`）或携带了 `Idempotency-Key` 的出站通知，且只针对网络异常、`429` 和 `5xx` 触发，最多重试 2 次并遵守响应头 `Retry-After`。当目标上游在 30 秒滑动窗口内失败率超过 50%（且样本数不少于 10 次）时，熔断器断开 20 秒快速失败，随后放行单次探测请求验证恢复状态。
