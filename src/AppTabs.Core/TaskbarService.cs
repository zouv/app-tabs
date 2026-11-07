using System;
using System.Runtime.InteropServices;

namespace AppTabs.Core
{
    public interface ITaskbarService
    {
        void Add(WindowIdentity identity);
        void Remove(WindowIdentity identity);
    }

    /// <summary>Use the documented Shell taskbar interface; never synthesize activation messages.</summary>
    public sealed class ShellTaskbarService : ITaskbarService
    {
        public void Add(WindowIdentity identity) => Update(identity, true);
        public void Remove(WindowIdentity identity) => Update(identity, false);

        private readonly Action<string>? trace;
        public ShellTaskbarService(Action<string>? trace = null) { this.trace = trace; }
        private void Update(WindowIdentity identity, bool add)
        {
            if (!NativeWindowApi.IsEligibleIdentity(identity)) return;
            object? instance = null;
            try
            {
                var type = Type.GetTypeFromCLSID(new Guid("56FDF344-FD6D-11D0-958A-006097C9A090"), true)!;
                instance = Activator.CreateInstance(type)!;
                var list = (ITaskbarList)instance;
                int hr = list.HrInit();
                Report(identity, "HrInit", hr);
                Marshal.ThrowExceptionForHR(hr);
                hr = add ? list.AddTab(identity.Hwnd) : list.DeleteTab(identity.Hwnd);
                Report(identity, add ? "AddTab" : "DeleteTab", hr);
                Marshal.ThrowExceptionForHR(hr);
            }
            finally
            {
                if (instance != null && Marshal.IsComObject(instance)) Marshal.FinalReleaseComObject(instance);
            }
        }

        private void Report(WindowIdentity identity, string method, int hr)
        {
            try { trace?.Invoke($"taskbar.COM HWND=0x{identity.Handle:X} method={method} HRESULT=0x{hr:X8} membership=not-verified"); }
            catch { /* Logging must not affect Shell operations. */ }
        }

        [ComImport, Guid("56FDF342-FD6D-11D0-958A-006097C9A090"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ITaskbarList
        {
            [PreserveSig] int HrInit();
            [PreserveSig] int AddTab(IntPtr hwnd);
            [PreserveSig] int DeleteTab(IntPtr hwnd);
            [PreserveSig] int ActivateTab(IntPtr hwnd);
            [PreserveSig] int SetActiveAlt(IntPtr hwnd);
        }
    }
}
