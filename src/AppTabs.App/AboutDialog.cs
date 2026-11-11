using System.Drawing;
using System.Windows.Forms;

namespace AppTabs.App
{
    internal sealed class AboutDialog : ShellDialog
    {
        private readonly PictureBox logo;
        internal static string VersionText => typeof(AboutDialog).Assembly.GetName().Version?.ToString(3) ?? "未知";
        public AboutDialog(bool services = true) : base(services)
        {
            Text = "关于 AppTabs"; ClientSize = new Size(480, 244);
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.White };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
            var content = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false, Padding = new Padding(0, 12, 28, 12), BackColor = Color.White
            };
            var close = Theme.Button("关闭", true); close.AutoSize = false; close.Size = new Size(90, 32);
            close.DialogResult = DialogResult.OK; footer.Controls.Add(close);

            logo = new PictureBox { Name = "aboutLogo", SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(64, 64), Location = new Point(32, 22) };
            using (var icon = AppIcon.Load(64)) logo.Image = icon.ToBitmap();
            var title = new Label
            {
                Name = "aboutTitle", Text = "AppTabs", AutoSize = true, Location = new Point(120, 18),
                Font = new Font(Font.FontFamily, 18F, FontStyle.Bold), ForeColor = Theme.Ink
            };
            var version = new Label
            {
                Name = "aboutVersion", Text = "版本 " + VersionText + " · 作者 @zouv", AutoSize = true,
                Location = new Point(120, 58), ForeColor = Theme.Muted
            };
            var purpose = new Label
            {
                Name = "aboutPurpose", Text = "用标签页管理 Windows 桌面软件窗口。\n支持兼容分组与实验性嵌入，不自动启动或接管软件。",
                AutoSize = false, Size = new Size(330, 44), Location = new Point(120, 88), ForeColor = Theme.Ink, UseMnemonic = false
            };
            content.Controls.AddRange(new Control[] { logo, title, version, purpose });
            root.Controls.Add(content, 0, 0); root.Controls.Add(footer, 0, 1);
            Controls.Add(root); AcceptButton = close; CancelButton = close;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && logo != null) { var image = logo.Image; logo.Image = null; image?.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
