using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using AppTabs.Core;

namespace AppTabs.App
{
    public sealed partial class MainForm
    {
        private readonly IAppLibraryStore libraryStore;
        private readonly IApplicationLauncher applicationLauncher;
        private AppLibrary library = new AppLibrary();
        private string libraryError = "";

        private void ReadLibrary()
        {
            try { library = libraryStore.Read(); library.Validate(); }
            catch (Exception error) { libraryError = "标签组 / 收藏读取失败，原文件保留且禁止覆盖：" + error.Message; }
        }
        private void CommitLibrary(AppLibrary candidate)
        {
            if (libraryError.Length != 0) throw new InvalidOperationException(libraryError);
            candidate.Validate(); libraryStore.Save(candidate); library = candidate.Copy();
        }
        private SavedWindowGroup CaptureGroup(string name)
        {
            var sessions = controller.Sessions.Where(s => s.State == SessionState.Attached && s.CloseRequestedUtc == null).ToArray();
            return new SavedWindowGroup
            {
                Name = name, ActiveIndex = Math.Max(0, Array.IndexOf(sessions, controller.Active)),
                Windows = sessions.Select(s => new SavedWindow { ImagePath = s.Identity.ImagePath, ClassName = s.Identity.ClassName,
                    Title = s.Title, CustomTitle = s.CustomTitle, Mode = s.Snapshot.Mode }).ToList()
            };
        }
        private void RestoreSnapshot(IReadOnlyList<SavedWindow> savedWindows, int activeIndex)
        {
            if (modalDepth != 0 || exiting || savedWindows.Count == 0) return;
            var candidates = NativeWindowApi.DiscoverWindows(controller.Sessions.Select(s => s.Identity.Handle)).ToList();
            var errors = new List<string>(); WindowSession? active = null;
            for (int index = 0; index < savedWindows.Count; index++)
            {
                var saved = savedWindows[index];
                var identity = GroupMatching.Match(saved, candidates);
                if (identity == null) { errors.Add(saved.Title + "：没有唯一匹配的已打开窗口，请使用“加入窗口”。"); continue; }
                // Never reuse a candidate after a failed attach with an unresolved recovery record.
                candidates.Remove(identity);
                try
                {
                    var session = JoinWindow(identity, saved.Mode == HostingMode.Grouped ? AttachPreference.Grouped : AttachPreference.Auto);
                    if (saved.CustomTitle != null) controller.RenameTab(session, saved.CustomTitle);
                    if (index == activeIndex) active = session;
                }
                catch (Exception error) { errors.Add(saved.Title + "：" + error.Message); }
            }
            if (active != null) Select(active); RefreshUi();
            if (errors.Count > 0) ShowErrors(string.Join("\n", errors));
            else SetStatus("窗口组已恢复；仅加入已有窗口，未启动软件。");
        }
        private void RebuildLibraryMenu()
        {
            // Keep the legacy menu items owned by MainForm; dispose only generated entries.
            groupMenu.Items.Remove(saveGroup); groupMenu.Items.Remove(restoreGroup);
            foreach (ToolStripItem item in groupMenu.Items.Cast<ToolStripItem>().ToArray()) item.Dispose();
            groupMenu.Items.Clear(); groupMenu.Items.Add(saveGroup); groupMenu.Items.Add(restoreGroup);
            groupMenu.Items.Add(new ToolStripSeparator());
            var saveNamed = groupMenu.Items.Add("保存为标签组…", null, (_, __) => Queue(SaveNamedGroup));
            saveNamed.Enabled = libraryError.Length == 0 && controller.Sessions.Any(s => s.State == SessionState.Attached && s.CloseRequestedUtc == null);
            var favorite = groupMenu.Items.Add("收藏应用…", null, (_, __) => Queue(AddFavorite));
            favorite.Enabled = libraryError.Length == 0;
            var manage = groupMenu.Items.Add("管理标签组 / 收藏…", null, (_, __) => Queue(ManageLibrary));
            manage.Enabled = libraryError.Length == 0;
            if (libraryError.Length != 0) { groupMenu.Items.Add("库读取失败，原文件保留").Enabled = false; return; }
            var groups = new ToolStripMenuItem("自定义标签组");
            foreach (var group in library.Groups)
            {
                string id = group.Id;
                groups.DropDownItems.Add(group.Name, null, (_, __) => Queue(() => OpenNamedGroup(id)));
            }
            groups.Enabled = groups.DropDownItems.Count > 0; groupMenu.Items.Add(groups);
            var favorites = new ToolStripMenuItem("应用收藏 · 点击启动");
            foreach (var item in library.Favorites)
            {
                string id = item.Id;
                favorites.DropDownItems.Add(item.Name, null, (_, __) => Queue(() => LaunchFavorite(id)));
            }
            favorites.Enabled = favorites.DropDownItems.Count > 0; groupMenu.Items.Add(favorites);
        }
        private void SaveNamedGroup()
        {
            using (var dialog = new TextDialog("保存为标签组", "保存当前窗口快照；打开时只匹配已打开窗口。", "我的标签组", services))
            {
                if (RunModal(() => dialog.ShowDialog(this)) != DialogResult.OK) return;
                string name = TabManagementPolicy.NormalizeTitle(dialog.Value);
                var candidate = library.Copy();
                var existing = candidate.Groups.FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));
                if (existing != null && RunModal(() => MessageBox.Show(this, "覆盖标签组“" + existing.Name + "”？", "保存标签组",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question)) != DialogResult.OK) return;
                var group = CaptureGroup(name);
                if (group.Windows.Count == 0) throw new InvalidOperationException("没有可保存的已附着窗口。");
                if (existing != null) { group.Id = existing.Id; candidate.Groups[candidate.Groups.IndexOf(existing)] = group; }
                else candidate.Groups.Add(group);
                CommitLibrary(candidate); SetStatus("标签组已保存：" + name);
            }
        }
        private void AddFavorite()
        {
            using (var picker = new OpenFileDialog { Title = "选择收藏的应用", Filter = "应用程序 (*.exe)|*.exe", CheckFileExists = true, Multiselect = false })
            {
                if (RunModal(() => picker.ShowDialog(this)) != DialogResult.OK) return;
                using (var dialog = new TextDialog("收藏应用", "仅明确点击收藏时启动，不会自动挂载。", Path.GetFileNameWithoutExtension(picker.FileName), services))
                {
                    if (RunModal(() => dialog.ShowDialog(this)) != DialogResult.OK) return;
                    var candidate = library.Copy();
                    candidate.Favorites.Add(new ApplicationFavorite { Name = TabManagementPolicy.NormalizeTitle(dialog.Value), ImagePath = picker.FileName });
                    CommitLibrary(candidate); SetStatus("应用已收藏；点击收藏后启动。");
                }
            }
        }
        private void ManageLibrary()
        {
            if (libraryError.Length != 0) throw new InvalidOperationException(libraryError);
            using (var dialog = new LibraryManagerDialog(library, services, CommitLibrary))
            {
                if (RunModal(() => dialog.ShowDialog(this)) != DialogResult.OK || dialog.Result == null) return;
                if (dialog.RequestedFavoriteId != null) LaunchFavorite(dialog.RequestedFavoriteId);
                else if (dialog.RequestedGroupId != null) OpenNamedGroup(dialog.RequestedGroupId);
                else SetStatus("标签组 / 收藏已保存。");
            }
        }
        private void OpenNamedGroup(string id)
        {
            var group = library.Groups.Single(g => g.Id == id);
            RestoreSnapshot(group.Windows, group.ActiveIndex);
        }
        private void LaunchFavorite(string id)
        {
            if (modalDepth != 0 || exiting) return;
            applicationLauncher.Launch(library.Favorites.Single(f => f.Id == id).Copy());
            SetStatus("已发送应用启动请求；未自动挂载，请使用“加入窗口”。");
        }
    }
}
