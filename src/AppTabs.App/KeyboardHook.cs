using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AppTabs.App
{
    public enum TabAction { Next, Previous, Detach, Search, First, Second, Third, Fourth, Fifth, Sixth, Seventh, Eighth, Ninth }

    public sealed class KeyboardHook : IDisposable
    {
        private delegate IntPtr HookCallback(int code, IntPtr message, IntPtr data);
        private readonly HookCallback callback;
        private readonly Action<TabAction> dispatch;
        private IntPtr hook;
        private IntPtr host, target;
        private bool enabled;
        public KeyboardHook(Action<TabAction> dispatch)
        {
            this.dispatch = dispatch;
            callback = OnKey;
            hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        public void SetScope(IntPtr host, IntPtr target, bool enabled)
        { this.host = host; this.target = target; this.enabled = enabled; }
        public static TabAction? Resolve(Keys key, bool control, bool shift, bool alt)
        {
            if (!control || alt) return null;
            if (key == Keys.Tab) return shift ? TabAction.Previous : TabAction.Next;
            if (!shift && key == Keys.PageDown) return TabAction.Next;
            if (!shift && key == Keys.PageUp) return TabAction.Previous;
            if (!shift && key == Keys.W) return TabAction.Detach;
            if (!shift && key == Keys.F) return TabAction.Search;
            if (!shift && key >= Keys.D1 && key <= Keys.D9) return (TabAction)((int)TabAction.First + (int)key - (int)Keys.D1);
            return null;
        }
        private IntPtr OnKey(int code, IntPtr message, IntPtr data)
        {
            try
            {
                if (code >= 0 && enabled && (message.ToInt64() == 0x100 || message.ToInt64() == 0x104))
                {
                    var item = Marshal.PtrToStructure<KeyData>(data);
                    IntPtr foreground = NativeUi.GetForegroundWindow();
                    IntPtr root = NativeUi.GetAncestor(foreground, 2);
                    if ((item.Flags & 0x10) == 0 && (root == host || foreground == target))
                    {
                        var action = Resolve((Keys)item.Key, (GetAsyncKeyState(0x11) & 0x8000) != 0,
                            (GetAsyncKeyState(0x10) & 0x8000) != 0, (GetAsyncKeyState(0x12) & 0x8000) != 0);
                        if (action != null) { dispatch(action.Value); return new IntPtr(1); }
                    }
                }
            }
            catch { /* Never propagate exceptions from a native hook. */ }
            return CallNextHookEx(hook, code, message, data);
        }
        public void Dispose() { if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
        [StructLayout(LayoutKind.Sequential)] private struct KeyData { public uint Key, Scan, Flags, Time; public UIntPtr Extra; }
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int type, HookCallback callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? module);
    }
}
