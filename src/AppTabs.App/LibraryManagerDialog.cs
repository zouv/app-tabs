using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using AppTabs.Core;

namespace AppTabs.App
{
    public sealed class LibraryManagerDialog : ShellDialog
    {
        private AppLibrary working;
        private AppLibrary? result;
        private readonly Action<AppLibrary>? commitLibrary;
        public Exception? LastCommitError { get; private set; }
        private bool groupsPage;
        private readonly ListBox list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, DisplayMember = "Name", BorderStyle = BorderStyle.FixedSingle };
        private readonly Label details = new Label { Dock = DockStyle.Fill, ForeColor = Theme.Muted, Padding = new Padding(12), AutoEllipsis = true };
        private readonly Button favoritesNavigation = Theme.Button("收藏");
        private readonly Button groupsNavigation = Theme.Button("标签组");
        private readonly Button add = Theme.Button("新增收藏");
        private readonly Button rename = Theme.Button("改名");
        private readonly Button changeExe = Theme.Button("更改程序…");
        private readonly Button delete = Theme.Button("删除");
        private readonly Button open = Theme.Button("保存并打开", true);

        // Only an accepted dialog supplies a result. An optional host callback must save
        // successfully before acceptance. Without it, the host must save before acting on an ID.
        // This dialog never launches or restores windows itself.
        public AppLibrary? Result => result?.Copy();
        public string? RequestedFavoriteId { get; private set; }
        public string? RequestedGroupId { get; private set; }

