using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using AppTabs.App;
using AppTabs.Core;

namespace AppTabs.Tests
{
    internal static partial class Program
    {
        private static void LibraryChecks()
        {
            string directory = Path.Combine(testDirectory, "library-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var store = new AppLibraryStore(Path.Combine(directory, "library.json"));
            Check(store.Read().Favorites.Count == 0 && !File.Exists(store.FilePath), "missing library is empty without writing");
            var favorite = new ApplicationFavorite { Name = "编辑器", ImagePath = "C:\\sample\\Editor.exe" };
            var window = new SavedWindow { ImagePath = favorite.ImagePath, ClassName = "EditorMainWindow", Title = "文档", CustomTitle = "工作", Mode = HostingMode.Grouped };
            var group = new SavedWindowGroup { Name = "工作组", Windows = new List<SavedWindow> { window } };
            var library = new AppLibrary { Favorites = new List<ApplicationFavorite> { favorite }, Groups = new List<SavedWindowGroup> { group } };
            library.Validate();
            var copy = library.Copy(); copy.Favorites[0].Name = "副本"; copy.Groups[0].Windows[0].Title = "副本窗口";
            Check(favorite.Name == "编辑器" && window.Title == "文档", "library copies favorites groups and saved windows deeply");
            var state = new AppState { X = 5, Y = 6, Width = 700, Height = 600, Maximized = true, Preference = AttachPreference.Embedded,
                HotkeyChoice = 3, HideToTray = true, Group = new List<SavedWindow> { window }, ActiveIndex = 0 };
            var stateCopy = state.Copy(); stateCopy.Group[0].CustomTitle = "副本";
            Check(window.CustomTitle == "工作" && stateCopy.X == 5 && stateCopy.Y == 6 && stateCopy.Width == 700 && stateCopy.Height == 600 &&
                stateCopy.Maximized && stateCopy.Preference == AttachPreference.Embedded && stateCopy.HotkeyChoice == 3 && stateCopy.HideToTray,
                "AppState copy preserves settings without aliasing saved windows");
            store.Save(library);
            var loaded = store.Read();
            Check(loaded.Favorites[0].Id == favorite.Id && loaded.Favorites[0].Name == favorite.Name && loaded.Groups[0].Windows[0].Title == window.Title,
                "library UTF8 roundtrip preserves stable IDs and selectors");
            loaded.Favorites[0].Name = "已更新"; store.Save(loaded);
            Check(store.Read().Favorites[0].Name == "已更新" && Directory.GetFiles(directory, "*.tmp").Length == 0, "atomic library replacement cleans temporary files");
            string preserved = File.ReadAllText(store.FilePath);
            var invalid = library.Copy(); invalid.Version = 2;
            Throws(() => invalid.Validate(), "unsupported library version rejected");
            Throws(() => store.Save(invalid), "invalid save rejected before replacement");
            Check(File.ReadAllText(store.FilePath) == preserved, "invalid save leaves previous library intact");
            invalid = library.Copy(); invalid.Favorites.Add(favorite.Copy());
            Throws(() => invalid.Validate(), "duplicate favorite IDs rejected");
            invalid = library.Copy(); invalid.Favorites.Add(new ApplicationFavorite { Name = favorite.Name, ImagePath = favorite.ImagePath });
            Throws(() => invalid.Validate(), "duplicate favorite names rejected");
            invalid = library.Copy(); invalid.Groups.Add(new SavedWindowGroup { Name = group.Name });
            Throws(() => invalid.Validate(), "duplicate group names rejected");
            invalid = library.Copy(); invalid.Groups[0].Id = invalid.Favorites[0].Id;
            Throws(() => invalid.Validate(), "IDs unique across both library pages");
            foreach (string id in new[] { "", "invalid", Guid.Empty.ToString("N") })
            { invalid = library.Copy(); invalid.Favorites[0].Id = id; Throws(() => invalid.Validate(), "invalid favorite ID " + id); }
            foreach (string name in new[] { "", " ", " trim", "line\nbreak", new string('a', 121) })
                Throws(() => AppLibrary.ValidateName(name), "invalid library name rejected");
            invalid = library.Copy(); invalid.Favorites = null!; Throws(() => invalid.Validate(), "null favorite list rejected");
            invalid = library.Copy(); invalid.Groups[0].Windows.Add(null!); Throws(() => invalid.Validate(), "null window rejected");
            invalid = library.Copy(); invalid.Groups[0].Windows[0].Mode = (HostingMode)999; Throws(() => invalid.Validate(), "invalid saved mode rejected");
            invalid = library.Copy(); invalid.Groups[0].Windows[0].ImagePath = "relative.exe"; Throws(() => invalid.Validate(), "relative saved window image rejected");
            invalid = library.Copy(); invalid.Groups[0].ActiveIndex = 1; Throws(() => invalid.Validate(), "out of bounds active index rejected");
            invalid = library.Copy(); invalid.Groups[0].Windows.Clear(); invalid.Groups[0].ActiveIndex = 1;
            Throws(() => invalid.Validate(), "empty group requires zero active index");
            new SavedWindowGroup { Name = "空组" }.Validate(); Check(true, "empty group with zero index allowed");
            var maximum = new AppLibrary
            {
                Favorites = Enumerable.Range(0, 100).Select(i => new ApplicationFavorite { Name = "程序 " + i, ImagePath = favorite.ImagePath }).ToList(),
                Groups = Enumerable.Range(0, 50).Select(i => new SavedWindowGroup { Name = "组 " + i,
                    Windows = Enumerable.Range(0, 100).Select(_ => window.Copy()).ToList(), ActiveIndex = 99 }).ToList()
            };
            maximum.Validate(); Check(true, "maximum counts accepted");
            maximum.Favorites.Add(new ApplicationFavorite { Name = "额外", ImagePath = favorite.ImagePath });
            Throws(() => maximum.Validate(), "101 favorites rejected"); maximum.Favorites.RemoveAt(100);
            maximum.Groups.Add(new SavedWindowGroup { Name = "额外组" });
            Throws(() => maximum.Validate(), "51 groups rejected"); maximum.Groups.RemoveAt(50);
            maximum.Groups[0].Windows.Add(window.Copy()); Throws(() => maximum.Validate(), "101 windows rejected"); maximum.Groups[0].Windows.RemoveAt(100);
            foreach (var item in maximum.Groups.SelectMany(item => item.Windows)) item.Title = new string('文', 1024);
            Throws(() => store.Save(maximum), "serialized library above 8MB rejected");
            Check(File.ReadAllText(store.FilePath) == preserved && Directory.GetFiles(directory, "*.tmp").Length == 0, "oversized save preserves file and cleans temp");
            foreach (string alias in new[] { "", " ", " leading", "trailing ", "line\nbreak", "control\u0001", new string('a', 121) })
            {
                invalid = library.Copy(); invalid.Groups[0].Windows[0].CustomTitle = alias;
                Throws(() => invalid.Validate(), "invalid saved alias rejected");
                Throws(() => store.Save(invalid), "invalid saved alias cannot overwrite existing library");
                Check(File.ReadAllText(store.FilePath) == preserved, "bad alias save preserves original library");
                // Inject corruption directly to verify read never repairs or discards bad aliases.
                using (var stream = File.Create(store.FilePath))
                    new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(AppLibrary)).WriteObject(stream, invalid);
                string corrupt = File.ReadAllText(store.FilePath);
                Throws(() => store.Read(), "persisted invalid alias throws");
                Check(File.ReadAllText(store.FilePath) == corrupt, "bad alias read preserves original corrupt file");
                File.WriteAllText(store.FilePath, preserved);
            }
            foreach (string broken in new[] { "{broken", "null", "{}", "{\"Version\":1,\"Favorites\":null,\"Groups\":[]}" })
            {
                File.WriteAllText(store.FilePath, broken); Throws(() => store.Read(), "bad library throws rather than resetting");
                Check(File.ReadAllText(store.FilePath) == broken, "bad library retained for repair");
            }
            using (var stream = new FileStream(store.FilePath, FileMode.Create, FileAccess.Write)) stream.SetLength(AppLibrary.MaxFileBytes + 1);
            Throws(() => store.Read(), "oversized file refused before deserialization");
            Check(new FileInfo(store.FilePath).Length == AppLibrary.MaxFileBytes + 1, "oversized read leaves file untouched");
            File.WriteAllText(store.FilePath, ""); Throws(() => store.Read(), "empty existing library is corruption not missing");
            store.Save(library);
            LauncherChecks(directory);
            LibraryDialogChecks(library);
        }

