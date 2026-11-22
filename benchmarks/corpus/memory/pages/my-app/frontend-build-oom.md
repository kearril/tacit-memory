---
title: Web 控制台打包时 Node 进程内存溢出
aliases: [前端构建 OOM, React 打包爆内存, Vite 构建崩溃]
dates: { first: 2026-09-04, last: 2026-09-05 }
related: [my-app/job-scheduler-migration, _topics/ci-env-bootstrap]
type: incident
---

## 现象
在 4GB 内存的 `build-node` 容器中执行 `pnpm build` 时，打包阶段反复在 Rollup 代码分块与 TypeScript 检查并行阶段中止，日志报 `FATAL ERROR: Reached heap limit Allocation failed - JavaScript heap out of memory`。

## 排查与解决
对比本地与构建环境后确认两个直接诱因：一是未设置 `NODE_OPTIONS=--max-old-space-size=3072`，默认堆上限偏低；二是生产构建开启了 `sourcemap: true`，导致峰值堆占用几乎翻倍。修复方案包括显式传入上述环境变量、关闭生产完整 sourcemap、在流水线强制执行 `pnpm install --frozen-lockfile`，以及将 `tsc --noEmit` 与 Vite 打包拆分为先后串行步骤。

## Lesson
构建容器内存不超过 4GB 时，必须显式配置 `--max-old-space-size` 并避免将类型检查与产物打包并行执行；生产环境默认关闭完整 sourcemap，确需定位压缩堆栈时改为按需开启 hidden 模式。
