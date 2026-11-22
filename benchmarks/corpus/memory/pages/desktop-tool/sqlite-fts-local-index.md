---
title: 桌面端本地 SQLite FTS5 全文索引与防抖写入设计
aliases: [桌面端本地搜索索引, SQLite FTS5 分词, 索引防抖批量提交]
dates: { first: 2026-08-25, last: 2026-10-17 }
related: [_topics/sqlite-wal-tuning, desktop-tool/tauri-ipc-locking]
type: entity
---

## 索引表结构与分词选择
桌面端快速搜索基于内置 SQLite FTS5 虚表 `notes_fts`，采用外部内容表（`content='notes'`）减少正文重复存储。为兼顾中文子串模糊匹配与英文代码标识符精确检索，主索引使用 `trigram` 分词器承载中文及任意片段检索，并辅以独立标题前缀列支持快捷键唤起时的毫秒级即时联想。

## 防抖写入与连接隔离
编辑区高频击键不会每次都触发数据库写入。前端输入事件经 300ms debounce 聚合后通过单次 IPC 命令下发，后端由独立写入协程在单个事务内批量更新主表与 `notes_fts` 索引，避免频繁 fsync 引发卡顿；搜索查询始终复用只读连接池，与后台索引重建互不阻塞。

## 索引重建与一致性校验
应用启动时比对索引 schema 版本号与文档总校验和，若检测到异常断电或版本升级则触发后台异步 `INSERT INTO notes_fts(notes_fts) VALUES('rebuild')`，重建期间搜索栏降级展示轻量提示而不锁死主界面。
