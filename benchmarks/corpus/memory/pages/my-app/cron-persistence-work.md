---
title: 定时触发器持久化与休眠补偿
aliases: [Cron 持久化改造, 定时任务防漏跑, misfire 补偿]
dates: { first: 2026-09-18, last: 2026-10-14 }
related: [my-app/job-scheduler-migration, _topics/sqlite-wal-tuning]
type: work
---

## 已完成改造
在本地数据库中新增 `scheduled_jobs` 表保存 `next_fire_utc` 与上次完成时间，启动和唤醒时对比当前 UTC 时间计算逾期计划。同时引入 `MisfirePolicy` 枚举支持 `Skip`（直接跳到下一周期）与 `FireOnce`（合并补跑一次），核心时间推进逻辑改用 `TimeProvider` 抽象并用 `FakeTimeProvider` 补齐单元测试。

## 验证结果
在 `dev-box` 上模拟宿主机休眠两小时后恢复，验证配置为 `FireOnce` 的每 15 分钟计划仅合并执行一次补跑，不会连续堆积触发，也不会出现重复派发。

## Leftovers
- Web 控制台界面尚未暴露 `MisfirePolicy` 下拉切换，目前只能通过修改 `appsettings.json` 调整。
- 跨夏令时切换边界（例如 `America/New_York` 跳小时与重复小时）缺少专项回归用例，下一迭代补齐。
