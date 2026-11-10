# 接力进度 / Handoff · AppTabs

> **这是什么**：AI 断点续作的「交接包」。一次对话没干完，**下一个 AI 会话先读本文件**，就知道「做到哪、还剩什么、卡在哪、下一步从哪开始」。
>
> **维护规则见 AGENTS §5**：只留还在路上的内容；任务完结时在写入 CHANGELOG、ROADMAP 打 ✅ 的同一会话内删掉对应内容；长期有效的坑与理由迁到 ARCH §7；全文不超过 8,000 字。
>
> **接手步骤**：读 `AGENTS.md` → 按「当前任务」的范围查 ARCH §0.5，只读相关文件 → 从「下一步」第 1 条开始 → 每完成一个子步骤就更新本文件。

---

## 状态快照

- **最近更新**：2026-11-10
- **整体状态**：✅ 无进行中的开发任务；0.2.0 已提交、推送并上传 GitHub Release；本地 `dist` 待重打
- **工作分支**：`main`（已推送到 `origin/main`；`v0.2.0` 标签与 Release 已创建）
- **最后验证状态**：`test.bat` ✅ 0 警告 / 0 错误，606 项断言通过；`sh check-docs.sh` ✅ 0 警告；独立工作树打包 ✅ `AppTabs_0.2.0_x64-unpack.zip` / debug zip / SHA256 已上传

---

## 当前任务（In Progress）

- **目标**：无；等待下一个开发需求。
- **范围 / 涉及文件**：无。
- **完成定义（Definition of Done）**：无。

> 当前：无进行中的任务。

---

## 进行中（子步骤 / 半成品）

- （无。）

## 待验收（代码已完成，等用户确认）

1. **本地 `dist` 重打**：GitHub Release `v0.2.0` 已上传；当前 `dist\release\unpack` 仍是旧包，且 AppTabs 正从该目录运行。验收看点：正常退出后双击 `package.bat`，生成 0.2.0 的 `dist\dev`、`dist\release\unpack\`、release zip 与 `SHA256SUMS.txt`。`package.bat` 在运行中会拒绝覆盖且不结束进程。
2. **真实交互验收**：标题栏拖动 / 双击 / 贴边、页签拖拽排序与取消、跨屏 DPI、任务栏、托盘、全局快捷键，以及真实第三方软件的焦点 / 输入 / 弹窗仍需用户实测；合成窗口测试不能替代。

## 阻塞 / 待决策（Blockers）

- 当前 AppTabs 仍从 `dist\release\unpack` 运行，本地发布包无法替换。需要用户通过主窗或托盘**正常退出**，等待窗口恢复结束后重试 `package.bat`；不要强制结束进程。

---

## 下一步（Next Steps）

1. 用户正常退出 AppTabs。
2. 在项目根双击 `package.bat`，确认版本为 0.2.0、产物布局与 SHA256 正确。
3. 用新包做一轮真实交互验收；通过后删除「待验收」第 1 条。

---

## 本任务验证（Verify）

- 构建 / 测试：`test.bat`（当前 606 项断言通过）。
- 开发构建：`dev.bat -BuildOnly`（不启动程序）。
- 文档体检：`sh check-docs.sh`。
- 打包：`package.bat`（需没有 AppTabs 从 `dist\release\unpack` 运行）。
- 不注册真实全局热键 / 键盘钩子，不操作已有用户软件；native helper 只使用自建离屏窗口。

---

## 本任务决策（Decisions）

- 0.2.0 已合并到 `main` 并推送；Release `v0.2.0` 的资产由提交 `0a9cf06` 的独立工作树构建，避免覆盖正在运行的本地 `dist`。
- 发布包采用 `dist\dev` debug zip + `dist\release\unpack` + release zip + SHA256 的 ExplorerTabs 布局；运行中只拒绝、不结束进程。

---

## 待解问题 / 给用户的提问（Open Questions）

- （无。）
