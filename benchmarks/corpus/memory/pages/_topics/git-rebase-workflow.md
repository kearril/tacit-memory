---
title: 特性分支同步与合并历史规范
aliases: [Git 分支合并策略, rebase 与 merge 规范, 提交历史清理规则]
dates: { first: 2026-08-01, last: 2026-09-14 }
related: [_topics/ci-env-bootstrap]
type: decision
---

## 分支整理规则
本地特性分支在发起评审前统一先变基到最新 `origin/main`（`git pull --rebase origin main`），消除冗余同步提交并保持变更集清晰；正式合入 `main` 时一律使用 `git merge --no-ff`，在主线历史中保留明确的功能边界节点。

## 冲突与回滚收益
相比纯 fast-forward 或 squash，保留非快进合并节点既能留住功能内部的原子提交上下文，又能在上线出现异常时直接执行 `git revert -m 1 <merge-sha>` 一键整批撤回整个特性。
