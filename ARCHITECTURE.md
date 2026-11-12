# 代码链路图谱 · AppTabs

> **本文件的目的**：给 AI（及人类）一份「读这一份就能定位代码」的导航地图，避免每次改动 / 排查都全量扫描源码。
>
> **维护**：改了代码结构（新增入口 / 移动逻辑 / 改数据流 / 改接口），同步更新本文件（见 AGENTS §1）。
>
> **体量**：不超过 25,000 字；超了按 AGENTS §1.1 拆到 `arch/<module>.md`，总览只留 §0 / §0.5 / §1。不写日期、版本、改动经过（归 CHANGELOG / git log）。
>
> 文中「AGENTS §x」指 `AGENTS.md` 的章节；不带前缀的 §x 指本文件。

---

## 0. 一句话架构

单进程 WinForms 宿主（`AppTabs.App`）承载一个标签工作区；`AppTabs.Core` 负责窗口身份 / 接管 / 还原 / 恢复日志 / 设置与库的纯策略和 Win32 封装，测试工程用 fake 与自建窗口验证，不依赖第三方测试框架。

```
Program.Main
 ├─ 单实例 mutex + RecoveryJournal.LockForRecovery
 ├─ 启动恢复 / 看门狗握手
 └─ MainForm（WinForms）
      ├─ MainForm.Chrome / TabBar / ChromeControls / Dialogs
      ├─ HostController ──> NativeWindowApi ──> Win32 / taskbar
      ├─ StateStore（settings.json）
      ├─ AppLibraryStore（library.json）
      └─ RecoveryJournal（recovery.json）
```

架构约束：WinForms / .NET Framework 4.8 / x64；无第三方 NuGet；Core 不引用 WinForms；所有窗口操作经 `WindowIdentity` 校验；恢复日志是原生修改的前置条件。

---

## 0.5 任务作用域路由（先定范围，避免污染上下文）

| 任务类型 | 该读（仅限） | 可忽略 | 入口小节 |
|---|---|---|---|
| 窗口选择 / 加入窗口 | `WindowPicker.cs`、`WindowEligibility.cs`、`NativeWindowApi.DiscoverWindows / ValidateAttach`、`MainForm.JoinWindow` | 恢复 / 打包细节 | §2.1 |
| 布局 / 切换 / 分离 | `HostController.cs`、`NativeWindowApi.Layout / Show / Restore`、`MainForm.Select / Detach` | 标签组 / 设置 UI | §2.2 |
| 标题栏 / 页签 / 弹窗 | `MainForm.Chrome.cs`、`TabBar.cs`、`ChromeControls.cs`、`DialogChrome.cs`、`Dialogs.cs`、`SettingsDialog.cs`、`AboutDialog.cs`、`WindowPicker.cs` | Core 的恢复 / 状态存储 | §2.3 |
| 设置 / 快捷键 / 托盘 | `SettingsDialog.cs`、`SettingsCommit.cs`、`KeyboardHook.cs`、`NativeUi.cs` 的 `GlobalHotkey`、`MainForm.OnReady` | 标签组 / 打包 | §2.4 |
| 标签组 / 应用收藏 | `MainForm.Library.cs`、`AppLibrary.cs`、`LibraryManagerDialog.cs`、`ApplicationLauncher.cs`、`GroupMatching.Match` | UI chrome 细节 | §2.5 |
| 启动 / 恢复 / 看门狗 | `Program.cs`、`RecoveryJournal.cs`、`HostController.Detach / DetachAll` | 标签组 / 设置 UI | §2.6 |
| 构建 / 打包 / 文档 | `scripts/*.ps1`、根目录 `*.bat`、`README.md`、`docs/`、`check-docs.sh` | `src/`、`tests/` | §6 |

> 跨多个区域的任务才需要跨列读；仍然遵循「先定位函数名，再只读那几个函数」。

---

## 1. 文件职责速查（先看这张表）

