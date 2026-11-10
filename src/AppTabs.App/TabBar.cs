using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using AppTabs.Core;

namespace AppTabs.App
{
    public sealed class TabBar : Control, IMessageFilter
    {
        private IReadOnlyList<WindowSession> sessions = Array.Empty<WindowSession>();
        private WindowSession? active, pressed, hovered;
        private readonly Dictionary<string, Icon?> icons = new Dictionary<string, Icon?>(StringComparer.OrdinalIgnoreCase);
        private readonly ToolTip tip = new ToolTip();
        private readonly Timer scroll = new Timer { Interval = 60 };
        private int offset, insertion;
        private Point dragOrigin, pointer;
        private bool dragging, closeHovered, releasingMouse, filteringKeys;
        private MouseButtons pressedButton;
        private SessionState pressedState;
        private PointerAction action;
        private string hint = "";
        private enum PointerAction { None, Select, Detach }
        public event Action<WindowSession>? SelectTab;
        public event Action<WindowSession>? DetachTab;
        public event Action<WindowSession, int>? ReorderTab;
        public event Action<WindowSession, Point>? ContextTab;
        public event Action? AddWindow;
        public event Action<MouseEventArgs>? ChromeMouseDown;

        public TabBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 36;
            BackColor = Color.FromArgb(233, 239, 247);
            ForeColor = Theme.Ink;
            AccessibleName = "应用标签栏";
            Name = "tabs";
            scroll.Tick += (_, __) => AutoScrollDrag();
        }
        private int Px(int value) => Math.Max(1, (int)Math.Round(value * DeviceDpi / 96.0));
        private int TabTop => FindForm()?.WindowState == FormWindowState.Maximized ? 0 : Px(4);
        private bool InResizeBand(Point point) => TabTop != 0 && point.Y >= 0 && point.Y < TabTop;
        public void Bind(IReadOnlyList<WindowSession> value, WindowSession? selected)
        {
            // The controller can mutate the same list between timer ticks. Keep a structural snapshot,
            // but do not cancel a press or undo drag scrolling on a title/active-state refresh.
            bool structureChanged = !sessions.SequenceEqual(value);
            bool selectionChanged = active != selected;
            if (structureChanged) CancelPointer();
            sessions = value.ToArray(); active = selected;
            if (pressed != null && !ValidTarget()) CancelPointer();
            if (hovered != null && !sessions.Contains(hovered)) { hovered = null; closeHovered = false; }
            foreach (string path in icons.Keys.Where(p => !sessions.Any(s => string.Equals(s.Identity.ImagePath, p, StringComparison.OrdinalIgnoreCase))).ToArray())
            { icons[path]?.Dispose(); icons.Remove(path); }
            foreach (var session in sessions)
            {
                string path = session.Identity.ImagePath;
                if (icons.ContainsKey(path)) continue;
                try { icons[path] = File.Exists(path) ? Icon.ExtractAssociatedIcon(path) : null; }
                catch { icons[path] = null; }
            }
            ClampOffset();
            if (pressed == null && selected != null && (structureChanged || selectionChanged))
            {
                int x = Px(4);
                foreach (var session in sessions)
                {
                    int width = TabWidth(session);
                    if (session == selected)
                    {
                        if (x - offset < Px(4)) offset = x - Px(4);
                        else if (x + width - offset > Viewport.Right) offset = x + width - Viewport.Right;
                        break;
                    }
                    x += width + Px(4);
                }
            }
            ClampOffset();
            if (dragging) UpdateInsertion();
            Invalidate();
        }
        private int TabWidth(WindowSession session) => Math.Min(Px(230), Math.Max(Px(148),
            TextRenderer.MeasureText(session.DisplayTitle, Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width + Px(60)));
        private int TotalWidth => Px(4) + sessions.Sum(s => TabWidth(s) + Px(4));
        private Rectangle AddRect => new Rectangle(Math.Min(TotalWidth, Math.Max(Px(4), Width - Px(38))), TabTop, Px(32), Math.Max(1, Height - TabTop - Px(2)));
        private Rectangle Viewport => new Rectangle(0, 0, Math.Max(0, AddRect.Left), Height);
        private IEnumerable<(WindowSession Session, Rectangle Rect)> Rectangles()
        {
            int x = Px(4) - offset;
            foreach (var session in sessions)
            {
                int width = TabWidth(session);
                yield return (session, new Rectangle(x, TabTop, width, Math.Max(1, Height - TabTop - Px(2))));
                x += width + Px(4);
            }
        }
        private void ClampOffset() => offset = Math.Max(0, Math.Min(offset, Math.Max(0, TotalWidth - Viewport.Width)));
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); ClampOffset(); if (dragging) UpdateInsertion(); }
        protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); ClampOffset(); if (dragging) UpdateInsertion(); Invalidate(); }
        private GraphicsPath TabShape(Rectangle rect)
        {
            float radius = Math.Min(Px(6), Math.Min(rect.Width, rect.Height) / 2F), diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
            path.AddLine(rect.Right, rect.Bottom, rect.Left, rect.Bottom);
            path.CloseFigure();
            return path;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            using (var pen = new Pen(Theme.Accent, Math.Max(1, DeviceDpi / 96F)))
            {
                var add = AddRect; int x = add.Left + add.Width / 2, y = add.Top + add.Height / 2;
                g.DrawLine(pen, x - Px(5), y, x + Px(5), y); g.DrawLine(pen, x, y - Px(5), x, y + Px(5));
            }
            var saved = g.Save();
            try
            {
                g.SetClip(Viewport, CombineMode.Intersect); g.SmoothingMode = SmoothingMode.AntiAlias;
                foreach (var entry in Rectangles())
                {
                    if (!entry.Rect.IntersectsWith(Viewport)) continue;
                    bool selected = entry.Session == active;
                    using (var brush = new SolidBrush(selected ? Theme.Surface : entry.Session == hovered ? Theme.Hover : BackColor))
                    using (var path = TabShape(entry.Rect)) g.FillPath(brush, path);
                    if (selected) using (var pen = new Pen(Theme.Accent, Px(2)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawLine(pen, entry.Rect.Left + Px(10), entry.Rect.Bottom - Px(2), entry.Rect.Right - Px(10), entry.Rect.Bottom - Px(2));
                    if (icons.TryGetValue(entry.Session.Identity.ImagePath, out var icon) && icon != null)
                        g.DrawIcon(icon, new Rectangle(entry.Rect.X + Px(9), entry.Rect.Y + (entry.Rect.Height - Px(16)) / 2, Px(16), Px(16)));
                    else using (var pen = new Pen(Theme.Muted, Math.Max(1, DeviceDpi / 96F)))
                    {
                        var box = new Rectangle(entry.Rect.X + Px(9), entry.Rect.Y + (entry.Rect.Height - Px(14)) / 2, Px(15), Px(12));
                        g.DrawRectangle(pen, box); g.DrawLine(pen, box.Left, box.Top + Px(3), box.Right, box.Top + Px(3));
                    }
                    string title = (entry.Session.State == SessionState.Attached ? "" : "! ") + entry.Session.DisplayTitle;
                    TextRenderer.DrawText(g, title, Font, new Rectangle(entry.Rect.X + Px(32), entry.Rect.Y, entry.Rect.Width - Px(58), entry.Rect.Height),
                        selected ? Theme.Ink : Theme.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping);
                    if (selected || entry.Session == hovered || (entry.Session == pressed && action == PointerAction.Detach))
                    {
                        var close = CloseRect(entry.Rect);
                        bool over = entry.Session == hovered && closeHovered;
                        bool down = entry.Session == pressed && action == PointerAction.Detach && pressedButton == MouseButtons.Left && over;
                        if (over || down)
                            using (var brush = new SolidBrush(down ? Color.FromArgb(239, 202, 207) : Color.FromArgb(247, 225, 229)))
                                g.FillEllipse(brush, close);
                        using (var pen = new Pen(over ? Theme.Danger : Theme.Muted, Math.Max(1, DeviceDpi / 96F)))
                        {
                            int x = close.Left + close.Width / 2, y = close.Top + close.Height / 2, half = Px(4);
                            g.DrawLine(pen, x - half, y - half, x + half, y + half); g.DrawLine(pen, x + half, y - half, x - half, y + half);
                        }
                    }
                    else using (var pen = new Pen(Color.FromArgb(202, 214, 231)))
                        g.DrawLine(pen, entry.Rect.Right, entry.Rect.Top + Px(8), entry.Rect.Right, entry.Rect.Bottom - Px(8));
                }
                if (dragging && pressed != null)
                {
                    var remaining = Rectangles().Where(t => t.Session != pressed).ToArray();
                    int x = insertion < remaining.Length ? remaining[insertion].Rect.Left - Px(2) :
                        remaining.Length > 0 ? remaining[remaining.Length - 1].Rect.Right + Px(2) : Px(2);
                    x = Math.Max(Px(2), Math.Min(x, Viewport.Right - Px(2)));
                    using (var pen = new Pen(Theme.Accent, Px(2)))
                        g.DrawLine(pen, x, TabTop + Px(3), x, Height - Px(4));
                }
            }
            finally { g.Restore(saved); }
        }
        private Rectangle CloseRect(Rectangle rect) => new Rectangle(rect.Right - Px(25), rect.Top + Px(3), Px(22), Math.Max(1, rect.Height - Px(6)));
        private WindowSession? HitTab(Point point) => Viewport.Contains(point) && !InResizeBand(point) ? Rectangles().FirstOrDefault(t => t.Rect.Contains(point)).Session : null;
        private bool HitClose(WindowSession target, Point point) => HitTab(point) == target &&
            Rectangles().Any(t => t.Session == target && CloseRect(t.Rect).Contains(point));
        private bool ValidTarget() => pressed != null && sessions.Contains(pressed) && pressed.State != SessionState.Lost &&
            pressed.State == pressedState && (action == PointerAction.Detach || (pressed.State == SessionState.Attached && pressed.CloseRequestedUtc == null));
        private bool InDragArea(Point point) => Viewport.Contains(point) && !InResizeBand(point);
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            CancelPointer();
            if (InResizeBand(e.Location)) { ChromeMouseDown?.Invoke(e); return; }
            if (AddRect.Contains(e.Location)) { if (e.Button == MouseButtons.Left) AddWindow?.Invoke(); return; }
            var target = HitTab(e.Location);
            if (target == null) { ChromeMouseDown?.Invoke(e); return; }
            if (e.Button == MouseButtons.Right) { ContextTab?.Invoke(target, e.Location); return; }
            if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Middle) return;
            pressed = target; pressedState = target.State; pressedButton = e.Button;
            action = e.Button == MouseButtons.Middle || HitClose(target, e.Location) ? PointerAction.Detach : PointerAction.Select;
            if (!ValidTarget()) { CancelPointer(); return; }
            dragOrigin = pointer = e.Location;
            // This is an application-thread filter only, scoped to the captured gesture. It catches
            // Escape even when keyboard focus still belongs to the embedded content, without hooks.
            Application.AddMessageFilter(this); filteringKeys = true;
            Focus(); // Keep Escape on this self-owned control while an external tab is not activated.
            Capture = true;
            UpdateHover(e.Location); Invalidate();
        }
        private void UpdateHover(Point point)
        {
            var hit = HitTab(point);
            bool close = hit != null && HitClose(hit, point);
            if (hit != hovered || close != closeHovered) { hovered = hit; closeHovered = close; Invalidate(); }
            string nextHint = hit != null ? hit.DisplayTitle + "\n" + Path.GetFileName(hit.Identity.ImagePath) + " · " + (hit.Snapshot.Mode == HostingMode.Embedded ? "嵌入" : "兼容分组") + "\n× / 中键：分离并保留软件；拖动：排序" : AddRect.Contains(point) ? "加入已打开的窗口" : "拖动空白处移动窗口；滚轮滚动标签";
            if (nextHint != hint) { hint = nextHint; tip.SetToolTip(this, hint); }
            Cursor = dragging ? Cursors.SizeWE : InResizeBand(point) ? Cursors.SizeNS : hit != null || AddRect.Contains(point) ? Cursors.Hand : Cursors.Default;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            pointer = e.Location;
            if (pressed != null)
            {
                if (!Capture || !ValidTarget() || !ClientRectangle.Contains(pointer) || (dragging && !InDragArea(pointer))) CancelPointer();
                else if (action == PointerAction.Select && TabManagementPolicy.StartsDrag(dragOrigin, pointer, SystemInformation.DragSize))
                {
                    if (!InDragArea(pointer)) CancelPointer();
                    else { dragging = true; scroll.Start(); UpdateInsertion(); }
                }
            }
            UpdateHover(e.Location);
        }
        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            CancelPointer(); hovered = null; closeHovered = false; hint = ""; tip.SetToolTip(this, null); Invalidate();
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            var target = pressed;
            bool valid = target != null && ValidTarget() && e.Button == pressedButton && ClientRectangle.Contains(e.Location);
            bool move = valid && dragging && InDragArea(e.Location);
            bool detach = valid && action == PointerAction.Detach && HitTab(e.Location) == target &&
                (pressedButton == MouseButtons.Middle || HitClose(target!, e.Location));
            bool select = valid && !dragging && action == PointerAction.Select && HitTab(e.Location) == target && !HitClose(target!, e.Location);
            if (move) { pointer = e.Location; UpdateInsertion(); }
            int index = insertion;
            // Release capture and stop timers before callbacks can mutate the collection or dispose us.
            CancelPointer();
            if (move) ReorderTab?.Invoke(target!, index);
            else if (detach) DetachTab?.Invoke(target!);
            else if (select) SelectTab?.Invoke(target!);
        }
        private void UpdateInsertion()
        {
            insertion = TabManagementPolicy.InsertionIndex(Rectangles().Where(t => t.Session != pressed).Select(t => t.Rect.Left + t.Rect.Width / 2), pointer.X);
            Invalidate();
        }
        private void AutoScrollDrag()
        {
            if (pressed == null || !dragging) return;
            if (!Capture || !ValidTarget() || !InDragArea(pointer)) { CancelPointer(); return; }
            int edge = Math.Max(Px(16), SystemInformation.DragSize.Width * 2);
            int direction = pointer.X < Viewport.Left + edge ? -1 : pointer.X >= Viewport.Right - edge ? 1 : 0;
            if (direction == 0) return;
            offset += direction * Px(24); ClampOffset(); UpdateInsertion();
        }
        private void CancelPointer()
        {
            pressed = null; action = PointerAction.None; pressedButton = MouseButtons.None; dragging = false;
            scroll.Stop();
            if (filteringKeys) { Application.RemoveMessageFilter(this); filteringKeys = false; }
            if (Capture) Capture = false;
            Cursor = Cursors.Default; Invalidate();
        }
        public bool PreFilterMessage(ref Message m)
        {
            if (pressed != null && (m.Msg == 0x100 || m.Msg == 0x104) && m.WParam.ToInt32() == (int)Keys.Escape)
            { CancelPointer(); return true; }
            return false;
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if ((keyData & Keys.KeyCode) == Keys.Escape && pressed != null) { CancelPointer(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        protected override void WndProc(ref Message m)
        {
            // WinForms can release capture before dispatching managed MouseUp. Only that native
            // release is deferred; WM_CAPTURECHANGED from another window still cancels immediately.
            bool up = m.Msg == 0x202 || m.Msg == 0x208;
            if (up) releasingMouse = true;
            try { base.WndProc(ref m); }
            finally { if (up) { releasingMouse = false; if (pressed != null && !Capture) CancelPointer(); } }
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture && !releasingMouse) CancelPointer();
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            offset -= e.Delta / 120 * Px(100); ClampOffset();
            if (dragging) UpdateInsertion();
            else if (pressed != null) CancelPointer();
            Invalidate();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { CancelPointer(); scroll.Dispose(); tip.Dispose(); foreach (var icon in icons.Values) icon?.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
