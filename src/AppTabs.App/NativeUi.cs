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
    }

    internal sealed class GlobalHotkey : IDisposable
    {
        private readonly IntPtr host;
        private bool registered;
        public const int Id = 0x4154;
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
