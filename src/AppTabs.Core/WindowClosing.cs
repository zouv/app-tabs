using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AppTabs.Core
{
    public interface IWindowCloser { void RequestClose(WindowIdentity identity); }
    /// <summary>Close only the selected, identity-checked HWND. Never terminate the process.</summary>
    public sealed class NativeWindowCloser : IWindowCloser
    {
        public void RequestClose(WindowIdentity identity)
        {
            if (!NativeWindowApi.IsEligibleIdentity(identity) || !new NativeWindowApi().Matches(identity))
                throw new InvalidOperationException("目标窗口身份无法确认，未发送关闭。");
            if (!PostMessage(identity.Hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
    }
}
