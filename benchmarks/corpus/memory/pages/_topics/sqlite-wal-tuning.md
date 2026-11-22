---
title: SQLite WAL 模式并发调优与文件膨胀治理
aliases: [SQLite 性能参数, WAL 文件过大清理, 嵌入式库并发配置]
dates: { first: 2026-08-08, last: 2026-10-12 }
related: [my-app/cron-persistence-work, desktop-tool/tauri-ipc-locking]
type: entity
---

## 推荐连接参数
每个数据库连接打开后统一执行初始化语句：`PRAGMA journal_mode=WAL;`、`PRAGMA synchronous=NORMAL;`、`PRAGMA busy_timeout=5000;`、`PRAGMA foreign_keys=ON;` 与 `PRAGMA cache_size=-32000;`（约 32MB 页缓存），兼顾并发读吞吐与掉电安全。

## WAL 膨胀与截断
长时间未关闭的只读事务或忘记释放的查询游标会阻塞自动 checkpoint，导致 `-wal` 文件持续膨胀到数百 MB。除及时释放读事务外，需在空闲维护窗口周期执行 `PRAGMA wal_checkpoint(TRUNCATE);`（对应原生 C 接口中的 `WAL_CHECKPOINT_TRUNCATE`），将已提交页刷回主库并把日志文件截断归零。