| 文件 / 目录 | 职责 | 何时改它 |
|---|---|---|
| `src/AppTabs.Core/Models.cs` | `WindowIdentity`、`WindowSnapshot`、`WindowSession`、`IWindowApi`、`ISnapshotStore`、模式 / 状态枚举 | 改身份字段、快照结构、接口契约 |
| `HostController.cs` | 接管 / 激活 / 分离 / 关闭 / 排序 / 标签策略的 UI 线程编排；所有原生修改前的恢复写盘 | 改接管 / 恢复语义、会话状态 |
| `NativeWindowApi.cs` | Win32 枚举、身份核验、Capture / Validate / Attach / Layout / Show / Restore、DPI 查询 | 改 Win32 行为、DPI、窗口枚举 |
| `RecoveryJournal.cs` | 恢复 JSON 的读写、owner 校验、`Recover` / `TryClearAllMissing`、恢复 gate | 改异常恢复 / 看门狗数据 |
| `AppState.cs` / `RecentApps.cs` | settings.json v1、`SavedWindow`、`GroupMatching`、最近加入应用策略、原子 `StateStore` | 改设置字段 / 单窗口组匹配 / 最近应用 |
| `AppLibrary.cs` | library.json v1、收藏 / 命名标签组、验证、原子存储 | 改标签组 / 收藏数据 |
| `TabManagementPolicy.cs` | 标题规范化、拖动阈值 / 插入索引、后继标签纯策略 | 改标签排序 / 选择规则 |
| `WindowEligibility.cs` | 过滤系统窗口、Elevation / 兼容性判断 | 改“哪些窗口可加入” |
| `RestorePresentation.cs` / `WindowDiagnostics.cs` / `WindowClosing.cs` | 还原后的显示修正、诊断、单窗口关闭请求 | 改还原呈现 / 关闭行为 |
| `src/AppTabs.App/Program.cs` | 单实例、启动恢复、看门狗启动 / 运行、诊断日志 | 改启动流程 / 看门狗 |
| `MainForm.cs` | 主窗生命周期、加入 / 选择 / 分离 / 退出、内容布局、欢迎页、托盘按需显示 | 改宿主行为 / 欢迎页 |
| `MainForm.Chrome.cs` | 两行 chrome、菜单作用域、原生边框消息、最大化工作区 | 改标题栏 / 菜单 / 边框 |
| `MainForm.Library.cs` | 单组保存 / 恢复、命名组、收藏菜单、管理页提交 | 改标签组 / 收藏编排 |
| `TabBar.cs` | 单行页签绘制、命中、拖拽排序、溢出滚动、关闭反馈 | 改页签视觉 / 手势 |
| `ChromeControls.cs` / `Theme.cs` | 矢量图标按钮、工具栏分隔、颜色与 DPI 像素 | 改图标 / 控件样式 |
| `DialogChrome.cs` / `Dialogs.cs` / `SettingsDialog.cs` / `AboutDialog.cs` / `WindowPicker.cs` | 共享圆角弹窗、搜索 / 改名、设置、关于、加入窗口 | 改对话框 / 选择器 |
| `SettingsCommit.cs` / `NativeUi.cs` | 设置候选提交、热键补偿、Win32 常量 / 系统菜单 / DPI 边框 | 改设置保存 / 原生 UI |
| `ApplicationLauncher.cs` / `AppIcon.cs` | 收藏 exe 的显式启动、嵌入 ICO 加载 | 改启动方式 / 图标 |
| `KeyboardHook.cs` | 标签快捷键解析与前台范围判断 | 改键盘映射 |
| `tests/AppTabs.Tests/Program.cs` | 控制台断言主入口、fake、native helper、watchdog | 改测试框架 / 新增检查 |
| `tests/AppTabs.Tests/*Checks.cs` | 按模块拆分的断言（Chrome / Tab / Dialog / Library / Settings / Native / DPI / Watchdog） | 改对应模块测试 |
| `scripts/build.ps1` / `scripts/dev.ps1` / `scripts/package.ps1` / `PackageTransaction.ps1` / `ProcessGuard.ps1` | 构建测试、开发构建、打包快照 / 发布 / 回滚、运行中 AppTabs 的交互式关闭确认 | 改构建与打包流程 |
| `scripts/create-icon.ps1` / `assets/` | 生成七尺寸 ICO 与预览；SVG 源图 | 改图标 |
| 根目录 `*.bat` | Windows 双击入口：工具检查、参数与退出码 | 改脚本入口 |
| `Directory.Build.props` / `app.manifest` | 版本 / TFM / 警告即错误；清单身份 | 发版 / 改平台约束 |
| `README.md` / `docs/` | 面向人的运行 / 维护说明 | 用户可见用法变化 |
| `dist/` / `artifacts/` / `.runtime/` | 发布包 / 测试截图 / 构建暂存，均不入库 | 不手改 |

