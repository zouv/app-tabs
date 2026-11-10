using System;
using System.Diagnostics;
using System.IO;
using AppTabs.Core;

namespace AppTabs.App
{
    public interface IApplicationLauncher
    {
        void Launch(ApplicationFavorite favorite);
    }

    public sealed class ApplicationLauncher : IApplicationLauncher
    {
        private readonly Action<ProcessStartInfo> start;
        public ApplicationLauncher() : this(StartProcess) { }
        // An injected start operation allows tests to inspect the exact request without launching software.
        public ApplicationLauncher(Action<ProcessStartInfo> start)
        {
            this.start = start ?? throw new ArgumentNullException(nameof(start));
        }
        public void Launch(ApplicationFavorite favorite)
        {
            if (favorite == null) throw new ArgumentNullException(nameof(favorite));
            var request = favorite.Copy();
            request.Validate();
            var drive = new DriveInfo(Path.GetPathRoot(request.ImagePath)!);
            if (drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable &&
                drive.DriveType != DriveType.CDRom && drive.DriveType != DriveType.Ram)
                throw new InvalidOperationException("收藏只能启动本地磁盘程序，不支持网络映射盘。");
            if (!File.Exists(request.ImagePath)) throw new FileNotFoundException("收藏的程序不存在，请在管理收藏中更新路径。", request.ImagePath);
            start(new ProcessStartInfo
            {
                FileName = request.ImagePath,
                Arguments = "",
                UseShellExecute = false,
                Verb = "",
                WorkingDirectory = Path.GetDirectoryName(request.ImagePath)!
            });
        }
        private static void StartProcess(ProcessStartInfo info)
        {
            // Dispose the managed process wrapper; do not wait, discover windows, attach, or terminate.
            using (var process = Process.Start(info))
            {
                if (process == null) throw new InvalidOperationException("程序启动失败。");
            }
        }
    }
}
