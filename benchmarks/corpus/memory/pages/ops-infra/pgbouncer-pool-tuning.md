---
title: PgBouncer 事务级连接池参数与预处理语句配置
aliases: [PgBouncer 池模式选型, 数据库连接复用调优, transaction pooling 配置]
dates: { first: 2026-09-05, last: 2026-10-04 }
related: [ops-infra/postgres-slow-query, ops-infra/container-compose-layout]
type: decision
---

## 池模式选择
在应用容器与 PostgreSQL 之间引入 PgBouncer 作为连接复用层，将 `pool_mode` 设为 `transaction` 而非默认的 `session`。多个应用副本在空闲事务间隙共享有限的后端数据库会话，将 `max_client_conn` 设为 600、单库 `default_pool_size` 控制在 25，避免短连接高峰耗尽 PostgreSQL `max_connections` 并带来显著进程上下文切换开销。

## 预处理语句与驱动兼容
PgBouncer 1.21 起在事务池模式下支持服务端预处理语句复用，需显式开启 `max_prepared_statements = 200`；同时禁止在应用层依赖跨事务存活的 `SET` 会话参数或 `LISTEN/NOTIFY` 通道。Npgsql 连接串保留 `Multiplexing=false` 并显式配置 `No Reset On Close=true`，避免归还连接时额外发送 `DISCARD ALL` 往返。

## 权衡与取舍
`session` 模式无需担心会话级临时表或顾问锁，但每个空闲客户端都会独占一条后端进程，扩容副本时容易打满服务端内存；`statement` 模式则无法支持显式多语句事务。对于需要长连接监听的独立任务进程，单独分配一条直连端口绕过事务池。