---

## 2. 按「我要做什么」反查代码位置（任务 → 入口）

> 只登记常见改动的入口函数 / 注释锚点，不是全量函数表。按函数名 / 锚点搜索，不写行号。
>
> **自愈规则**：函数名搜不到 / 对不上时，以代码为准，完成任务后顺手订正本表。

### 2.1 窗口选择 / 加入 / 接管（`WindowPicker.cs`、`MainForm.cs`、`HostController.cs`）

| 我要改的东西 | 关键函数 / 锚点 | 备注 |
|---|---|---|
| 枚举可加入窗口 | `NativeWindowApi.DiscoverWindows`、`WindowEligibility.IsSystemWindow / IsElevated` | 排除已有宿主窗口、系统窗口与不满足条件的进程 |
| 选择器过滤 / 勾选 / 刷新 | `WindowPicker.Reload / Rebuild` | 软件列在前，保留跨筛选勾选集合；测试可注入候选加载器 |
| 加入窗口的自动 / 分组 / 嵌入回退 | `MainForm.JoinWindow` | `Auto` 先 Validate 嵌入，不满足再 `Grouped`；失败不盲目重试；成功后记录最近应用 |
| 接管前的持久化与回滚 | `HostController.Attach` | `FrameStripped` 必须在 `store.Save` 前写入快照；先 Capture / ValidateAttach / Save，再 Attach |
| 原生接管条件 | `NativeWindowApi.ValidateAttach` | 同 DPI awareness、目标无 owner / 非 topmost 等 |
| 分组无边框 / 任务栏 | `NativeWindowApi.Attach / Restore`、`WindowSnapshot.FrameStripped` | 去掉 caption/thickframe/sysmenu/min/max，设 tool window；任务栏 Remove / Add |

### 2.2 布局 / 切换 / 分离 / 恢复（`HostController.cs`、`NativeWindowApi.cs`、`RecoveryJournal.cs`）

| 我要改的东西 | 关键函数 / 锚点 | 备注 |
|---|---|---|
| 标签切换 | `HostController.Activate` | 先显示新窗口再隐藏旧窗口；失败回滚 |
| 宿主激活时的 z-order | `MainForm.Activated`、`HostController.RaiseActive` | 分组窗口是独立顶层窗口；点击宿主会把它抬到上面，必须立即抬回 |
| 最大化 / 还原 / 移动 | `ProcessChromeMessage` 的 `WM_SIZE`、`MainForm.LayoutActiveNow` | WM_SIZE 状态变化后布局；LocationChanged / content.Resize 同步布局，模态循环里不依赖定时器 |
| 客户区布局 | `HostController.LayoutActive`、`NativeWindowApi.Layout` | `bounds` 是 content 客户区；分组模式转屏幕坐标 |
| 分离单个 / 全部 | `HostController.Detach / DetachAll` | 原生还原成功才离开恢复日志；失败保留标签 |
| 标签后继选择 | `TabManagementPolicy.Successor / CloseSuccessor` | 活动标签消失时优先左邻 |
| 启动恢复 | `Program.Main`、`RecoveryJournal.Read / Recover / TryClearAllMissing` | owner 仍运行拒绝；未知查询保留记录 |
| 异常看门狗 | `Program.StartWatchdog / RunWatchdog` | 等待准确宿主退出，`LockForRecovery` 串行化 |

### 2.3 标题栏 / 页签 / 弹窗（`MainForm.Chrome.cs`、`TabBar.cs`、`ChromeControls.cs`、`DialogChrome.cs`）

