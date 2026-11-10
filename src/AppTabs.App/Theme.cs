using System.Drawing;
using System.Windows.Forms;

namespace AppTabs.App
{
    internal static class Theme
    {
        public static readonly Color Ink = Color.FromArgb(30, 41, 59);
        public static readonly Color Muted = Color.FromArgb(100, 116, 139);
        public static readonly Color Line = Color.FromArgb(226, 232, 240);
        public static readonly Color Surface = Color.FromArgb(248, 250, 252);
        public static readonly Color Accent = Color.FromArgb(42, 103, 211);
        public static readonly Color Chrome = Color.FromArgb(233, 238, 245);
        public static readonly Color Toolbar = Color.FromArgb(253, 254, 255);
        public static readonly Color Hover = Color.FromArgb(222, 232, 245);
        public static readonly Color Pressed = Color.FromArgb(209, 222, 241);
        public static readonly Color AccentSoft = Color.FromArgb(228, 237, 253);
        public static readonly Color Disabled = Color.FromArgb(166, 175, 189);
        public static readonly Color Danger = Color.FromArgb(220, 48, 65);
        internal static int Pixels(int dpi, int logical) => System.Math.Max(1, (int)System.Math.Round(logical * dpi / 96F));
        public static Button Button(string text, bool primary = false)
        {
            var button = new Button { Text = text, AutoSize = true, Height = 32, FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Ink,
                Padding = new Padding(10, 2, 10, 2), Margin = new Padding(4), Cursor = Cursors.Hand };
            button.FlatAppearance.BorderColor = primary ? Accent : Line;
            return button;
        }
        public static void Dialog(Form form)
        {
            form.Font = new Font("Microsoft YaHei UI", 9F);
            form.BackColor = Surface;
            form.ForeColor = Ink;
            form.StartPosition = FormStartPosition.CenterParent;
            form.MinimizeBox = false;
            form.MaximizeBox = false;
            form.ShowInTaskbar = false;
        }
    }
}
