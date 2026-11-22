---
title: 线上共享缓存迁至 Valkey 并引入 singleflight 防击穿
aliases: [Valkey 共享缓存方案, 现行缓存架构, 分布式缓存与防击穿]
dates: { first: 2026-09-04, last: 2026-09-12 }
related: [my-app/in-memory-cache-decision, my-app/cache-stampede-incident, ops-infra/container-compose-layout]
supersedes: [my-app/in-memory-cache-decision]
type: decision
---

## 现行方案与选型依据
针对八月双实例缓存击穿与脏读事故，正式废弃一期单机 `IMemoryCache` 方案，在 `internal: true` 内网中部署单节点持久化 Valkey 8 实例作为 `my-app` 现行共享缓存层。选择 Valkey 而非 Redis 是出于开放许可证考量且协议完全兼容 `StackExchange.Redis` 客户端，迁移零改造成本。

## 防击穿与一致性机制
业务读取统一封装在 `HybridCache` 风格的 `SingleflightCache` 中：同一节点内针对相同键的并发未命中请求通过 `SemaphoreSlim` 键级锁折叠为单次回源，跨节点辅以短时分布式回源租约与基础 TTL 叠加 15% 随机抖动，彻底消除整点缓存雪崩。配置写入提交后主动向 Valkey 执行删除并发布 `cache:invalidate` 频道通知，保证多副本视图一致。

## 降级与容量策略
Valkey 配置 `maxmemory 512mb` 与 `allkeys-lru` 淘汰策略，开启 RDB 周期快照但不启用 AOF 以降低磁盘写放大。当缓存连接异常时客户端在 150ms 内快速失败并走限流回源保护，避免缓存抖动反向拖垮应用线程池。
