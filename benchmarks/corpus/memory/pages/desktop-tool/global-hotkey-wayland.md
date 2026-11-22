---
title: Linux Wayland 全局快捷键适配
aliases: [桌面端热键兼容, Linux 唤起组合键, XDG Portal 热键]
dates: { first: 2026-09-25, last: 2026-10-28 }
related: [desktop-tool/tauri-ipc-locking, desktop-tool/auto-update-signing]
type: work
---

## 桌面门户接入实现
通过 `ashpd` 封装调用 `org.freedesktop.portal.GlobalShortcuts` 的 D-Bus 接口，在会话初始化阶段申请绑定并持久化返回的 `session_handle`，监听到 `Activated` 信号后将主窗口置顶并聚焦输入框；目前已在 GNOME 46 和 KDE Plasma 6 桌面环境中完成实机验证。

## 降级兼容逻辑
检测到环境变量 `XDG_SESSION_TYPE=x11` 时回退到传统 X11 `XGrabKey` 注册路径；若当前合成器未实现对应桌面门户，则在设置页展示非阻塞提示条，引导用户在窗口管理器中自行配置唤起命令。

## Leftovers
- Sway 与 wlroots 生态下的 `xdg-desktop-portal-wlr` 暂未支持该门户接口，下一步需补充单实例 CLI 参数 `--toggle-window` 供用户绑定到合成器配置。
- 系统托盘菜单目前尚未展示当前已生效的组合键提示文案。
