using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using AppTabs.Core;

namespace AppTabs.App
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--watchdog") { RunWatchdog(args); return; }
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AppTabs");
            using (var mutex = new Mutex(false, "Local\\AppTabs." + WindowsIdentity.GetCurrent().User!.Value))
            {
                bool acquired;
                try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) { MessageBox.Show("AppTabs 已在运行，请使用全局快捷键、托盘图标或任务栏激活。", "AppTabs"); return; }
                try
                {
                    var journal = new RecoveryJournal(Path.Combine(directory, "recovery.json"));
                    var store = new StateStore(Path.Combine(directory, "settings.json"));
                    string logPath = Path.Combine(directory, "app.log");
                    var windows = new NativeWindowApi(trace: message => Log(logPath, message));
                    string recoveryNotice = "";
                    using (journal.LockForRecovery())
                    {
                        var pending = journal.Read();
                        if (pending != null && pending.Windows.Count > 0)
                        {
                            if (RecoveryJournal.OwnerIsRunning(pending)) throw new InvalidOperationException("恢复日志对应的宿主仍在运行，未接管窗口。");
                            if (journal.TryClearAllMissing(pending, windows, out var missing)) recoveryNotice = missing!.Summary;
                            else
                            {
                                if (MessageBox.Show($"上次运行留下 {pending.Windows.Count} 个待还原窗口。\n先还原这些窗口，再继续启动 AppTabs？", "恢复窗口", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                                var report = journal.Recover(pending, windows);
                                Log(logPath, report.Summary);
                                if (report.Errors.Count > 0) throw new InvalidOperationException(report.Summary + "\n" + string.Join("\n", report.Errors));
                                recoveryNotice = report.Summary;
                            }
                        }
                    }
                    var state = store.Read();
                    StartWatchdog(journal.FilePath);
                    using (var form = new MainForm(windows, journal, store, state))
                    {
                        if (recoveryNotice.Length > 0) form.SetStatus(recoveryNotice);
                        Application.Run(form);
                    }
                }
                catch (Exception error)
                {
                    Environment.ExitCode = 1;
                    string logPath = Path.Combine(directory, "app.log");
                    Log(logPath, "startup.failed " + error);
                    MessageBox.Show(error.Message + "\n诊断日志：" + logPath, "AppTabs 启动失败", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                finally { mutex.ReleaseMutex(); }
            }
        }

        private static void StartWatchdog(string journalPath)
        {
            using (var parent = Process.GetCurrentProcess())
            {
                string readyName = "Local\\AppTabs.Watchdog." + Guid.NewGuid().ToString("N");
                using (var ready = new EventWaitHandle(false, EventResetMode.ManualReset, readyName))
                using (var watchdog = Process.Start(new ProcessStartInfo(Application.ExecutablePath,
                    $"--watchdog \"{journalPath}\" {parent.Id} {parent.StartTime.ToUniversalTime().Ticks} \"{readyName}\"")
                    { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden }))
                {
                    if (watchdog == null || !ready.WaitOne(5000))
                        throw new InvalidOperationException("无法启动窗口恢复守护进程，本次未接管任何软件。");
                }
            }
        }

        private static void RunWatchdog(string[] args)
        {
            if (args.Length != 5 || !int.TryParse(args[2], out int pid) || !long.TryParse(args[3], out long started)) return;
            string logPath = Path.Combine(Path.GetDirectoryName(args[1])!, "watchdog.log");
            try
            {
                using (var ready = EventWaitHandle.OpenExisting(args[4])) ready.Set();
                try
                {
                    using (var parent = Process.GetProcessById(pid))
                        if (parent.StartTime.ToUniversalTime().Ticks == started) parent.WaitForExit();
                }
                catch (ArgumentException) { }
                var journal = new RecoveryJournal(args[1]);
                using (journal.LockForRecovery())
                {
                    var document = journal.Read();
                    if (document == null || document.Windows.Count == 0 || document.HostProcessId != pid || document.HostStartedUtcTicks != started) return;
                    var report = journal.Recover(document, new NativeWindowApi(trace: message => Log(logPath, message)));
                    Log(logPath, report.Summary + "\n" + string.Join("\n", report.Errors));
                }
            }
            catch (Exception error) { Log(logPath, "watchdog.failed " + error.Message); }
        }
        private static void Log(string path, string message)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length > 2 * 1024 * 1024)
                { if (File.Exists(path + ".previous")) File.Delete(path + ".previous"); File.Move(path, path + ".previous"); }
                File.AppendAllText(path, DateTime.UtcNow.ToString("O") + " " + message + Environment.NewLine);
            }
            catch { /* Local diagnostics cannot change window ownership. */ }
        }
    }
}
