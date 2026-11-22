---
title: 双实例扩容引发缓存击穿与配置状态不一致事故
aliases: [缓存雪崩事故, 双副本缓存脏读, cache stampede 排查]
dates: { first: 2026-08-19, last: 2026-08-21 }
related: [my-app/in-memory-cache-decision, my-app/valkey-shared-cache-decision, ops-infra/postgres-slow-query]
type: incident
---

## 事故现象
`my-app` 扩容为双副本后的首个早高峰，管理员调整权限策略后，约一半请求仍持续命中旧权限长达 5 分钟；随后在整点热点配置缓存到期瞬间，数百个并发请求同时穿透到 PostgreSQL，导致数据库 CPU 飙升至 98%、控制台接口大面积超时。

## 根因分析
一期采用的进程内 `IMemoryCache` 在双副本间彼此隔离，配置变更仅清除了处理写请求的那台节点缓存，另一节点继续返回旧数据。同时缓存缺失回源链路没有并发合并保护，热门键按固定 5 分钟绝对 TTL 到期后触发典型的缓存击穿（Cache Stampede），瞬时放大了数据库回源查询压力。

## 止损与后续推进
紧急将副本数回退为单实例并给热点配置键补加 20% 随机 TTL 抖动稳住流量；复盘后正式立项废弃进程内共享缓存，启动独立共享缓存选型，最终在九月迁至 Valkey 并引入 singleflight 并发回源合并（见 `my-app/valkey-shared-cache-decision`）。

## Lesson
进程内缓存一旦跨越单实例边界必然引发多副本脏读；高并发热点键必须同时具备统一失效通道、TTL 随机抖动以及回源并发折叠（singleflight），不能把数据库暴露在缓存到期瞬间的突发流量下。
