# AppTabs 代码链路图谱

## 作用域入口

| 任务 | 入口 |
|---|---|
| 窗口选择 / 通用兼容 | `WindowPicker.Reload`、`NativeWindowApi.DiscoverWindows / ValidateAttach`、`WindowEligibility` |
| 接管 / 切换 / 分离 | `HostController.Attach / Activate / Detach`、`NativeWindowApi.Attach / Layout / Restore` |
| 标签显示 / 排序 / 重命名 | `TabBar.Bind / OnMouseUp`、`MainForm.ShowTabMenu / Rename`、`TabManagementPolicy` |
| 快捷键 / 托盘 | `KeyboardHook.Resolve / OnKey`、`GlobalHotkey.Apply`、`MainForm.OnReady / ToggleHost / WndProc` |
| 窗口组 / 设置 | `AppState`、`StateStore`、`GroupMatching.Match`、`MainForm.SaveGroup / RestoreGroup / OpenSettings` |
| 正常退出 / 异常恢复 | `MainForm.OnFormClosing`、`RecoveryJournal`、`Program.Main / StartWatchdog / RunWatchdog` |
| 自动测试 / 构建 | `tests/AppTabs.Tests/Program.cs`、`scripts/build.ps1` |

## 工程与职责

- `AppTabs.Core`：纯策略、恢复 JSON、状态 JSON、Win32 接管与原结构还原。原生方法与 taskbar COM 封装在此。
- `AppTabs.App`：WinForms 界面，主窗、标签栏、窗口选择器、设置 / 搜索 / 改名对话框，托盘与键盘钩子。
- `AppTabs.Tests`：无测试框架依赖的控制台断言；fake 故障注入，自建跨进程窗口与守护进程测试，离屏界面渲染。
- `assets`：代码绘制的应用图标；`scripts/create-icon.ps1` 可重新生成 ICO。
- `dist/release`：便携程序；`artifacts`：仅合成窗口的测试界面截图。两者不进 Git。

## 数据与关键约束

1. `WindowIdentity` = HWND / PID / TID / 进程启动时间 / 映像 / 类名。所有外部窗口操作按这套身份校验；标题只作显示或显式恢复组的筛选。
2. `HostController.Attach` 先 Capture / Validate，再持久化全部快照，才执行原生修改；失败分离回滚。回滚失败保留标签与日志，阻止继续接管。
3. 同一宿主支持混合模式；调用方给出的 `bounds` 一律是 content HWND 客户区坐标。`NativeWindowApi.Layout` 为分组模式转换到屏幕坐标，嵌入模式保持子窗口坐标。
4. 通用嵌入要求 DPI awareness 相同；不套用 ExplorerTabs 旧的低 awareness 白名单。自动模式只在原生修改之前回退；修改后失败先回滚，不盲目重试分组。
5. `Detach` 原生还原成功后进入 `RestoredPendingJournal`；写盘失败重试只清理日志，不再次还原 / 移动窗口。失效身份零 Restore，未知查询错误保留记录。
6. 窗口组不保存可复用 HWND。显式恢复按映像 / 类名 / 标题全等筛选唯一候选，歧义和不存在都跳过，不自动启动第三方软件。
7. `KeyboardHook` 只做轻量前台范围判断和快捷键解析，向 UI 队列投递；不在钩子中接管、枚举进程、写盘或调用跨进程同步消息。模态对话框期间停用标签快捷键。
8. `OnFormClosing` 默认 DetachAll；恢复失败取消退出。× / Ctrl+W / 中键分离；明确的独立关闭操作只对选定 HWND 发 WM_CLOSE，保留日志直到窗口确实消失。
9. 看门狗在接管前完成启动握手，等待准确的宿主进程退出后恢复仍存活的窗口；`LockForRecovery` 串行化启动恢复与旧看门狗，避免争夺日志。已销毁窗口无法复活。
10. 应用设置、恢复日志、轮转诊断在 `%LOCALAPPDATA%/AppTabs`。单实例 mutex 与恢复 gate 独立于 ExplorerTabs。测试使用独立 Guid 临时目录，不读写实际用户配置。

## 测试限制

测试不注册全局热键 / 键盘钩子，不枚举接管用户软件。native helper 使用自建的无任务栏工具窗口、离屏位置和非激活显示；任务栏服务注入 no-op。真实第三方软件兼容性与键鼠交互仍需用户实测，不能用 fake / 合成窗口验证代替。
