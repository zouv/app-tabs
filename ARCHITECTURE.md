# AppTabs 代码链路图谱

## 作用域入口

| 任务 | 入口 |
|---|---|
| 窗口选择 / 通用兼容 | `WindowPicker.Reload`、`NativeWindowApi.DiscoverWindows / ValidateAttach`、`WindowEligibility` |
| 接管 / 切换 / 分离 | `HostController.Attach / Activate / Detach`、`NativeWindowApi.Attach / Layout / Restore` |
| 标题栏 / 工具栏 / 图标 | `MainForm.InitializeChrome / SyncChromeMetrics`、`MainForm.Chrome.cs`、`ChromeControls.cs`、`AppIcon.Load` |
| 标签显示 / 排序 / 重命名 | `TabBar.Bind / OnMouseUp`、`MainForm.ShowTabMenu / Rename`、`TabManagementPolicy` |
| 快捷键 / 托盘 | `KeyboardHook.Resolve / OnKey`、`GlobalHotkey.Apply`、`MainForm.OnReady / ToggleHost / WndProc` |
| 窗口组 / 标签组 / 应用收藏 | `MainForm.Library.cs`、`AppLibrary / AppLibraryStore`、`LibraryManagerDialog`、`ApplicationLauncher`、`GroupMatching.Match` |
| 设置 / 关于 / 弹窗边框 | `SettingsDialog`、`SettingsCommit.Save`、`AboutDialog`、`ShellDialog / DialogTitleBar`、`MainForm.OpenSettings` |
| 正常退出 / 异常恢复 | `MainForm.OnFormClosing`、`RecoveryJournal`、`Program.Main / StartWatchdog / RunWatchdog` |
| 自动测试 / 构建 / 开发启动 / 打包 | `tests/AppTabs.Tests/Program.cs`、`scripts/build.ps1`、`scripts/dev.ps1`、`scripts/package.ps1`、根目录 `*.bat` |

## 工程与职责