| 我要改的东西 | 关键函数 / 锚点 | 备注 |
|---|---|---|
| 标题栏布局 / 高度 / 图标 | `MainForm.InitializeChrome / SyncChromeMetrics / LayoutCaptionButtons / LayoutToolbar` | DPI 逻辑像素；不重建宿主 HWND |
| 无 caption 的原生边框 | `CreateParams`、`ProcessChromeMessage` 的 `WM_NCCALCSIZE / WM_NCHITTEST / WM_GETMINMAXINFO`、`SyncTopResizeBand` | 原生 side / bottom thick frame 留作宽缩放命中区；顶部 3px Padding 缩放带；最大化按工作区收敛 |
| 空白拖窗 / 双击最大化 | `ChromeMouseDown` | 顶部 4px 优先缩放；空白 `HTCLIENT` → `HTCAPTION` |
| 品牌 / 组菜单作用域 | `ShowChromeMenu / QueueMenuCleanup / ReleaseClosedMenus` | 关闭栈返回后释放 scope；后台关闭不抬分组窗口 |
| 页签绘制 / 命中 | `TabBar.OnPaint / Rectangles / HitTab / HitClose` | 单行；文本裁剪用 `PreserveGraphicsClipping` |
| 状态消息 / 快捷键提示 | `MainForm` 的 `status` 标签、`LayoutToolbar` | 位于工具栏中间空白区，不占独立一行；长文本用 tooltip |
| 工具栏加入 / 分离 | `MainForm.ShowAddWindowsMenu / ShowDetachMenu` | 加入平铺最近应用（不二级菜单）；多窗口打开筛选选择器；分离提供当前 / 全部 |
| 页签手势 / 排序 | `TabBar.OnMouseDown / OnMouseMove / OnMouseUp / CancelPointer`、`TabManagementPolicy.StartsDrag / InsertionIndex` | 按下不选择；拖动不激活；栏外 / Esc / capture 丢失取消 |
| 弹窗圆角 / 边框 / 缩放 | `ShellDialog`、`DialogTitleBar`、`DialogStyle.Round`、`ShellDialog.ResizeHit` | 1 物理像素环；picker 保留四边四角缩放 |

### 2.4 设置 / 快捷键 / 托盘（`SettingsDialog.cs`、`SettingsCommit.cs`、`KeyboardHook.cs`、`NativeUi.cs`）

| 我要改的东西 | 关键函数 / 锚点 | 备注 |
|---|---|---|
| 设置两页 / 候选值 | `SettingsDialog`、`MainForm.OpenSettings` | 常规 / 快捷键；取消不发布 |
| 热键注册 / 补偿 | `GlobalHotkey.Apply`、`SettingsCommit.Save` | 先注册新键再写盘；失败恢复旧键；回滚失败明确报告不可用 |
| 标签快捷键映射 | `KeyboardHook.Resolve / OnKey / SetScope` | 菜单 / 模态期间停用；不接管、不写盘 |
| 全局呼出 / 切后台 | `MainForm.WndProc` 的 `WM_HOTKEY`、`MainForm.ToggleHost` | 托盘与快捷键共用；切后台按设置最小化或隐藏到托盘 |
| 托盘菜单 | `MainForm.ShowTrayIcon / HideTrayIcon` | 启动不创建；仅隐藏到托盘时按需显示，显示 / 加入 / 退出 |

### 2.5 标签组 / 应用收藏（`MainForm.Library.cs`、`AppLibrary.cs`、`LibraryManagerDialog.cs`）

| 我要改的东西 | 关键函数 / 锚点 | 备注 |
|---|---|---|
| 保存当前快照 | `MainForm.CaptureGroup` | 只取 Attached 且未请求关闭的会话；活动索引在过滤后计算 |
| 恢复单组 / 命名组 | `MainForm.RestoreSnapshot` | 只匹配已打开唯一窗口并追加；不启动软件 |
| 保存 / 覆盖命名组 | `MainForm.SaveNamedGroup` | 同名确认；候选成功写盘后才提交 |
| 收藏应用 / 启动 | `MainForm.AddFavorite / LaunchFavorite`、`ApplicationLauncher.Launch` | 只接受本地绝对 exe；显式点击；不自动挂载 |
| 库管理页 | `LibraryManagerDialog.TryCommit` | 私有副本；提交回调成功才关闭；失败保留编辑 |
| 库文件格式 | `AppLibrary / SavedWindowGroup / ApplicationFavorite / AppLibraryStore` | v1、数量上限、原子写盘、坏库不覆盖 |

