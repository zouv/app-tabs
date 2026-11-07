using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using AppTabs.Core;

namespace AppTabs.App
{
    public sealed class MainForm : Form
    {
        private readonly IWindowApi windows;
        private readonly HostController controller;
        private readonly StateStore stateStore;
        private readonly AppState state;
        private readonly bool services;
        private readonly TabBar tabs = new TabBar { Dock = DockStyle.Top };
        private readonly Panel content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface };
        private readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 28, Padding = new Padding(12, 5, 0, 0), ForeColor = Theme.Muted, BackColor = Color.White, AutoEllipsis = true };
        private readonly ComboBox mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 164, Margin = new Padding(8, 8, 8, 4) };
        private readonly Button detach = Theme.Button("分离当前");
        private readonly Button restoreGroup = Theme.Button("恢复窗口组");
        private readonly Button saveGroup = Theme.Button("保存窗口组");
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

        public MainForm(IWindowApi windows, RecoveryJournal journal, StateStore stateStore, AppState state, bool services = true)
        {
            this.windows = windows; this.stateStore = stateStore; this.state = state; this.services = services;
            controller = new HostController(windows, journal);
            Font = new Font("Microsoft YaHei UI", 9F); Text = "AppTabs"; BackColor = Color.White;
            MinimumSize = new Size(780, 460); StartPosition = FormStartPosition.Manual;
            var requested = new Rectangle(state.X, state.Y, state.Width, state.Height);
            var screen = Screen.FromRectangle(requested).WorkingArea;
            Bounds = new Rectangle(Math.Max(screen.Left, Math.Min(requested.X, screen.Right - Math.Min(requested.Width, screen.Width))),
                Math.Max(screen.Top, Math.Min(requested.Y, screen.Bottom - Math.Min(requested.Height, screen.Height))),
                Math.Min(requested.Width, screen.Width), Math.Min(requested.Height, screen.Height));
            lastNormal = Bounds; lastMaximized = state.Maximized;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { Icon = SystemIcons.Application; }
            mode.Items.AddRange(new object[] { "自动 · 优先嵌入", "兼容分组", "实验性嵌入" }); mode.SelectedIndex = (int)state.Preference;
            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(7, 2, 4, 2), BackColor = Color.White, WrapContents = false };
            var join = Theme.Button("＋ 加入窗口", true); var search = Theme.Button("搜索标签"); var settings = Theme.Button("设置");
            toolbar.Controls.AddRange(new Control[] { join, mode, detach, search, saveGroup, restoreGroup, settings });
            content.Paint += PaintWelcome;
            content.DoubleClick += (_, __) => { if (controller.Sessions.Count == 0) OpenPicker(); };
            Controls.Add(content); Controls.Add(status); Controls.Add(toolbar); Controls.Add(tabs);
            join.Click += (_, __) => OpenPicker();
            detach.Click += (_, __) => { if (controller.Active != null) Detach(controller.Active); };
            search.Click += (_, __) => SearchTabs();
            saveGroup.Click += (_, __) => TryAction(SaveGroup);
            restoreGroup.Click += (_, __) => RestoreGroup();
            settings.Click += (_, __) => OpenSettings();
            mode.SelectedIndexChanged += (_, __) => { state.Preference = (AttachPreference)mode.SelectedIndex; };
            tabs.SelectTab += session => TryAction(() => Select(session));
            tabs.DetachTab += session => Queue(() => Detach(session));
            tabs.ReorderTab += (session, index) => TryAction(() => { controller.MoveTab(session, index); RefreshUi(); });
            tabs.ContextTab += ShowTabMenu;
            tabs.AddWindow += () => Queue(OpenPicker);
            layoutTimer.Tick += (_, __) => { layoutTimer.Stop(); LayoutContent(pendingRaise); pendingRaise = false; };
            monitor.Tick += (_, __) => MonitorWindows();
            Move += (_, __) => TrackAndLayout(); Resize += (_, __) => TrackAndLayout();
            content.Resize += (_, __) => ScheduleLayout(false);
            Activated += (_, __) => ScheduleLayout(true);
            Shown += (_, __) => OnReady();
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
                var menu = new ContextMenuStrip();
                menu.Items.Add("显示 AppTabs", null, (_, __) => ShowHost());
                menu.Items.Add("加入窗口…", null, (_, __) => { ShowHost(); Queue(OpenPicker); });
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("退出并保留所有软件", null, (_, __) => Close());
                tray = new NotifyIcon { Icon = Icon, Text = "AppTabs", ContextMenuStrip = menu, Visible = true };
                tray.DoubleClick += (_, __) => ShowHost();
            }
            monitor.Start(); RefreshUi();
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
            var session = controller.Attach(identity, content.Handle, chosen, content.ClientRectangle);
            Select(session);
            SetStatus(fallback.Length == 0 ? "已加入：" + session.DisplayTitle + " · " + ModeName(chosen) : "已用兼容分组加入：" + fallback);
            return session;
        }
        private void OpenPicker()
        {
            if (modalDepth != 0 || exiting) return;
            using (var picker = new WindowPicker(controller.Sessions.Select(s => s.Identity.Handle)))
            {
                if (RunModal(() => picker.ShowDialog(this)) != DialogResult.OK) return;
                var errors = new List<string>();
                foreach (var identity in picker.SelectedWindows)
                {
                    try { JoinWindow(identity, state.Preference); }
                    catch (Exception error) { errors.Add(identity.Title + "：" + error.Message); }
                }
                RefreshUi();
                if (errors.Count > 0) ShowErrors(string.Join("\n", errors));
            }
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
            menu.Closed += (_, __) => Queue(menu.Dispose);
            menu.Show(tabs, location);
        }
        private void Rename(WindowSession session)
        {
            using (var dialog = new TextDialog("重命名标签", "名称只影响 AppTabs 标签。", session.DisplayTitle))
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
            state.Group = controller.Sessions.Where(s => s.State == SessionState.Attached).Select(s => new SavedWindow
            { ImagePath = s.Identity.ImagePath, ClassName = s.Identity.ClassName, Title = s.Title, CustomTitle = s.CustomTitle, Mode = s.Snapshot.Mode }).ToList();
            state.ActiveIndex = controller.Active == null ? 0 : controller.Sessions.ToList().IndexOf(controller.Active);
            PersistState(); RefreshUi(); SetStatus("窗口组已保存；恢复时重新匹配已打开的窗口。");
        }
        private void RestoreGroup()
        {
            if (state.Group.Count == 0 || modalDepth != 0) return;
            TryAction(() =>
            {
                var candidates = NativeWindowApi.DiscoverWindows(controller.Sessions.Select(s => s.Identity.Handle)).ToList();
                var errors = new List<string>(); WindowSession? active = null; int index = 0;
                foreach (var saved in state.Group)
                {
                    var identity = GroupMatching.Match(saved, candidates);
                    if (identity == null) { errors.Add(saved.Title + "：没有唯一匹配的已打开窗口，请使用“加入窗口”。"); index++; continue; }
                    try
                    {
                        var session = JoinWindow(identity, saved.Mode == HostingMode.Grouped ? AttachPreference.Grouped : AttachPreference.Auto);
                        candidates.Remove(identity);
                        if (saved.CustomTitle != null) controller.RenameTab(session, saved.CustomTitle);
                        if (index == state.ActiveIndex) active = session;
                    }
                    catch (Exception error) { errors.Add(saved.Title + "：" + error.Message); }
                    index++;
                }
                if (active != null) Select(active); RefreshUi();
                if (errors.Count > 0) ShowErrors(string.Join("\n", errors));
                else SetStatus("窗口组已恢复。");
            });
        }
        private void SearchTabs()
        {
            if (controller.Sessions.Count == 0 || modalDepth != 0) return;
            using (var dialog = new TabSearchDialog(controller.Sessions))
                if (RunModal(() => dialog.ShowDialog(this)) == DialogResult.OK && dialog.Selected != null)
                    TryAction(() => Select(dialog.Selected));
        }
        private void OpenSettings()
        {
            using (var dialog = new SettingsDialog(state))
            {
                if (RunModal(() => dialog.ShowDialog(this)) != DialogResult.OK) return;
                int oldChoice = state.HotkeyChoice;
                TryAction(() =>
                {
                    try { hotkey?.Apply(dialog.HotkeyChoice); }
                    catch { try { hotkey?.Apply(oldChoice); } catch { } throw; }
                    state.HotkeyChoice = dialog.HotkeyChoice; state.HideToTray = dialog.HideToTray;
                    PersistState(); SetStatus("设置已保存。");
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
            finally { modalDepth--; UpdateKeyboardScope(); ScheduleLayout(true); }
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
            restoreGroup.Enabled = state.Group.Count > 0;
            Text = controller.Active == null ? "AppTabs" : controller.Active.DisplayTitle + " — AppTabs";
            UpdateKeyboardScope();
            if (status.Text.Length == 0) SetStatus("Ctrl+Tab 切换 · Ctrl+W 分离 · Ctrl+F 搜索 · Ctrl+Alt+Space 呼出");
        }
        private void PaintWelcome(object? sender, PaintEventArgs e)
        {
            if (controller.Sessions.Count > 0) return;
            int mid = content.Width / 2, top = Math.Max(30, content.Height / 2 - 110);
            using (var accent = new SolidBrush(Theme.Accent)) e.Graphics.FillRectangle(accent, mid - 26, top, 52, 44);
            using (var logoFont = new Font(Font.FontFamily, 16, FontStyle.Bold))
                TextRenderer.DrawText(e.Graphics, "AT", logoFont, new Rectangle(mid - 26, top, 52, 44), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            using (var titleFont = new Font(Font.FontFamily, 22, FontStyle.Bold))
                TextRenderer.DrawText(e.Graphics, "一个窗口，管理多个软件", titleFont, new Rectangle(10, top + 66, content.Width - 20, 52), Theme.Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(e.Graphics, "点击“加入窗口”，把已打开的软件收进标签页。", Font, new Rectangle(10, top + 126, content.Width - 20, 30), Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(e.Graphics, "可随时分离；退出 AppTabs 时，各软件恢复为独立窗口。", Font, new Rectangle(10, top + 162, content.Width - 20, 30), Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        private void ToggleHost()
        {
            if (modalDepth != 0) return;
            var foreground = NativeUi.GetForegroundWindow();
            var root = NativeUi.GetAncestor(foreground, 2);
            if (HostVisible && (root == Handle || foreground == controller.Active?.Identity.Hwnd))
            {
                controller.SetActiveVisible(false);
                if (state.HideToTray) Hide(); else WindowState = FormWindowState.Minimized;
                UpdateKeyboardScope();
            }
            else ShowHost();
        }
        private void ShowHost()
        { Show(); WindowState = lastMaximized ? FormWindowState.Maximized : FormWindowState.Normal; NativeUi.SetForegroundWindow(Handle); ScheduleLayout(true); UpdateKeyboardScope(); }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x312 && m.WParam.ToInt32() == GlobalHotkey.Id) { Queue(() => TryAction(ToggleHost)); return; }
            base.WndProc(ref m);
        }
        private void PersistState()
        {
            state.X = lastNormal.X; state.Y = lastNormal.Y; state.Width = lastNormal.Width; state.Height = lastNormal.Height;
            state.Maximized = lastMaximized; stateStore.Save(state);
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
        public void SetStatus(string message) { if (!IsDisposed) status.Text = message; }
        private void TryAction(Action action)
        { try { action(); } catch (Exception error) { RefreshUi(); ShowErrors(error.Message); } }
        private void ShowErrors(string message)
        { RunModal(() => MessageBox.Show(this, message, "AppTabs", MessageBoxButtons.OK, MessageBoxIcon.Information)); }
        private void Queue(Action action)
        { if (!IsDisposed && IsHandleCreated) try { BeginInvoke(new Action(() => { if (!IsDisposed && !exiting) TryAction(action); })); } catch (InvalidOperationException) { } }
        private static string ModeName(HostingMode mode) => mode == HostingMode.Embedded ? "嵌入" : "兼容分组";
        protected override void Dispose(bool disposing)
        {
            if (disposing) { monitor.Dispose(); layoutTimer.Dispose(); keyboard?.Dispose(); hotkey?.Dispose(); if (tray != null) { tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose(); } }
            base.Dispose(disposing);
        }
    }
}