        private static void LauncherChecks(string directory)
        {
            int starts = 0; ProcessStartInfo? request = null;
            var launcher = new ApplicationLauncher(info => { starts++; request = info; });
            foreach (string path in new[] { "app.exe", "C:app.exe", "\\app.exe", "\\\\server\\share\\app.exe", "https://sample/app.exe", "C:\\sample\\app.cmd",
                "C:\\sample\\app.ps1", "C:\\sample\\app.exe --flag", "C:\\sample\\app.exe:stream.exe", "C:\\sample\\..\\app.exe", "\\\\?\\C:\\app.exe" })
                Throws(() => launcher.Launch(new ApplicationFavorite { Name = "测试", ImagePath = path }), "unsafe favorite path rejected: " + path);
            Check(starts == 0, "invalid favorites never reach process start");
            Throws(() => launcher.Launch(new ApplicationFavorite { Name = "丢失", ImagePath = Path.Combine(directory, "missing.exe") }), "missing executable reported");
            Check(starts == 0, "missing executable never reaches process start");
            string executable = Path.Combine(directory, "fake target.exe"); File.WriteAllText(executable, "not a real executable; start is injected");
            var favorite = new ApplicationFavorite { Name = "假程序", ImagePath = executable };
            launcher.Launch(favorite);
            Check(starts == 1 && request!.FileName == executable && request.Arguments == "" && !request.UseShellExecute && request.Verb == "" &&
                request.WorkingDirectory == directory, "launcher requests exact executable no arguments shell elevation or waiting");
            var fake = new FakeApplicationLauncher(); fake.Launch(favorite); favorite.Name = "原对象变化";
            Check(fake.Launched!.Name == "假程序", "fake launcher isolates requests without running software");
            var fakeStore = new FakeAppLibraryStore(); fakeStore.Save(new AppLibrary()); fakeStore.FailSave = true;
            Throws(() => fakeStore.Save(new AppLibrary()), "fake library save failure surfaced");
            Check(fakeStore.Saves == 1, "failed fake save does not replace state");
        }

