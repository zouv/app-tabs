using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using AppTabs.Core;

namespace AppTabs.App
{
    public sealed class WindowPicker : ShellDialog
    {
        private readonly TextBox search = new TextBox { Dock = DockStyle.Top, Margin = new Padding(12) };
        private readonly ListView list = new ListView { Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true, HideSelection = false };
        private readonly Label count = new Label { AutoSize = true, Text = "正在读取已打开的窗口…", Padding = new Padding(8) };
        private readonly Button refresh = Theme.Button("刷新");
        private readonly Button join = Theme.Button("加入选中的窗口", true);
        private readonly HashSet<long> checkedHandles = new HashSet<long>();
        private readonly long[] excluded;
        private readonly Func<IReadOnlyList<WindowIdentity>> candidateLoader;
        private IReadOnlyList<WindowIdentity> candidates = Array.Empty<WindowIdentity>();
        private bool rebuilding;
        public IReadOnlyList<WindowIdentity> SelectedWindows { get; private set; } = Array.Empty<WindowIdentity>();
        public WindowPicker(IEnumerable<long> excluded, Func<IReadOnlyList<WindowIdentity>>? candidateLoader = null, bool services = true) : base(services, true)
        {
            this.excluded = excluded.ToArray();
            this.candidateLoader = candidateLoader ?? (() => NativeWindowApi.DiscoverWindows(this.excluded));
            Text = "加入窗口 — AppTabs"; ClientSize = new System.Drawing.Size(830, 530); MinimumSize = new System.Drawing.Size(650, 400);
            search.Name = "windowSearch"; list.Name = "windowCandidates"; list.ShowItemToolTips = true;
            var hint = new Label { Text = "勾选软件主窗口加入；可输入标题或软件名称筛选。", Dock = DockStyle.Top, Height = 36, Padding = new Padding(4, 8, 0, 0), ForeColor = Theme.Muted };
            list.Columns.Add("软件", 210); list.Columns.Add("窗口标题", 540);
            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
            body.Controls.Add(list); body.Controls.Add(search); body.Controls.Add(hint);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(8) };
            var cancel = Theme.Button("取消"); cancel.DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(join); buttons.Controls.Add(cancel); buttons.Controls.Add(refresh); buttons.Controls.Add(count);
            Controls.Add(body); Controls.Add(buttons); CancelButton = cancel; AcceptButton = join;
            search.TextChanged += (_, __) => Rebuild();
            list.ItemChecked += (_, e) => { if (!rebuilding && e.Item.Tag is WindowIdentity identity) { if (e.Item.Checked) checkedHandles.Add(identity.Handle); else checkedHandles.Remove(identity.Handle); UpdateCount(); } };
            join.Click += (_, __) => { SelectedWindows = candidates.Where(w => checkedHandles.Contains(w.Handle)).ToArray(); if (SelectedWindows.Count > 0) { DialogResult = DialogResult.OK; Close(); } };
            refresh.Click += async (_, __) => await Reload();
            Shown += async (_, __) => { await Reload(); if (Services && !IsDisposed) search.Focus(); };
        }
        private async Task Reload()
        {
            refresh.Enabled = false; join.Enabled = false;
            try
            {
                var result = await Task.Run(candidateLoader);
                if (IsDisposed) return;
                candidates = result.OrderBy(w => Path.GetFileName(w.ImagePath)).ThenBy(w => w.Title).ToArray();
                checkedHandles.IntersectWith(candidates.Select(w => w.Handle)); Rebuild();
            }
            catch (Exception error) { if (!IsDisposed) count.Text = "读取失败：" + error.Message; }
            finally { if (!IsDisposed) refresh.Enabled = true; }
        }
        private void Rebuild()
        {
            rebuilding = true; list.BeginUpdate();
            try
            {
                list.Items.Clear(); string query = search.Text.Trim();
                foreach (var window in candidates)
                {
                    string app = Path.GetFileNameWithoutExtension(window.ImagePath);
                    if (query.Length > 0 && (window.Title + " " + app).IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    list.Items.Add(new ListViewItem(new[] { app, window.Title }) { Tag = window, Checked = checkedHandles.Contains(window.Handle), ToolTipText = window.ImagePath });
                }
            }
            finally { list.EndUpdate(); rebuilding = false; UpdateCount(); }
        }
        private void UpdateCount() { count.Text = $"{candidates.Count} 个窗口，已选 {checkedHandles.Count} 个"; join.Enabled = checkedHandles.Count > 0; }
    }
}
