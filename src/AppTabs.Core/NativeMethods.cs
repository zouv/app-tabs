using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace AppTabs.Core
{
    internal static class NativeMethods
    {
        internal const int GwlStyle = -16, GwlExStyle = -20;
        internal const long WsChild = 0x40000000L, WsPopup = 0x80000000L, WsCaption = 0x00C00000L;
        internal const long WsThickFrame = 0x00040000L, WsMinimizeBox = 0x00020000L, WsMaximizeBox = 0x00010000L;
        internal const long WsVisible = 0x10000000L;
        internal const long WsSysMenu = 0x00080000L, WsClipSiblings = 0x04000000L;
        internal const long WsExAppWindow = 0x00040000L, WsExTopmost = 0x00000008L;
        internal const uint SwpNoSize = 0x0001, SwpNoMove = 0x0002, SwpNoZOrder = 0x0004;
        internal const uint SwpNoActivate = 0x0010, SwpFrameChanged = 0x0020;
        internal const uint SwpShowWindow = 0x0040, SwpHideWindow = 0x0080;
        internal const uint GwOwner = 4;
        internal static readonly IntPtr HwndTop = IntPtr.Zero;
        internal delegate bool EnumWindowsCallback(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr lParam);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsHungAppWindow(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetClassName(IntPtr hwnd, StringBuilder text, int capacity);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int capacity);
        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint processId);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetProcessTimes(SafeProcessHandle process, out long created, out long exited, out long kernel, out long user);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder path, ref uint size);
        [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetFocus(IntPtr hwnd);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")]
        internal static extern IntPtr GetParent(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern IntPtr GetLastActivePopup(IntPtr hwnd);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ClientToScreen(IntPtr hwnd, ref NativePoint point);
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")]
        internal static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        internal static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        internal static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
        [DllImport("kernel32.dll")]
        internal static extern void SetLastError(uint error);
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetParent(IntPtr hwnd, IntPtr parent);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowPlacement(IntPtr hwnd, ref WindowPlacement placement);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPlacement(IntPtr hwnd, ref WindowPlacement placement);
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam,
            uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll")]
        internal static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AreDpiAwarenessContextsEqual(IntPtr first, IntPtr second);
        [DllImport("user32.dll")]
        internal static extern IntPtr GetThreadDpiAwarenessContext();
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ShowWindow(IntPtr hwnd, int command);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RedrawWindow(IntPtr hwnd, IntPtr updateRect, IntPtr region, uint flags);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumChildWindows(IntPtr hwnd, EnumWindowsCallback callback, IntPtr lParam);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetUpdateRect(IntPtr hwnd, out NativeRect rect, bool erase);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr GetProp(IntPtr hwnd, string name);
        [DllImport("dwmapi.dll")]
        internal static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
    }
}
