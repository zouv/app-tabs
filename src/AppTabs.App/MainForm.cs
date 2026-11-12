using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using AppTabs.Core;

namespace AppTabs.App
{
    public sealed partial class MainForm : Form
    {
        private readonly IWindowApi windows;
        private readonly HostController controller;
        private readonly StateStore stateStore;
        private AppState state;
        private readonly bool services;
        private readonly TabBar tabs = new TabBar { Name = "tabs" };
        private readonly Panel content = new Panel { Name = "content", Dock = DockStyle.Fill, BackColor = Theme.Surface };
        private readonly Label status = new Label { Name = "status", TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Muted, BackColor = Theme.Toolbar, AutoEllipsis = true, AutoSize = false };
        private readonly IconButton detach = new IconButton(UiGlyph.DetachWindow, "分离标签…") { Name = "detach" };
        private readonly ToolStripMenuItem restoreGroup = new ToolStripMenuItem("恢复窗口组");
        private readonly ToolStripMenuItem saveGroup = new ToolStripMenuItem("保存窗口组");
        private readonly Timer monitor = new Timer { Interval = 1000 };
        private readonly Timer layoutTimer = new Timer { Interval = 40 };
        private KeyboardHook? keyboard;
        private GlobalHotkey? hotkey;
        private NotifyIcon? tray;
        private int modalDepth;
        private bool exiting, ready, pendingRaise;
        private Rectangle lastNormal;
        private bool lastMaximized;
        public HostController Controller => controller;
        public IntPtr ContentHandle => content.Handle;
        public Rectangle ContentBounds => content.ClientRectangle;
        protected override bool ShowWithoutActivation => !services;

        public MainForm(IWindowApi windows, RecoveryJournal journal, StateStore stateStore, AppState state, bool services = true,
            IAppLibraryStore? libraryStore = null, IApplicationLauncher? applicationLauncher = null)
        {
            this.windows = windows; this.stateStore = stateStore; this.state = state; this.services = services;
            this.libraryStore = libraryStore ?? new AppLibraryStore(Path.Combine(Path.GetDirectoryName(stateStore.FilePath)!, "library.json"));
            this.applicationLauncher = applicationLauncher ?? new ApplicationLauncher();
            ReadLibrary();
            controller = new HostController(windows, journal);
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96F, 96F);
            FormBorderStyle = FormBorderStyle.None;
            Font = new Font("Microsoft YaHei UI", 9F); Text = "AppTabs"; BackColor = Theme.Chrome;
            DoubleBuffered = true;
            MinimumSize = new Size(780, 460); StartPosition = FormStartPosition.Manual;
            var requested = new Rectangle(state.X, state.Y, state.Width, state.Height);
            var screen = Screen.FromRectangle(requested).WorkingArea;
            Bounds = new Rectangle(Math.Max(screen.Left, Math.Min(requested.X, screen.Right - Math.Min(requested.Width, screen.Width))),
                Math.Max(screen.Top, Math.Min(requested.Y, screen.Bottom - Math.Min(requested.Height, screen.Height))),
                Math.Min(requested.Width, screen.Width), Math.Min(requested.Height, screen.Height));
            lastNormal = Bounds; lastMaximized = state.Maximized;
            applicationIcon = AppIcon.Load(); Icon = applicationIcon;
            content.Paint += PaintWelcome;
            content.DoubleClick += (_, __) => { if (controller.Sessions.Count == 0) OpenPicker(); };
            Controls.Add(content);
            InitializeChrome();
            detach.Click += (_, __) => ShowDetachMenu();
            saveGroup.Click += (_, __) => Queue(SaveGroup);
            restoreGroup.Click += (_, __) => Queue(RestoreGroup);
            tabs.SelectTab += session => TryAction(() => Select(session));
            tabs.DetachTab += session => Queue(() => Detach(session));
            tabs.ReorderTab += (session, index) => TryAction(() => { controller.MoveTab(session, index); RefreshUi(); });
            tabs.ContextTab += ShowTabMenu;
            tabs.AddWindow += () => Queue(OpenPicker);
            layoutTimer.Tick += (_, __) => { layoutTimer.Stop(); LayoutContent(pendingRaise); pendingRaise = false; };
            monitor.Tick += (_, __) => MonitorWindows();
            LocationChanged += (_, __) => { TrackAndLayout(); LayoutActiveNow(); };
            Resize += (_, __) => { TrackAndLayout(); if (controller.Sessions.Count == 0) content.Invalidate(); };
            content.Resize += (_, __) =>
            {
                if (controller.Sessions.Count == 0) content.Invalidate();
                LayoutActiveNow();
                ScheduleLayout(false);
            };
            Activated += (_, __) =>
            {
                // Clicking the inactive host raises it above an active grouped window, exposing
                // the content surface until the 40 ms layout timer raises the window back.
                // Re-raise immediately; keep the full layout scheduled for geometry changes.
                if (HostVisible && modalDepth == 0 && controller.Active?.Snapshot.Mode == HostingMode.Grouped)
                    try { controller.RaiseActive(); } catch (Exception error) { SetStatus(error.Message); }
                ScheduleLayout(true);
            };
            Shown += (_, __) => OnReady();
            RefreshUi();
        }