### 2.6 启动 / 恢复 / 看门狗（`Program.cs`、`RecoveryJournal.cs`、`HostController.cs`）

| 我要改的东西 | 关键函数 / 锚点 | 备注 |
|---|---|---|
| 单实例 | `Program.Main` 的 `Local\AppTabs.{SID}` mutex | 已有实例只提示，不抢窗口 |
| 启动前恢复 | `Program.Main`、`OwnerIsRunning`、`TryClearAllMissing`、`Recover` | 未知状态保留记录；失败阻止启动 |
| 看门狗握手 | `StartWatchdog`、`RunWatchdog` | 同 EXE `--watchdog`；PID + 启动时间核验 |
| 退出 / 分离 | `MainForm.OnFormClosing`、`HostController.DetachAll` | 默认分离保留软件；失败取消退出 |
| 日志轮转 | `Program.Log` | `%LOCALAPPDATA%\AppTabs\app.log`，约 2 MB 轮转 |

### 2.7 构建 / 打包 / 文档（`scripts/`、根目录 `*.bat`）

| 我要改的东西 | 关键函数 / 锚点 | 备注 |
|---|---|---|
| 完整构建 / 测试 / 打包 | `scripts/build.ps1`、`build.bat` | 测试通过后调用 package 三个动作 |
| 只测试 | `test.bat` | 构建并运行控制台断言 |
| 开发构建 | `scripts/dev.ps1`、`dev.bat` | `.runtime\dev`；`-BuildOnly` 不启动 |
| 打包 | `scripts/package.ps1` + `PackageTransaction.ps1`、`ProcessGuard.ps1`、`package.bat` | snapshot / build / collect；占用时先询问再关闭；dev + release 布局；失败回滚 |
| 文档体检 | `check-docs.sh` | 占位符 / 体量 / CLAUDE.md / 换行 / CHANGELOG 漏记 |

---

## 3. 重点链路详解（高频改动区 / 容易写错的地方）

### 3.1 接管（durable-first）

- **需求语义**：用户明确选择窗口后，先把“可还原所需的一切”落盘，再改原生窗口。
- **关键坑**：写盘失败、身份核验失败、DPI 不兼容、模态 / topmost 窗口都不能先改再回滚。
- **正确实现**：`HostController.Attach` → `windows.Capture` → `windows.ValidateAttach` → 写入 `FrameStripped` → `store.Save` → `windows.Attach`；失败进入 `RecoveryRequired` 并尝试 `Detach` 回滚，回滚失败保留标签与日志并阻止继续接管。
- **调用链**：`WindowPicker` → `MainForm.JoinWindow` → `HostController.Attach` → `NativeWindowApi.Attach`。

### 3.2 分离 / 退出 / 异常恢复

- **需求语义**：标签 × / Ctrl+W / 中键 / 默认退出都还原窗口并保留软件；关闭软件是独立动作。
- **关键坑**：还原成功但清理日志失败不能再次还原 / 移动窗口；查询失败不是窗口消失；已销毁窗口无法复活。
- **正确实现**：`HostController.Detach` 原生 Restore 成功后写 `RestoredPendingJournal`；重试只清理日志；`RecoveryJournal.TryClearAllMissing` 只在全部确认缺失时清空；未知异常保留记录。
- **调用链**：`TabBar.DetachTab` → `MainForm.Detach` → `HostController.Detach` → `NativeWindowApi.Restore`。

### 3.3 Chrome 菜单作用域与 Z-order

