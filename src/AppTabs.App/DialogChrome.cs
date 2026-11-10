using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AppTabs.App
{
    /// <summary>Only owns this dialog's chrome; never operates on a candidate or hosted window.</summary>
    public class ShellDialog : Form
    {
        private readonly Panel body;
        private readonly DialogTitleBar titleBar;
        private readonly Font dialogFont;
        private readonly List<ResizeChildWindow> resizeChildren = new List<ResizeChildWindow>();
        private bool installing = true, shaping;
        protected bool Services { get; }
        public bool Resizable { get; }
        protected override bool ShowWithoutActivation => !Services;

        public ShellDialog(bool services = true, bool resizable = false)
        {
            Services = services; Resizable = resizable;
            DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true);
            Theme.Dialog(this); dialogFont = Font;
            FormBorderStyle = FormBorderStyle.None; ShowIcon = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(190, 202, 219); Padding = new Padding(1);
            body = new Panel { Name = "dialogBody", Dock = DockStyle.Fill, BackColor = Theme.Surface };
            titleBar = new DialogTitleBar(this);
            Controls.Add(body); Controls.Add(titleBar);
            installing = false;
            WatchChildren(body); WatchChildren(titleBar);
        }

        // Existing dialog callers keep the familiar Controls.Add(root) construction pattern.
        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            if (!installing && e.Control != body && e.Control != titleBar)
            {
                Controls.Remove(e.Control); body.Controls.Add(e.Control);
            }
        }
        protected override CreateParams CreateParams
        {
            get
            {
                var value = base.CreateParams;
                value.ClassStyle &= ~0x00020000; // no CS_DROPSHADOW
                if (!Services) value.ExStyle |= 0x08000000 | 0x00000080; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
                value.Style &= ~0x00C00000;
                if (Resizable) value.Style |= 0x00040000; // native size loop, no native visible frame
                return value;
            }
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // DWM shadow/nonclient rendering is disabled only for our own newly-created HWND.
            try { int disabled = 1; DwmSetWindowAttribute(Handle, 2, ref disabled, sizeof(int)); }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            Shape();
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            // The visible ring is one physical pixel, even after WinForms DPI scaling.
            if (Padding != new Padding(1)) Padding = new Padding(1);
            if (titleBar != null) titleBar.Height = Theme.Pixels(DeviceDpi, 38);
            base.OnLayout(e); Shape();
        }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); Shape(); }
        protected override void OnDpiChanged(DpiChangedEventArgs e) { base.OnDpiChanged(e); PerformLayout(); Shape(); }
        private void Shape()
        {
            if (shaping || body == null || titleBar == null || ClientSize.Width < 3 || ClientSize.Height < 3) return;
            shaping = true;
            try
            {
                float radius = Theme.Pixels(DeviceDpi, 12);
                using (var outer = DialogStyle.Round(ClientRectangle, radius)) ReplaceRegion(this, new Region(outer));
                using (var inner = DialogStyle.Round(new Rectangle(1, 1, ClientSize.Width - 2, ClientSize.Height - 2), radius - 1))
                {
                    foreach (Control child in Controls)
                        using (var path = (GraphicsPath)inner.Clone())
                        using (var shift = new Matrix())
                        {
                            shift.Translate(-child.Left, -child.Top); path.Transform(shift);
                            ReplaceRegion(child, new Region(path));
                        }
                }
            }
            finally { shaping = false; }
        }
        private static void ReplaceRegion(Control control, Region region)
        {
            // WinForms takes ownership and disposes the old Region in its setter.
            // The current Region remains alive until replaced or the control is disposed.
            control.Region = region;
        }
        internal int ResizeHit(Point screen)
        {
            if (!Resizable || WindowState != FormWindowState.Normal) return 1;
            Point p = PointToClient(screen); int grip = Theme.Pixels(DeviceDpi, 6);
            bool left = p.X < grip, right = p.X >= ClientSize.Width - grip;
            bool top = p.Y < grip, bottom = p.Y >= ClientSize.Height - grip;
            if (top) return left ? 13 : right ? 14 : 12;
            if (bottom) return left ? 16 : right ? 17 : 15;
            return left ? 10 : right ? 11 : 1;
        }
        internal void DragTitle()
        {
            if (!Services || !IsHandleCreated || IsDisposed) return;
            NativeUi.ReleaseCapture();
            NativeUi.SendMessage(Handle, NativeUi.WmNcLButtonDown, new IntPtr(2), IntPtr.Zero);
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeUi.WmNcCalcSize) { m.Result = IntPtr.Zero; return; }
            if (m.Msg == 0x85) { m.Result = IntPtr.Zero; return; } // no native frame painting
            if (m.Msg == NativeUi.WmNcActivate) { m.Result = new IntPtr(1); return; }
            if (m.Msg == NativeUi.WmNcHitTest)
            {
                m.Result = new IntPtr(ResizeHit(NativeUi.MessagePoint(m.LParam))); return;
            }
            base.WndProc(ref m);
        }
        private void WatchChildren(Control parent)
        {
            if (Resizable) resizeChildren.Add(new ResizeChildWindow(this, parent));
            parent.ControlAdded += ChildAdded;
            foreach (Control child in parent.Controls) WatchChildren(child);
        }
        private void ChildAdded(object? sender, ControlEventArgs e) { if (e.Control != null) WatchChildren(e.Control); }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { foreach (var window in resizeChildren) window.Dispose(); resizeChildren.Clear(); }
            base.Dispose(disposing);
            if (disposing) dialogFont.Dispose();
        }
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        // Child HWNDs must pass the outer resize band through to this dialog's hit-test.
        private sealed class ResizeChildWindow : NativeWindow, IDisposable
        {
            private readonly ShellDialog dialog;
            private readonly Control control;
            internal ResizeChildWindow(ShellDialog dialog, Control control)
            {
                this.dialog = dialog; this.control = control;
                control.HandleCreated += Created; control.HandleDestroyed += Destroyed;
                if (control.IsHandleCreated) AssignHandle(control.Handle);
            }
            private void Created(object? sender, EventArgs e) { AssignHandle(control.Handle); }
            private void Destroyed(object? sender, EventArgs e) { ReleaseHandle(); }
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == NativeUi.WmNcHitTest && dialog.ResizeHit(NativeUi.MessagePoint(m.LParam)) != 1)
                { m.Result = new IntPtr(-1); return; } // HTTRANSPARENT, same UI thread only
                base.WndProc(ref m);
            }
            public void Dispose() { control.HandleCreated -= Created; control.HandleDestroyed -= Destroyed; ReleaseHandle(); }
        }
    }

    internal sealed class DialogTitleBar : Panel
    {
        internal DialogTitleBar(ShellDialog dialog)
        {
            Name = "dialogTitleBar"; Dock = DockStyle.Top; Height = 38; BackColor = Color.White; TabStop = false;
            var title = new Label { Name = "dialogTitle", Text = dialog.Text, Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(14, 0, 0, 0), UseMnemonic = false };
            var close = new DialogCloseButton { Name = "dialogClose", Dock = DockStyle.Right, Width = 44 };
            Controls.Add(title); Controls.Add(close);
            dialog.TextChanged += (_, __) => title.Text = dialog.Text;
            close.Click += (_, __) => { dialog.DialogResult = DialogResult.Cancel; dialog.Close(); };
            title.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) dialog.DragTitle(); };
            MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) dialog.DragTitle(); };
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            var close = Controls["dialogClose"];
            if (close != null) close.Width = Theme.Pixels(DeviceDpi, 44);
            var title = Controls["dialogTitle"];
            if (title != null) title.Padding = new Padding(Theme.Pixels(DeviceDpi, 14), 0, 0, 0);
            base.OnLayout(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        { base.OnPaint(e); using (var pen = new Pen(Theme.Line)) e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1); }
    }
    internal sealed class DialogCloseButton : Button
    {
        private bool hover;
        internal DialogCloseButton()
        {
            AccessibleName = "关闭"; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(hover ? Theme.Danger : Color.White);
            int half = Theme.Pixels(DeviceDpi, 5), x = Width / 2, y = Height / 2;
            using (var pen = new Pen(hover ? Color.White : Theme.Ink, 1.4F))
            { e.Graphics.DrawLine(pen, x - half, y - half, x + half, y + half); e.Graphics.DrawLine(pen, x + half, y - half, x - half, y + half); }
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -5, -5));
        }
    }
    internal static class DialogStyle
    {
        internal static GraphicsPath Round(Rectangle bounds, float radius)
        {
            var path = new GraphicsPath(); float diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            if (diameter < 2) { path.AddRectangle(bounds); return path; }
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90); path.CloseFigure(); return path;
        }
        // Compatibility for dialogs constructed using the ExplorerTabs pattern. The base applies automatically.
        internal static void Apply(ShellDialog dialog) { dialog.PerformLayout(); }
    }
}
