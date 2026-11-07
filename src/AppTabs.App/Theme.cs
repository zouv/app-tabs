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
        public static readonly Color Accent = Color.FromArgb(79, 70, 229);
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
