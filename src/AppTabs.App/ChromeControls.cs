using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AppTabs.App
{
    internal enum UiGlyph { App, AddWindow, DetachWindow, WindowGroup, Search, Settings, Minimize, Maximize, Restore, Close }

    internal static class ChromeGlyphs
    {
        internal static void Draw(Graphics graphics, UiGlyph glyph, Rectangle bounds, Color color)
        {
            var state = graphics.Save();
            graphics.TranslateTransform(bounds.Left, bounds.Top);
            graphics.ScaleTransform(bounds.Width / 24F, bounds.Height / 24F);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(color, 1.7F) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                switch (glyph)
                {
                    case UiGlyph.AddWindow:
                        graphics.DrawRectangle(pen, 3, 5, 12, 14);
                        graphics.DrawLine(pen, 13, 12, 21, 12);
                        graphics.DrawLines(pen, new[] { new Point(18, 9), new Point(21, 12), new Point(18, 15) }); break;
                    case UiGlyph.DetachWindow:
                        graphics.DrawRectangle(pen, 3, 8, 13, 13);
                        graphics.DrawLine(pen, 11, 13, 21, 3);
                        graphics.DrawLines(pen, new[] { new Point(15, 3), new Point(21, 3), new Point(21, 9) }); break;
                    case UiGlyph.WindowGroup:
                        graphics.DrawLines(pen, new[] { new Point(6, 3), new Point(18, 3), new Point(18, 21),
                            new Point(12, 16), new Point(6, 21), new Point(6, 3) });
                        graphics.DrawLine(pen, 9, 7, 15, 7); break;
                    case UiGlyph.Search:
                        graphics.DrawEllipse(pen, 4, 4, 12, 12); graphics.DrawLine(pen, 14, 14, 20, 20); break;
                    case UiGlyph.Settings:
                        var points = new PointF[32];
                        for (int i = 0; i < points.Length; i++)
                        {
                            double angle = i * Math.PI * 2 / points.Length;
                            float radius = i % 4 == 0 || i % 4 == 3 ? 7.5F : 10F;
                            points[i] = new PointF(12 + (float)Math.Cos(angle) * radius, 12 + (float)Math.Sin(angle) * radius);
                        }
                        using (var path = new GraphicsPath(FillMode.Alternate))
                        using (var brush = new SolidBrush(color))
                        { path.AddPolygon(points); path.AddEllipse(9, 9, 6, 6); graphics.FillPath(brush, path); }
                        break;
                    case UiGlyph.Minimize: graphics.DrawLine(pen, 6, 13, 18, 13); break;
                    case UiGlyph.Maximize: graphics.DrawRectangle(pen, 6, 6, 12, 12); break;
                    case UiGlyph.Restore:
                        graphics.DrawRectangle(pen, 5, 9, 10, 10);
                        graphics.DrawLines(pen, new[] { new Point(9, 7), new Point(9, 5), new Point(19, 5), new Point(19, 15), new Point(17, 15) }); break;
                    case UiGlyph.Close:
                        graphics.DrawLine(pen, 6, 6, 18, 18); graphics.DrawLine(pen, 18, 6, 6, 18); break;
                }
            }
            graphics.Restore(state);
        }
    }

    // Button keeps WinForms keyboard / accessibility semantics; only its visual is replaced.
    internal sealed class IconButton : Button
    {
        private bool hovered, pressed;
        private UiGlyph glyph;
        public UiGlyph Glyph { get => glyph; set { glyph = value; Invalidate(); } }
        public bool Caption { get; set; }
        public bool Primary { get; set; }
        public bool ChromeTopEdge { get; set; }
        public Icon? ApplicationIcon { get; set; } // Borrowed; owned by MainForm.
        public IconButton(UiGlyph glyph, string accessibleName)
        {
            Glyph = glyph; Text = accessibleName; AccessibleName = accessibleName;
            AccessibleRole = AccessibleRole.PushButton;
            AutoSize = false; Size = new Size(32, 30); Margin = Padding.Empty;
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; TabStop = true;
            Cursor = Cursors.Hand; BackColor = Theme.Toolbar;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnMouseCaptureChanged(EventArgs e) { pressed = false; Invalidate(); base.OnMouseCaptureChanged(e); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { hovered = pressed = false; base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            graphics.Clear(Parent?.BackColor ?? Theme.Toolbar);
            bool danger = Enabled && Caption && Glyph == UiGlyph.Close && hovered;
            Color background = danger ? Theme.Danger : !Enabled ? Color.Transparent : pressed ? Theme.Pressed : hovered ? Theme.Hover : Primary ? Theme.AccentSoft : Color.Transparent;
            if (background != Color.Transparent)
                using (var brush = new SolidBrush(background)) graphics.FillRectangle(brush, ClientRectangle);
            int size = Theme.Pixels(DeviceDpi, Glyph == UiGlyph.App ? 24 : 18);
            var rect = new Rectangle((Width - size) / 2, (Height - size) / 2, size, size);
            Color foreground = !Enabled ? Theme.Disabled : danger ? Color.White : Primary ? Theme.Accent : Theme.Ink;
            if (Glyph == UiGlyph.App && ApplicationIcon != null) graphics.DrawIcon(ApplicationIcon, rect);
            else ChromeGlyphs.Draw(graphics, Glyph, rect, foreground);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(graphics, Rectangle.Inflate(ClientRectangle, -Theme.Pixels(DeviceDpi, 3), -Theme.Pixels(DeviceDpi, 3)));
        }
        protected override void WndProc(ref Message m)
        {
            if (ChromeTopEdge && m.Msg == NativeUi.WmNcHitTest && FindForm()?.WindowState == FormWindowState.Normal)
            {
                Point point = PointToClient(NativeUi.MessagePoint(m.LParam));
                if (point.Y >= 0 && point.Y < Theme.Pixels(DeviceDpi, 4))
                { m.Result = new IntPtr(-1); return; } // Let the host's top resize band win.
            }
            base.WndProc(ref m);
        }
    }

    // Transparent top-band hit testing passes through nested panels to the MainForm.
    internal sealed class ChromePanel : Panel
    {
        public ChromePanel() { DoubleBuffered = true; }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeUi.WmNcHitTest && FindForm()?.WindowState == FormWindowState.Normal)
            {
                Point point = PointToClient(NativeUi.MessagePoint(m.LParam));
                if (point.Y >= 0 && point.Y < Theme.Pixels(DeviceDpi, 4))
                { m.Result = new IntPtr(-1); return; }
            }
            base.WndProc(ref m);
        }
    }

    internal sealed class ToolbarDivider : Control
    {
        public ToolbarDivider() { TabStop = false; SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            int inset = Theme.Pixels(DeviceDpi, 8);
            using (var pen = new Pen(Theme.Line)) e.Graphics.DrawLine(pen, Width / 2, inset, Width / 2, Math.Max(inset, Height - inset));
        }
    }
}
