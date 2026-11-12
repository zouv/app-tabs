using System;
using System.Drawing;
using System.Windows.Forms;
using AppTabs.Core;

namespace AppTabs.App
{
    internal sealed class SettingsDialog : ShellDialog
    {
        private readonly ComboBox preference = new ComboBox { Name = "preference", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private readonly ComboBox hotkey = new ComboBox { Name = "hotkeyChoice", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private readonly NumericUpDown recentLimit = new NumericUpDown { Name = "recentJoinLimit", Minimum = RecentAppsPolicy.MinimumLimit, Maximum = RecentAppsPolicy.MaximumLimit, Dock = DockStyle.Fill };
        private readonly CheckBox borderless = new CheckBox { Name = "borderlessGrouped", Text = "分组窗口使用无边框", Dock = DockStyle.Fill, AutoSize = false };
        private readonly CheckBox hide = new CheckBox { Name = "hideToTray", Text = "切后台时隐藏到系统托盘", Dock = DockStyle.Fill, AutoSize = false };
        private readonly Panel general, keys;
        private readonly Button generalNav, keysNav;
        public AttachPreference Preference => (AttachPreference)preference.SelectedIndex;
        public int HotkeyChoice => hotkey.SelectedIndex;
        public bool HideToTray => hide.Checked;
        public int RecentJoinLimit => (int)recentLimit.Value;
        public bool BorderlessGrouped => borderless.Checked;

        public SettingsDialog(AppState state, bool services = true) : base(services)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            Text = "设置 — AppTabs"; ClientSize = new Size(800, 640);
            preference.Items.AddRange(new object[] { "自动 · 优先嵌入", "分组", "实验性嵌入" });
            preference.SelectedIndex = Enum.IsDefined(typeof(AttachPreference), state.Preference) ? (int)state.Preference : 0;
            hotkey.Items.AddRange(new object[] { "Ctrl + Alt + Space", "Ctrl + Alt + A", "Ctrl + Alt + F12", "停用全局快捷键" });
            hotkey.SelectedIndex = state.HotkeyChoice >= 0 && state.HotkeyChoice <= 3 ? state.HotkeyChoice : 0;
            hide.Checked = state.HideToTray;
            recentLimit.Value = Math.Max(RecentAppsPolicy.MinimumLimit, Math.Min(RecentAppsPolicy.MaximumLimit, state.RecentJoinLimit));
            borderless.Checked = state.BorderlessGrouped != false;

            var host = new Panel { Name = "settingsPages", Dock = DockStyle.Fill, Padding = new Padding(24, 18, 24, 12), BackColor = Color.White };
            general = new Panel { Name = "generalPage", Dock = DockStyle.Fill, BackColor = Color.White };
            keys = new Panel { Name = "hotkeyPage", Dock = DockStyle.Fill, BackColor = Color.White };
            var generalRows = PageRows(); general.Controls.Add(generalRows);
            AddRow(generalRows, Section("常规"), 38);
            AddRow(generalRows, Note("设置仅由您主动保存，不会自动接管或启动软件。"), 42);
            AddRow(generalRows, Section("加入窗口"), 34);
            AddRow(generalRows, Field("默认挂载模式", preference), 42);
            AddRow(generalRows, Note("自动模式优先嵌入，兼容性检查不通过时回退分组。\n分组默认无边框，由 AppTabs 统一管理拖动 / 缩放 / 最大化；\n关闭下方无边框选项后保留软件原生边框。模式只影响之后加入的窗口。"), 80);
            AddRow(generalRows, Field("最近加入应用数量", recentLimit), 42);
            AddRow(generalRows, Note("“加入”菜单按最近成功加入的应用排序，只列出当前仍打开的匹配窗口；不会自动启动软件。"), 34);
            AddRow(generalRows, borderless, 34);
            AddRow(generalRows, Note("默认开启：分组窗口去掉原生标题栏 / 边框，由 AppTabs 接管拖动、缩放与最大化。\n仅影响之后加入的窗口；软件显示异常时可关闭后重新加入。"), 46);
            AddRow(generalRows, Section("安全行为"), 34);
            AddRow(generalRows, Note("标签 ×、Ctrl + W 与默认退出都会分离并保留软件。\n关闭软件必须使用独立的明确操作；AppTabs 不杀进程。\n查询失败会保留恢复记录，不会当作窗口已消失。"), 80);
            AddSpacer(generalRows);

            var keyRows = PageRows(); keys.Controls.Add(keyRows);
            AddRow(keyRows, Section("全局快捷键"), 34);
            AddRow(keyRows, Note("其他软件前台时呼出 AppTabs；AppTabs 前台时切后台。"), 34);
            AddRow(keyRows, Field("呼出 / 切后台", hotkey), 42);
            AddRow(keyRows, Note("仅提供以上四个候选；保存后由宿主注册并反馈占用情况。"), 34);
            AddRow(keyRows, hide, 34);
            AddRow(keyRows, Note("默认不勾选：最小化并保留任务栏按钮。\n勾选后隐藏到系统托盘；托盘图标只在隐藏期间出现。"), 42);
            AddRow(keyRows, Section("固定标签快捷键"), 34);
            AddRow(keyRows, FixedBindings(), 168);
            AddRow(keyRows, Note("仅宿主或当前标签前台生效；模态对话框期间停用。\n这些标签快捷键为固定说明，不支持任意重绑。"), 48);
            AddSpacer(keyRows);
            host.Controls.Add(general); host.Controls.Add(keys);

            var navigation = new FlowLayoutPanel { Name = "settingsNavigation", Dock = DockStyle.Left, Width = 156,
                FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(10, 18, 10, 0), BackColor = Theme.Surface };
            generalNav = Navigation("常规", "generalNav"); keysNav = Navigation("快捷键", "hotkeyNav");
            navigation.Controls.Add(generalNav); navigation.Controls.Add(keysNav);
            generalNav.Click += (_, __) => SelectPage(false); keysNav.Click += (_, __) => SelectPage(true);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 56, FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false, Padding = new Padding(8), BackColor = Color.White };
            var save = Theme.Button("保存", true); save.Name = "saveSettings"; save.DialogResult = DialogResult.OK;
            var cancel = Theme.Button("取消"); cancel.DialogResult = DialogResult.Cancel;
            footer.Controls.Add(save); footer.Controls.Add(cancel);
            var root = new Panel { Dock = DockStyle.Fill }; root.Controls.Add(host); root.Controls.Add(navigation); root.Controls.Add(footer);
            Controls.Add(root); AcceptButton = save; CancelButton = cancel; SelectPage(false);
        }
        private void SelectPage(bool shortcuts)
        {
            general.Visible = !shortcuts; keys.Visible = shortcuts;
            generalNav.BackColor = shortcuts ? Theme.Surface : Theme.AccentSoft;
            keysNav.BackColor = shortcuts ? Theme.AccentSoft : Theme.Surface;
            generalNav.ForeColor = shortcuts ? Theme.Ink : Theme.Accent;
            keysNav.ForeColor = shortcuts ? Theme.Accent : Theme.Ink;
        }
        private static Button Navigation(string text, string name)
        {
            var button = Theme.Button(text); button.Name = name; button.AutoSize = false; button.Size = new Size(132, 40);
            button.TextAlign = ContentAlignment.MiddleLeft; button.FlatAppearance.BorderSize = 0; return button;
        }
        private static TableLayoutPanel PageRows() => new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 0, Margin = Padding.Empty };
        private static void AddRow(TableLayoutPanel table, Control control, int height)
        {
            int row = table.RowCount++; table.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            control.Dock = DockStyle.Fill; control.Margin = Padding.Empty; table.Controls.Add(control, 0, row);
        }
        private static void AddSpacer(TableLayoutPanel table) { table.RowCount++; table.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); }
        private static Label Section(string text) => new Label { Text = text, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Ink };
        private static Label Note(string text) => new Label { Text = text, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false };
        private static Control Field(string label, Control input)
        {
            var row = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Padding = new Padding(0, 6, 0, 6) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 136)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            input.Margin = Padding.Empty; row.Controls.Add(input, 1, 0); return row;
        }
        private static Control FixedBindings()
        {
            var table = new TableLayoutPanel { Name = "fixedBindings", ColumnCount = 2, RowCount = 6, BackColor = Theme.Surface, Padding = new Padding(10, 0, 8, 0) };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
            string[,] bindings = { { "Ctrl + Tab / Ctrl + PageDown", "下一个标签" }, { "Ctrl + Shift + Tab / Ctrl + PageUp", "上一个标签" },
                { "Ctrl + 1 … 8", "跳转第 1 … 8 个标签" }, { "Ctrl + 9", "跳转第 9 个标签" }, { "Ctrl + W", "分离并保留软件" }, { "Ctrl + F", "搜索标签" } };
            for (int i = 0; i < 6; i++)
            {
                table.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / 6));
                for (int j = 0; j < 2; j++) table.Controls.Add(new Label { Text = bindings[i, j], Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false }, j, i);
            }
            return table;
        }
    }
}
