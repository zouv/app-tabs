using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using AppTabs.Core;

namespace AppTabs.App
{
    internal sealed class TextDialog : ShellDialog
    {
        private readonly TextBox input = new TextBox { Dock = DockStyle.Top, MaxLength = 120 };
        public string Value => input.Text;
        public TextDialog(string title, string hint, string value, bool services = true) : base(services)
        {
            Text = title; ClientSize = new Size(480, 204);
            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16) };
            var label = new Label { Text = hint, Dock = DockStyle.Top, Height = 34, ForeColor = Theme.Muted };
            input.Text = value; body.Controls.Add(input); body.Controls.Add(label);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
            var ok = Theme.Button("确定", true); var cancel = Theme.Button("取消"); cancel.DialogResult = DialogResult.Cancel;
            ok.Click += (_, __) => { if (input.Text.Trim().Length > 0) { DialogResult = DialogResult.OK; Close(); } };
            actions.Controls.Add(ok); actions.Controls.Add(cancel); Controls.Add(body); Controls.Add(actions); AcceptButton = ok; CancelButton = cancel;
            Shown += (_, __) => { if (Services) input.Focus(); input.SelectAll(); };
        }
    }

    internal sealed class TabSearchDialog : ShellDialog
    {
        public WindowSession? Selected { get; private set; }
        public TabSearchDialog(IReadOnlyList<WindowSession> sessions, bool services = true) : base(services)
        {
            Text = "搜索标签"; ClientSize = new Size(560, 400);
            var search = new TextBox { Dock = DockStyle.Top }; var list = new ListBox { Dock = DockStyle.Fill, DisplayMember = "DisplayTitle", IntegralHeight = false };
            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) }; body.Controls.Add(list); body.Controls.Add(search); Controls.Add(body);
            void Filter() { list.Items.Clear(); foreach (var session in sessions.Where(s => s.DisplayTitle.IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) >= 0)) list.Items.Add(session); if (list.Items.Count > 0) list.SelectedIndex = 0; }
            void Pick() { if (list.SelectedItem is WindowSession session) { Selected = session; DialogResult = DialogResult.OK; Close(); } }
            search.TextChanged += (_, __) => Filter(); list.DoubleClick += (_, __) => Pick();
            search.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { Pick(); e.SuppressKeyPress = true; } else if (e.KeyCode == Keys.Down && list.Items.Count > 0) { if (Services) list.Focus(); e.SuppressKeyPress = true; } };
            list.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { Pick(); e.SuppressKeyPress = true; } };
            KeyPreview = true; KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); }; Filter(); Shown += (_, __) => { if (Services) search.Focus(); };
        }
    }
}
