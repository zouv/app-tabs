using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace AppTabs.Core
{
    [DataContract]
    public sealed class RecoveryDocument
    {
        [DataMember] public int Version { get; set; } = 1;
        [DataMember] public int HostProcessId { get; set; }
        [DataMember] public long HostStartedUtcTicks { get; set; }
        [DataMember] public List<WindowSnapshot> Windows { get; set; } = new List<WindowSnapshot>();
    }

    public sealed class RecoveryReport
    {
        public List<string> Restored { get; } = new List<string>();
        public List<string> Missing { get; } = new List<string>();
        public List<string> Errors { get; } = new List<string>();
        public string Summary => $"恢复结果：{Restored.Count} 个原窗口已恢复；{Missing.Count} 个原窗口已失效（已消失或身份不匹配）；{Errors.Count} 个恢复失败。";
    }

    public sealed class RecoveryJournal : ISnapshotStore
    {
        private readonly int ownerPid;
        private readonly long ownerStarted;
        public string FilePath { get; }

        public RecoveryJournal(string filePath)
        {
            FilePath = Path.GetFullPath(filePath);
            using (var process = Process.GetCurrentProcess())
            {
                ownerPid = process.Id;
                ownerStarted = process.StartTime.ToUniversalTime().Ticks;
            }
        }

        // A startup and the previous host's watchdog must never recover the same journal together.
        public IDisposable LockForRecovery() => new RecoveryLock(FilePath);
        private sealed class RecoveryLock : IDisposable
        {
            private readonly Mutex mutex;
            public RecoveryLock(string path)
            {
                string key;
                using (var hash = SHA256.Create()) key = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(path.ToUpperInvariant()))).Replace("-", "");
                mutex = new Mutex(false, "Local\\AppTabs.Recovery." + key);
                bool acquired;
                try { acquired = mutex.WaitOne(15000); } catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) { mutex.Dispose(); throw new IOException("另一个进程正在恢复窗口，请稍后重试。"); }
            }
            public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); }
        }

        public void Save(IReadOnlyCollection<WindowSnapshot> snapshots)
        {
            var document = new RecoveryDocument
            {
                HostProcessId = ownerPid, HostStartedUtcTicks = ownerStarted,
                Windows = snapshots.ToList()
            };
            Write(document);
        }

        public RecoveryDocument? Read()
        {
            if (!File.Exists(FilePath)) return null;
            using (var stream = File.OpenRead(FilePath))
            {
                var document = (RecoveryDocument?)new DataContractJsonSerializer(typeof(RecoveryDocument)).ReadObject(stream);
                if (document == null || document.Version != 1 || document.Windows == null || document.Windows.Count > 100)
                    throw new InvalidDataException("恢复日志格式 / 版本无效。为安全起见没有接管窗口。");
                if (document.HostProcessId <= 0 || document.HostStartedUtcTicks <= 0 ||
                    document.Windows.Where(s => s != null && s.Identity != null).Select(s => s.Identity.Handle).Distinct().Count() != document.Windows.Count)
                    throw new InvalidDataException("恢复日志的宿主或窗口标识无效。");
                foreach (var snapshot in document.Windows)
                {
                    if (snapshot == null || snapshot.Identity == null || !NativeWindowApi.IsEligibleIdentity(snapshot.Identity) ||
                        snapshot.Identity.Handle == 0 || snapshot.Identity.ProcessId <= 0 || snapshot.Identity.ThreadId == 0 ||
                        snapshot.Identity.ProcessStartedUtcTicks <= 0 || snapshot.HostHandle == 0 ||
                        snapshot.Placement.Length != 44 || snapshot.Parent != 0 ||
                        snapshot.Owner != 0 || (snapshot.Style & 0x40000000L) != 0 ||
                        !Enum.IsDefined(typeof(HostingMode), snapshot.Mode))
                        throw new InvalidDataException("恢复日志含有不符合接管条件的窗口。");
                }
                return document;
            }
        }

        public static bool OwnerIsRunning(RecoveryDocument document)
        {
            try
            {
                using (var process = Process.GetProcessById(document.HostProcessId))
                    return process.StartTime.ToUniversalTime().Ticks == document.HostStartedUtcTicks && !process.HasExited;
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (System.ComponentModel.Win32Exception) { return true; } // Unknown => do not touch live state.
        }

        public bool TryClearAllMissing(RecoveryDocument document, IWindowApi windows, out RecoveryReport? report)
        {
            report = null;
            if (OwnerIsRunning(document)) return false;
            var candidate = new RecoveryReport();
            bool allMissing = true;
            foreach (var snapshot in document.Windows)
            {
                try
                {
                    if (windows.Matches(snapshot.Identity)) allMissing = false;
                    else candidate.Missing.Add(snapshot.Identity.Title);
                }
                catch (Exception)
                {
                    // UNKNOWN is not evidence of a missing identity; leave manual recovery in charge.
                    allMissing = false;
                }
            }
            if (!allMissing || OwnerIsRunning(document)) return false;
            // Commit a separate empty document, retaining the original owner and input even if
            // writing fails. This path never restores windows or changes their presentation.
            Write(new RecoveryDocument
            {
                Version = document.Version, HostProcessId = document.HostProcessId,
                HostStartedUtcTicks = document.HostStartedUtcTicks,
                Windows = new List<WindowSnapshot>()
            });
            report = candidate;
            return true;
        }

        public RecoveryReport Recover(RecoveryDocument document, IWindowApi windows)
        {
            if (OwnerIsRunning(document)) throw new InvalidOperationException("恢复日志的原宿主仍在运行，拒绝争夺窗口。");
            var pending = new List<WindowSnapshot>();
            var report = new RecoveryReport();
            foreach (var snapshot in document.Windows)
            {
                try
                {
                    if (!windows.Matches(snapshot.Identity))
                    {
                        report.Missing.Add(snapshot.Identity.Title);
                        continue; // Never manipulate a recycled HWND or imply it was resurrected.
                    }
                    windows.Restore(snapshot);
                    report.Restored.Add(snapshot.Identity.Title);
                }
                catch (Exception error)
                {
                    pending.Add(snapshot);
                    report.Errors.Add(snapshot.Identity.Title + "：" + error.Message);
                }
            }
            // Retain the ORIGINAL owner while retryable entries remain. The new app must not
            // turn a dead owner's journal into an apparently live one before recovery is done.
            document.Windows = pending;
            Write(document);
            return report;
        }

        private void Write(RecoveryDocument document)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    new DataContractJsonSerializer(typeof(RecoveryDocument)).WriteObject(stream, document);
                    stream.Flush(true);
                }
                if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null);
                else File.Move(temporary, FilePath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
