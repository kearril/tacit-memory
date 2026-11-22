---
title: Restic 仓库独占锁残留导致定时 prune 失败
aliases: [备份锁残留事故, Restic unlock 排查, 快照修剪报错]
dates: { first: 2026-09-22, last: 2026-09-23 }
related: [ops-infra/cloud-snapshot-backup, ops-infra/container-compose-layout]
type: incident
---

## 故障现象
连续两晚的备份归档与修剪作业在 `build-node` 上报错退出，systemd 日志输出 `unable to create lock in backend: repository is already locked exclusively by PID 18422 on build-node`，导致新快照未能写入且旧索引无法回收。

## 根因定位
回溯前一日凌晨的系统内核日志，发现当次 `restic prune` 在重组大包文件阶段触及 cgroup 内存上限被 OOM Killer 强行终止，进程未能执行清理钩子，在对象存储 `locks/` 目录下遗留了一个尚未超时的独占锁文件；次日定时任务再次尝试获取锁时即被拒绝。

## 处置与加固
核实宿主机已无任何活跃 `restic` 进程后，执行 `restic unlock --remove-all` 清理陈旧独占锁，随后补跑 `restic check` 确认仓库元数据完好。为降低单次修剪的内存与重打包压力，在定时脚本中为 `prune` 增加 `--max-unused 5%` 与 `--max-repack-size 2G` 参数，并将备份服务单元的 `MemoryMax` 调高至 2G。

## Lesson
对象存储备份的 `prune` 操作应设置明确的 `--max-repack-size` 上限并与普通 `backup` 任务错峰调度；同时为备份脚本补充启动前 stale lock 检查与失败告警，避免独占锁静默阻塞多日备份。
