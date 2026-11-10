using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AppTabs.App;
using AppTabs.Core;

namespace AppTabs.Tests
{
    internal static partial class Program
    {
        private sealed class HostLibraryStore : IAppLibraryStore
        {
            public AppLibrary Value = new AppLibrary();
            public bool FailRead, FailSave;
            public int Saves;
            public AppLibrary Read() { if (FailRead) throw new IOException("bad library"); return Value.Copy(); }
            public void Save(AppLibrary value) { Saves++; if (FailSave) throw new IOException("disk"); Value = value.Copy(); }
        }
        private sealed class HostLauncher : IApplicationLauncher
        {
            public int Calls;
            public void Launch(ApplicationFavorite favorite) { Calls++; }
        }
        private static object? HostCall(MainForm form, string name, params object[] arguments) =>
            typeof(MainForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, arguments);
        private static void LibraryHostChecks()
        {
            var store = new HostLibraryStore(); var launcher = new HostLauncher(); var api = new FakeApi();
            store.Value.Favorites.Add(new ApplicationFavorite { Name = "Editor", ImagePath = @"C:\sample\Editor.exe" });
            var settings = new AppState();
            using (var ui = new MainForm(api, new RecoveryJournal(Path.Combine(testDirectory, "library-host-journal.json")),
                new StateStore(Path.Combine(testDirectory, "library-host-settings.json")), settings, false, store, launcher))
            {
                var hostHandle = ui.Handle;
                Check(ui.Controls.Find("mode", true).Length == 0 && ui.Controls.Find("settings", true).Length == 0, "toolbar removes settings and attach mode controls");
                var appMenu = (System.Windows.Forms.ContextMenuStrip)typeof(MainForm).GetField("appMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ui)!;
                Check(appMenu.Items.Count == 2 && appMenu.Items[0].Text.StartsWith("设置") && appMenu.Items[1].Text.StartsWith("关于"), "brand menu exposes settings and about");
                using (var canceled = new System.Windows.Forms.ContextMenuStrip())
                {
                    canceled.Items.Add("cancelled"); canceled.Opening += (_, e) => e.Cancel = true;
                    HostCall(ui, "ShowChromeMenu", canceled, ui.Controls.Find("brand", true).Single(), null!, false);
                    Check((int)typeof(MainForm).GetField("modalDepth", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ui)! == 1,
                        "cancelled menu keeps scope until close stack returns");
                    System.Windows.Forms.Application.DoEvents();
                    Check((int)typeof(MainForm).GetField("modalDepth", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ui)! == 0,
                        "cancelled menu opening releases delayed scope");
                }
                HostCall(ui, "RebuildLibraryMenu");
                var menu = (System.Windows.Forms.ContextMenuStrip)typeof(MainForm).GetField("groupMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ui)!;
                Check(menu.Items.Cast<System.Windows.Forms.ToolStripItem>().Any(i => i.Text == "应用收藏 · 点击启动"), "library menu contains explicit favorite launch entries");
                HostCall(ui, "RestoreSnapshot", Array.Empty<SavedWindow>(), 0);
                Check(launcher.Calls == 0 && ui.Controller.Sessions.Count == 0, "reading library, empty restore and constructing menu never launches or attaches");
                HostCall(ui, "LaunchFavorite", store.Value.Favorites[0].Id);
                Check(launcher.Calls == 1 && api.Mutations == 0 && ui.Controller.Sessions.Count == 0, "explicit favorite request uses fake launcher without attaching");
                var candidate = store.Value.Copy(); candidate.Favorites[0].Name = "Changed"; store.FailSave = true;
                Throws(() => HostCall(ui, "CommitLibrary", candidate), "library failed write propagates to host");
                var published = (AppLibrary)typeof(MainForm).GetField("library", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ui)!;
                Check(published.Favorites[0].Name == "Editor", "failed library commit keeps published copy");
                store.FailSave = false;
                HostCall(ui, "CommitLibrary", candidate); candidate.Favorites[0].Name = "Later edit";
                published = (AppLibrary)typeof(MainForm).GetField("library", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ui)!;
                Check(published.Favorites[0].Name == "Changed", "committed library owns independent copy");
                var one = ui.Controller.Attach(Identity(801), ui.ContentHandle, HostingMode.Embedded, ui.ContentBounds);
                var two = ui.Controller.Attach(Identity(802), ui.ContentHandle, HostingMode.Embedded, ui.ContentBounds);
                ui.Controller.Activate(two, ui.ContentBounds, false, false);
                typeof(WindowSession).GetProperty("CloseRequestedUtc")!.SetValue(one, DateTime.UtcNow);
                var snapshot = (SavedWindowGroup)HostCall(ui, "CaptureGroup", "filtered")!;
                Check(snapshot.Windows.Count == 1 && snapshot.Windows[0].Title == two.Title && snapshot.ActiveIndex == 0, "snapshot active index uses filtered attached nonclosing list");
                HostCall(ui, "SaveGroup");
                var saved = new StateStore(Path.Combine(testDirectory, "library-host-settings.json")).Read();
                Check(saved.Group.Count == 1 && saved.ActiveIndex == 0 && settings.Group.Count == 0, "legacy snapshot saves candidate without mutating caller state");
                typeof(WindowSession).GetProperty("CloseRequestedUtc")!.SetValue(two, DateTime.UtcNow);
                snapshot = (SavedWindowGroup)HostCall(ui, "CaptureGroup", "empty")!;
                Check(snapshot.Windows.Count == 0 && snapshot.ActiveIndex == 0, "excluded active session produces normalized empty snapshot");
                using (var locked = new FileStream(Path.Combine(testDirectory, "library-host-settings.json"), FileMode.Open, FileAccess.Read, FileShare.None))
                    Throws(() => HostCall(ui, "SaveGroup"), "legacy snapshot failed atomic replacement propagates");
                var retained = (AppState)typeof(MainForm).GetField("state", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ui)!;
                saved = new StateStore(Path.Combine(testDirectory, "library-host-settings.json")).Read();
                Check(retained.Group.Count == 1 && saved.Group.Count == 1, "failed legacy snapshot save preserves published and disk records");
                Check(launcher.Calls == 1, "saving both snapshot types does not launch");
            }
            store = new HostLibraryStore { FailRead = true };
            using (var ui = new MainForm(new FakeApi(), new RecoveryJournal(Path.Combine(testDirectory, "bad-library-host-journal.json")),
                new StateStore(Path.Combine(testDirectory, "bad-library-host-settings.json")), new AppState(), false, store, launcher))
            {
                Throws(() => HostCall(ui, "CommitLibrary", new AppLibrary()), "bad library blocks replacement");
                Check(store.Saves == 0, "bad library cannot be overwritten by empty fallback");
            }
        }
    }
}
