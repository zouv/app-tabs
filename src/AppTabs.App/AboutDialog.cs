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
            Text = "关于 AppTabs"; ClientSize = new Size(560, 356);
            logo = new PictureBox { Name = "aboutLogo", SizeMode = PictureBoxSizeMode.Zoom, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 20, 0) };
            using (var icon = AppIcon.Load(64)) logo.Image = icon.ToBitmap();
            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.Controls.Add(logo, 0, 0);
            header.Controls.Add(new Label { Name = "aboutVersion", Text = "AppTabs\n版本 " + VersionText, Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Ink }, 1, 0);
            var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(24, 18, 24, 8) };
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 80)); content.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            content.Controls.Add(header, 0, 0);
            content.Controls.Add(new Label { Name = "aboutPurpose", Text = "用标签页管理 Windows 桌面软件窗口。\n支持兼容分组与实验性嵌入，不自动启动或接管软件。", Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false }, 0, 1);
            content.Controls.Add(new Label { Name = "aboutSafety", Text = "默认分离或退出会恢复并保留原软件，不结束进程。\n接管前先保存恢复日志，窗口查询失败会保留恢复记录。\n嵌入兼容性取决于目标软件；恢复不能复活已关闭的窗口，\n也不能保证未保存内容。", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Muted, UseMnemonic = false }, 0, 2);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(8) };
            var close = Theme.Button("关闭", true); close.DialogResult = DialogResult.OK; footer.Controls.Add(close);
            var root = new Panel { Dock = DockStyle.Fill }; root.Controls.Add(content); root.Controls.Add(footer);
            Controls.Add(root); AcceptButton = close; CancelButton = close;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && logo != null) { var image = logo.Image; logo.Image = null; image?.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
