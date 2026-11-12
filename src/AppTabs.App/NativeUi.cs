using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AppTabs.App
{
    internal static class NativeUi
    {
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern IntPtr GetLastActivePopup(IntPtr window);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool UnregisterHotKey(IntPtr window, int id);

        internal const int WmNcCalcSize = 0x83, WmNcHitTest = 0x84, WmNcActivate = 0x86, WmNcLButtonDown = 0xA1;
        internal const int WmGetMinMaxInfo = 0x24, WmSysCommand = 0x112, WmEnterMenuLoop = 0x211, WmExitMenuLoop = 0x212;
        internal static System.Drawing.Point MessagePoint(IntPtr value)
        { long packed = value.ToInt64(); return new System.Drawing.Point((short)(packed & 0xffff), (short)((packed >> 16) & 0xffff)); }
        internal static IntPtr PointMessage(System.Drawing.Point point) => new IntPtr(unchecked((int)(((uint)point.Y & 0xffff) << 16 | ((uint)point.X & 0xffff))));
        [StructLayout(LayoutKind.Sequential)] internal struct NativeRect { internal int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] internal struct NcCalcSizeParams { internal NativeRect Rc0, Rc1, Rc2; internal IntPtr WindowPos; }
        [StructLayout(LayoutKind.Sequential)] internal struct NativePoint { internal int X, Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct MinMaxInfo { internal NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsZoomed(IntPtr window);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ReleaseCapture();
        [DllImport("user32.dll", EntryPoint = "SendMessageW")] internal static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", EntryPoint = "PostMessageW")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", EntryPoint = "DefWindowProcW")] internal static extern IntPtr DefWindowProc(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] internal static extern IntPtr GetSystemMenu(IntPtr window, [MarshalAs(UnmanagedType.Bool)] bool revert);
        [DllImport("user32.dll")] internal static extern uint EnableMenuItem(IntPtr menu, uint item, uint flags);
        [DllImport("user32.dll")] internal static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr window, IntPtr parameters);
    }

    internal sealed class GlobalHotkey : IDisposable
    {
        private readonly IntPtr host;
        private bool registered;
        public const int Id = 0x4154;
        public bool Registered => registered;
        public GlobalHotkey(IntPtr host) { this.host = host; }
        public void Apply(int choice)
        {
            Dispose();
            if (choice == 3) return;
            uint key = choice == 0 ? 0x20u : choice == 1 ? 0x41u : 0x7Bu;
            if (!NativeUi.RegisterHotKey(host, Id, 0x4003, key))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "全局快捷键已被占用，请在设置中更换。");
            registered = true;
        }
        public void Dispose()
        {
            if (registered) NativeUi.UnregisterHotKey(host, Id);
            registered = false;
        }
    }
}
