---
title: 早期 ETL 调度采用宿主机 crontab 搭配 shell 串联
aliases: [crontab 定时跑批, 早期 ETL 触发脚本, shell 串行编排]
dates: { first: 2026-07-10, last: 2026-07-22 }
related: [data-pipeline/dag-orchestrator-migration, data-pipeline/duckdb-parquet-engine]
type: decision
---

## 早期方案设计
流水线刚起步时只有抽取、清洗、日汇总三个线性步骤，为了尽快跑通产出报表，直接在 `build-node` 宿主机配置系统 `crontab` 每日 UTC 01:00 触发 `run_nightly.sh`。脚本按顺序串联 Python 作业，并通过 `flock -n /var/lock/etl-nightly.lock` 防止前一日异常超时与次日任务重入。

## 局限与暴露问题
随着下游派生宽表与数据质量校验环节增加，线性 shell 脚本无法表达分支并行依赖，中段步骤失败后只能手动注释前面命令从头重跑。八月底补跑过去三天历史分区时，人工传参疏漏导致依赖顺序错乱，临时脚本把未校验完的旧分区覆盖进了生产汇总目录。

## Overturned
九月中旬已由 `data-pipeline/dag-orchestrator-migration` 全面接管调度与回填，宿主机 `crontab` 入口与 `run_nightly.sh` 已彻底停用下线。本页方案仅保留作历史演进参考，不再允许通过直接编辑宿主机 crontab 新增数据流水线任务。
