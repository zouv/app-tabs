using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using AppTabs.Core;

namespace AppTabs.App
{
    internal sealed class TextDialog : Form
    {
        private readonly TextBox input = new TextBox { Dock = DockStyle.Top, MaxLength = 120 };
        public string Value => input.Text;
        public TextDialog(string title, string hint, string value)
        {
            Theme.Dialog(this); Text = title; ClientSize = new Size(480, 156); FormBorderStyle = FormBorderStyle.FixedDialog;
            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16) };
            var label = new Label { Text = hint, Dock = DockStyle.Top, Height = 34, ForeColor = Theme.Muted };
            input.Text = value; body.Controls.Add(input); body.Controls.Add(label);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
            var ok = Theme.Button("确定", true); var cancel = Theme.Button("取消"); cancel.DialogResult = DialogResult.Cancel;
            ok.Click += (_, __) => { if (input.Text.Trim().Length > 0) { DialogResult = DialogResult.OK; Close(); } };
            actions.Controls.Add(ok); actions.Controls.Add(cancel); Controls.Add(body); Controls.Add(actions); AcceptButton = ok; CancelButton = cancel;
            Shown += (_, __) => { input.Focus(); input.SelectAll(); };
        }
    }

    internal sealed class TabSearchDialog : Form
    {
        public WindowSession? Selected { get; private set; }
        public TabSearchDialog(IReadOnlyList<WindowSession> sessions)
        {
            Theme.Dialog(this); Text = "搜索标签"; ClientSize = new Size(560, 360);
            var search = new TextBox { Dock = DockStyle.Top }; var list = new ListBox { Dock = DockStyle.Fill, DisplayMember = "DisplayTitle", IntegralHeight = false };
            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) }; body.Controls.Add(list); body.Controls.Add(search); Controls.Add(body);
            void Filter() { list.Items.Clear(); foreach (var session in sessions.Where(s => s.DisplayTitle.IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) >= 0)) list.Items.Add(session); if (list.Items.Count > 0) list.SelectedIndex = 0; }
            void Pick() { if (list.SelectedItem is WindowSession session) { Selected = session; DialogResult = DialogResult.OK; Close(); } }
            search.TextChanged += (_, __) => Filter(); list.DoubleClick += (_, __) => Pick();
            search.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { Pick(); e.SuppressKeyPress = true; } else if (e.KeyCode == Keys.Down && list.Items.Count > 0) { list.Focus(); e.SuppressKeyPress = true; } };
            list.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { Pick(); e.SuppressKeyPress = true; } };
            KeyPreview = true; KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); }; Filter(); Shown += (_, __) => search.Focus();
        }
    }

    internal sealed class SettingsDialog : Form
    {
        private readonly ComboBox hotkey = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 340 };
        private readonly CheckBox hide = new CheckBox { Text = "切后台时隐藏到系统托盘", AutoSize = true };
        public int HotkeyChoice => hotkey.SelectedIndex;
        public bool HideToTray => hide.Checked;
        public SettingsDialog(AppState state)
        {
            Theme.Dialog(this); Text = "设置 — AppTabs"; ClientSize = new Size(490, 262); FormBorderStyle = FormBorderStyle.FixedDialog;
            var body = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(20), WrapContents = false };
            body.Controls.Add(new Label { Text = "全局呼出 / 切后台快捷键", AutoSize = true, Margin = new Padding(0, 0, 0, 12) });
            hotkey.Items.AddRange(new object[] { "Ctrl + Alt + Space", "Ctrl + Alt + A", "Ctrl + Alt + F12", "停用全局快捷键" }); hotkey.SelectedIndex = state.HotkeyChoice;
            body.Controls.Add(hotkey); hide.Checked = state.HideToTray; hide.Margin = new Padding(0, 20, 0, 16); body.Controls.Add(hide);
            body.Controls.Add(new Label { Text = "Ctrl+Tab / Ctrl+Shift+Tab：切换标签\nCtrl+1…9：跳转标签；Ctrl+W：分离；Ctrl+F：搜索", AutoSize = true, ForeColor = Theme.Muted });
            var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
            var ok = Theme.Button("保存", true); ok.DialogResult = DialogResult.OK;
            var cancel = Theme.Button("取消"); cancel.DialogResult = DialogResult.Cancel; actions.Controls.Add(ok); actions.Controls.Add(cancel);
            Controls.Add(body); Controls.Add(actions); AcceptButton = ok; CancelButton = cancel;
        }
    }
}
