using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace AppTabs.Core
{
    /// <summary>UI-thread confined. Every native mutation is preceded by a durable recovery snapshot.</summary>
    public sealed class HostController
    {
        private readonly IWindowApi windows;
        private readonly ISnapshotStore store;
        private readonly IWindowCloser closer;
        private readonly List<WindowSession> sessions = new List<WindowSession>();
        public IReadOnlyList<WindowSession> Sessions => sessions.AsReadOnly();
        public WindowSession? Active { get; private set; }

        public HostController(IWindowApi windows, ISnapshotStore store, IWindowCloser? closer = null)
        {
            this.windows = windows;
            this.store = store;
            this.closer = closer ?? new NativeWindowCloser();
        }

        public WindowSession Attach(WindowIdentity identity, IntPtr host, HostingMode mode, Rectangle bounds, bool restoreCreatedVisible = false,
            bool borderlessGrouped = false)
        {
            if (sessions.Count >= 100) throw new InvalidOperationException("最多管理 100 个窗口。");
            if (sessions.Any(s => s.State != SessionState.Attached))
                throw new InvalidOperationException("请先处理未完成恢复的标签，再接管新窗口。");
            if (sessions.Any(s => s.Identity.Handle == identity.Handle))
                throw new InvalidOperationException("这个窗口已经在标签列表中。");
            var snapshot = windows.Capture(identity);
            windows.ValidateAttach(snapshot, host, mode);
            if (restoreCreatedVisible)
            {
                // A freshly created hidden browser must become an ordinary visible window on detach/recovery.
                snapshot.WasVisible = true;
                var placement = snapshot.Placement;
                if (placement.ShowCommand == 0) placement.ShowCommand = 1;
                snapshot.Placement = placement;
            }
            snapshot.HostHandle = host.ToInt64();
            snapshot.Mode = mode;
            snapshot.FrameStripped = mode == HostingMode.Grouped && borderlessGrouped;
            var session = new WindowSession(snapshot);
            // No Win32 mutation if persisting the recovery information fails.
            store.Save(sessions.Select(s => s.Snapshot).Concat(new[] { snapshot }).ToArray());
            sessions.Add(session);
            try
            {
                windows.Attach(snapshot, host, bounds);
                return session;
            }
            catch (Exception attachError)
            {
                session.State = SessionState.RecoveryRequired;
                session.LastError = attachError.Message;
                try { Detach(session); }
                catch (Exception restoreError)
                {
                    throw new AggregateException("接管失败，而且回滚尚未完成。窗口仍保留在列表；请重试分离，不要强杀程序。",
                        attachError, restoreError);
                }
                throw new InvalidOperationException("接管失败，已恢复原窗口。" + attachError.Message, attachError);
            }
        }

        public void Activate(WindowSession next, Rectangle bounds, bool raise = true, bool visible = true)
        {
            RequireSession(next);
            if (next.State != SessionState.Attached)
                throw new InvalidOperationException("此标签需要先分离 / 恢复，不能激活。");
            if (!windows.Matches(next.Identity))
                throw new InvalidOperationException("目标窗口已失效，请刷新标签列表。");
            var previous = Active;
            try
            {
                if (visible) windows.Layout(next.Snapshot, bounds);
                // Show before hiding the old window to avoid an intentional blank content frame.
                windows.Show(next.Snapshot, visible, visible && raise);
                if (previous != null && previous != next && windows.Matches(previous.Identity))
                    windows.Show(previous.Snapshot, false, false);
            }
            catch (Exception switchError)
            {
                var rollbackErrors = new List<Exception>();
                if (next != previous)
                {
                    try { if (windows.Matches(next.Identity)) windows.Show(next.Snapshot, false, false); }
                    catch (Exception error) { rollbackErrors.Add(error); }
                }
                try
                {
                    if (previous != null && windows.Matches(previous.Identity))
                        windows.Show(previous.Snapshot, visible, visible && raise);
                }
                catch (Exception error) { rollbackErrors.Add(error); }
                if (rollbackErrors.Count > 0)
                {
                    next.State = SessionState.RecoveryRequired;
                    next.LastError = switchError.Message;
                    if (previous != null) { previous.State = SessionState.RecoveryRequired; previous.LastError = switchError.Message; }
                    throw new AggregateException("标签切换及显隐回滚失败，请先分离相关窗口。",
                        new[] { switchError }.Concat(rollbackErrors));
                }
                throw;
            }
            Active = next;
        }

        public WindowSession? Adjacent(int direction)
        {
            if (direction != -1 && direction != 1) throw new ArgumentOutOfRangeException(nameof(direction));
            var available = sessions.Where(s => s.State == SessionState.Attached).ToArray();
            if (available.Length < 2) return null;
            int index = Active == null ? -1 : Array.IndexOf(available, Active);
            if (index < 0) return direction > 0 ? available[0] : available[available.Length - 1];
            return available[(index + direction + available.Length) % available.Length];
        }

        public void LayoutActive(Rectangle bounds, bool raise = false)
        {
            if (Active == null || Active.State != SessionState.Attached || !windows.Matches(Active.Identity)) return;
            windows.Layout(Active.Snapshot, bounds);
            if (raise) windows.Show(Active.Snapshot, true, true);
        }

        public void RaiseActive()
        {
            // Grouped windows are independent top-level windows. Re-raise the active one
            // (SWP_NOACTIVATE) without touching its geometry, so activating the host does
            // not expose the content surface while the scheduled layout is still pending.
            if (Active == null || Active.State != SessionState.Attached || !windows.Matches(Active.Identity)) return;
            windows.Show(Active.Snapshot, true, true);
        }

        public void SetActiveVisible(bool visible)
        {
            if (Active != null && Active.State == SessionState.Attached && windows.Matches(Active.Identity))
                windows.Show(Active.Snapshot, visible, false);
        }

        public void RequestClose(WindowSession session)
        {
            RequireSession(session);
            if (session.State != SessionState.Attached || !windows.Matches(session.Identity))
                throw new InvalidOperationException("目标标签已失效或需要恢复，未关闭。");
            if (session.CloseRequestedUtc != null) return;
            store.Save(sessions.Select(s => s.Snapshot).ToArray());
            closer.RequestClose(session.Identity);
            // Keep both the live session and durable journal until the HWND has actually disappeared.
            session.CloseRequestedUtc = DateTime.UtcNow;
        }

        public void RetryClose(WindowSession session)
        { RequireSession(session); session.CloseRequestedUtc = null; }

        public void Detach(WindowSession session)
        {
            RequireSession(session);
            if (session.State != SessionState.RestoredPendingJournal && session.State != SessionState.Lost)
            {
                if (!windows.Matches(session.Identity)) session.State = SessionState.Lost;
                else
                {
                    try { windows.Restore(session.Snapshot); }
                    catch (Exception error)
                    {
                        session.State = SessionState.RecoveryRequired;
                        session.LastError = error.Message;
                        throw;
                    }
                    // If journal cleanup fails, a retry must NOT restore/move the window again.
                    session.State = SessionState.RestoredPendingJournal;
                }
            }
            if (Active == session) Active = null;
            try { store.Save(sessions.Where(s => s != session).Select(s => s.Snapshot).ToArray()); }
            catch (Exception error)
            {
                session.LastError = "窗口已恢复或消失，但恢复日志更新失败：" + error.Message;
                throw;
            }
            sessions.Remove(session);
        }

        public IReadOnlyList<string> DetachAll()
        {
            var errors = new List<string>();
            foreach (var session in sessions.ToArray())
            {
                try { Detach(session); }
                catch (Exception error) { errors.Add(session.Title + "：" + error.Message); }
            }
            return errors;
        }

        public void MoveTab(WindowSession session, int finalIndex)
        {
            RequireSession(session);
            if (finalIndex < 0 || finalIndex >= sessions.Count) throw new ArgumentOutOfRangeException(nameof(finalIndex));
            int oldIndex = sessions.IndexOf(session);
            if (oldIndex == finalIndex) return;
            sessions.RemoveAt(oldIndex);
            sessions.Insert(finalIndex, session);
            // Ordering is session-only metadata. No navigation, focus change, reparent or journal rewrite.
        }

        public void RenameTab(WindowSession session, string title)
        {
            RequireSession(session);
            session.CustomTitle = TabManagementPolicy.NormalizeTitle(title);
        }

        public void ResetTabTitle(WindowSession session)
        {
            RequireSession(session);
            session.CustomTitle = null;
        }

        public IReadOnlyList<string> DetachTabs(WindowSession anchor, TabDetachScope scope)
        {
            var targets = TabManagementPolicy.Select(Sessions, anchor, scope);
            var errors = new List<string>();
            foreach (var session in targets)
            {
                try { Detach(session); }
                catch (Exception error) { errors.Add(session.DisplayTitle + "：" + error.Message); }
            }
            return errors;
        }

        public IReadOnlyList<string> PruneLost() => PruneTargets(sessions.ToArray());

        // Fast close confirmation checks only explicit managed targets, never discovers windows.
        public IReadOnlyList<string> PruneTargets(IEnumerable<WindowSession> targets)
        {
            var errors = new List<string>();
            foreach (var session in targets.ToArray())
            {
                RequireSession(session);
                if (session.State == SessionState.Lost || !windows.Matches(session.Identity))
                {
                    session.State = SessionState.Lost;
                    try { Detach(session); }
                    catch (Exception error) { errors.Add(error.Message); }
                }
            }
            return errors;
        }

        private void RequireSession(WindowSession session)
        {
            if (!sessions.Contains(session)) throw new InvalidOperationException("标签不属于当前宿主。");
        }
    }
}
