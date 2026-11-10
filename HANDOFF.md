# 会话接力

状态日期：2026-11-10；版本 0.2.0；无进行中功能；`dist` 需在运行实例正常退出后重新打包。
验证：606 项断言通过，Release / Debug 构建零警告 / 零错误；欢迎页缩放重绘修复已进入测试构建与 `.runtime\dev`。当前 AppTabs 正从 `dist\release\unpack` 运行，`package.bat` 按规则拒绝覆盖且未结束进程；`dist` 便携包需正常退出后重新打包。覆盖 fake 故障与恢复、自建跨进程嵌入 / 分组混用、DPI 自动回退、默认退出还原、单行页签手势、四边 / 四角命中、两页设置与圆角弹窗、独立标签组 / 收藏库、候选提交失败、fake 启动、空欢迎页缩放重绘。96 / 144 / 192 DPI 弹窗截图为合成布局，不替代真实跨屏验证。未接管用户软件、未注册真实全局热键 / 键盘钩子、未启动真实收藏。
已知测试未稳定项：既有 `DpiChecks` 的 ready 文件创建后读取竞态仍未修；本轮完整回归另有一次 `NativeChecks` 辅助进程 5 秒就绪超时，重跑通过，未修改就绪协议，不能视为已解决。
脚本对齐：`dev.bat` 已加工具链检查与首次还原，`-BuildOnly` 隔离构建通过；`package.bat` 已改为 snapshot → Debug / Release 全新构建 → collect，生成 `dist\dev` debug zip 与 `dist\release\unpack` / zip / SHA256；正式打包成功，另用自建文件锁验证失败时恢复旧 `dist\dev` / `dist\release` 且未结束进程；`build.bat` 复用同一流程。606 项断言在脚本调整后重跑通过。
入口：`dist/release/unpack/AppTabs.exe`；release zip 与 SHA256 在 `dist\release\`，debug zip 在 `dist\dev\`；构建 `build.bat`，结构导航见 ARCHITECTURE.md。
待用户实测：`dev.bat` 真实启动后的等待 / 退出码提示（已验证运行占用时拒绝与 `-BuildOnly`，不启动已有软件）；标题栏拖动 / 双击 / 贴边、单行标签拖拽与取消、弹窗实际缩放、跨屏 DPI 和任务栏交互；品牌菜单 / 设置 / 命名标签组与真实收藏 exe 显式启动；目标第三方软件的焦点、输入、弹窗、标题栏、任务栏和多显示器表现；全局快捷键 / 托盘真实交互。嵌入兼容性不能用合成窗口结果代替。
默认 × / Ctrl+W / 退出都会分离并保留软件。异常后只恢复仍存活的窗口，无法复活已销毁窗口或保证未保存内容。
版本控制：当前分支 main，初版源码纳入本地提交；未配置远程、未推送或发布 GitHub。原 ExplorerTabs 仓库未修改。
