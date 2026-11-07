using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace AppTabs.Core
{
    public enum TabDetachScope { Current, Others, ToRight, All }

    /// <summary>Pure tab metadata/selection policy. No native window calls or recovery writes.</summary>
    public static class TabManagementPolicy
    {
        public const int MaximumTitleLength = 120;
        public static string NormalizeTitle(string title)
        {
            if (title == null) throw new ArgumentNullException(nameof(title));
            string result = title.Trim();
            if (result.Length == 0) throw new ArgumentException("标签名称不能为空，请用“恢复默认名称”取消自定义名称。");
            if (result.Length > MaximumTitleLength) throw new ArgumentException("标签名称最多 120 个字符。");
            if (result.Any(char.IsControl)) throw new ArgumentException("标签名称不能包含换行或控制字符。");
            return result;
        }

        public static IReadOnlyList<WindowSession> Select(IReadOnlyList<WindowSession> sessions,
            WindowSession anchor, TabDetachScope scope)
        {
            int index = sessions.ToList().IndexOf(anchor);
            if (index < 0) throw new InvalidOperationException("标签不属于当前宿主。");
            switch (scope)
            {
                case TabDetachScope.Current: return new[] { anchor };
                case TabDetachScope.Others: return sessions.Where(s => s != anchor).ToArray();
                case TabDetachScope.ToRight: return sessions.Skip(index + 1).ToArray();
                case TabDetachScope.All: return sessions.ToArray();
                default: throw new ArgumentOutOfRangeException(nameof(scope));
            }
        }

        public static WindowSession? Successor(IReadOnlyList<WindowSession> sessions, WindowSession? active,
            IReadOnlyCollection<WindowSession> removing)
        {
            var available = sessions.Where(s => s.State == SessionState.Attached && !removing.Contains(s)).ToArray();
            if (active != null && available.Contains(active)) return active;
            int index = active == null ? -1 : sessions.ToList().IndexOf(active);
            // Prefer the next neighbour, then the nearest one to the left. Skip unresolved tabs.
            return sessions.Skip(index + 1).FirstOrDefault(available.Contains) ??
                sessions.Take(Math.Max(index, 0)).Reverse().FirstOrDefault(available.Contains) ?? available.FirstOrDefault();
        }

        /// <summary>Tab to activate after a window disappears: nearest left neighbour first,
        /// then the right neighbour. Closing a non-active tab keeps the current active tab.</summary>
        public static WindowSession? CloseSuccessor(IReadOnlyList<WindowSession> sessions, WindowSession? active, WindowSession closing,
            IReadOnlyCollection<WindowSession>? available = null)
        {
            if (active != null && active != closing && sessions.Contains(active) && active.State == SessionState.Attached)
                return active;
            int index = sessions.ToList().IndexOf(closing);
            if (index < 0) return active;
            bool Eligible(WindowSession session) => session != closing && session.State == SessionState.Attached &&
                session.CloseRequestedUtc == null && (available == null || available.Contains(session));
            return sessions.Take(index).Reverse().FirstOrDefault(Eligible) ??
                sessions.Skip(index + 1).FirstOrDefault(Eligible);
        }

        public static bool StartsDrag(Point origin, Point current, Size threshold) =>
            Math.Abs((long)current.X - origin.X) > Math.Max(1, threshold.Width) / 2 ||
            Math.Abs((long)current.Y - origin.Y) > Math.Max(1, threshold.Height) / 2;

        // Centers exclude the dragged tab; the result is its final index after removal/insertion.
        public static int InsertionIndex(IEnumerable<int> remainingCenters, int x) => remainingCenters.Count(center => x >= center);
    }
}
