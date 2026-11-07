using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using AppTabs.Core;

namespace AppTabs.App
{
    public sealed class TabBar : Control
    {
        private IReadOnlyList<WindowSession> sessions = Array.Empty<WindowSession>();
        private WindowSession? active, pressed;
        private readonly Dictionary<string, Icon?> icons = new Dictionary<string, Icon?>(StringComparer.OrdinalIgnoreCase);
        private readonly ToolTip tip = new ToolTip();
        private int offset;
        private Point dragOrigin;
        private bool dragging;
        private WindowSession? hovered;
        public event Action<WindowSession>? SelectTab;
        public event Action<WindowSession>? DetachTab;
        public event Action<WindowSession, int>? ReorderTab;
        public event Action<WindowSession, Point>? ContextTab;
        public event Action? AddWindow;

        public TabBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 46;
            BackColor = Color.FromArgb(15, 23, 42);
            ForeColor = Color.White;
            Cursor = Cursors.Hand;
            AccessibleName = "应用标签栏";
        }
        public void Bind(IReadOnlyList<WindowSession> value, WindowSession? selected)
        {
            sessions = value; active = selected;
            foreach (string path in icons.Keys.Where(p => !sessions.Any(s => s.Identity.ImagePath == p)).ToArray())
            { icons[path]?.Dispose(); icons.Remove(path); }
            foreach (var session in sessions)
            {
                string path = session.Identity.ImagePath;
                if (icons.ContainsKey(path)) continue;
                try { icons[path] = File.Exists(path) ? Icon.ExtractAssociatedIcon(path) : null; }
                catch { icons[path] = null; }
            }
            ClampOffset();
            if (selected != null)
            {
                int x = 8;
                foreach (var session in sessions)
                {
                    int width = TabWidth(session);
                    if (session == selected)
                    {
                        if (x - offset < 8) offset = x - 8;
                        else if (x + width - offset > Width - 46) offset = x + width - Width + 46;
                        break;
                    }
                    x += width + 4;
                }
            }
            ClampOffset(); Invalidate();
        }
        private int TabWidth(WindowSession session) => Math.Min(230, Math.Max(148, TextRenderer.MeasureText(session.DisplayTitle, Font).Width + 64));
        private IEnumerable<(WindowSession Session, Rectangle Rect)> Rectangles()
        {
            int x = 8 - offset;
            foreach (var session in sessions)
            {
                int width = TabWidth(session);
                yield return (session, new Rectangle(x, 7, width, Height - 12));
                x += width + 4;
            }
        }
        private void ClampOffset() { offset = Math.Max(0, Math.Min(offset, Math.Max(0, sessions.Sum(s => TabWidth(s) + 4) + 16 - Math.Max(1, Width - 46)))); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SetClip(new Rectangle(0, 0, Math.Max(0, Width - 44), Height));
            foreach (var entry in Rectangles())
            {
                if (entry.Rect.Right < 0 || entry.Rect.Left > Width - 44) continue;
                bool selected = entry.Session == active;
                using (var brush = new SolidBrush(selected ? Color.FromArgb(51, 65, 85) : entry.Session == hovered ? Color.FromArgb(30, 41, 59) : BackColor))
                    g.FillRectangle(brush, entry.Rect);
                if (selected) using (var brush = new SolidBrush(Color.FromArgb(129, 140, 248)))
                    g.FillRectangle(brush, entry.Rect.Left, entry.Rect.Bottom - 2, entry.Rect.Width, 2);
                if (icons.TryGetValue(entry.Session.Identity.ImagePath, out var icon) && icon != null)
                    g.DrawIcon(icon, new Rectangle(entry.Rect.X + 10, entry.Rect.Y + (entry.Rect.Height - 18) / 2, 18, 18));
                string title = (entry.Session.State == SessionState.Attached ? "" : "! ") + entry.Session.DisplayTitle;
                TextRenderer.DrawText(g, title, Font, new Rectangle(entry.Rect.X + 35, entry.Rect.Y, entry.Rect.Width - 62, entry.Rect.Height),
                    selected ? Color.White : Color.FromArgb(203, 213, 225), TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, "×", Font, CloseRect(entry.Rect), Color.FromArgb(148, 163, 184), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            g.ResetClip();
            using (var plusFont = new Font(Font.FontFamily, 16))
                TextRenderer.DrawText(g, "+", plusFont, new Rectangle(Width - 40, 4, 36, Height - 8), Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        private static Rectangle CloseRect(Rectangle rect) => new Rectangle(rect.Right - 26, rect.Top + 3, 22, rect.Height - 6);
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.X >= Width - 44) { if (e.Button == MouseButtons.Left) AddWindow?.Invoke(); return; }
            foreach (var entry in Rectangles()) if (entry.Rect.Contains(e.Location))
            {
                if (e.Button == MouseButtons.Right) ContextTab?.Invoke(entry.Session, e.Location);
                else if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && CloseRect(entry.Rect).Contains(e.Location))) DetachTab?.Invoke(entry.Session);
                else if (e.Button == MouseButtons.Left) { pressed = entry.Session; dragOrigin = e.Location; SelectTab?.Invoke(entry.Session); Capture = true; }
                break;
            }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var hit = e.X < Width - 44 ? Rectangles().FirstOrDefault(t => t.Rect.Contains(e.Location)).Session : null;
            if (hit != hovered)
            {
                hovered = hit;
                tip.SetToolTip(this, hit == null ? "加入窗口；标签较多时可用滚轮滚动" : hit.DisplayTitle + "\n" + Path.GetFileName(hit.Identity.ImagePath) + " · " + (hit.Snapshot.Mode == HostingMode.Embedded ? "嵌入" : "兼容分组") + "\n× / 中键：分离并保留软件；拖动：排序");
                Invalidate();
            }
            if (pressed != null && Capture && TabManagementPolicy.StartsDrag(dragOrigin, e.Location, SystemInformation.DragSize)) dragging = true;
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            var target = pressed;
            if (target != null && dragging)
            {
                var remaining = Rectangles().Where(t => t.Session != target).Select(t => t.Rect.Left + t.Rect.Width / 2);
                int index = TabManagementPolicy.InsertionIndex(remaining, e.X);
                ReorderTab?.Invoke(target, index);
            }
            pressed = null; dragging = false; Capture = false;
        }
        protected override void OnMouseWheel(MouseEventArgs e) { offset -= e.Delta / 120 * 100; ClampOffset(); Invalidate(); }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { tip.Dispose(); foreach (var icon in icons.Values) icon?.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
