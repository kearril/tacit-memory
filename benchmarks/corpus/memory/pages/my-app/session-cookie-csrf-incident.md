---
title: SameSite Cookie 与 CSRF 双重提交校验误拦控制台写请求
aliases: [CSRF 403 故障, Cookie Domain 冲突, 控制台保存报错]
dates: { first: 2026-09-26, last: 2026-09-27 }
related: [my-app/rbac-audit-architecture, ops-infra/reverse-proxy-tls]
type: incident
---

## 故障现象
测试环境切换二级子域后，操作员在 Web 控制台提交作业触发与配置保存请求全部返回 `403 Invalid CSRF token`，而只读 `GET` 接口与个人访问令牌调用均正常。

## 根因定位
抓包排查发现浏览器请求同时携带了两个同名的 `csrf_token` Cookie：一个来自旧部署设置的父域 `Domain=.example.internal`，另一个来自新服务签发的精确主机域。服务端中间件按字典顺序只读取了第一个 Cookie 值与 `X-CSRF-Token` 请求头比对，导致合法提交持续校验失败；同时单点跳转回调链路在 `SameSite=Strict` 下未能携带新建会话 Cookie。

## 修复措施
将非敏感的 CSRF 令牌 Cookie 改名为带 `__Host-` 前缀且省略 `Domain` 属性（强制绑定当前主机、路径 `/` 与 `Secure`），并在登录响应中显式下发过期头清理历史遗留的父域同名 Cookie；登录态导航跳转入口改为 `SameSite=Lax`，保留核心刷新令牌 Cookie 的 `SameSite=Strict` 与 `HttpOnly`。

## Lesson
面向多子域部署的 Web 应用，认证与防跨站 Cookie 应优先使用 `__Host-` 前缀消除父域遮蔽风险；升级鉴权中间件前必须在带有历史 Cookie 的浏览器会话中回归所有写操作接口。
