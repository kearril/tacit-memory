---
title: 一期业务缓存采用进程内 IMemoryCache
aliases: [单机内存缓存选型, IMemoryCache 方案, 早期本地缓存决策]
dates: { first: 2026-07-05, last: 2026-07-18 }
related: [my-app/cache-stampede-incident, my-app/valkey-shared-cache-decision]
type: decision
---

## 选型背景与实现
项目一期仅在单台节点上部署一个 `my-app` 进程，为降低角色权限矩阵与作业模板的高频数据库查询延迟，直接使用 .NET 内置的 `IMemoryCache` 在进程内存中缓存热点数据，默认设置 5 分钟绝对过期与 128MB 容量上限，做到零外部组件依赖与亚毫秒级读取。

## 一期适用边界
方案明确以单实例运行为前提，后台配置变更时直接调用 `IMemoryCache.Remove` 清理本机缓存键，无需维护跨节点失效通道，也省去了额外运维独立缓存服务的成本。

## Overturned
八月下旬业务扩容为双实例部署后，进程内缓存暴露出严重的跨副本数据不一致与同时过期击穿问题（详见 `my-app/cache-stampede-incident`）。九月上旬已由 `my-app/valkey-shared-cache-decision` 正式替代，全局共享热点数据统一迁至 Valkey 实例；本页方案已废止，进程内缓存仅允许用于不可变静态元数据的极短时只读兜底。
