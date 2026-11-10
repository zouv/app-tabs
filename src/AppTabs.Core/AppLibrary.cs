using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace AppTabs.Core
{
    [DataContract]
    public sealed class ApplicationFavorite
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString("N");
        [DataMember] public string Name { get; set; } = "";
        [DataMember] public string ImagePath { get; set; } = "";

        public ApplicationFavorite Copy() => new ApplicationFavorite { Id = Id, Name = Name, ImagePath = ImagePath };
        public void Validate()
        {
            AppLibrary.ValidateId(Id); AppLibrary.ValidateName(Name);
            ValidateImagePath(ImagePath);
        }

        // A favorite is an explicit executable path, never a command line or shell target.
        // Do not require existence here: an uninstalled favorite must remain editable/removable.
        public static void ValidateImagePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Length > 4096 || path.Length < 7 ||
                !char.IsLetter(path[0]) || path[1] != ':' || path[2] != '\\' ||
                path.IndexOf(':', 2) >= 0 || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0 ||
                path.IndexOfAny(new[] { '/', '*', '?', '"', '<', '>', '|', '\r', '\n' }) >= 0 ||
                path.Any(char.IsControl) || !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("收藏只接受本地磁盘的绝对 .exe 路径，不支持参数、网络路径、脚本或 URL。");
            string full;
            try { full = Path.GetFullPath(path); }
            catch (Exception error) when (error is ArgumentException || error is NotSupportedException || error is PathTooLongException)
            { throw new InvalidDataException("收藏的程序路径无效。", error); }
            if (!string.Equals(full, path, StringComparison.OrdinalIgnoreCase) ||
                path.Split('\\').Skip(1).Any(part => part.Length == 0 || part.EndsWith(" ", StringComparison.Ordinal) || part.EndsWith(".", StringComparison.Ordinal)))
                throw new InvalidDataException("收藏的程序路径必须是完整、规范的本地 .exe 路径。");
        }
    }

    [DataContract]
    public sealed class SavedWindowGroup
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString("N");
        [DataMember] public string Name { get; set; } = "";
        [DataMember] public List<SavedWindow> Windows { get; set; } = new List<SavedWindow>();
        [DataMember] public int ActiveIndex { get; set; }

        public SavedWindowGroup Copy() => new SavedWindowGroup
        {
            Id = Id, Name = Name, Windows = Windows.Select(window => window.Copy()).ToList(), ActiveIndex = ActiveIndex
        };
        public void Validate()
        {
            AppLibrary.ValidateId(Id); AppLibrary.ValidateName(Name);
            if (Windows == null || Windows.Count > AppLibrary.MaxWindowsPerGroup || ActiveIndex < 0 ||
                (Windows.Count == 0 ? ActiveIndex != 0 : ActiveIndex >= Windows.Count))
                throw new InvalidDataException("标签组的窗口数量或活动索引无效。");
            foreach (var window in Windows)
            {
                if (window == null) throw new InvalidDataException("标签组包含空窗口。");
                window.Validate();
            }
        }
    }

    [DataContract]
    public sealed class AppLibrary
    {
        public const int MaxFavorites = 100;
        public const int MaxGroups = 50;
        public const int MaxWindowsPerGroup = 100;
        public const long MaxFileBytes = 8 * 1024 * 1024;
        [DataMember] public int Version { get; set; } = 1;
        [DataMember] public List<ApplicationFavorite> Favorites { get; set; } = new List<ApplicationFavorite>();
        [DataMember] public List<SavedWindowGroup> Groups { get; set; } = new List<SavedWindowGroup>();

        public AppLibrary Copy() => new AppLibrary
        {
            Version = Version, Favorites = Favorites.Select(item => item.Copy()).ToList(),
            Groups = Groups.Select(item => item.Copy()).ToList()
        };
        public void Validate()
        {
            if (Version != 1 || Favorites == null || Groups == null || Favorites.Count > MaxFavorites || Groups.Count > MaxGroups)
                throw new InvalidDataException("应用库版本或条目数量无效，原文件未覆盖。");
            var ids = new HashSet<Guid>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var favorite in Favorites)
            {
                if (favorite == null) throw new InvalidDataException("应用库包含空收藏。");
                favorite.Validate();
                if (!ids.Add(Guid.Parse(favorite.Id)) || !names.Add(favorite.Name))
                    throw new InvalidDataException("收藏的标识或名称重复。");
            }
            names.Clear();
            foreach (var group in Groups)
            {
                if (group == null) throw new InvalidDataException("应用库包含空标签组。");
                group.Validate();
                if (!ids.Add(Guid.Parse(group.Id)) || !names.Add(group.Name))
                    throw new InvalidDataException("标签组的标识或名称重复。");
            }
        }
        public static void ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 120 || name != name.Trim() || name.Any(char.IsControl))
                throw new InvalidDataException("名称需为 1–120 个字符，不能含控制字符或首尾空格。");
        }
        internal static void ValidateId(string id)
        {
            if (!Guid.TryParse(id, out var value) || value == Guid.Empty)
                throw new InvalidDataException("应用库条目标识无效。");
        }
    }

    public interface IAppLibraryStore
    {
        AppLibrary Read();
        void Save(AppLibrary library);
    }

    // This file is independent of settings.json and recovery journals.
    public sealed class AppLibraryStore : IAppLibraryStore
    {
        public string FilePath { get; }
        public AppLibraryStore(string filePath) { FilePath = Path.GetFullPath(filePath); }
        public AppLibrary Read()
        {
            FileStream stream;
            try { stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read); }
            catch (FileNotFoundException) { return new AppLibrary(); }
            catch (DirectoryNotFoundException) { return new AppLibrary(); }
            using (stream)
            {
                if (stream.Length == 0 || stream.Length > AppLibrary.MaxFileBytes)
                    throw new InvalidDataException("应用库为空或超过 8 MB，原文件未覆盖。");
                var result = (AppLibrary?)new DataContractJsonSerializer(typeof(AppLibrary)).ReadObject(stream);
                if (result == null) throw new InvalidDataException("应用库格式无效，原文件未覆盖。");
                result.Validate();
                return result;
            }
        }
        public void Save(AppLibrary library)
        {
            if (library == null) throw new ArgumentNullException(nameof(library));
            library.Validate();
            var snapshot = library.Copy();
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    new DataContractJsonSerializer(typeof(AppLibrary)).WriteObject(new LimitedWriteStream(stream), snapshot);
                    stream.Flush(true);
                }
                if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null);
                else File.Move(temporary, FilePath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private sealed class LimitedWriteStream : Stream
        {
            private readonly Stream inner;
            public LimitedWriteStream(Stream inner) { this.inner = inner; }
            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => inner.Length;
            public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
            public override void Flush() => inner.Flush();
            public override void Write(byte[] buffer, int offset, int count)
            {
                if (inner.Position + count > AppLibrary.MaxFileBytes) throw new InvalidDataException("应用库超过 8 MB，原文件未覆盖。");
                inner.Write(buffer, offset, count);
            }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
        }
    }
}
