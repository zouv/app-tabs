using System;
using System.Runtime.InteropServices;
using System.Text;

namespace AppTabs.Core
{
    /// <summary>Read-only diagnostic state. No foreign text messages, screenshots, input or Shell mutations.</summary>
    public static class WindowDiagnostics
    {
        public static string Capture(WindowIdentity identity, bool children = false)
        {
            // Unknown identities are not queried further, even when collecting diagnostics.
            if (!new NativeWindowApi().Matches(identity)) return $"HWND=0x{identity.Handle:X} identity=missing-or-changed";
            var text = new StringBuilder();
            text.Append($"HWND=0x{identity.Handle:X} PID={identity.ProcessId} TID={identity.ThreadId} started={identity.ProcessStartedUtcTicks} ");
            AppendWindow(text, identity.Hwnd);
            int hr = NativeMethods.DwmGetWindowAttribute(identity.Hwnd, 14, out int cloaked, sizeof(int)); // DWMWA_CLOAKED
            text.Append($" DWM_CLOAKED={cloaked} DWM_HR=0x{hr:X8} foreground=0x{NativeMethods.GetForegroundWindow().ToInt64():X}");
            var placement = new WindowPlacement { Length = (uint)Marshal.SizeOf<WindowPlacement>() };
            if (NativeMethods.GetWindowPlacement(identity.Hwnd, ref placement))
                text.Append($" showCmd={placement.ShowCommand} placementFlags=0x{placement.Flags:X} normal={Rect(placement.NormalPosition)}");
            else text.Append(" placement=unavailable");
            if (children)
            {
                int count = 0;
                NativeMethods.EnumChildWindows(identity.Hwnd, (hwnd, _) =>
                {
                    if (++count > 80) return false;
                    text.Append($"\n child#{count} HWND=0x{hwnd.ToInt64():X} ");
                    AppendWindow(text, hwnd);
                    return true;
                }, IntPtr.Zero);
                text.Append($"\n childCount={Math.Min(count, 80)} capped={count > 80}");
            }
            return text.ToString();
        }

        private static void AppendWindow(StringBuilder text, IntPtr hwnd)
        {
            var name = new StringBuilder(256);
            NativeMethods.GetClassName(hwnd, name, name.Capacity);
            long style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlStyle).ToInt64() & 0xFFFFFFFFL;
            long exstyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64() & 0xFFFFFFFFL;
            text.Append($"class={name} parentOrOwner=0x{NativeMethods.GetParent(hwnd).ToInt64():X} owner=0x{NativeMethods.GetWindow(hwnd, NativeMethods.GwOwner).ToInt64():X} root=0x{NativeMethods.GetAncestor(hwnd, 2).ToInt64():X} style=0x{style:X8} exstyle=0x{exstyle:X8} visible={NativeMethods.IsWindowVisible(hwnd)} DPI={NativeWindowApi.DescribeDpi(hwnd)}");
            if (NativeMethods.GetWindowRect(hwnd, out var rect)) text.Append(" rect=" + Rect(rect));
            if (NativeMethods.GetClientRect(hwnd, out rect)) text.Append(" client=" + Rect(rect));
            bool pending = NativeMethods.GetUpdateRect(hwnd, out rect, false);
            text.Append($" updatePending={pending} update={Rect(rect)} SysSetRedraw=0x{NativeMethods.GetProp(hwnd, "SysSetRedraw").ToInt64():X}");
        }
        private static string Rect(NativeRect r) => $"({r.Left},{r.Top},{r.Right},{r.Bottom})";
    }
}