- `AppTabs.Core`：纯策略、恢复 JSON、状态 JSON、Win32 接管与原结构还原。原生方法与 taskbar COM 封装在此。
- `AppTabs.App`：WinForms 界面，主窗、标签栏、窗口选择器、设置 / 搜索 / 改名对话框，托盘与键盘钩子。`MainForm.Chrome.cs` 管理无原生标题但保留缩放边框的两行顶部及菜单作用域；`ChromeControls.cs` 管理矢量图标按钮；`DialogChrome.cs` 提供圆角、1px 实色边框和选择器缩放的共享弹窗。`MainForm.Library.cs` 编排命名快照与收藏，`LibraryManagerDialog` 编辑私有副本，`ApplicationLauncher` 只响应明确点击。`AppIcon` 加载多尺寸图标。
- `AppTabs.Tests`：无测试框架依赖的控制台断言；fake 故障注入，自建跨进程窗口与守护进程测试，离屏界面渲染。`ChromeUiChecks` / `TabGestureChecks` / `DialogUiChecks` 覆盖布局、手势、边框与 ICO；`LibraryChecks` / `LibraryHostChecks` / `SettingsChecks` 使用临时库及 fake 启动 / 热键，不启动真实收藏。
- `assets`：叠层应用窗口与活动标签图标，SVG 源图与 16–256 像素七帧 32bpp ICO；`scripts/create-icon.ps1` 可重新生成 ICO 和小尺寸预览。
- 根目录 `setup.bat` / `build.bat` / `dev.bat` / `test.bat` / `clean.bat` / `package.bat`：Windows 脚本入口；构建与测试集中在 `scripts/build.ps1`，开发检查 / 构建 / 等待集中在 `scripts/dev.ps1`，打包快照 / 构建 / 收集集中在 `scripts/package.ps1`，bat 负责工具检查、参数与退出码传递。`dev.bat` 在调用 `dev.ps1` 前检查 .NET SDK，并在缺少 `project.assets.json` 时先还原；`package.bat` 按 `snapshot → build（Debug / Release，输出到 `.runtime\package-staging`）→ collect` 执行，不运行测试。
- `scripts/dev.ps1`：启动前核验 AppTabs 映像并拒绝已有实例；Debug 输出到 `.runtime/dev`，启动后等待并传递退出码，`-BuildOnly` 不启动。与发布版共享单实例 / 配置，不关闭宿主或看门狗。
- `scripts/package.ps1` + `scripts/PackageTransaction.ps1`：`snapshot` 检查 `dist\dev` / `dist\release` / 工具输出是否被运行中的 AppTabs 占用（只报告，不结束进程），清理并记录本次构建前的产物；`build` 做全新 Debug / Release 构建；`collect` 拒绝过期构建，生成 `dist\dev\AppTabs_<版本>_x64-unpack-debug.zip`、`dist\release\unpack\`、`dist\release\AppTabs_<版本>_x64-unpack.zip` 与 `SHA256SUMS.txt`，并校验 zip 条目 / 长度 / SHA256。发布时先备份旧 `dist\dev` / `dist\release` 再整体替换，失败逆序恢复；成功后删除隔离目录。`dist` 为发布 / 验证产物，不进 Git；`artifacts` 存离屏截图。

## 数据与关键约束

1. `WindowIdentity` = HWND / PID / TID / 进程启动时间 / 映像 / 类名。所有外部窗口操作按这套身份校验；标题只作显示或显式恢复组的筛选。
2. `HostController.Attach` 先 Capture / Validate，再持久化全部快照，才执行原生修改；失败分离回滚。回滚失败保留标签与日志，阻止继续接管。
3. 同一宿主支持混合模式；调用方给出的 `bounds` 一律是 content HWND 客户区坐标。`NativeWindowApi.Layout` 为分组模式转换到屏幕坐标，嵌入模式保持子窗口坐标。
4. 通用嵌入要求 DPI awareness 相同；不套用 ExplorerTabs 旧的低 awareness 白名单。自动模式只在原生修改之前回退；修改后失败先回滚，不盲目重试分组。
5. `Detach` 原生还原成功后进入 `RestoredPendingJournal`；写盘失败重试只清理日志，不再次还原 / 移动窗口。失效身份零 Restore，未知查询错误保留记录。
6. 窗口组不保存可复用 HWND。单组与命名标签组共用过滤后的快照与恢复流程：排除未附着 / 正在关闭会话，活动索引在过滤后计算；显式恢复按映像 / 类名 / 标题全等筛选唯一候选，只追加已有窗口，不自动启动第三方软件。`library.json` 独立于 settings v1，缺失为空、损坏禁止覆盖；候选成功原子写盘后才提交内存。收藏只记录本地 exe 与名称，明确点击才通过可注入启动器启动，不自动挂载。
7. `KeyboardHook` 只做轻量前台范围判断和快捷键解析，向 UI 队列投递；不在钩子中接管、枚举进程、写盘或调用跨进程同步消息。模态对话框 / 菜单期间停用标签快捷键，菜单关闭栈返回后释放作用域再执行动作。设置热键变更采用补偿流程，写盘失败不发布候选；旧热键恢复失败明确提示不可用，实际注册失效时同组合可重试。管理页提交回调落盘成功才关闭，失败保留编辑。
8. `OnFormClosing` 默认 DetachAll；恢复失败取消退出。× / Ctrl+W / 中键分离；明确的独立关闭操作只对选定 HWND 发 WM_CLOSE，保留日志直到窗口确实消失。
9. 看门狗在接管前完成启动握手，等待准确的宿主进程退出后恢复仍存活的窗口；`LockForRecovery` 串行化启动恢复与旧看门狗，避免争夺日志。已销毁窗口无法复活。
10. 应用设置、恢复日志、轮转诊断在 `%LOCALAPPDATA%/AppTabs`。单实例 mutex 与恢复 gate 独立于 ExplorerTabs。测试使用独立 Guid 临时目录，不读写实际用户配置。

## 测试限制

测试不注册全局热键 / 键盘钩子，不枚举接管用户软件。native helper 使用自建的无任务栏工具窗口、离屏位置和非激活显示；任务栏服务注入 no-op。真实第三方软件兼容性与键鼠交互仍需用户实测，不能用 fake / 合成窗口验证代替。