        private static void LibraryDialogChecks(AppLibrary library)
        {
            var workingField = typeof(LibraryManagerDialog).GetField("working", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var commit = typeof(LibraryManagerDialog).GetMethod("Commit", BindingFlags.Instance | BindingFlags.NonPublic)!;
            using (var dialog = new LibraryManagerDialog(library, false))
            {
                var working = (AppLibrary)workingField.GetValue(dialog)!; working.Favorites[0].Name = "取消的修改";
                dialog.DialogResult = DialogResult.Cancel;
                Check(dialog.Result == null && dialog.RequestedFavoriteId == null && dialog.RequestedGroupId == null && library.Favorites[0].Name == "编辑器",
                    "dialog cancel has no result and private edits never touch caller");
            }
            using (var dialog = new LibraryManagerDialog(library, false))
            {
                var working = (AppLibrary)workingField.GetValue(dialog)!; working.Favorites[0].Name = "保存的修改";
                commit.Invoke(dialog, new object?[] { library.Favorites[0].Id, null });
                var result = dialog.Result!;
                Check(dialog.DialogResult == DialogResult.OK && result.Favorites[0].Name == "保存的修改" && library.Favorites[0].Name == "编辑器" &&
                    dialog.RequestedFavoriteId == library.Favorites[0].Id && dialog.RequestedGroupId == null, "dialog returns saved copy and favorite request without launch");
                result.Favorites[0].Name = "外部变化";
                Check(dialog.Result!.Favorites[0].Name == "保存的修改", "dialog result getter also isolates library");
            }
            using (var dialog = new LibraryManagerDialog(library, false))
            {
                commit.Invoke(dialog, new object?[] { null, library.Groups[0].Id });
                Check(dialog.RequestedFavoriteId == null && dialog.RequestedGroupId == library.Groups[0].Id,
                    "dialog group action returns only restore request not actual restore");
            }
            bool failCommit = true;
            int commitAttempts = 0;
            AppLibrary? persisted = null;
            using (var dialog = new LibraryManagerDialog(library, false, value =>
            {
                commitAttempts++;
                Check(!ReferenceEquals(value, library) && value.Favorites[0].Name == "待保存编辑", "commit callback receives independent editable snapshot");
                if (failCommit) throw new IOException("injected dialog save failure");
                persisted = value.Copy(); value.Favorites[0].Name = "callback mutation";
            }))
            {
                var working = (AppLibrary)workingField.GetValue(dialog)!; working.Favorites[0].Name = "待保存编辑";
                Check(!dialog.TryCommit(library.Favorites[0].Id) && dialog.LastCommitError is IOException &&
                    dialog.DialogResult == DialogResult.None && dialog.Result == null && dialog.RequestedFavoriteId == null &&
                    dialog.RequestedGroupId == null && !dialog.IsDisposed && working.Favorites[0].Name == "待保存编辑",
                    "failed dialog commit retains private edits no result request or close for retry");
                failCommit = false;
                Check(dialog.TryCommit(library.Favorites[0].Id) && dialog.LastCommitError == null && commitAttempts == 2 &&
                    persisted!.Favorites[0].Name == "待保存编辑" && dialog.Result!.Favorites[0].Name == "待保存编辑" &&
                    dialog.RequestedFavoriteId == library.Favorites[0].Id && library.Favorites[0].Name == "编辑器",
                    "dialog retry saves before acceptance and callback mutation cannot alias working or result");
            }
            using (var dialog = new LibraryManagerDialog(library, false))
            {
                Check(!dialog.TryCommit(Guid.NewGuid().ToString("N")) && dialog.LastCommitError is InvalidDataException && dialog.Result == null,
                    "dialog rejects missing requested ID without error UI");
            }
            LibraryDialogPreviews(library);
        }

        private static void LibraryDialogPreviews(AppLibrary library)
        {
            string output = Path.Combine(ProjectRoot(), "artifacts"); Directory.CreateDirectory(output);
            var selectPage = typeof(LibraryManagerDialog).GetMethod("SelectPage", BindingFlags.Instance | BindingFlags.NonPublic)!;
            using (var dialog = new LibraryManagerDialog(library, false))
            {
                // Synthetic 96-DPI rendering of a test-owned, non-activating offscreen dialog only.
                PrepareDialogDpiPreview(dialog, 96);
                foreach (bool groups in new[] { false, true })
                {
                    selectPage.Invoke(dialog, new object[] { groups });
                    dialog.PerformLayout(); Application.DoEvents(); dialog.Location = new Point(-24000, -24000);
                    SavePreview(dialog, output, groups ? "app-tabs-library-groups.png" : "app-tabs-library-favorites.png");
                }
                dialog.Width = 680; dialog.PerformLayout(); Application.DoEvents();
                foreach (bool groups in new[] { false, true })
                {
                    selectPage.Invoke(dialog, new object[] { groups });
                    dialog.PerformLayout(); Application.DoEvents();
                    foreach (var button in Descendants(dialog).OfType<Button>().Where(button => button.Visible))
                    {
                        var parent = button.Parent!;
                        Check(button.Left >= 0 && button.Top >= 0 && button.Right <= parent.ClientSize.Width && button.Bottom <= parent.ClientSize.Height,
                            "library button fits at minimum 680 width: " + (groups ? "groups / " : "favorites / ") + button.Text + " bounds=" + button.Bounds + ", parent=" + parent.ClientSize);
                    }
                }
                Check(dialog.Result == null && dialog.RequestedFavoriteId == null && dialog.RequestedGroupId == null,
                    "library preview and navigation do not save launch or restore");
                dialog.Close();
            }
        }

        private sealed class FakeApplicationLauncher : IApplicationLauncher
        {
            public ApplicationFavorite? Launched;
            public void Launch(ApplicationFavorite favorite) { Launched = favorite.Copy(); }
        }
        private sealed class FakeAppLibraryStore : IAppLibraryStore
        {
            public int Saves;
            public bool FailSave;
            private AppLibrary value = new AppLibrary();
            public AppLibrary Read() => value.Copy();
            public void Save(AppLibrary library)
            {
                if (FailSave) throw new IOException("injected library failure");
                library.Validate(); value = library.Copy(); Saves++;
            }
        }
    }
}
