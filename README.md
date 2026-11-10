# AppTabs

把已打开的 Windows 桌面软件放进标签工作区，集中切换、移动和分离。

## 运行

Windows 10 1703+ / Windows 11，x64，.NET Framework 4.8。
源码目录中双击 `dist/release/unpack/AppTabs.exe` 或 `run.bat`；便携 ZIP 解压后直接双击 `AppTabs.exe`。软件不会自动接管任何窗口；点击“加入窗口”选择已打开的软件。

## 功能

- 多选加入窗口，按软件名 / 窗口标题筛选。
- 标签显示软件图标，支持拖动排序、重命名、搜索，滚轮横向滚动。
- 浅色标题栏中标签与窗口控制同行；下方图标工具栏提供加入、分离、窗口组 / 收藏菜单与搜索。点击左上角应用图标打开设置或关于。拖动标题栏空白处移动窗口，双击空白处最大化 / 还原。
- 设置分“常规 / 快捷键”两页：挂载模式在常规页选择，仅影响新加入窗口；快捷键页选择或停用全局呼出组合，并展示固定标签快捷键。页签始终单行，不提供多行设置。
- 圆角标签支持拖拽插入线和溢出边缘自动滚动；排序不激活软件，Esc 或移出栏外取消。× 在同一按钮按下并松开才分离。弹窗统一圆角实色细边框，“加入窗口”仍可缩放，软件列在窗口标题前。
- 每个标签可独立使用嵌入或兼容分组；自动模式优先尝试满足条件的嵌入。
- Ctrl+Tab / Ctrl+Shift+Tab、Ctrl+PageDown / PageUp 切换；Ctrl+1…9 跳转。
- Ctrl+W、标签 ×、中键和“分离当前”均还原窗口并保留软件。
- Ctrl+F 搜索标签；全局 Ctrl+Alt+Space 呼出 / 切后台，可在设置中更换或停用。
- 系统托盘呼出、加入、退出；可选择切后台时隐藏到托盘。
- 保留单窗口组保存 / 恢复，并可将当前窗口另存为多个命名标签组；同名覆盖需要确认。恢复只匹配已经打开、标题 / 类名 / 程序路径一致的唯一窗口并追加，不自动启动软件。
- 窗口组的书签菜单中可收藏本地 `.exe`，点击收藏直接发起启动；不支持参数、脚本、URL 或网络程序，也不会自动挂载。启动后用“加入窗口”选择。管理页可编辑 / 删除收藏和标签组，删除记录不关闭软件；取消不保存修改。
- 正常退出分离并保留所有软件；失败时继续运行并保留恢复日志。
- 独立恢复日志和守护进程，异常退出后尽力还原仍存活的窗口。

## 两种模式

**嵌入**会将真实软件主窗口变成 AppTabs 的子窗口。软件的地址栏、编辑内容和操作状态仍由原软件维护。通用嵌入属于实验性功能，焦点、自绘标题栏、弹窗与多窗口行为需要按软件验证。缩放模式不一致时自动模式回退到兼容分组，不改变目标软件的 DPI awareness。

**兼容分组**保留顶层窗口结构，把当前软件排列在标签栏下方；其他标签隐藏。它不是严格的单 HWND 容器，活动软件仍可能有自己的任务栏项、边框和弹窗。右键标签可切换模式。

Windows 没有保证任意应用可以跨进程嵌入。浏览器、Electron、WPF、UWP / 打包应用、自绘窗口均需实测；接管时主窗口有未关闭的模态对话框、置顶或未响应状态时不应尝试。管理员权限的软件需要以同等权限启动 AppTabs。

标签针对窗口，一个软件可有多个窗口。软件仍在后台运行，标签管理不会降低其内存使用或暂停进程。窗口标题变化会影响已保存窗口组的匹配；未匹配时使用“加入窗口”重新选择。

**异常恢复的边界**：强制终止、系统注销或软件自身退出可能销毁嵌入窗口；守护进程与日志无法复活已销毁的窗口，也无法保证恢复未保存内容。正常使用请通过 AppTabs 的关闭按钮 / 托盘退出，避免在任务管理器结束宿主。关闭某个软件请右键标签选择独立的“关闭这个软件窗口…”操作；AppTabs 只发送窗口关闭请求，不结束整个进程。

设置、窗口组、恢复日志与本机诊断仅存放在 `%LOCALAPPDATA%/AppTabs`，与 ExplorerTabs 分离。命名标签组与应用收藏独立保存在 `library.json`，不改变原 settings v1；最多 100 个收藏、50 个标签组，每组 100 个窗口。损坏的设置 / 库 / 日志不会静默覆盖。日志超过约 2 MB 时保留一份轮转副本。

## 构建与验证

安装 .NET SDK 与 .NET Framework 4.8 Targeting Pack，运行 `build.bat`，或：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1
```

辅助脚本：`setup.bat` 检查 .NET SDK / .NET Framework 4.8 Targeting Pack 并还原；`dev.bat` 先检查 .NET SDK，缺少还原输入时执行 `dotnet restore`，再构建到 `.runtime\dev` 并等待程序退出，失败时保留错误窗口；`dev.bat -BuildOnly` 只构建、不启动；`test.bat` 只构建并运行测试；`build.bat` 构建并测试后复用打包流程；`package.bat` 按“记录现有产物 → Debug / Release 全新构建 → 收集”流程生成 `dist\dev\AppTabs_<版本>_x64-unpack-debug.zip`、`dist\release\unpack\`、`dist\release\AppTabs_<版本>_x64-unpack.zip` 与 `dist\release\SHA256SUMS.txt`，失败时恢复上一版 `dist\dev` / `dist\release`；打包不运行测试，也不关闭占用进程；`clean.bat` 清理 `bin` / `obj` 与测试截图，保留 `dist\`。

如果 AppTabs 或其恢复看门狗正从 `dist\release\unpack` 运行，`package.bat` 会列出进程并保留旧包后取消。请通过主窗 / 托盘正常退出并等待恢复结束再重试，不要强制结束进程。

开发版与发布版共用单实例和本机配置。已有 AppTabs 或其恢复看门狗运行时，`dev.bat` 会列出进程并取消；请通过主窗 / 托盘正常退出并等待恢复结束后重试，不要在任务管理器强制结束。

无第三方 NuGet；默认构建启用警告即错误并运行测试。测试只使用 fake 接口与自建的 WinForms 跨进程窗口，绝不接管用户已有软件，也不注册实际全局钩子 / 热键。

## 文档

- [AGENTS.md](AGENTS.md)：AI 工作规则、技术 / 安全约束与验证入口。
- [ARCHITECTURE.md](ARCHITECTURE.md)：代码链路图谱与任务 → 入口。
- [ROADMAP.md](ROADMAP.md)：总体计划与完成度。
- [CHANGELOG.md](CHANGELOG.md)：按版本记录使用者可感知的变化。
- [HANDOFF.md](HANDOFF.md)：进行中任务与真实未验证事项。
- [docs/00-目录.md](docs/00-目录.md)：面向人的使用 / 维护文档索引。

文档体检：`sh check-docs.sh`。Claude Code 通过 `CLAUDE.md` 加载 `AGENTS.md`，其余协作文档由 `AGENTS.md` §0.5 索引。

代码的 Win32 快照、接管、分离与恢复基础改编自同作者的 [ExplorerTabs](https://github.com/zouv/explorer-tabs)，本项目独立维护；保留专用 Explorer Ribbon 裁切，其余接管条件、恢复身份与模式管理已泛化。
