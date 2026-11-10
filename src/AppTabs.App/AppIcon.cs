using System;
using System.Drawing;
using System.IO;

namespace AppTabs.App
{
    public static class AppIcon
    {
        private const string ResourceName = "AppTabs.App.app-tabs.ico";

        /// <summary>Loads the requested icon size. The caller owns and must dispose the returned icon.</summary>
        public static Icon Load(int size = 32)
        {
            if (size < 1 || size > 256)
                throw new ArgumentOutOfRangeException(nameof(size), "Icon size must be between 1 and 256 pixels.");

            using (Stream stream = typeof(AppIcon).Assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException("The embedded AppTabs icon is missing."))
            using (Stream? selectedFrame = size == 256 ? SelectLargestFrame(stream) : null)
            using (var icon = new Icon(selectedFrame ?? stream, size, size))
            {
                // Keep the stream alive through construction and cloning; no resource stream
                // or shared Icon escapes, so callers can dispose their copies independently.
                return (Icon)icon.Clone();
            }
        }

        private static MemoryStream SelectLargestFrame(Stream stream)
        {
            // Framework 4.8 treats a directory dimension byte of 0 as smaller than
            // 128 when selecting from a multi-frame ICO. Isolate the 256 px entry
            // in a single-frame container; no scaling or native handle ownership.
            using (var input = new MemoryStream())
            {
                stream.CopyTo(input);
                byte[] data = input.ToArray();
                if (data.Length < 6 || BitConverter.ToUInt16(data, 0) != 0 || BitConverter.ToUInt16(data, 2) != 1)
                    throw new InvalidDataException("Invalid embedded ICO header.");

                int count = BitConverter.ToUInt16(data, 4);
                if (data.Length < 6 + count * 16)
                    throw new InvalidDataException("Invalid embedded ICO directory.");

                for (int i = 0; i < count; i++)
                {
                    int entry = 6 + i * 16;
                    if (data[entry] != 0 || data[entry + 1] != 0)
                        continue;

                    uint length = BitConverter.ToUInt32(data, entry + 8);
                    uint offset = BitConverter.ToUInt32(data, entry + 12);
                    if (offset < 6 + count * 16 || length == 0 || (ulong)offset + length > (ulong)data.Length)
                        throw new InvalidDataException("Invalid embedded ICO frame bounds.");

                    var result = new MemoryStream();
                    try
                    {
                        result.Write(data, 0, 4);
                        result.WriteByte(1);
                        result.WriteByte(0);
                        result.Write(data, entry, 12);
                        byte[] payloadOffset = BitConverter.GetBytes(22u);
                        result.Write(payloadOffset, 0, payloadOffset.Length);
                        result.Write(data, (int)offset, (int)length);
                        result.Position = 0;
                        return result;
                    }
                    catch { result.Dispose(); throw; }
                }
                throw new InvalidDataException("The embedded ICO has no 256 pixel frame.");
            }
        }
    }
}
