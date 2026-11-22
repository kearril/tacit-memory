---
title: 内网服务间 mTLS 双向认证与私有 CA 签发策略
aliases: [Caddy 双向证书认证, 内网 mTLS 配置, step-ca 客户端证书]
dates: { first: 2026-09-16, last: 2026-10-15 }
related: [ops-infra/reverse-proxy-tls, ops-infra/container-compose-layout]
type: decision
---

## 双向认证架构
面向内部管理接口与跨节点任务回调端点，在 Caddy 反代上启用 `tls { client_auth { mode require_and_verify trust_pool file /etc/certs/internal-client-ca.pem } }`，要求调用方在 TLS 握手阶段出示由私有 `step-ca` 签发的客户端证书，从传输层隔离未授权的内网横向访问。

## 证书生命周期与自动续签
私有根 CA 离线保存在加密介质中，日常仅由中级签发 CA 在线签发有效期 7 天的短周期客户端证书。`dev-box` 与 `build-node` 上的调用端通过 `step-ca` daemon 定时在到期前 48 小时自动轮换证书，并通过 SIGHUP 通知客户端重载，避免静态长期证书泄漏后难以回收。

## 吊销与例外边界
当某台构建节点退役或怀疑私钥外泄时，将其序列号写入 CRL 列表并触发 Caddy 同步刷新，同时在 `step-ca` 端撤回对应的 provisioner 签发资格。对外暴露的公网入口仍维持单向 TLS 与应用层鉴权，不强制终端浏览器安装私有客户端证书。