        private bool HostVisible => Visible && WindowState != FormWindowState.Minimized && !exiting;
        private void OnReady()
        {
            if (ready) return; ready = true;
            if (state.Maximized) WindowState = FormWindowState.Maximized;
            if (services)
            {
                try { keyboard = new KeyboardHook(action => Queue(() => HandleAction(action))); }
                catch (Exception error) { SetStatus("标签快捷键未启用：" + error.Message); }
                hotkey = new GlobalHotkey(Handle);
                try { hotkey.Apply(state.HotkeyChoice); }
                catch (Exception error) { SetStatus(error.Message); }
            }
            monitor.Start(); RefreshUi();
            if (libraryError.Length != 0) SetStatus(libraryError);
        }
        public WindowSession JoinWindow(WindowIdentity identity, AttachPreference preference)
        {
            var chosen = preference == AttachPreference.Grouped ? HostingMode.Grouped : HostingMode.Embedded;
            string fallback = "";
            if (preference == AttachPreference.Auto)
            {
                var snapshot = windows.Capture(identity);
                try { windows.ValidateAttach(snapshot, content.Handle, chosen); }
                catch (InvalidOperationException error)
                {
                    windows.ValidateAttach(snapshot, content.Handle, HostingMode.Grouped);
                    chosen = HostingMode.Grouped; fallback = error.Message;
                }
            }
            var session = controller.Attach(identity, content.Handle, chosen, content.ClientRectangle,
                restoreCreatedVisible: false, borderlessGrouped: state.BorderlessGrouped != false);
            Select(session);
            SetStatus(fallback.Length == 0 ? "已加入：" + session.DisplayTitle + " · " + ModeName(chosen) : "已用" + ModeName(chosen) + "加入：" + fallback);
            RememberRecentApp(session);
            return session;
        }
        private void RememberRecentApp(WindowSession session)
        {
            try
            {
                RecentAppsPolicy.Remember(state.RecentApps, session.Identity.ImagePath, session.Identity.ClassName,
                    session.Title, state.RecentJoinLimit);
                stateStore.Save(CaptureState());
            }
            catch (Exception error) { SetStatus("最近应用记录保存失败：" + error.Message); }
        }
        private void OpenPicker()
        {
            if (modalDepth != 0 || exiting) return;
            using (var picker = new WindowPicker(controller.Sessions.Select(s => s.Identity.Handle)))
            {
                if (RunModal(() => picker.ShowDialog(this)) != DialogResult.OK) return;
                JoinWindows(picker.SelectedWindows);
            }
        }
        private void JoinWindows(IEnumerable<WindowIdentity> identities)
        {
            var errors = new List<string>();
            foreach (var identity in identities)
            {
                try { JoinWindow(identity, state.Preference); }
                catch (Exception error) { errors.Add(identity.Title + "：" + error.Message); }
            }
            RefreshUi();
            if (errors.Count > 0) ShowErrors(string.Join("\n", errors));
        }
        private void ShowAddWindowsMenu()
        {
            if (modalDepth != 0 || exiting) return;
            var menu = new ContextMenuStrip { ShowImageMargin = false, ShowItemToolTips = true };
            IReadOnlyList<WindowIdentity> candidates;
            try { candidates = NativeWindowApi.DiscoverWindows(controller.Sessions.Select(s => s.Identity.Handle)); }
            catch (Exception error) { SetStatus("读取窗口失败：" + error.Message); candidates = Array.Empty<WindowIdentity>(); }
            int limit = Math.Max(RecentAppsPolicy.MinimumLimit, Math.Min(RecentAppsPolicy.MaximumLimit, state.RecentJoinLimit));
            var recent = state.RecentApps.Take(limit).ToArray();
            if (recent.Length == 0)
                menu.Items.Add(new ToolStripMenuItem("暂无最近加入的应用") { Enabled = false });
            foreach (var app in recent)
            {
                string appName = Path.GetFileNameWithoutExtension(app.ImagePath);
                if (string.IsNullOrWhiteSpace(appName)) appName = app.ImagePath;
                var matches = candidates.Where(candidate => RecentAppsPolicy.Matches(app, candidate)).ToArray();
                var item = new ToolStripMenuItem(appName)
                {
                    ToolTipText = (string.IsNullOrWhiteSpace(app.LastTitle) ? appName : app.LastTitle) + "\n" + app.ImagePath
                };
                if (matches.Length == 0)
                {
                    bool attached = controller.Sessions.Any(session => RecentAppsPolicy.Matches(app, session.Identity));
                    item.Text = appName + (attached ? "（已在标签中）" : "（当前未打开）");
                    item.Enabled = false;
                }
                else if (matches.Length == 1)
                {
                    var chosen = matches[0];
                    item.Click += (_, __) => QueueAfterMenu(() => JoinWindows(new[] { chosen }));
                }
                else
                {
                    item.Click += (_, __) => QueueAfterMenu(() => PickWindows(app, appName));
                }
                menu.Items.Add(item);
            }
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("选择窗口…", null, (_, __) => QueueAfterMenu(OpenPicker));
            ShowChromeMenu(menu, join, new Point(0, join.Height), true);
        }
        private void PickWindows(RecentApp app, string appName)
        {
            if (modalDepth != 0 || exiting) return;
            var excluded = controller.Sessions.Select(s => s.Identity.Handle).ToArray();
            Func<IReadOnlyList<WindowIdentity>> loader = () => NativeWindowApi.DiscoverWindows(excluded)
                .Where(identity => RecentAppsPolicy.Matches(app, identity)).ToArray();
            using (var picker = new WindowPicker(excluded, loader))
            {
                picker.Text = "加入窗口 — " + appName;
                if (RunModal(() => picker.ShowDialog(this)) != DialogResult.OK) return;
                JoinWindows(picker.SelectedWindows);
            }
        }
        private void ShowDetachMenu()
        {
            if (modalDepth != 0 || exiting) return;
            var menu = new ContextMenuStrip { ShowImageMargin = false, ShowItemToolTips = true };
            var active = controller.Active;
            var current = menu.Items.Add("分离当前标签", null, (_, __) => { if (active != null) QueueAfterMenu(() => Detach(active)); });
            current.Enabled = active != null;
            current.ToolTipText = "恢复当前标签对应的软件窗口，软件保持运行。";
            var all = menu.Items.Add("分离所有标签", null, (_, __) => QueueAfterMenu(() =>
            {
                var errors = controller.DetachAll(); RefreshUi();
                if (errors.Count > 0) ShowErrors(string.Join("\n", errors));
                else SetStatus("已分离所有窗口，各软件保持运行。");
            }));
            all.Enabled = controller.Sessions.Count > 0;
            all.ToolTipText = "恢复所有标签对应的软件窗口，软件保持运行。";
            ShowChromeMenu(menu, detach, new Point(0, detach.Height), true);
        }
        private void QueueAfterMenu(Action action)
        {
            // The chrome menu owns a modal scope until its Closed cleanup is posted. Queueing
            // twice puts the action behind that cleanup so OpenPicker's modal guard passes.
            Queue(() => Queue(action));
        }
        public void Select(WindowSession session)
        {
            if (controller.Active != null && controller.Active != session)
            {
                IntPtr popup = NativeUi.GetLastActivePopup(controller.Active.Identity.Hwnd);
                if (popup != controller.Active.Identity.Hwnd && NativeUi.IsWindowVisible(popup))
                    throw new InvalidOperationException("请先处理当前软件的弹窗，再切换标签。");
            }
            controller.Activate(session, content.ClientRectangle, HostVisible && modalDepth == 0, HostVisible && modalDepth == 0);
            RefreshUi();
            if (HostVisible && modalDepth == 0) NativeWindowApi.FocusAfterShortcut(session.Snapshot);
        }
        public void Detach(WindowSession session)
        {
            TryAction(() =>
            {
                var successor = TabManagementPolicy.CloseSuccessor(controller.Sessions, controller.Active, session);
                controller.Detach(session);
                if (controller.Active == null && successor != null && controller.Sessions.Contains(successor)) Select(successor);
                RefreshUi(); SetStatus("已分离，软件窗口保持运行。");
            });
        }
        private void SwitchMode(WindowSession session, HostingMode target)
        {
            TryAction(() =>
            {
                if (session.Snapshot.Mode == target) return;
                // Validate the embedded context before detaching, when possible.
                if (target == HostingMode.Embedded)
                {
                    var original = session.Snapshot;
                    windows.ValidateAttach(original, content.Handle, target);
                }
                var identity = session.Identity; string? label = session.CustomTitle;
                controller.Detach(session);
                try
                {
                    var next = JoinWindow(identity, target == HostingMode.Embedded ? AttachPreference.Embedded : AttachPreference.Grouped);
                    if (label != null) controller.RenameTab(next, label);
                }
                finally { RefreshUi(); if (controller.Active == null) { var next = controller.Sessions.FirstOrDefault(s => s.State == SessionState.Attached); if (next != null) Select(next); } }
            });
        }
        private void ShowTabMenu(WindowSession session, Point location)
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("分离并保留软件", null, (_, __) => Queue(() => Detach(session)));
            menu.Items.Add("重命名标签…", null, (_, __) => Queue(() => Rename(session)));
            menu.Items.Add("恢复默认名称", null, (_, __) => Queue(() => { controller.ResetTabTitle(session); RefreshUi(); }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("使用兼容分组", null, (_, __) => Queue(() => SwitchMode(session, HostingMode.Grouped)));
            menu.Items.Add("使用实验性嵌入", null, (_, __) => Queue(() => SwitchMode(session, HostingMode.Embedded)));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("分离所有窗口", null, (_, __) => Queue(() =>
            {
                var errors = controller.DetachAll(); RefreshUi();
                if (errors.Count > 0) ShowErrors(string.Join("\n", errors));
                else SetStatus("已分离所有窗口，各软件保持运行。");
            }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("关闭这个软件窗口…", null, (_, __) => Queue(() => CloseSoftware(session)));
            ShowChromeMenu(menu, tabs, location, true);
        }
        private void Rename(WindowSession session)
        {
            using (var dialog = new TextDialog("重命名标签", "名称只影响 AppTabs 标签。", session.DisplayTitle, services))
                if (RunModal(() => dialog.ShowDialog(this)) == DialogResult.OK)
                    TryAction(() => { controller.RenameTab(session, dialog.Value); RefreshUi(); });
        }
        private void CloseSoftware(WindowSession session)
        {
            if (!controller.Sessions.Contains(session)) return;
            if (RunModal(() => MessageBox.Show(this, "向“" + session.DisplayTitle + "”发送关闭请求？\n请留意软件自己的未保存提示。", "关闭软件窗口", MessageBoxButtons.OKCancel, MessageBoxIcon.Question)) != DialogResult.OK) return;
            TryAction(() => { controller.RetryClose(session); controller.RequestClose(session); SetStatus("已发送关闭请求；如果软件提示保存，请在软件中处理。"); });
        }
        private void SaveGroup()
        {
            var snapshot = CaptureGroup("窗口组");
            var candidate = CaptureState(); candidate.Group = snapshot.Windows; candidate.ActiveIndex = snapshot.ActiveIndex;
            stateStore.Save(candidate); state = candidate;
            RefreshUi(); SetStatus("窗口组已保存；恢复时重新匹配已打开的窗口。");
        }
        private void RestoreGroup() => RestoreSnapshot(state.Group, state.ActiveIndex);
        private void SearchTabs()
        {
            if (controller.Sessions.Count == 0 || modalDepth != 0) return;
            using (var dialog = new TabSearchDialog(controller.Sessions, services))
                if (RunModal(() => dialog.ShowDialog(this)) == DialogResult.OK && dialog.Selected != null)
                    TryAction(() => Select(dialog.Selected));
        }
        private void OpenSettings()
        {
            using (var dialog = new SettingsDialog(state, services))
            {
                if (RunModal(() => dialog.ShowDialog(this)) != DialogResult.OK) return;
                TryAction(() =>
                {
                    var candidate = CaptureState();
                    candidate.Preference = dialog.Preference;
                    candidate.HotkeyChoice = dialog.HotkeyChoice;
                    candidate.HideToTray = dialog.HideToTray;
                    candidate.RecentJoinLimit = dialog.RecentJoinLimit;
                    candidate.BorderlessGrouped = dialog.BorderlessGrouped;
                    RecentAppsPolicy.Trim(candidate.RecentApps, candidate.RecentJoinLimit);
                    SettingsCommit.Save(state, candidate, stateStore.Save, hotkey == null ? (Action<int>?)null : hotkey.Apply,
                        hotkey == null || hotkey.Registered);
                    state = candidate; SetStatus("设置已保存；挂载模式只影响新加入窗口。");
                });
            }
        }
        private void MonitorWindows()
        {
            if (modalDepth != 0 || exiting) return;
            try
            {
                var previous = controller.Sessions.ToArray(); var oldActive = controller.Active;
                var errors = controller.PruneLost();
                if (errors.Count > 0) SetStatus(errors[0]);
                if (controller.Active == null && controller.Sessions.Count > 0)
                {
                    int index = Array.IndexOf(previous, oldActive);
                    var next = previous.Take(Math.Max(0, index)).Reverse().FirstOrDefault(s => controller.Sessions.Contains(s) && s.State == SessionState.Attached) ??
                        controller.Sessions.FirstOrDefault(s => s.State == SessionState.Attached);
                    if (next != null) Select(next);
                }
                bool changed = previous.Length != controller.Sessions.Count;
                foreach (var session in controller.Sessions)
                {
                    if (session.State != SessionState.Attached) continue;
                    string title = NativeWindowApi.ReadTitle(session.Identity.Hwnd);
                    if (title.Length > 0 && title != session.Title) { session.Title = title; changed = true; }
                }
                if (!HostVisible) controller.SetActiveVisible(false);
                if (changed) RefreshUi();
            }
            catch (Exception error) { SetStatus("窗口状态暂时无法读取：" + error.Message); }
        }
        private void LayoutActiveNow()
        {
            // System move/resize loops run a modal message loop; the coalescing timer does not
            // fire reliably there. Follow the host geometry immediately so the content panel
            // never shows through while dragging or maximizing.
            if (!ready || exiting || modalDepth != 0 || controller.Active == null || !HostVisible) return;
            try
            {
                // A hidden grouped window (for example after a menu scope) must come back
                // before the geometry update; otherwise the content panel stays exposed.
                if (!NativeUi.IsWindowVisible(controller.Active.Identity.Hwnd)) controller.SetActiveVisible(true);
                controller.LayoutActive(content.ClientRectangle);
            }
            catch (Exception error) { SetStatus(error.Message); }
        }
        private void TrackAndLayout()
        {
            if (ready && Visible && WindowState != FormWindowState.Minimized)
            {
                lastMaximized = WindowState == FormWindowState.Maximized;
                if (WindowState == FormWindowState.Normal) lastNormal = Bounds;
            }
            UpdateKeyboardScope(); ScheduleLayout(false);
        }
        private void ScheduleLayout(bool raise)
        { if (!ready || exiting) return; pendingRaise |= raise; if (!layoutTimer.Enabled) layoutTimer.Start(); }
        private void LayoutContent(bool raise)
        {
            if (modalDepth != 0 || exiting) return;
            try
            {
                if (!HostVisible) controller.SetActiveVisible(false);
                else if (controller.Active != null) controller.Activate(controller.Active, content.ClientRectangle, raise, true);
            }
            catch (Exception error) { SetStatus(error.Message); }
        }
        private T RunModal<T>(Func<T> action)
        {
            modalDepth++; UpdateKeyboardScope();
            try
            {
                if (controller.Active?.Snapshot.Mode == HostingMode.Grouped)
                    try { controller.SetActiveVisible(false); } catch (Exception error) { SetStatus(error.Message); }
                return action();
            }
            finally { LeaveChromeMenuScope(); }
        }
        private void HandleAction(TabAction action)
        {
            if (modalDepth != 0 || exiting || !HostVisible) return;
            TryAction(() =>
            {
                if (action == TabAction.Detach) { if (controller.Active != null) Detach(controller.Active); return; }
                if (action == TabAction.Search) { SearchTabs(); return; }
                WindowSession? next;
                if (action == TabAction.Next || action == TabAction.Previous) next = controller.Adjacent(action == TabAction.Next ? 1 : -1);
                else { int index = (int)action - (int)TabAction.First; next = index >= 0 ? controller.Sessions.ElementAtOrDefault(index) : null; }
                if (next != null) Select(next);
            });
        }
        private void UpdateKeyboardScope() => keyboard?.SetScope(Handle, controller.Active?.Identity.Hwnd ?? IntPtr.Zero,
            modalDepth == 0 && HostVisible && controller.Sessions.Count > 0);
        private void RefreshUi()
        {
            tabs.Bind(controller.Sessions, controller.Active); content.Invalidate();
            detach.Enabled = controller.Active != null; saveGroup.Enabled = controller.Sessions.Count > 0;
            restoreGroup.Enabled = state.Group.Count > 0; search.Enabled = controller.Sessions.Count > 0;
            Text = controller.Active == null ? "AppTabs" : controller.Active.DisplayTitle + " — AppTabs";
            UpdateKeyboardScope();
            if (status.Text.Length == 0) SetStatus("Ctrl+Tab 切换 · Ctrl+W 分离 · Ctrl+F 搜索 · Ctrl+Alt+Space 呼出");
        }
        private void PaintWelcome(object? sender, PaintEventArgs e)
        {
            if (controller.Sessions.Count > 0) return;
            int mid = content.Width / 2, top = Math.Max(ChromePixels(24), content.Height / 2 - ChromePixels(110));
            int iconSize = ChromePixels(48);
            if (welcomeIcon != null) e.Graphics.DrawIcon(welcomeIcon, new Rectangle(mid - iconSize / 2, top, iconSize, iconSize));
            using (var titleFont = new Font(Font.FontFamily, 22, FontStyle.Bold))
                TextRenderer.DrawText(e.Graphics, "一个窗口，管理多个软件", titleFont, new Rectangle(ChromePixels(10), top + ChromePixels(66), content.Width - ChromePixels(20), ChromePixels(52)), Theme.Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(e.Graphics, "点击“加入窗口”，把已打开的软件收进标签页。", Font, new Rectangle(ChromePixels(10), top + ChromePixels(126), content.Width - ChromePixels(20), ChromePixels(30)), Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(e.Graphics, "可随时分离；退出 AppTabs 时，各软件恢复为独立窗口。", Font, new Rectangle(ChromePixels(10), top + ChromePixels(162), content.Width - ChromePixels(20), ChromePixels(30)), Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        private void ToggleHost()
        {
            if (modalDepth != 0) return;
            var foreground = NativeUi.GetForegroundWindow();
            var root = NativeUi.GetAncestor(foreground, 2);
            if (HostVisible && (root == Handle || foreground == controller.Active?.Identity.Hwnd))
            {
                controller.SetActiveVisible(false);
                if (state.HideToTray) { Hide(); ShowTrayIcon(); }
                else { WindowState = FormWindowState.Minimized; HideTrayIcon(); }
                UpdateKeyboardScope();
            }
            else ShowHost();
        }
        private void ShowHost()
        { Show(); WindowState = lastMaximized ? FormWindowState.Maximized : FormWindowState.Normal; NativeUi.SetForegroundWindow(Handle); ScheduleLayout(true); HideTrayIcon(); UpdateKeyboardScope(); }
        private void ShowTrayIcon()
        {
            if (IsDisposed) return;
            if (tray == null)
            {
                var menu = new ContextMenuStrip();
                menu.Items.Add("显示 AppTabs", null, (_, __) => ShowHost());
                menu.Items.Add("加入窗口…", null, (_, __) => { ShowHost(); Queue(OpenPicker); });
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("退出并保留所有软件", null, (_, __) => Close());
                tray = new NotifyIcon { Icon = Icon, Text = "AppTabs", ContextMenuStrip = menu };
                tray.DoubleClick += (_, __) => ShowHost();
            }
            tray.Visible = true;
        }
        private void HideTrayIcon() { if (tray != null) tray.Visible = false; }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x312 && m.WParam.ToInt32() == GlobalHotkey.Id) { Queue(() => TryAction(ToggleHost)); return; }
            if (ProcessChromeMessage(ref m)) return;
            base.WndProc(ref m);
        }
        private AppState CaptureState()
        {
            var candidate = state.Copy();
            candidate.X = lastNormal.X; candidate.Y = lastNormal.Y; candidate.Width = lastNormal.Width; candidate.Height = lastNormal.Height;
            candidate.Maximized = lastMaximized;
            return candidate;
        }
        private void PersistState()
        {
            var candidate = CaptureState(); stateStore.Save(candidate); state = candidate;
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (exiting) { base.OnFormClosing(e); return; }
            if (modalDepth > 0) { e.Cancel = true; return; }
            exiting = true; monitor.Stop(); layoutTimer.Stop(); UpdateKeyboardScope();
            // Restoring windows must remain possible even if settings persistence fails.
            string saveError = "";
            try { PersistState(); } catch (Exception error) { saveError = error.Message; }
            var errors = controller.DetachAll();
            if (errors.Count > 0)
            {
                exiting = false; e.Cancel = true; monitor.Start(); RefreshUi();
                ShowErrors("部分窗口还没有还原，AppTabs 将继续运行。请重试分离。\n" + string.Join("\n", errors));
                return;
            }
            if (saveError.Length > 0) SetStatus("窗口已还原，设置保存失败：" + saveError);
            keyboard?.Dispose(); hotkey?.Dispose(); if (tray != null) tray.Visible = false;
            base.OnFormClosing(e);
        }
        public void SetStatus(string message)
        {
            if (IsDisposed) return;
            status.Text = message;
            chromeTips.SetToolTip(status, message);
        }
        private void TryAction(Action action)
        { try { action(); } catch (Exception error) { RefreshUi(); ShowErrors(error.Message); } }
        private void ShowErrors(string message)
        { RunModal(() => MessageBox.Show(this, message, "AppTabs", MessageBoxButtons.OK, MessageBoxIcon.Information)); }
        private void Queue(Action action)
        { if (!IsDisposed && IsHandleCreated) try { BeginInvoke(new Action(() => { if (!IsDisposed && !exiting) { ReleaseClosedMenus(); TryAction(action); } })); } catch (InvalidOperationException) { } }
        private string ModeName(HostingMode mode) => mode == HostingMode.Embedded ? "嵌入"
            : state.BorderlessGrouped != false ? "无边框分组" : "兼容分组";
        protected override void Dispose(bool disposing)
        {
            if (disposing) { monitor.Dispose(); layoutTimer.Dispose(); keyboard?.Dispose(); hotkey?.Dispose(); if (tray != null) { tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose(); } DisposeChrome(); }
            base.Dispose(disposing);
        }
    }
}
