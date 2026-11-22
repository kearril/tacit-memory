---
title: 云端快照定时备份方案
aliases: [异地冷备策略, Restic 定时归档, 对象存储快照同步]
dates: { first: 2026-08-05, last: 2026-09-28 }
related: [ops-infra/container-compose-layout, ops-infra/postgres-slow-query]
type: decision
---

## 保留策略与调度
在 `build-node` 上通过 systemd timer 每日 UTC 03:20 触发归档任务，修剪参数固定为 `--keep-daily 7 --keep-weekly 4 --keep-monthly 6`。仓库口令通过环境变量 `RESTIC_PASSWORD_FILE` 指向权限 600 的独立文件读取，不写入脚本正文。

## 增量传输与校验步骤
执行前先对数据库做一致性只读导出，再调用 `restic` 开启 `--pack-size 32` 与 `--limit-upload 4096` 限速推送到兼容 S3 的远端桶；每周日凌晨追加执行 `check --read-data-subset=5%` 抽样核验数据块完整性，发现校验异常时立刻向告警 Webhook 发送通知。

## 恢复演练要点
每季度在 `dev-box` 上新建隔离临时卷做一次完整拉取与解压验证，确认解密口令可用、文件属主与权限位一致，并核对最新一次导出库可正常启动查询。
