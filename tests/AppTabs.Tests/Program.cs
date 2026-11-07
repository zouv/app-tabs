using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using AppTabs.App;
using AppTabs.Core;

namespace AppTabs.Tests
{
    internal static class Program
    {
        private static int assertions;
        private static string testDirectory = "";
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 2 && args[0] == "--helper") return Helper(args[1]);
            if (args.Length == 2 && args[0] == "--helper-unaware") { SetThreadDpiAwarenessContext(new IntPtr(-1)); return Helper(args[1]); }
            if (args.Length == 4 && args[0] == "--owner") return Owner(args[1], args[2], args[3]);
            testDirectory = Path.Combine(Path.GetTempPath(), "AppTabs.Tests." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDirectory);
            try
            {
                ControllerChecks(); PersistenceChecks(); PolicyChecks(); NativeChecks(); DpiChecks(); WatchdogChecks();
                Console.WriteLine($"PASS: {assertions} assertions; fake failures, recovery, isolated native cross-process windows, mixed modes, UI rendering and watchdog.");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); Console.Error.WriteLine("Diagnostics: " + testDirectory); return 1; }
        }
        private static void Check(bool value, string message) { assertions++; if (!value) throw new Exception("Assertion failed: " + message); }
        private static void Throws(Action action, string message) { bool threw = false; try { action(); } catch { threw = true; } Check(threw, message); }
        private static WindowIdentity Identity(long handle) => new WindowIdentity { Handle = handle, ProcessId = 4000, ThreadId = 10, ProcessStartedUtcTicks = 100, ImagePath = "C:\\sample\\Editor.exe", ClassName = "EditorMainWindow", Title = "Document " + handle };
        private static WindowSnapshot Snapshot(WindowIdentity identity) => new WindowSnapshot { Identity = identity, Style = 0x10CF0000, WasVisible = true, HostHandle = 900, Placement = new WindowPlacement { Length = 44, ShowCommand = 1, NormalPosition = new NativeRect { Left = 10, Top = 20, Right = 610, Bottom = 420 } } };

        private static void ControllerChecks()
        {
            var api = new FakeApi(); var store = new FakeStore(); var closer = new FakeCloser();
            var controller = new HostController(api, store, closer); var bounds = new Rectangle(0, 0, 700, 500);
            store.Fail = true;
            Throws(() => controller.Attach(Identity(1), new IntPtr(900), HostingMode.Embedded, bounds), "durable save failure aborts attach");
            Check(api.Mutations == 0 && controller.Sessions.Count == 0, "save precedes all mutations");
            store.Fail = false;
            var one = controller.Attach(Identity(1), new IntPtr(900), HostingMode.Embedded, bounds);
            var two = controller.Attach(Identity(2), new IntPtr(900), HostingMode.Grouped, bounds);
            Check(controller.Sessions.Count == 2 && store.Last.Count == 2, "mixed modes supported");
            controller.Activate(one, bounds); controller.Activate(two, bounds);
            Check(controller.Active == two && api.Visible[2] && !api.Visible[1], "switch toggles real window visibility");
            Check(controller.Adjacent(1) == one && controller.Adjacent(-1) == one, "adjacent wraps");
            int mutations = api.Mutations;
            controller.RenameTab(one, "My editor"); controller.MoveTab(one, 1);
            Check(one.DisplayTitle == "My editor" && controller.Sessions[1] == one, "tab metadata changes");
            Check(api.Mutations == mutations, "metadata performs no window mutation");
            Throws(() => controller.Attach(Identity(1), new IntPtr(900), HostingMode.Grouped, bounds), "duplicate handle rejected");
            api.FailShowOnce = 1;
            Throws(() => controller.Activate(one, bounds), "failed switch surfaced");
            Check(controller.Active == two && api.Visible[2] && !api.Visible[1], "failed switch rolls back visibility");
            controller.RequestClose(two);
            Check(closer.Target == two.Identity && controller.Sessions.Contains(two) && store.Last.Count == 2, "close waits for actual HWND disappearance");
            controller.Detach(one);
            Check(api.Restores == 1 && controller.Sessions.Count == 1 && store.Last.Count == 1, "detach restores only selected window");
            api.Missing.Add(2); controller.PruneLost();
            Check(controller.Sessions.Count == 0 && api.Restores == 1 && store.Last.Count == 0, "lost identity discarded with no restore");
            var three = controller.Attach(Identity(3), new IntPtr(900), HostingMode.Grouped, bounds);
            api.Unknown.Add(3);
            Throws(() => controller.PruneLost(), "unknown query propagated");
            Check(controller.Sessions.Count == 1 && store.Last.Count == 1, "unknown identity retains journal");
            api.Unknown.Clear(); store.Fail = true;
            Throws(() => controller.Detach(three), "journal cleanup failure surfaced");
            int restores = api.Restores;
            Check(three.State == SessionState.RestoredPendingJournal, "restored pending cleanup recorded");
            store.Fail = false; controller.Detach(three);
            Check(api.Restores == restores && controller.Sessions.Count == 0, "retry cleanup does not restore twice");
            api.FailAttach = true;
            Throws(() => controller.Attach(Identity(4), new IntPtr(900), HostingMode.Embedded, bounds), "failed attach surfaced");
            Check(controller.Sessions.Count == 0 && store.Last.Count == 0, "failed attach rolled back and cleared");
            api.FailRestore = true;
            Throws(() => controller.Attach(Identity(5), new IntPtr(900), HostingMode.Embedded, bounds), "rollback failure surfaced");
            Check(controller.Sessions.Count == 1 && store.Last.Count == 1 && controller.Sessions[0].State == SessionState.RecoveryRequired, "rollback failure retains tracked state");
            api.FailRestore = false; api.FailAttach = false; controller.DetachAll();
            Check(controller.Sessions.Count == 0, "rollback may be retried");
        }
        private static void PersistenceChecks()
        {
            var journal = new RecoveryJournal(Path.Combine(testDirectory, "unit-recovery.json"));
            journal.Save(new[] { Snapshot(Identity(100)) });
            var doc = journal.Read()!;
            Check(doc.Windows.Count == 1 && doc.Windows[0].Identity.ImagePath == Identity(100).ImagePath, "generic app journal roundtrip");
            Check(RecoveryJournal.OwnerIsRunning(doc), "live owner detected");
            var api = new FakeApi();
            Throws(() => journal.Recover(doc, api), "refuse recovery from live owner");
            doc.HostProcessId = int.MaxValue; doc.HostStartedUtcTicks = 1;
            api.Unknown.Add(100);
            Check(!journal.TryClearAllMissing(doc, api, out _), "unknown never cleared");
            var result = journal.Recover(doc, api);
            Check(result.Errors.Count == 1 && journal.Read()!.Windows.Count == 1, "recovery errors persisted");
            api.Unknown.Clear(); api.Missing.Add(100);
            Check(journal.TryClearAllMissing(doc, api, out var missing), "all missing journal cleared");
            Check(missing!.Missing.Count == 1 && api.Restores == 0 && journal.Read()!.Windows.Count == 0, "missing clears without native mutation");
            doc.Windows = new List<WindowSnapshot> { Snapshot(Identity(101)) };
            var restored = journal.Recover(doc, new FakeApi());
            Check(restored.Restored.Count == 1 && journal.Read()!.Windows.Count == 0, "dead-owner live window recovered");
            var invalid = Snapshot(Identity(102)); invalid.Identity.ClassName = "Shell_TrayWnd";
            journal.Save(new[] { invalid }); Throws(() => journal.Read(), "system window rejected from journal");
            invalid = Snapshot(Identity(103)); invalid.Owner = 50;
            journal.Save(new[] { invalid }); Throws(() => journal.Read(), "owned window rejected from journal");
            journal.Save(new[] { Snapshot(Identity(104)), Snapshot(Identity(104)) }); Throws(() => journal.Read(), "duplicate recovery identities rejected");
            var stateStore = new StateStore(Path.Combine(testDirectory, "settings.json"));
            var state = new AppState { HotkeyChoice = 2, HideToTray = true, Preference = AttachPreference.Grouped,
                Group = new List<SavedWindow> { new SavedWindow { ImagePath = Identity(1).ImagePath, ClassName = Identity(1).ClassName, Title = "文档 中文", CustomTitle = "工具", Mode = HostingMode.Grouped } } };
            stateStore.Save(state); var loaded = stateStore.Read();
            Check(loaded.Group[0].Title == "文档 中文" && loaded.HideToTray && loaded.HotkeyChoice == 2, "UTF8 state roundtrip");
            stateStore.Save(loaded); Check(stateStore.Read().Group.Count == 1, "atomic state replacement");
            File.WriteAllText(stateStore.FilePath, "{broken"); Throws(() => stateStore.Read(), "malformed settings rejected");
            Check(File.ReadAllText(stateStore.FilePath) == "{broken", "invalid settings not overwritten");
        }
        private static void PolicyChecks()
        {
            foreach (var pair in new[] { (Keys.Tab, false, TabAction.Next), (Keys.Tab, true, TabAction.Previous), (Keys.W, false, TabAction.Detach), (Keys.F, false, TabAction.Search), (Keys.PageDown, false, TabAction.Next), (Keys.PageUp, false, TabAction.Previous), (Keys.D1, false, TabAction.First), (Keys.D9, false, TabAction.Ninth) })
                Check(KeyboardHook.Resolve(pair.Item1, true, pair.Item2, false) == pair.Item3, "shortcut " + pair.Item1);
            Check(KeyboardHook.Resolve(Keys.Tab, false, false, false) == null, "no unmodified tab capture");
            Check(KeyboardHook.Resolve(Keys.W, true, false, true) == null, "no Alt chord capture");
            var candidates = new[] { Identity(20), Identity(21) };
            var saved = new SavedWindow { ImagePath = candidates[0].ImagePath, ClassName = candidates[0].ClassName, Title = candidates[0].Title };
            Check(GroupMatching.Match(saved, candidates) == candidates[0], "group unique exact match");
            candidates[1].Title = candidates[0].Title;
            Check(GroupMatching.Match(saved, candidates) == null, "ambiguous group never guesses");
            candidates[1].Title = "other"; saved.Title = "changed";
            Check(GroupMatching.Match(saved, candidates) == null, "changed title never guesses");
            Check(!NativeWindowApi.IsEligibleIdentity(new WindowIdentity()), "invalid identity excluded");
            Check(NativeWindowApi.IsEligibleIdentity(Identity(1)), "generic app eligible");
            foreach (string name in new[] { "Progman", "WorkerW", "Shell_TrayWnd", "#32770" }) Check(WindowEligibility.IsSystemWindow(name, "C:\\sample\\app.exe"), "system class " + name);
            Check(TabManagementPolicy.NormalizeTitle("  编 辑 器  ") == "编 辑 器", "title normalized");
            Throws(() => TabManagementPolicy.NormalizeTitle("a\nb"), "control characters excluded");
            Check(RestorePresentation.HiddenStyle(0x10CF0000) == 0x00CF0000, "restore styles stay hidden");
        }

        private static void NativeChecks()
        {
            string ready = Path.Combine(testDirectory, "native-ready.txt");
            using (var helper = StartHelper(ready))
            using (var host = new QuietForm { Text = "AppTabs isolated test host", Size = new Size(900, 650), Location = new Point(-20000, -20000) })
            {
                try
                {
                    long[] handles = WaitHandles(ready, helper); host.Show(); Application.DoEvents();
                    var api = new NativeWindowApi(new NoTaskbar()); var journal = new RecoveryJournal(Path.Combine(testDirectory, "native-recovery.json"));
                    var controller = new HostController(api, journal);
                    var oneId = NativeWindowApi.Inspect(new IntPtr(handles[0])); var twoId = NativeWindowApi.Inspect(new IntPtr(handles[1]));
                    Check(oneId.ProcessId == helper.Id && twoId.ProcessId == helper.Id, "only owned helper process selected");
                    var originalOne = api.Capture(oneId); var originalTwo = api.Capture(twoId);
                    var bounds = new Rectangle(0, 0, 700, 450);
                    var one = controller.Attach(oneId, host.Handle, HostingMode.Embedded, bounds);
                    var two = controller.Attach(twoId, host.Handle, HostingMode.Grouped, bounds);
                    Check(GetParent(oneId.Hwnd) == host.Handle && GetParent(twoId.Hwnd) == IntPtr.Zero, "cross-process mixed parenting");
                    controller.Activate(one, bounds); Application.DoEvents();
                    Check(IsWindowVisible(oneId.Hwnd) && !IsWindowVisible(twoId.Hwnd), "embedded active, group hidden");
                    controller.Activate(two, bounds); Application.DoEvents();
                    Check(!IsWindowVisible(oneId.Hwnd) && IsWindowVisible(twoId.Hwnd), "group active, embedded hidden");
                    var groupRect = NativeWindowApi.ReadRect(twoId.Hwnd).ToRectangle(); var origin = host.PointToScreen(Point.Empty);
                    Check(groupRect.Location == origin && groupRect.Size == bounds.Size, "group uses screen coordinates");
                    controller.SetActiveVisible(false); Check(!IsWindowVisible(twoId.Hwnd), "host hidden hides group");
                    controller.SetActiveVisible(true); controller.Detach(one); controller.Detach(two);
                    var restoredOne = api.Capture(oneId); var restoredTwo = api.Capture(twoId);
                    Check(restoredOne.Style == originalOne.Style && restoredOne.ExtendedStyle == originalOne.ExtendedStyle && restoredOne.Parent == 0, "embedded styles restored");
                    Check(restoredTwo.Style == originalTwo.Style && restoredTwo.Parent == 0, "group styles preserved");
                    Check(restoredOne.Placement.NormalPosition.ToRectangle() == originalOne.Placement.NormalPosition.ToRectangle(), "embedded placement restored");
                    Check(restoredTwo.Placement.NormalPosition.ToRectangle() == originalTwo.Placement.NormalPosition.ToRectangle(), "group placement restored");
                    Check(journal.Read()!.Windows.Count == 0, "native journal cleared after detach");
                    var recycled = NativeWindowApi.Inspect(oneId.Hwnd); recycled.ProcessStartedUtcTicks++;
                    Check(!api.Matches(recycled), "recycled identity mismatch rejected");
                    // Render the real app's own controls without launching its tray or global hooks.
                    var uiStore = new StateStore(Path.Combine(testDirectory, "ui-settings.json"));
                    using (var ui = new MainForm(api, new RecoveryJournal(Path.Combine(testDirectory, "ui-recovery.json")), uiStore, new AppState(), false))
                    {
                        ui.Location = new Point(-22000, -22000); ui.Show(); Application.DoEvents();
                        var uiOne = ui.JoinWindow(oneId, AttachPreference.Embedded);
                        var uiTwo = ui.JoinWindow(twoId, AttachPreference.Grouped);
                        Check(ui.Controller.Sessions.Count == 2 && ui.Controller.Active == uiTwo, "UI joins mixed modes");
                        ui.Select(uiOne); Check(ui.Controller.Active == uiOne, "UI selects tab");
                        ui.Controller.RenameTab(uiOne, "文本编辑器"); ui.Select(uiTwo);
                        var output = Path.Combine(ProjectRoot(), "artifacts"); Directory.CreateDirectory(output);
                        using (var bitmap = new Bitmap(ui.Width, ui.Height)) { ui.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(output, "app-tabs-preview.png")); }
                        ui.Close();
                        Check(ui.Controller.Sessions.Count == 0, "closing UI detaches all");
                    }
                    Check(api.Capture(oneId).Parent == 0 && api.Capture(twoId).Parent == 0 && IsWindowVisible(oneId.Hwnd) && IsWindowVisible(twoId.Hwnd), "UI close preserves both helper windows");
                }
                finally { CloseHelper(helper, ready); }
            }
        }
        private static void WatchdogChecks()
        {
            string ready = Path.Combine(testDirectory, "watchdog-targets.txt");
            using (var helper = StartHelper(ready))
            {
                try
                {
                    long[] handles = WaitHandles(ready, helper);
                    var api = new NativeWindowApi(new NoTaskbar());
                    var original = api.Capture(NativeWindowApi.Inspect(new IntPtr(handles[0])));
                    string journal = Path.Combine(testDirectory, "watchdog-recovery.json"), ownerReady = Path.Combine(testDirectory, "owner-ready.txt"), stop = ownerReady + ".stop";
                    using (var owner = Process.Start(StartInfo(Assembly.GetExecutingAssembly().Location, $"--owner \"{ready}\" \"{journal}\" \"{ownerReady}\""))!)
                    {
                        try
                        {
                        WaitFile(ownerReady, owner);
                        Check(!IsWindowVisible(original.Identity.Hwnd), "test owner hides group target");
                        string eventName = "Local\\AppTabs.TestReady." + Guid.NewGuid().ToString("N");
                        using (var signal = new EventWaitHandle(false, EventResetMode.ManualReset, eventName))
                        using (var watchdog = Process.Start(StartInfo(typeof(MainForm).Assembly.Location,
                            $"--watchdog \"{journal}\" {owner.Id} {owner.StartTime.ToUniversalTime().Ticks} \"{eventName}\""))!)
                        {
                            Check(signal.WaitOne(5000), "production watchdog handshake");
                            File.WriteAllText(stop, "stop");
                            Check(owner.WaitForExit(5000), "synthetic owner exits without detach");
                            Check(watchdog.WaitForExit(10000), "production watchdog completes recovery");
                            var restored = api.Capture(original.Identity);
                            Check(IsWindowVisible(original.Identity.Hwnd) && restored.Parent == 0 && restored.Placement.NormalPosition.ToRectangle() == original.Placement.NormalPosition.ToRectangle(), "watchdog restores surviving target");
                            Check(new RecoveryJournal(journal).Read()!.Windows.Count == 0, "watchdog clears recovered journal");
                        }
                        }
                        finally { CloseHelper(owner, ownerReady); }
                    }
                }
                finally { CloseHelper(helper, ready); }
            }
        }
        private static void DpiChecks()
        {
            string ready = Path.Combine(testDirectory, "dpi-ready.txt");
            using (var helper = Process.Start(StartInfo(Assembly.GetExecutingAssembly().Location, $"--helper-unaware \"{ready}\""))!)
            {
                try
                {
                    long[] handles = WaitHandles(ready, helper); var api = new NativeWindowApi(new NoTaskbar());
                    var identity = NativeWindowApi.Inspect(new IntPtr(handles[0])); string originalDpi = NativeWindowApi.DescribeDpi(identity.Hwnd);
                    Check(originalDpi == "Unaware", "synthetic target uses DPI Unaware");
                    using (var ui = new MainForm(api, new RecoveryJournal(Path.Combine(testDirectory, "dpi-recovery.json")),
                        new StateStore(Path.Combine(testDirectory, "dpi-settings.json")), new AppState(), false))
                    {
                        ui.Location = new Point(-23000, -23000); ui.Show(); Application.DoEvents();
                        Throws(() => api.ValidateAttach(api.Capture(identity), ui.ContentHandle, HostingMode.Embedded), "DPI mismatch rejects SetParent");
                        var session = ui.JoinWindow(identity, AttachPreference.Auto);
                        Check(session.Snapshot.Mode == HostingMode.Grouped && GetParent(identity.Hwnd) == IntPtr.Zero, "auto DPI fallback preserves top-level window");
                        Check(NativeWindowApi.DescribeDpi(identity.Hwnd) == originalDpi, "target DPI awareness never reset");
                        ui.Close(); Check(IsWindowVisible(identity.Hwnd) && api.Capture(identity).Parent == 0, "DPI fallback detach preserves window");
                    }
                }
                finally { CloseHelper(helper, ready); }
            }
        }
        private static string ProjectRoot() => Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", ".."));
        private static ProcessStartInfo StartInfo(string exe, string arguments) => new ProcessStartInfo(exe, arguments) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        private static Process StartHelper(string ready) => Process.Start(StartInfo(Assembly.GetExecutingAssembly().Location, $"--helper \"{ready}\""))!;
        private static long[] WaitHandles(string path, Process process) { WaitFile(path, process); return File.ReadAllLines(path).Select(long.Parse).ToArray(); }
        private static void WaitFile(string path, Process process)
        {
            var timer = Stopwatch.StartNew();
            while (!File.Exists(path) && !process.HasExited && timer.ElapsedMilliseconds < 5000) { Application.DoEvents(); Thread.Sleep(20); }
            Check(File.Exists(path), "helper ready");
        }
        private static void CloseHelper(Process process, string ready)
        {
            if (process.HasExited) return;
            File.WriteAllText(ready + ".stop", "stop");
            if (!process.WaitForExit(5000)) { process.Kill(); process.WaitForExit(5000); }
        }
        private static int Helper(string ready)
        {
            using (var one = new QuietForm { Text = "AppTabs synthetic editor", Size = new Size(600, 440), Location = new Point(-18000, -18000) })
            using (var two = new QuietForm { Text = "AppTabs synthetic diff tool", Size = new Size(640, 460), Location = new Point(-17500, -17500) })
            using (var timer = new System.Windows.Forms.Timer { Interval = 50 })
            {
                one.Controls.Add(new TextBox { Multiline = true, Dock = DockStyle.Fill, Text = "AppTabs isolated test data" });
                one.Show(); two.Show();
                File.WriteAllLines(ready, new[] { one.Handle.ToInt64().ToString(), two.Handle.ToInt64().ToString() });
                timer.Tick += (_, __) => { if (File.Exists(ready + ".stop")) Application.ExitThread(); };
                timer.Start(); Application.Run();
            }
            return 0;
        }
        private static int Owner(string targets, string journalPath, string ready)
        {
            using (var host = new QuietForm { Size = new Size(700, 450), Location = new Point(-19000, -19000) })
            using (var timer = new System.Windows.Forms.Timer { Interval = 50 })
            {
                host.Show();
                var api = new NativeWindowApi(new NoTaskbar()); var controller = new HostController(api, new RecoveryJournal(journalPath));
                foreach (var handle in File.ReadAllLines(targets).Select(long.Parse))
                {
                    var session = controller.Attach(NativeWindowApi.Inspect(new IntPtr(handle)), host.Handle, HostingMode.Grouped, host.ClientRectangle);
                    api.Show(session.Snapshot, false, false);
                }
                File.WriteAllText(ready, "ready");
                timer.Tick += (_, __) => { if (File.Exists(ready + ".stop")) Environment.Exit(0); };
                timer.Start(); Application.Run();
            }
            return 0;
        }
        private sealed class QuietForm : Form
        {
            public QuietForm() { StartPosition = FormStartPosition.Manual; }
            protected override CreateParams CreateParams { get { var value = base.CreateParams; value.ExStyle |= 0x80; return value; } }
            protected override bool ShowWithoutActivation => true;
        }
        private sealed class NoTaskbar : ITaskbarService { public void Add(WindowIdentity identity) { } public void Remove(WindowIdentity identity) { } }
        private sealed class FakeStore : ISnapshotStore
        {
            public bool Fail; public IReadOnlyCollection<WindowSnapshot> Last = Array.Empty<WindowSnapshot>();
            public void Save(IReadOnlyCollection<WindowSnapshot> snapshots) { if (Fail) throw new IOException("synthetic disk failure"); Last = snapshots.ToArray(); }
        }
        private sealed class FakeCloser : IWindowCloser { public WindowIdentity? Target; public void RequestClose(WindowIdentity identity) { Target = identity; } }
        private sealed class FakeApi : IWindowApi
        {
            public int Mutations, Restores; public bool FailAttach, FailRestore; public long FailShowOnce;
            public readonly HashSet<long> Missing = new HashSet<long>(), Unknown = new HashSet<long>();
            public readonly Dictionary<long, bool> Visible = new Dictionary<long, bool>();
            public bool Matches(WindowIdentity identity) { if (Unknown.Contains(identity.Handle)) throw new IOException("synthetic unknown"); return !Missing.Contains(identity.Handle); }
            public WindowSnapshot Capture(WindowIdentity identity) { if (!Matches(identity)) throw new Exception("missing"); return Snapshot(identity); }
            public void ValidateAttach(WindowSnapshot snapshot, IntPtr host, HostingMode mode) { }
            public void Attach(WindowSnapshot snapshot, IntPtr host, Rectangle bounds) { Mutations++; Visible[snapshot.Identity.Handle] = false; if (FailAttach) throw new Exception("synthetic attach failure"); }
            public void Layout(WindowSnapshot snapshot, Rectangle bounds) { Mutations++; }
            public void Show(WindowSnapshot snapshot, bool visible, bool raise)
            { Mutations++; if (FailShowOnce == snapshot.Identity.Handle) { FailShowOnce = 0; throw new Exception("synthetic show failure"); } Visible[snapshot.Identity.Handle] = visible; }
            public void Restore(WindowSnapshot snapshot) { Mutations++; if (FailRestore) throw new Exception("synthetic restore failure"); Restores++; Visible[snapshot.Identity.Handle] = snapshot.WasVisible; }
        }
        [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr window);
    }
}
