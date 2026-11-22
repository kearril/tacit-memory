## Workstream
### my-app
Background: 内部任务编排与 Web 控制台应用（.NET + React），负责作业触发、调度编排与审计管理。
Recent: 已完成事件驱动调度器与定时触发器持久化改造，补齐个人访问令牌范围校验。

### ops-infra
Background: 家用与测试集群容器运维、反向代理与备份体系，基于 Docker Compose、Caddy、PostgreSQL 与 Restic。
Recent: 已完成 PostgreSQL 慢查询索引优化、TLS 完整链修复及容器日志轮转上限治理。

### data-pipeline
Background: 定时 ETL 与增量指标汇总流水线（Python + DuckDB），处理每日分区落盘与数据质量校验。
Recent: 已落地 Parquet 分区汇总、游标原子推进以及上游字段漂移死信分流机制。

### desktop-tool
Background: 跨平台本地效率桌面工具（Rust + Tauri），提供快速唤起、本地索引与后台同步能力。
Recent: 已修复命令层异步读写锁死锁，跑通 GNOME 与 KDE 下的 Wayland 全局快捷键 Portal。
