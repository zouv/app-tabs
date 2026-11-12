# 接力进度 / Handoff · AppTabs

> **这是什么**：AI 断点续作的「交接包」。一次对话没干完，**下一个 AI 会话先读本文件**，就知道「做到哪、还剩什么、卡在哪、下一步从哪开始」。
>
> **维护规则见 AGENTS §5**：只留还在路上的内容；任务完结时在写入 CHANGELOG、ROADMAP 打 ✅ 的同一会话内删掉对应内容；长期有效的坑与理由迁到 ARCH §7；全文不超过 8,000 字。
>
> **接手步骤**：读 `AGENTS.md` → 按「当前任务」的范围查 ARCH §0.5，只读相关文件 → 从「下一步」第 1 条开始 → 每完成一个子步骤就更新本文件。

---

## 状态快照

- **最近更新**：2026-11-12
- **整体状态**：✅ 无进行中的开发任务；0.3.0 已提交、推送、打包并上传 GitHub Release，等待用户实测
- **工作分支**：`main`（已推送到 `origin/main`；`v0.3.0` 标签与 Release 已创建）
- **最后验证状态**：`test.bat` ✅ 0 警告 / 0 错误，638 项断言通过；`sh check-docs.sh` ✅ 0 警告；`dist\release\unpack` 与 zip 均为 0.3.0，SHA256 匹配，Release 资产已上传

---

## 当前任务（In Progress）

- **目标**：无；等待下一个开发需求。
- **范围 / 涉及文件**：无。
- **完成定义（Definition of Done）**：无。

> 当前：0.3.0 发布完成，等待真实交互验收。

---

## 进行中（子步骤 / 半成品）

- （无。发布提交、打包与 Release 资产已完成。）

## 待验收（代码已完成，等用户确认）

1. **0.3.0 发布包**：已确认 `dist\release\unpack\AppTabs.exe` / `AppTabs.Core.dll` 版本为 0.3.0，zip SHA256 匹配；GitHub Release `v0.3.0` 的 release zip / debug zip / `SHA256SUMS.txt` 已上传。用户验收时可再次核对。
2. **真实交互验收**：标题栏拖动 / 双击 / 贴边、页签拖拽排序与取消、跨屏 DPI、任务栏、托盘、全局快捷键，以及真实第三方软件的焦点 / 输入 / 弹窗仍需用户实测。
3. **最近应用菜单**：工具栏“加入”应平铺最近成功加入的应用（无二级菜单，最多数量按设置 1–20）；同一应用多窗口时打开筛选选择器；无窗口 / 已在标签时禁用且不自动启动。
4. **无边框分组**：新加入的分组窗口应无原生标题栏 / 边框、无独立任务栏 / Alt+Tab 项，拖动 / 缩放 / 最大化由 AppTabs 接管；关闭“分组窗口使用无边框”后应回退原生边框，分离后样式与任务栏恢复。
5. **UI 细化**：边框视觉上只剩 DWM 细线、左右 / 底部缩放顺手；最大化 / 还原与拖动标题栏不白屏；状态消息在工具栏；启动后无托盘图标，隐藏到托盘后才出现。

## 阻塞 / 待决策（Blockers）

- （无。）

---

## 下一步（Next Steps）

1. 用户用 0.3.0 产物做真实交互验收（见「待验收」第 2–5 条）。
2. 发现问题时记录模式（分组 / 嵌入）、软件名、触发动作与现象，再建新任务。
3. 若无需改动，删除已完成的验收条目并开始下一轮规划。

---

## 本任务验证（Verify）