- **需求语义**：菜单 / 下拉打开时停用标签快捷键，关闭后恢复；不能让后台关闭的菜单把分组窗口抬到前台。
- **关键坑**：`ToolStripDropDown.Closed` 仍在 WinForms 关闭栈内；提前 Dispose / 提前 `LayoutContent(true)` 会抢焦点；后台切换应用后不能 `HWND_TOP`。
- **正确实现**：`EnterChromeMenuScope` 增加 `modalDepth` 并隐藏分组活动窗口；`ShowChromeMenu` 在 Closed 后 `QueueMenuCleanup`；`LeaveChromeMenuScope` 先判断宿主是否前台，后台清 `pendingRaise` 并以 `ScheduleLayout(false)` 布局。
- **调用链**：`brand / groupMenu / ShowTabMenu` → `ShowChromeMenu` → `ReleaseClosedMenus`。

### 3.4 单行页签手势

- **需求语义**：点击选择、拖动排序、× / 中键分离；拖动不激活软件；栏外 / Esc 取消。
- **关键坑**：`MouseDown` 立即选择会把拖动变成激活；`TextRenderer` 默认忽略 GDI+ clip，溢出文字会盖住 `+`；集合刷新不能无条件取消正在进行的拖动。
- **正确实现**：`TabBar.OnMouseDown` 只记录 `pressed / dragOrigin`；`OnMouseUp` 在目标仍有效且未拖动时 `SelectTab`；`TabManagementPolicy.StartsDrag / InsertionIndex` 复用阈值与插入算法；`OnPaint` 先画 `+`、`SetClip(Viewport, Intersect)`、标题加 `PreserveGraphicsClipping`；`CancelPointer` 统一清理 capture / 定时器 / 预览。

### 3.5 标签组 / 收藏提交

- **需求语义**：命名组是当前窗口快照；打开只追加已打开窗口；收藏是显式启动。
- **关键坑**：先改内存再写盘会在失败时丢掉旧快照；活动索引在过滤前计算会错位；坏库不能用空库覆盖；管理页保存失败不能丢编辑。
- **正确实现**：`CaptureGroup` 过滤 Attached / `CloseRequestedUtc == null`；`AppLibrary.Copy` + `Validate` + `Save` 成功后才替换内存；`LibraryManagerDialog.TryCommit` 先调用宿主 `CommitLibrary` 回调，成功才返回结果；`LaunchFavorite` 只调用 `IApplicationLauncher.Launch`，不 `JoinWindow`。

### 3.6 打包发布

- **需求语义**：只发布本次构建产物；失败保留上一版；运行中的 AppTabs 不能被覆盖或结束。
- **关键坑**：直接往 `dist\release` 复制会留下半成品；旧布局与 `unpack` 布局混用；`dotnet build` 增量复制可能保留旧时间戳。
- **正确实现**：`package.ps1 snapshot` 记录旧 `dist\dev` / `dist\release`、清理 staging；`build` 用 `--no-incremental` 构建 Debug / Release 到 staging；`collect` 校验 zip 条目 / 长度 / SHA256 后 `Publish-PackageSet` 整体替换；`ProcessGuard.Request-CloseRunningAppTabs` 只在 snapshot 步骤列出占用进程并询问，仅 Y 才先正常关闭、12 秒后强制结束 AppTabs 自身；后续 `Assert-OutputUnlocked` 仍拒绝占用且不结束进程。

---

## 4. 数据流 / 接口契约（模块边界）

| 数据 | 位置 / 类型 | 关键约定 |
|---|---|---|
| 窗口身份 | `WindowIdentity` | HWND / PID / TID / 进程启动时间 / 映像路径 / 类名；所有外部窗口操作先匹配 |
| 窗口快照 | `WindowSnapshot`（recovery v1） | 原样式、位置、父 / owner、可见性、HostHandle、模式、`FrameStripped`；JSON 长度 / 数量校验 |
| 设置 | `AppState` / `StateStore`（settings.json v1） | 宿主位置、挂载偏好、热键候选、托盘、单窗口组、活动索引、`RecentJoinLimit` / `RecentApps` / `BorderlessGrouped`；原子写盘 |
| 标签组 / 收藏 | `AppLibrary` / `AppLibraryStore`（library.json v1） | 命名快照、收藏 exe；名称 / ID 唯一；100 收藏 / 50 组 / 每组 100 窗口；8 MB 上限 |
| 恢复日志 | `RecoveryDocument`（recovery.json v1） | 宿主 PID + 启动时间 + 窗口快照；未知查询保留；写入失败不接管 |
| 应用启动 | `ApplicationFavorite.ImagePath` | 本地绝对 `.exe`、无参数、`UseShellExecute=false`、不提权、不自动挂载 |
| 布局 | `Rectangle bounds` | 一律 content HWND 客户区坐标；分组模式由 `NativeWindowApi.Layout` 转屏幕坐标 |
| 用户数据 | `%LOCALAPPDATA%\AppTabs` | settings.json / library.json / recovery.json / app.log / watchdog.log；测试用独立临时目录 |

