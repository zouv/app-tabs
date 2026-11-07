using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace AppTabs.Core
{
    public static class WindowEligibility
    {
        public static bool IsSystemWindow(string className, string imagePath)
        {
            foreach (string blocked in new[] { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "#32770", "DV2ControlHost", "MultitaskingViewFrame" })
                if (string.Equals(className, blocked, StringComparison.OrdinalIgnoreCase)) return true;
            string image = Path.GetFileNameWithoutExtension(imagePath);
            foreach (string blocked in new[] { "dwm", "winlogon", "csrss", "lsass", "services", "LogonUI", "LockApp", "StartMenuExperienceHost", "SearchApp", "SearchHost", "ShellExperienceHost", "AppTabs" })
                if (string.Equals(image, blocked, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static bool IsElevated(int processId)
        {
            using (var process = NativeMethods.OpenProcess(0x1000, false, (uint)processId))
            {
                if (process.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (!OpenProcessToken(process.DangerousGetHandle(), 8, out IntPtr token))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                try
                {
                    if (!GetTokenInformation(token, 20, out int elevated, sizeof(int), out _))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    return elevated != 0;
                }
                finally { CloseHandle(token); }
            }
        }

        [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetTokenInformation(IntPtr token, int kind, out int information, int length, out int returned);
        [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
    }
}