        public LibraryManagerDialog(AppLibrary library, bool services = true, Action<AppLibrary>? commit = null) : base(services)
        {
            if (library == null) throw new ArgumentNullException(nameof(library));
            library.Validate(); working = library.Copy(); commitLibrary = commit;
            Text = "收藏与标签组 — AppTabs";
            ClientSize = new Size(780, 490); MinimumSize = new Size(680, 430);
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(12) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            var navigation = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            favoritesNavigation.Width = groupsNavigation.Width = 110;
            navigation.Controls.Add(favoritesNavigation); navigation.Controls.Add(groupsNavigation);
            root.Controls.Add(navigation, 0, 0);
            var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            content.Controls.Add(list, 0, 0); content.Controls.Add(details, 0, 1);
            var edits = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            edits.Controls.Add(add); edits.Controls.Add(rename); edits.Controls.Add(changeExe); edits.Controls.Add(delete);
            content.Controls.Add(edits, 0, 2); root.Controls.Add(content, 1, 0);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 8, 0, 0) };
            var save = Theme.Button("保存", true); var cancel = Theme.Button("取消"); cancel.DialogResult = DialogResult.Cancel;
            actions.Controls.Add(save); actions.Controls.Add(cancel); actions.Controls.Add(open);
            root.Controls.Add(actions, 0, 1); root.SetColumnSpan(actions, 2); Controls.Add(root);
            AcceptButton = save; CancelButton = cancel;
            favoritesNavigation.Click += (_, __) => SelectPage(false);
            groupsNavigation.Click += (_, __) => SelectPage(true);
            list.SelectedIndexChanged += (_, __) => UpdateSelection();
            list.DoubleClick += (_, __) => RequestOpen();
            add.Click += (_, __) => AddFavorite(); rename.Click += (_, __) => RenameSelected();
            changeExe.Click += (_, __) => ChangeExecutable(); delete.Click += (_, __) => DeleteSelected();
            save.Click += (_, __) => Commit(null, null); open.Click += (_, __) => RequestOpen();
            SelectPage(false);
        }

        private void SelectPage(bool groups)
        {
            groupsPage = groups;
            favoritesNavigation.BackColor = groups ? Color.White : Theme.AccentSoft;
            groupsNavigation.BackColor = groups ? Theme.AccentSoft : Color.White;
            add.Visible = !groups; changeExe.Visible = !groups;
            open.Text = groups ? "保存并恢复" : "保存并打开";
            RefreshList(null);
        }
        private void RefreshList(string? selectedId)
        {
            list.BeginUpdate();
            try
            {
                list.Items.Clear();
                if (groupsPage) foreach (var group in working.Groups) list.Items.Add(group);
                else foreach (var favorite in working.Favorites) list.Items.Add(favorite);
                int index = -1;
                for (int i = 0; i < list.Items.Count; i++)
                    if (ItemId(list.Items[i]) == selectedId) { index = i; break; }
                list.SelectedIndex = index >= 0 ? index : (list.Items.Count > 0 ? 0 : -1);
            }
            finally { list.EndUpdate(); }
            UpdateSelection();
        }
        private static string? ItemId(object? item) => item is ApplicationFavorite favorite ? favorite.Id : (item as SavedWindowGroup)?.Id;
        private void UpdateSelection()
        {
            bool selected = list.SelectedItem != null;
            rename.Enabled = delete.Enabled = open.Enabled = selected;
            changeExe.Enabled = selected && !groupsPage;
            add.Enabled = working.Favorites.Count < AppLibrary.MaxFavorites;
            if (list.SelectedItem is ApplicationFavorite favorite)
                details.Text = favorite.ImagePath + "\n\n仅启动此程序，不传参数，不自动接管。程序窗口需另行选择加入标签。";
            else if (list.SelectedItem is SavedWindowGroup group)
                details.Text = $"{group.Windows.Count} 个窗口 · 只匹配已有窗口，不启动软件。\n更新组内容：回到主窗，以相同组名保存当前标签快照。";
            else details.Text = groupsPage
                ? "还没有标签组。请在主窗保存当前标签快照；此处可改名、删除或请求恢复。\n恢复只匹配已有窗口，不启动软件。"
                : "还没有收藏。选择本地 .exe 并命名；仅保存路径，不支持启动参数。";
        }
        private void AddFavorite()
        {
            string? path = PickExecutable(null); if (path == null) return;
            using (var dialog = new TextDialog("新增收藏", "收藏名称（最多 120 字符）", Path.GetFileNameWithoutExtension(path)))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var favorite = new ApplicationFavorite { Name = dialog.Value.Trim(), ImagePath = path };
                TryEdit(copy => copy.Favorites.Add(favorite), favorite.Id);
            }
        }
        private void RenameSelected()
        {
            var selected = list.SelectedItem; if (selected == null) return;
            string? id = ItemId(selected); if (id == null) return;
            string name = selected is ApplicationFavorite favorite ? favorite.Name : ((SavedWindowGroup)selected).Name;
            using (var dialog = new TextDialog("改名", "新名称（最多 120 字符，同一页名称不能重复）", name))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                TryEdit(copy =>
                {
                    if (groupsPage) copy.Groups.Single(item => item.Id == id).Name = dialog.Value.Trim();
                    else copy.Favorites.Single(item => item.Id == id).Name = dialog.Value.Trim();
                }, id);
            }
        }
        private void ChangeExecutable()
        {
            if (!(list.SelectedItem is ApplicationFavorite selected)) return;
            string? path = PickExecutable(selected.ImagePath); if (path == null) return;
            TryEdit(copy => copy.Favorites.Single(item => item.Id == selected.Id).ImagePath = path, selected.Id);
        }
        private string? PickExecutable(string? previous)
        {
            using (var picker = new OpenFileDialog { Title = "选择本地程序", Filter = "Windows 程序 (*.exe)|*.exe", CheckFileExists = true,
                CheckPathExists = true, Multiselect = false, RestoreDirectory = true, DereferenceLinks = true })
            {
                if (previous != null && File.Exists(previous)) picker.FileName = previous;
                if (picker.ShowDialog(this) != DialogResult.OK) return null;
                try { ApplicationFavorite.ValidateImagePath(picker.FileName); return picker.FileName; }
                catch (InvalidDataException error) { ShowError(error); return null; }
            }
        }
        private void DeleteSelected()
        {
            string? id = ItemId(list.SelectedItem); if (id == null) return;
            if (MessageBox.Show(this, "仅删除库中的记录，不会关闭或删除软件。确定删除？", "删除记录", MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question) != DialogResult.OK) return;
            TryEdit(copy =>
            {
                if (groupsPage) copy.Groups.RemoveAll(item => item.Id == id);
                else copy.Favorites.RemoveAll(item => item.Id == id);
            }, null);
        }
        private void TryEdit(Action<AppLibrary> edit, string? selectedId)
        {
            try
            {
                var copy = working.Copy(); edit(copy); copy.Validate(); working = copy; RefreshList(selectedId);
            }
            catch (InvalidDataException error) { ShowError(error); }
        }
        private void RequestOpen()
        {
            if (list.SelectedItem is ApplicationFavorite favorite) Commit(favorite.Id, null);
            else if (list.SelectedItem is SavedWindowGroup group) Commit(null, group.Id);
        }
        private void Commit(string? favoriteId, string? groupId)
        {
            if (!TryCommit(favoriteId, groupId) && Services) ShowError(LastCommitError!);
        }

        // No error UI here: test/fake callers can assert durable-save failures and retry.
        public bool TryCommit(string? favoriteId = null, string? groupId = null)
        {
            LastCommitError = null;
            AppLibrary accepted;
            try
            {
                working.Validate();
                if ((favoriteId != null && groupId != null) ||
                    (favoriteId != null && !working.Favorites.Any(item => item.Id == favoriteId)) ||
                    (groupId != null && !working.Groups.Any(item => item.Id == groupId)))
                    throw new InvalidDataException("请求打开的库条目不存在或请求冲突。");
                accepted = working.Copy();
                commitLibrary?.Invoke(accepted.Copy());
            }
            catch (Exception error)
            {
                LastCommitError = error;
                return false;
            }
            result = accepted; RequestedFavoriteId = favoriteId; RequestedGroupId = groupId;
            DialogResult = DialogResult.OK; Close();
            return true;
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (DialogResult != DialogResult.OK)
            {
                result = null; RequestedFavoriteId = null; RequestedGroupId = null;
            }
            base.OnFormClosing(e);
        }
        private void ShowError(Exception error) => MessageBox.Show(this, error.Message, "无法保存修改", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
