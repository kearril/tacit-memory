---
title: 容器编排目录结构与网络隔离规范
aliases: [Docker Compose 布局, 服务栈目录约定, 容器网段规划]
dates: { first: 2026-08-03, last: 2026-10-02 }
related: [ops-infra/reverse-proxy-tls, ops-infra/cloud-snapshot-backup]
type: entity
---

## 目录划分
所有常驻服务统一按 `/srv/stacks/<service>/compose.yaml` 组织，配套 `.env` 文件强制 `chmod 600`。持久数据全部显式绑定到 `/srv/data/<service>/`，避免使用 Docker 自动生成的随机卷，便于统一归档迁移与容量排查。

## 网络隔离
反向代理通过外部桥接网 `proxy-net` 只对接需要对外暴露的 Web 入口容器；数据库与缓存服务一律放在声明了 `internal: true` 的专用内部网络中，默认不向宿主机映射监听端口。

## 健康检查与日志轮转
每个服务都配置 `logging.driver: json-file` 并限制 `max-size: "20m"`、`max-file: "3"`，防止调试期日志写爆根分区；同时声明 `healthcheck` 探针，使依赖容器和滚动重启能够等待就绪状态。
