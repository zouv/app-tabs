using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AppTabs.App
{
    public sealed partial class MainForm
    {
        private readonly ToolTip chromeTips = new ToolTip { AutoPopDelay = 7000, InitialDelay = 400, ReshowDelay = 100, ShowAlways = true };
        private readonly ContextMenuStrip groupMenu = new ContextMenuStrip { Name = "groupMenu", ShowImageMargin = false };
        private readonly ContextMenuStrip appMenu = new ContextMenuStrip { Name = "appMenu", ShowImageMargin = false };
        private readonly Dictionary<ContextMenuStrip, Action> menuCleanups = new Dictionary<ContextMenuStrip, Action>();
        private readonly HashSet<ContextMenuStrip> closedMenus = new HashSet<ContextMenuStrip>();
        private Panel chrome = null!, toolbar = null!, captionButtons = null!;
        private IconButton brand = null!, maximizeButton = null!, join = null!, windowGroup = null!, search = null!;
        private ToolbarDivider toolbarDivider = null!;
        private Icon? applicationIcon, brandIcon, welcomeIcon;
        private bool nativeMenuOpen, layingOutChrome;
        private bool lastZoomed;
        private const int TopResizeBandLogical = 3;
        private int ChromePixels(int logical) => Theme.Pixels(DeviceDpi, logical);

        private void InitializeChrome()
        {
            chrome = new ChromePanel { Name = "chrome", Dock = DockStyle.Top, BackColor = Theme.Chrome, Height = 36 };
            captionButtons = new ChromePanel { Name = "captionButtons", Dock = DockStyle.Right, BackColor = Theme.Chrome, Width = 126 };
            brand = new IconButton(UiGlyph.App, "AppTabs · 设置 / 关于") { Name = "brand", Dock = DockStyle.Left, Width = 36, ChromeTopEdge = true };
            var minimize = new IconButton(UiGlyph.Minimize, "最小化") { Name = "minimize", Caption = true, ChromeTopEdge = true };
            maximizeButton = new IconButton(UiGlyph.Maximize, "最大化 / 还原") { Name = "maximize", Caption = true, ChromeTopEdge = true };
            var close = new IconButton(UiGlyph.Close, "退出并保留所有软件") { Name = "close", Caption = true, ChromeTopEdge = true };
            captionButtons.Controls.AddRange(new Control[] { minimize, maximizeButton, close });
            minimize.Click += (_, __) => WindowState = FormWindowState.Minimized;
            maximizeButton.Click += (_, __) => ToggleMaximize();
            close.Click += (_, __) => Close(); // Always uses OnFormClosing / DetachAll; failure cancels exit.
            appMenu.Items.Add("设置…", null, (_, __) => Queue(OpenSettings));
            appMenu.Items.Add("关于 AppTabs…", null, (_, __) => Queue(OpenAbout));
            brand.Click += (_, __) => ShowChromeMenu(appMenu, brand);
            tabs.Dock = DockStyle.Fill; tabs.BackColor = Theme.Chrome;
            tabs.ChromeMouseDown += e => ChromeMouseDown(tabs, e);
            chrome.Controls.Add(tabs); chrome.Controls.Add(captionButtons); chrome.Controls.Add(brand);
            chrome.MouseDown += (sender, e) => ChromeMouseDown((Control)sender!, e);

            toolbar = new Panel { Name = "toolbar", Dock = DockStyle.Top, Height = 36, BackColor = Theme.Toolbar };
            join = new IconButton(UiGlyph.AddWindow, "加入窗口…") { Name = "join", Primary = true };
            toolbarDivider = new ToolbarDivider { Name = "toolbarDivider" };
            windowGroup = new IconButton(UiGlyph.WindowGroup, "窗口组 · 标签组 / 应用收藏") { Name = "windowGroup" };
            search = new IconButton(UiGlyph.Search, "搜索标签 · Ctrl+F") { Name = "search" };
            toolbar.Controls.Add(status);
            toolbar.Controls.AddRange(new Control[] { join, detach, toolbarDivider, windowGroup, search });
            join.Click += (_, __) => ShowAddWindowsMenu();
            search.Click += (_, __) => SearchTabs();
            windowGroup.Click += (_, __) => { RebuildLibraryMenu(); ShowChromeMenu(groupMenu, windowGroup); };
            groupMenu.Items.AddRange(new ToolStripItem[] { saveGroup, restoreGroup });
            foreach (var button in new[] { brand, minimize, maximizeButton, close, join, detach, windowGroup, search })
                chromeTips.SetToolTip(button, button.AccessibleName);
            Controls.Add(toolbar); Controls.Add(chrome);
            toolbar.Layout += (_, __) => LayoutToolbar();
            captionButtons.Layout += (_, __) => LayoutCaptionButtons();
            Resize += (_, __) =>
            {
                SyncTopResizeBand();
                maximizeButton.Glyph = WindowState == FormWindowState.Maximized ? UiGlyph.Restore : UiGlyph.Maximize;
            };
            DpiChanged += (_, __) => { SyncChromeMetrics(); SyncTopResizeBand(); };
            SyncChromeMetrics();
        }

        private void SyncTopResizeBand()
        {
            // The native top frame is pulled into the client so the system-painted strip
            // disappears. Keep a small painted band at the top for resize hit-testing; the
            // side / bottom native frame remains wider and invisible outside the client.
            int band = NativeUi.IsZoomed(Handle) ? 0 : ChromePixels(TopResizeBandLogical);
            if (Padding.Top != band || Padding.Left != 0 || Padding.Right != 0 || Padding.Bottom != 0)
                Padding = new Padding(0, band, 0, 0);
        }
        private void SyncChromeMetrics()
        {
            SuspendLayout();
            try
            {
                chrome.Height = ChromePixels(36); toolbar.Height = ChromePixels(36);
                brand.Width = ChromePixels(36); captionButtons.Width = ChromePixels(126);
                brandIcon?.Dispose(); welcomeIcon?.Dispose();
                brandIcon = AppIcon.Load(Math.Min(256, ChromePixels(24))); welcomeIcon = AppIcon.Load(Math.Min(256, ChromePixels(48)));
                brand.ApplicationIcon = brandIcon; brand.Invalidate(); content.Invalidate();
                LayoutCaptionButtons(); LayoutToolbar();
                SyncTopResizeBand();
            }
            finally { ResumeLayout(true); }
        }
        private void LayoutCaptionButtons()
        {
            int height = ChromePixels(36);
            for (int i = 0; i < captionButtons.Controls.Count; i++)
            {
                int left = i == 0 ? 0 : ChromePixels(i * 42), right = ChromePixels((i + 1) * 42);
                captionButtons.Controls[i].Bounds = new Rectangle(left, 0, right - left, height);
            }
        }
        private void LayoutToolbar()
        {
            int x = ChromePixels(6), y = ChromePixels(3), width = ChromePixels(32), height = ChromePixels(30);
            join.Bounds = new Rectangle(x, y, width, height); x += width + ChromePixels(2);
            detach.Bounds = new Rectangle(x, y, width, height); x += width;
            toolbarDivider.Bounds = new Rectangle(x, 0, ChromePixels(14), toolbar.Height); x += toolbarDivider.Width;
            windowGroup.Bounds = new Rectangle(x, y, width, height); x += width;
            search.Bounds = new Rectangle(Math.Max(0, toolbar.Width - ChromePixels(6) - width), y, width, height);
            int statusLeft = x + ChromePixels(8), statusRight = Math.Max(statusLeft, search.Left - ChromePixels(8));
            status.Bounds = new Rectangle(statusLeft, 0, statusRight - statusLeft, toolbar.Height);
        }
        private void ToggleMaximize()
        {
            if (exiting || modalDepth != 0) return;
            WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        }
        private void ChromeMouseDown(Control source, MouseEventArgs e)
        {
            if (exiting || modalDepth != 0) return;
            Point screen = source.PointToScreen(e.Location), point = PointToClient(screen);
            bool topEdge = WindowState == FormWindowState.Normal && point.Y >= 0 && point.Y < ChromePixels(4);
            if (e.Button == MouseButtons.Left)
            {
                if (!topEdge && e.Clicks == 2) { ToggleMaximize(); return; }
                NativeUi.ReleaseCapture();
                int hit = topEdge ? (point.X < ChromePixels(4) ? 13 : point.X >= ClientSize.Width - ChromePixels(4) ? 14 : 12) : 2;
                NativeUi.SendMessage(Handle, NativeUi.WmNcLButtonDown, new IntPtr(hit), NativeUi.PointMessage(screen));
            }
            else if (e.Button == MouseButtons.Right && !topEdge) ShowSystemMenu(screen);
        }
        private void EnterChromeMenuScope()
        {
            modalDepth++; UpdateKeyboardScope();
            if (controller.Active?.Snapshot.Mode == AppTabs.Core.HostingMode.Grouped)
                try { controller.SetActiveVisible(false); } catch (Exception error) { SetStatus(error.Message); }
        }
        private void LeaveChromeMenuScope()
        {
            modalDepth = Math.Max(0, modalDepth - 1);
            UpdateKeyboardScope();
            var foreground = NativeUi.GetForegroundWindow();
            bool hostForeground = NativeUi.GetAncestor(foreground, 2) == Handle;
            if (!hostForeground) pendingRaise = false;
            ScheduleLayout(hostForeground);
        }
        private void ShowChromeMenu(ContextMenuStrip menu, Control anchor, Point? location = null, bool disposeOnClose = false)
        {
            if (exiting || modalDepth != 0 || anchor.IsDisposed)
            { if (disposeOnClose) menu.Dispose(); return; }
            menu.ForeColor = Theme.Ink; menu.BackColor = Theme.Toolbar;
            bool entered = true;
            EnterChromeMenuScope();
            ToolStripDropDownClosedEventHandler closed = (_, __) => QueueMenuCleanup(menu);
            EventHandler disposed = null!;
            Action cleanup = () =>
            {
                closedMenus.Remove(menu); menuCleanups.Remove(menu);
                menu.Closed -= closed; menu.Disposed -= disposed;
                if (entered) { entered = false; LeaveChromeMenuScope(); }
                if (disposeOnClose && !menu.IsDisposed) menu.Dispose();
            };
            disposed = (_, __) => cleanup();
            menuCleanups.Add(menu, cleanup);
            menu.Closed += closed; menu.Disposed += disposed;
            try
            {
                menu.Show(anchor, location ?? new Point(0, anchor.Height));
                if (!menu.Visible) QueueMenuCleanup(menu);
            }
            catch { QueueMenuCleanup(menu); throw; }
        }
        private void QueueMenuCleanup(ContextMenuStrip menu)
        {
            if (!menuCleanups.ContainsKey(menu) || !closedMenus.Add(menu)) return;
            if (!IsDisposed && !Disposing && IsHandleCreated)
                try { BeginInvoke(new Action(ReleaseClosedMenus)); } catch (InvalidOperationException) { }
        }
        private void ReleaseClosedMenus()
        {
            foreach (var menu in closedMenus.ToArray())
                if (menuCleanups.TryGetValue(menu, out var cleanup)) cleanup();
        }
        private void DisposeMenus()
        {
            foreach (var menu in menuCleanups.Keys.ToArray())
            {
                if (menu.Visible) menu.Close();
                if (menuCleanups.TryGetValue(menu, out var cleanup)) cleanup();
            }
        }
        private void OpenAbout()
        {
            using (var dialog = new AboutDialog(services)) RunModal(() => dialog.ShowDialog(this));
        }
        private void ShowSystemMenu(Point point)
        {
            if (exiting || modalDepth != 0) return;
            EnterChromeMenuScope();
            int command = 0;
            try
            {
                IntPtr menu = NativeUi.GetSystemMenu(Handle, false);
                if (menu == IntPtr.Zero) return;
                bool maximized = WindowState == FormWindowState.Maximized;
                NativeUi.EnableMenuItem(menu, 0xF120, maximized ? 0u : 1u);
                NativeUi.EnableMenuItem(menu, 0xF030, maximized ? 1u : 0u);
                NativeUi.EnableMenuItem(menu, 0xF000, maximized ? 1u : 0u);
                NativeUi.EnableMenuItem(menu, 0xF010, maximized ? 1u : 0u);
                command = NativeUi.TrackPopupMenuEx(menu, 0x0102, point.X, point.Y, Handle, IntPtr.Zero);
            }
            finally { LeaveChromeMenuScope(); }
            if (command != 0) NativeUi.PostMessage(Handle, NativeUi.WmSysCommand, new IntPtr(command), IntPtr.Zero);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            if (chrome != null && !layingOutChrome)
            {
                layingOutChrome = true;
                try
                {
                    chrome.Height = ChromePixels(36); toolbar.Height = ChromePixels(36);
                    brand.Width = ChromePixels(36); captionButtons.Width = ChromePixels(126);
                    LayoutCaptionButtons(); LayoutToolbar();
                }
                finally { layingOutChrome = false; }
            }
            base.OnLayout(e);
        }
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.Style = (cp.Style | 0x000F0000) & ~0x00C00000; return cp; }
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Never change FormBorderStyle after handle creation: hosted windows depend on this HWND.
            lastZoomed = NativeUi.IsZoomed(Handle);
            if (chrome != null) SyncChromeMetrics();
        }
        private bool ProcessChromeMessage(ref Message m)
        {
            if (m.Msg == 0x0005) // WM_SIZE
            {
                bool zoomed = NativeUi.IsZoomed(Handle);
                if (zoomed != lastZoomed)
                {
                    lastZoomed = zoomed;
                    // Maximize / restore changes the whole content rect at once. Let this WM_SIZE
                    // finish updating the layout, then move the hosted window before the 40 ms
                    // coalescing timer can let the near-white content panel show through.
                    try { BeginInvoke(new Action(() => { if (!IsDisposed && !exiting) LayoutContent(true); })); }
                    catch (InvalidOperationException) { }
                }
            }
            if (m.Msg == NativeUi.WmEnterMenuLoop && !nativeMenuOpen) { nativeMenuOpen = true; EnterChromeMenuScope(); }
            if (m.Msg == NativeUi.WmExitMenuLoop && nativeMenuOpen) { nativeMenuOpen = false; LeaveChromeMenuScope(); }
            if (m.Msg == NativeUi.WmNcCalcSize && m.WParam != IntPtr.Zero)
            {
                var p = Marshal.PtrToStructure<NativeUi.NcCalcSizeParams>(m.LParam);
                var proposed = Rectangle.FromLTRB(p.Rc0.Left, p.Rc0.Top, p.Rc0.Right, p.Rc0.Bottom);
                base.WndProc(ref m);
                p = Marshal.PtrToStructure<NativeUi.NcCalcSizeParams>(m.LParam);
                if (NativeUi.IsZoomed(Handle))
                {
                    // DWM's actual inset can differ from SM_CXSIZEFRAME + padding by a pixel.
                    // Choose the monitor from the proposed bounds (not the previous HWND position),
                    // then make the maximized client exactly its work area regardless of that inset.
                    Rectangle work = Screen.FromRectangle(proposed).WorkingArea;
                    p.Rc0.Left = work.Left; p.Rc0.Top = work.Top;
                    p.Rc0.Right = work.Right; p.Rc0.Bottom = work.Bottom;
                    m.Result = IntPtr.Zero;
                }
                else
                {
                    // Keep the wide native side / bottom resize frame, but pull the top frame
                    // into the client so no opaque system strip appears above the tab row.
                    p.Rc0.Top = proposed.Top;
                }
                Marshal.StructureToPtr(p, m.LParam, false);
                return true;
            }
            if (m.Msg == NativeUi.WmNcHitTest && !NativeUi.IsZoomed(Handle))
            {
                Point point = PointToClient(NativeUi.MessagePoint(m.LParam)); int band = Padding.Top;
                if (band > 0 && point.Y >= 0 && point.Y < band && point.X >= 0 && point.X < ClientSize.Width)
                { m.Result = new IntPtr(point.X < band ? 13 : point.X >= ClientSize.Width - band ? 14 : 12); return true; }
            }
            if (m.Msg == NativeUi.WmNcActivate)
            { m.Result = NativeUi.DefWindowProc(Handle, m.Msg, m.WParam, new IntPtr(-1)); return true; }
            if (m.Msg == NativeUi.WmGetMinMaxInfo)
            {
                base.WndProc(ref m);
                var info = Marshal.PtrToStructure<NativeUi.MinMaxInfo>(m.LParam); var screen = Screen.FromHandle(Handle);
                int side = Math.Max(0, (Width - ClientSize.Width) / 2);
                int bottom = Math.Max(0, Height - ClientSize.Height);
                info.MaxPosition = new NativeUi.NativePoint { X = screen.WorkingArea.Left - screen.Bounds.Left - side, Y = screen.WorkingArea.Top - screen.Bounds.Top - side };
                info.MaxSize = new NativeUi.NativePoint { X = screen.WorkingArea.Width + side * 2, Y = screen.WorkingArea.Height + side + bottom };
                info.MinTrackSize = new NativeUi.NativePoint { X = MinimumSize.Width, Y = MinimumSize.Height };
                Marshal.StructureToPtr(info, m.LParam, false); return true;
            }
            return false;
        }
        private void DisposeChrome()
        {
            DisposeMenus(); chromeTips.Dispose(); groupMenu.Dispose(); appMenu.Dispose();
            brand.ApplicationIcon = null; brandIcon?.Dispose(); welcomeIcon?.Dispose(); applicationIcon?.Dispose();
        }
    }
}
