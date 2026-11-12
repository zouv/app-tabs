using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;

namespace AppTabs.Core
{
    [DataContract]
    public sealed class RecentApp
    {
        [DataMember] public string ImagePath { get; set; } = "";
        [DataMember] public string ClassName { get; set; } = "";
        [DataMember] public string LastTitle { get; set; } = "";

        public RecentApp Copy() => new RecentApp { ImagePath = ImagePath, ClassName = ClassName, LastTitle = LastTitle };

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(ImagePath) || ImagePath.Length > 32767 || !Path.IsPathRooted(ImagePath) ||
                string.IsNullOrWhiteSpace(ClassName) || ClassName.Length > 256 ||
                LastTitle == null || LastTitle.Length > 1024)
                throw new InvalidDataException("最近应用记录含无效条目。");
        }
    }

    public static class RecentAppsPolicy
    {
        public const int MinimumLimit = 1, MaximumLimit = 20, DefaultLimit = 10;

        public static bool Matches(RecentApp app, WindowIdentity identity) =>
            string.Equals(app.ImagePath, identity.ImagePath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(app.ClassName, identity.ClassName, StringComparison.Ordinal);

        public static void Remember(List<RecentApp> apps, string imagePath, string className, string title, int limit)
        {
            if (apps == null) throw new ArgumentNullException(nameof(apps));
            if (string.IsNullOrWhiteSpace(imagePath) || string.IsNullOrWhiteSpace(className)) return;
            apps.RemoveAll(app => app != null &&
                string.Equals(app.ImagePath, imagePath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(app.ClassName, className, StringComparison.Ordinal));
            apps.Insert(0, new RecentApp { ImagePath = imagePath, ClassName = className, LastTitle = title ?? "" });
            Trim(apps, limit);
        }

        public static void Trim(List<RecentApp> apps, int limit)
        {
            if (apps == null) return;
            int capped = Math.Max(MinimumLimit, Math.Min(MaximumLimit, limit));
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var kept = new List<RecentApp>(capped);
            foreach (var app in apps)
            {
                if (app == null) continue;
                string key = app.ImagePath + "\n" + app.ClassName;
                if (!seen.Add(key)) continue;
                kept.Add(app);
                if (kept.Count == capped) break;
            }
            apps.Clear();
            apps.AddRange(kept);
        }
    }
}