- 构建 / 测试：`test.bat` ✅ 0 警告 / 0 错误，638 项断言通过（含最近应用策略 / 状态读写、设置页新字段、无边框分组样式剥离 / 恢复、任务栏移除 / 恢复、移动 / 最大化 / 还原立即布局）；ready 文件原子发布后连续通过。
- 开发构建：`dev.bat -BuildOnly`（不启动程序）。
- 文档体检：`sh check-docs.sh` ✅ 0 警告。
- 打包：`package.bat` ✅ 0.3.0 的 `dist\dev\AppTabs_0.3.0_x64-unpack-debug.zip`、`dist\release\unpack\`、release zip 与 `SHA256SUMS.txt`；程序集 0.3.0，SHA256 匹配。
- Release：`v0.3.0` 标签 / GitHub Release 已创建，三个资产已上传；debug build-info 指向发布提交。
- 不注册真实全局热键 / 键盘钩子，不操作已有用户软件；native helper 只使用自建离屏窗口。

---

## 本任务决策（Decisions）

- 兼容分组窗口是独立顶层窗口：点击宿主会让系统把宿主抬到它上面并露出 `content` 的 Surface 背景；修复放在 `MainForm.Activated` → `HostController.RaiseActive`，立即 `SWP_NOACTIVATE` 抬回活动分组窗口，而非等 40ms `layoutTimer`。
- 不改变 `TabBar` 的“按下不选择”手势语义；`MouseDown` 直接选中会提前切换前台并影响拖动中的 Esc / 弹窗检查，暂不采用。
- `dev.bat` / `package.bat` 的占用处理参考 explorer-tabs：只针对 AppTabs 自身进程，交互式列出 PID / 路径、默认取消，输入 Y 后先 `CloseMainWindow`，12 秒超时再 `Kill`；`package` 只拦截占用 dist / staging 的进程，非交互环境直接失败。
- 边框采用 explorer-tabs 方案：保留原生 side / bottom thick frame（约 8px 不可见缩放命中区），`WM_NCCALCSIZE` 只把顶部原生边框拉进 client；普通状态用 3px 顶部 `Padding` 作为可见 / 可拖带，最大化时归零；`WM_GETMINMAXINFO` 使用实际 `Width - ClientSize.Width` / `Height - ClientSize.Height`。
- 最大化 / 还原：`ProcessChromeMessage` 的 `WM_SIZE` 检测 `IsZoomed` 变化后 `BeginInvoke` `LayoutContent(true)`，在 40ms 合并定时器之前重新布局活动窗口，避免 `content` 近白背景露出。
- 移动 / 缩放：`LocationChanged` 与 `content.Resize` 同步调用 `LayoutActiveNow`（必要时先 `SetActiveVisible(true)`），系统 move / resize 模态循环里不再依赖定时器；定时器仍保留作合并兜底。
- 工具栏“加入”“分离”改为动态菜单：加入平铺最近成功加入的应用（`RecentAppsPolicy`，去重 / 置顶 / 按设置截断），同一应用多窗口时打开筛选选择器；分离提供当前 / 全部；移除“加入全部窗口”。
- 无边框分组：`HostController.Attach` 在恢复日志落盘前写 `FrameStripped`；`NativeWindowApi.Attach` 去掉 caption / thickframe / sysmenu / min-max、清 edge ex 样式、设 `WS_EX_TOOLWINDOW` 并调用 `UpdateTaskbar(Remove)`；`Restore` 写回原样式并 `UpdateTaskbar(Add)`。设置默认开启，可回退。
- 分组窗口保持顶层、不 SetParent，因此 System-DPI 应用仍可加入；客户区自绘拖动 / F11 等应用级操作无法完全阻止，需要更强托管时用嵌入模式。
- 测试 ready 文件改为临时文件 + `File.Move` 原子发布，读取端 `FileShare.ReadWrite` 重试，消除已知竞态。
- 状态栏去掉 `Dock=Bottom`，`status` 标签移入工具栏中间，`LayoutToolbar` 在 group 按钮与 search 之间布局；消息同时写入 tooltip，长文本不丢。
- 托盘按需创建：`OnReady` 不再创建 `NotifyIcon`；`ToggleHost` 隐藏到托盘时 `ShowTrayIcon`，`ShowHost` 调用 `HideTrayIcon`；托盘菜单只在首次隐藏时创建。
- 设置页把“切后台时隐藏到系统托盘”移到快捷键页，并把设置对话框从 560 增高到 640，保证 96 / 144 / 192 DPI 下说明不被裁切。

---

## 待解问题 / 给用户的提问（Open Questions）

- （无。）
