# AppTabs 代理指南

- 中文沟通，每次回复称呼用户“啊唯”。
- 开工先读 ARCHITECTURE.md 与 HANDOFF.md，按具体模块定位代码。
- C# / WinForms / .NET Framework 4.8 / x64，无第三方 NuGet。
- 不自动接管、关闭或启动用户已有软件；自动测试只能操作自建窗口。
- 接管之前必须落盘恢复日志。所有窗口操作核验 HWND、PID、TID、进程启动时间、映像与类名。
- 查询失败是未知状态，不能当作窗口消失并丢弃恢复记录。
- 默认退出和标签上的 × 都分离并保留软件。关闭软件必须独立、明确操作，不杀进程。
- 不自动提交、推送。修改结构同步 ARCHITECTURE.md，用户可感知改动同步 CHANGELOG.md。
- 验证：powershell -ExecutionPolicy Bypass -File scripts/build.ps1；只测试自建窗口。
- HANDOFF.md 只保存进行中任务和真实未验证事项，不堆积已完成记录。
