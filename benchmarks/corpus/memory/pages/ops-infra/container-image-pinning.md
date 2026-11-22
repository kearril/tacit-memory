---
title: 容器基础镜像按 SHA256 摘要锁定与定期更新策略
aliases: [镜像 Digest 锁定, 禁止 latest 标签, 容器镜像版本治理]
dates: { first: 2026-08-10, last: 2026-09-05 }
related: [ops-infra/container-compose-layout, _topics/ci-env-bootstrap]
type: decision
---

## 摘要锁定规则
所有 `compose.yaml` 与 `Dockerfile` 中引用的第三方镜像严禁使用 `:latest` 或浮动小版本标签，必须采用 `image:tag@sha256:<digest>` 形式显式锁定构建摘要，确保 `dev-box` 与 `build-node` 在任意时间重新拉取并重启服务时得到的二进制与系统库完全一致。

## 自动化升级与回滚
仓库内启用每周一凌晨执行的镜像版本检查工作流，针对安全补丁和次版本更新自动生成包含新摘要与变更日志链接的合并请求；合并前须在测试栈完成健康探针与冒烟检查。若新摘要引入回归，直接回退单行 `compose.yaml` 提交即可无损恢复至上一个已验证镜像。
