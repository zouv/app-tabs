using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace AppTabs.Core
{
    public enum AttachPreference { Auto, Grouped, Embedded }

    [DataContract]
    public sealed class SavedWindow
    {
        [DataMember] public string ImagePath { get; set; } = "";
        [DataMember] public string ClassName { get; set; } = "";
        [DataMember] public string Title { get; set; } = "";
        [DataMember] public string? CustomTitle { get; set; }
        [DataMember] public HostingMode Mode { get; set; }

        public SavedWindow Copy() => new SavedWindow
        {
            ImagePath = ImagePath, ClassName = ClassName, Title = Title, CustomTitle = CustomTitle, Mode = Mode
        };

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(ImagePath) || ImagePath.Length > 32767 || !Path.IsPathRooted(ImagePath) ||
                string.IsNullOrWhiteSpace(ClassName) || ClassName.Length > 256 || Title == null || Title.Length > 1024 ||
                !Enum.IsDefined(typeof(HostingMode), Mode) || (CustomTitle != null && CustomTitle.Length > 120))
                throw new InvalidDataException("保存的窗口组含无效条目。");
            if (CustomTitle != null)
            {
                try
                {
                    if (TabManagementPolicy.NormalizeTitle(CustomTitle) != CustomTitle)
                        throw new InvalidDataException("保存的自定义标签名称不能含首尾空格。");
                }
                catch (ArgumentException error)
                { throw new InvalidDataException("保存的自定义标签名称无效。", error); }
            }
        }
    }

    [DataContract]
    public sealed class AppState
    {
        [DataMember] public int Version { get; set; } = 1;
        [DataMember] public int X { get; set; } = 160;
        [DataMember] public int Y { get; set; } = 100;
        [DataMember] public int Width { get; set; } = 1120;
        [DataMember] public int Height { get; set; } = 760;
        [DataMember] public bool Maximized { get; set; }
        [DataMember] public AttachPreference Preference { get; set; }
        [DataMember] public int HotkeyChoice { get; set; }
        [DataMember] public bool HideToTray { get; set; }
        [DataMember] public int RecentJoinLimit { get; set; } = RecentAppsPolicy.DefaultLimit;
        [DataMember] public List<RecentApp> RecentApps { get; set; } = new List<RecentApp>();
        [DataMember] public bool? BorderlessGrouped { get; set; }
        [DataMember] public List<SavedWindow> Group { get; set; } = new List<SavedWindow>();
        [DataMember] public int ActiveIndex { get; set; }

        public AppState Copy() => new AppState
        {
            Version = Version, X = X, Y = Y, Width = Width, Height = Height, Maximized = Maximized,
            Preference = Preference, HotkeyChoice = HotkeyChoice, HideToTray = HideToTray,
            RecentJoinLimit = RecentJoinLimit, RecentApps = RecentApps.Select(app => app.Copy()).ToList(),
            BorderlessGrouped = BorderlessGrouped,
            Group = Group.Select(window => window.Copy()).ToList(), ActiveIndex = ActiveIndex
        };
    }

    public sealed class StateStore
    {
        public string FilePath { get; }
        public StateStore(string filePath) { FilePath = Path.GetFullPath(filePath); }
        public AppState Read()
        {
            if (!File.Exists(FilePath)) return new AppState();
            using (var stream = File.OpenRead(FilePath))
            {
                var result = (AppState?)new DataContractJsonSerializer(typeof(AppState)).ReadObject(stream);
                if (result == null || result.Version != 1 || result.Group == null || result.Group.Count > 100 ||
                    result.Width < 400 || result.Height < 300 || result.Width > 20000 || result.Height > 20000 ||
                    !Enum.IsDefined(typeof(AttachPreference), result.Preference) || result.HotkeyChoice < 0 || result.HotkeyChoice > 3)
                    throw new InvalidDataException("设置文件格式无效，原文件未覆盖。");
                // New fields may be absent in older settings files; DataContract leaves them at 0/null.
                if (result.RecentApps == null) result.RecentApps = new List<RecentApp>();
                if (result.RecentJoinLimit == 0) result.RecentJoinLimit = RecentAppsPolicy.DefaultLimit;
                if (result.RecentJoinLimit < RecentAppsPolicy.MinimumLimit || result.RecentJoinLimit > RecentAppsPolicy.MaximumLimit ||
                    result.RecentApps.Count > 100)
                    throw new InvalidDataException("最近应用设置无效，原文件未覆盖。");
                foreach (var app in result.RecentApps)
                {
                    if (app == null) throw new InvalidDataException("最近应用记录含无效条目。");
                    app.Validate();
                }
                RecentAppsPolicy.Trim(result.RecentApps, result.RecentJoinLimit);
                foreach (var item in result.Group)
                    if (item == null || string.IsNullOrWhiteSpace(item.ImagePath) || !Path.IsPathRooted(item.ImagePath) ||
                        string.IsNullOrWhiteSpace(item.ClassName) || item.Title == null || item.Title.Length > 1024 ||
                        !Enum.IsDefined(typeof(HostingMode), item.Mode) || (item.CustomTitle != null && item.CustomTitle.Length > 120))
                        throw new InvalidDataException("保存的窗口组含无效条目。");
                return result;
            }
        }
        public void Save(AppState state)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    new DataContractJsonSerializer(typeof(AppState)).WriteObject(stream, state);
                    stream.Flush(true);
                }
                if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null);
                else File.Move(temporary, FilePath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    public static class GroupMatching
    {
        // Explicit restore only. Ambiguous selectors never choose a window by recency/order.
        public static WindowIdentity? Match(SavedWindow saved, IEnumerable<WindowIdentity> candidates)
        {
            var matches = candidates.Where(w =>
                string.Equals(w.ImagePath, saved.ImagePath, StringComparison.OrdinalIgnoreCase) &&
                w.ClassName == saved.ClassName && w.Title == saved.Title).Take(2).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }
    }
}
