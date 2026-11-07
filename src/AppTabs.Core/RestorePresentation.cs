using System;

namespace AppTabs.Core
{
    /// <summary>Keep structural restoration hidden. Never restore WS_VISIBLE via SetWindowLongPtr.</summary>
    public static class RestorePresentation
    {
        public static long HiddenStyle(long savedStyle) => savedStyle & ~0x10000000L;
        public static WindowPlacement Placement(WindowPlacement saved, bool wasVisible)
        {
            // Retain workspace coordinates and flags; only substitute non-activating show commands
            // where Windows provides one. Maximised placement retains SW_SHOWMAXIMIZED semantics.
            if (!wasVisible) saved.ShowCommand = 0; // SW_HIDE
            else if (saved.ShowCommand == 2 || saved.ShowCommand == 6 || saved.ShowCommand == 7 || saved.ShowCommand == 11)
                saved.ShowCommand = 7; // SW_SHOWMINNOACTIVE
            else if (saved.ShowCommand != 3) saved.ShowCommand = 4; // SW_SHOWNOACTIVATE
            return saved;
        }
    }

    public interface IWindowRepaintService
    {
        void Request(WindowIdentity identity);
    }

    public sealed class WindowRepaintService : IWindowRepaintService
    {
        public void Request(WindowIdentity identity)
        {
            // RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN | RDW_FRAME.
            // Deliberately omit RDW_UPDATENOW/ERASENOW: do not synchronously execute foreign paints.
            if (!NativeMethods.RedrawWindow(identity.Hwnd, IntPtr.Zero, IntPtr.Zero, 0x485))
                throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error(), "RedrawWindow(request)");
        }
    }
}