---

## 6. 运维 / 脚本（启停 / 配置 / 打包）—— 不碰业务即可独立维护

> 做脚本 / 打包任务只读本节与 `scripts/`、根目录 bat，不用打开 `src/` 业务代码。

- 根目录 bat：`setup.bat`（工具链 / 还原）、`build.bat`（测试 + 打包）、`dev.bat`（隔离 Debug，`-BuildOnly` 不启动，占用时交互确认）、`test.bat`（只测试）、`package.bat`（dev + release 打包，占用时交互确认）、`clean.bat`（清 bin / obj / 截图，保留 dist）、`run.bat`（启动 `dist\release\unpack\AppTabs.exe`）。
- `scripts/build.ps1`：构建测试工程 → 运行断言 → 依次调用 `package.ps1 snapshot / build / collect`；snapshot 会列出占用发布目录的 AppTabs 并询问，输入 Y 后才关闭。
- `scripts/dev.ps1`：核验运行中的 AppTabs 映像；Debug 输出 `.runtime\dev`；`-BuildOnly` 只构建、只处理占用 dev 输出的进程；不带参数会启动并等待退出。占用时列出 PID / 路径，交互式 Y 后才关闭。
- `scripts/package.ps1` + `PackageTransaction.ps1` + `ProcessGuard.ps1`：`snapshot` 记录旧产物，并列出占用 dist / staging 的 AppTabs，交互式 Y 后才关闭（先正常退出，12 秒后强制结束）；`build` 以 `--no-incremental` 构建 Debug / Release；`collect` 生成 `dist\dev\AppTabs_<版本>_x64-unpack-debug.zip`、`dist\release\unpack\`、`dist\release\AppTabs_<版本>_x64-unpack.zip`、`SHA256SUMS.txt`，校验 zip 后整体替换，失败回滚。
- 版本号：`Directory.Build.props` 的 `Version` 是唯一来源；脚本用 `dotnet msbuild -getProperty:Version` 读取。不要另写版本常量。
- 测试限制：不注册真实全局热键 / 键盘钩子，不枚举 / 接管用户软件；native helper 只用自建离屏窗口。真实第三方软件与键鼠交互必须由用户实测。

---

## 7. 易踩的坑 / 设计取舍

- **查询失败 ≠ 窗口消失**：`NativeWindowApi.Matches` 抛错是未知状态，`TryClearAllMissing` 不能清空记录；否则会丢失恢复依据。
- **恢复日志先于原生修改**：`HostController.Attach` 的 `store.Save` 失败必须零原生变更；回滚失败保留标签与日志，阻止继续接管。
- **不保存可复用 HWND**：窗口组只存映像 / 类名 / 标题，恢复时重新枚举并唯一匹配；歧义 / 不存在都跳过，不启动软件。
- **默认 × / Ctrl+W / 退出不是关闭软件**：它们只分离并保留软件；关闭软件只走独立菜单并发送 `WM_CLOSE`，不杀进程。
- **最大化客户区**：DWM 实际 inset 可能比 `SM_CXSIZEFRAME + padding` 少 1px；`WM_NCCALCSIZE` 最大化时按 proposed bounds 所在显示器的工作区直接修正，普通状态保留原生 top。
- **顶部 4px**：普通窗口顶部优先 `HTTOP` 缩放；最大化时顶部不再留无效缩放带，页签应能命中。
- **页签绘制裁剪**：`TextRenderer` 默认不遵守 GDI+ clip；溢出标题必须加 `PreserveGraphicsClipping`，并先画 `+`。
- **菜单关闭抢前台**：菜单 Closed 仍在关闭栈内；清理延后到 BeginInvoke，后台关闭时清 `pendingRaise` 并以 `NOZORDER` 布局。
- **窗口缩放边框**：原生 side / bottom thick frame 保留，提供较宽的不可见缩放命中区；顶部原生边框拉进 client，用 3px `Padding` 作为可见 / 可拖的顶部带。不要直接改 client inset，否则命中区会缩到 3px；`WM_GETMINMAXINFO` 用实际 frame 宽，最大化仍按工作区收敛。
- **最大化 / 还原白屏**：窗口尺寸一步跳变时，40ms 的 `layoutTimer` 会让 `content` 的近白背景露出来；`WM_SIZE` 检测到 zoom 状态变化后 `BeginInvoke` `LayoutContent(true)`，不要只等定时器。
- **移动 / 缩放白屏**：系统移动 / 缩放循环里 WinForms Timer 不可靠；`LocationChanged` 与 `content.Resize` 必须同步 `LayoutActiveNow`（必要时先 `SetActiveVisible(true)`），不能只 `ScheduleLayout`。
- **分组边框一致性**：分组默认剥掉目标窗口的 caption / thickframe / sysmenu / min-max，并设为 tool window，避免不同软件边框厚度不一致；设置关闭无边框时回退原生边框。不要把“移除边框”误做成删掉 `WS_THICKFRAME` 却不恢复样式。
- **菜单动作与 modalDepth**：菜单 item 动作若只 `Queue`，会在 `LeaveChromeMenuScope` 之前执行，`OpenPicker` 等 `modalDepth != 0` 的入口会被跳过；工具栏菜单用 `QueueAfterMenu` 等 scope 释放后再运行。
- **无边框分组**：`FrameStripped` 必须在恢复日志落盘前写入 snapshot；`Restore` 写回原 style / extended style 并恢复任务栏；旧 journal 没有该字段时按 false 处理。
- **最近应用菜单**：只列最近成功加入的应用，平铺不嵌套；只匹配当前已打开且未加入的窗口，不自动启动软件；多窗口时打开筛选后的选择器。
- **分组无边框的边界**：目标仍是顶层 tool window，客户区自绘拖动、F11 等应用级操作无法完全阻止；需要强制输入 / 状态托管时用嵌入模式。
- **状态消息位置**：不要恢复 `Dock=Bottom` 状态栏；`status` 标签放在工具栏空白处，错误详情用 tooltip 显示，避免再占 24px 内容高度。
- **托盘图标按需显示**：启动和普通最小化都不创建托盘图标；只有勾选“隐藏到托盘”并由全局快捷键隐藏后才显示，`ShowHost` 后隐藏。不要恢复成常驻托盘。
- **兼容分组 z-order**：分组窗口是独立顶层窗口，点击宿主会让系统把宿主抬到它上面，`content` 背景（Surface）会露出一帧；`MainForm.Activated` 必须立即 `RaiseActive`（`SWP_NOACTIVATE` 抬回），不能只等 40ms 的布局定时器。
- **库坏文件**：读取 / 验证失败要保留原文件并禁止覆盖；空库不能作为 fallback 写回。
- **热键补偿不是事务**：`GlobalHotkey.Apply` 会先注销旧键；新键失败要恢复旧键，旧键恢复也失败时明确提示“当前不可用”。
- **打包占用**：AppTabs 从 `dist\release\unpack` 运行时，`package.bat` 先列出 PID / 路径并默认取消；仅输入 Y 才先正常关闭、12 秒后强制结束 AppTabs 自身。强制结束可能让已嵌入窗口无法还原，绝不对其它软件动手。非交互环境直接失败并说明原因。
- **欢迎页缩放**：内容区尺寸变化时必须 `Invalidate()`，否则文字停留在旧坐标；`content.Resize` 与主窗 `Resize` 都要覆盖。
- **测试 ready 文件**：helper 先用临时文件写完整内容再 `File.Move` 发布，读取端以 `FileShare.ReadWrite` 重试；不要退回直接写最终文件名。

<!-- vibe-coding-starter 2026-10-31.4 · 文档体系版本标记 -->
