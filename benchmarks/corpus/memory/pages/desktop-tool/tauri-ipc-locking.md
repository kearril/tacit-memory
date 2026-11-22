---
title: Tauri 命令层读写锁嵌套导致界面假死
aliases: [桌面端卡死排查, Rust 死锁事故, IPC 线程阻塞]
dates: { first: 2026-09-02, last: 2026-09-06 }
related: [desktop-tool/global-hotkey-wayland, _topics/sqlite-wal-tuning]
type: incident
---

## 复现现象
后台自动刷新进行期间，用户在界面点击 "Sync Now" 后整个主窗口彻底卡死无响应，开发者工具里所有 IPC invoke Promise 一直处于 pending 状态。

## 根因定位
追踪调用栈发现一个异步命令在跨越 `.await` 点期间持续持有 `tokio::sync::RwLock` 写锁，而被等待的内部辅助函数又尝试对同一个 `AppState` 获取读锁，形成典型重入死锁；同时另一个同步命令在主线程调用 `blocking_read()`，直接把事件循环一并锁死。

## 修复方式
将锁作用域收窄到纯内存状态读取，拷贝出轻量快照后立刻释放锁守卫再执行 `.await`；把粗粒度 `AppState` 拆分为按子模块隔离的独立 `Arc<Mutex<...>>`，并将重磁盘读写统一移入 `tokio::task::spawn_blocking`。

## Lesson
Rust 异步代码中严禁跨 `.await` 持有 `tokio::sync::RwLock` 写锁；状态锁只保护短暂的内存拷贝，IPC 命令中涉及磁盘或阻塞计算的操作必须交给 `spawn_blocking` 线程池执行。
