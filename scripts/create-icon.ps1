param(
    [string]$AssetDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'assets'),
    [string]$PreviewDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# A single set of 64-unit paths drives both the SVG and the GDI+ rasterizer.
# No native icon handles, fonts, external tools, or machine-dependent artwork.
$source = @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ICON_NAMESPACE
{
    public static class Artwork
    {
        private static readonly string[,] Shapes = {
            // Rear application window: complete rectangular title bar and content.
            { "#244DA8", "M20 6 L56 6 C58.21 6 60 7.79 60 10 L60 40 C60 42.21 58.21 44 56 44 L20 44 C17.79 44 16 42.21 16 40 L16 10 C16 7.79 17.79 6 20 6 Z" },
            { "#80B8FF", "M22 10 L36 10 L36 12 L22 12 Z" },
            { "#DBEAFE", "M19 16 L57 16 L57 39 C57 40.1 56.1 41 55 41 L21 41 C19.9 41 19 40.1 19 39 Z" },
            // Middle application window.
            { "#3B82F6", "M14 13 L50 13 C52.21 13 54 14.79 54 17 L54 46 C54 48.21 52.21 50 50 50 L14 50 C11.79 50 10 48.21 10 46 L10 17 C10 14.79 11.79 13 14 13 Z" },
            { "#DBEAFE", "M16 17 L31 17 L31 19 L16 19 Z" },
            { "#EFF6FF", "M13 23 L51 23 L51 45 C51 46.1 50.1 47 49 47 L15 47 C13.9 47 13 46.1 13 45 Z" },
            // Foreground window remains rectangular, not a folder-shaped silhouette.
            { "#165DCA", "M8 21 L46 21 C48.21 21 50 22.79 50 25 L50 54 C50 56.21 48.21 58 46 58 L8 58 C5.79 58 4 56.21 4 54 L4 25 C4 22.79 5.79 21 8 21 Z" },
            { "#F8FBFF", "M12 24 L26 24 C27.66 24 29 25.34 29 27 L29 34 L9 34 L9 27 C9 25.34 10.34 24 12 24 Z" },
            { "#165DCA", "M13 28 L25 28 L25 30 L13 30 Z" },
            { "#80B8FF", "M33 27 L43 27 L43 30 L33 30 Z" },
            { "#F8FBFF", "M7 34 L47 34 L47 53 C47 54.1 46.1 55 45 55 L9 55 C7.9 55 7 54.1 7 53 Z" },
            // A compact application tile and two content lines stay legible at 16 px.
            { "#3B82F6", "M12 40 L19 40 C20.1 40 21 40.9 21 42 L21 49 C21 50.1 20.1 51 19 51 L12 51 C10.9 51 10 50.1 10 49 L10 42 C10 40.9 10.9 40 12 40 Z" },
            { "#165DCA", "M26 40 L43 40 L43 43 L26 43 Z" },
            { "#80B8FF", "M26 47 L38 47 L38 50 L26 50 Z" }
        };

        // Only the explicit absolute M/L/C/Z commands used above are accepted.
        private static GraphicsPath MakePath(string data)
        {
            MatchCollection tokens = Regex.Matches(data, @"[MLCZ]|-?\d+(?:\.\d+)?");
            GraphicsPath path = new GraphicsPath();
            PointF current = new PointF();
            int i = 0;
            try
            {
                while (i < tokens.Count)
                {
                    string command = tokens[i++].Value;
                    if (command == "Z") { path.CloseFigure(); continue; }
                    int count = command == "C" ? 6 : 2;
                    float[] n = new float[count];
                    for (int j = 0; j < count; j++)
                        n[j] = float.Parse(tokens[i++].Value, CultureInfo.InvariantCulture);
                    PointF end = new PointF(n[count - 2], n[count - 1]);
                    if (command == "M") path.StartFigure();
                    else if (command == "L") path.AddLine(current, end);
                    else if (command == "C") path.AddBezier(current, new PointF(n[0], n[1]), new PointF(n[2], n[3]), end);
                    else throw new InvalidOperationException("Unsupported path command.");
                    current = end;
                }
                return path;
            }
            catch { path.Dispose(); throw; }
        }

        public static Bitmap Render(int size)
        {
            const int supersampling = 4;
            using (Bitmap large = new Bitmap(size * supersampling, size * supersampling, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(large))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.ScaleTransform(size * supersampling / 64f, size * supersampling / 64f);
                    for (int i = 0; i < Shapes.GetLength(0); i++)
                    using (GraphicsPath path = MakePath(Shapes[i, 1]))
                    using (Brush brush = new SolidBrush(ColorTranslator.FromHtml(Shapes[i, 0])))
                        g.FillPath(brush, path);
                }
                Bitmap result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
                try
                {
                    using (Graphics g = Graphics.FromImage(result))
                    using (ImageAttributes attributes = new ImageAttributes())
                    {
                        g.Clear(Color.Transparent);
                        g.CompositingMode = CompositingMode.SourceCopy;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        attributes.SetWrapMode(WrapMode.TileFlipXY);
                        g.DrawImage(large, new Rectangle(0, 0, size, size), 0, 0, large.Width, large.Height, GraphicsUnit.Pixel, attributes);
                    }
                    return result;
                }
                catch { result.Dispose(); throw; }
            }
        }

        public static byte[] EncodeIconFrame(Bitmap bitmap)
        {
            // .NET Framework Icon.ToBitmap assumes a DIB payload. Use uncompressed
            // BGRA + an AND mask rather than PNG frames so all consumers agree.
            int size = bitmap.Width;
            int maskStride = ((size + 31) / 32) * 4;
            byte[] mask = new byte[maskStride * size];
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(40); // BITMAPINFOHEADER
                writer.Write(size);
                writer.Write(size * 2); // XOR bitmap and AND mask combined height
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write(0); // BI_RGB
                writer.Write(size * size * 4 + mask.Length);
                writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
                for (int row = 0; row < size; row++)
                for (int x = 0; x < size; x++)
                {
                    Color pixel = bitmap.GetPixel(x, size - 1 - row);
                    writer.Write(pixel.B); writer.Write(pixel.G); writer.Write(pixel.R); writer.Write(pixel.A);
                    if (pixel.A == 0) mask[row * maskStride + x / 8] |= (byte)(0x80 >> (x % 8));
                }
                writer.Write(mask);
                return stream.ToArray();
            }
        }

        public static void SaveSvg(string file)
        {
            StringBuilder svg = new StringBuilder();
            svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 64 64\">\n");
            svg.Append("  <title>AppTabs — stacked application windows with an active tab</title>\n");
            for (int i = 0; i < Shapes.GetLength(0); i++)
                svg.Append("  <path fill=\"").Append(Shapes[i, 0]).Append("\" d=\"").Append(Shapes[i, 1]).Append("\"/>\n");
            svg.Append("</svg>\n");
            File.WriteAllText(file, svg.ToString(), new UTF8Encoding(false));
        }

        public static void SaveContactSheet(string file, int[] sizes)
        {
            const int cell = 288;
            const int rowHeight = 316;
            using (Bitmap sheet = new Bitmap(cell * sizes.Length, rowHeight * 2, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(sheet))
            using (Font font = new Font(FontFamily.GenericSansSerif, 12, FontStyle.Regular, GraphicsUnit.Pixel))
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                for (int row = 0; row < 2; row++)
                using (Brush background = new SolidBrush(ColorTranslator.FromHtml(row == 0 ? "#F3F6FB" : "#111827")))
                using (Brush text = new SolidBrush(ColorTranslator.FromHtml(row == 0 ? "#244DA8" : "#DBEAFE")))
                {
                    g.FillRectangle(background, 0, row * rowHeight, sheet.Width, rowHeight);
                    for (int column = 0; column < sizes.Length; column++)
                    using (Bitmap icon = Render(sizes[column]))
                    {
                        int x = column * cell + (cell - icon.Width) / 2;
                        int y = row * rowHeight + (288 - icon.Height) / 2;
                        // Show the actual pixel size, without enlarging small previews.
                        g.DrawImageUnscaled(icon, x, y);
                        g.DrawString(sizes[column].ToString(CultureInfo.InvariantCulture) + " px / " + (row == 0 ? "light" : "dark"), font, text,
                            new RectangleF(column * cell, row * rowHeight + 288, cell, 24), format);
                    }
                }
                sheet.Save(file, ImageFormat.Png);
            }
        }
    }
}
'@

# Content-addressed type name also permits rerunning an edited script in one session.
$sha = [Security.Cryptography.SHA256]::Create()
try { $hash = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($source))).Replace('-', '') }
finally { $sha.Dispose() }
$namespace = 'AppTabsIcon_' + $hash.Substring(0, 16)
$typeName = $namespace + '.Artwork'
if (-not ($typeName -as [type])) {
    Add-Type -TypeDefinition $source.Replace('ICON_NAMESPACE', $namespace) -ReferencedAssemblies System.Drawing
}
$artwork = $typeName -as [type]

[IO.Directory]::CreateDirectory($AssetDirectory) | Out-Null
[IO.Directory]::CreateDirectory($PreviewDirectory) | Out-Null
$sizes = [int[]]@(16, 24, 32, 48, 64, 128, 256)
$frames = New-Object 'Collections.Generic.List[byte[]]'
foreach ($size in $sizes) {
    $bitmap = $artwork::Render($size)
    $memory = New-Object IO.MemoryStream
    try {
        $bitmap.Save($memory, [Drawing.Imaging.ImageFormat]::Png)
        $bytes = $memory.ToArray()
        $frames.Add($artwork::EncodeIconFrame($bitmap))
        [IO.File]::WriteAllBytes((Join-Path $PreviewDirectory "icon-preview-$size.png"), $bytes)
    }
    finally { $memory.Dispose(); $bitmap.Dispose() }
}

# ICONDIR + ICONDIRENTRY records, followed by seven 32bpp BGRA DIB frames.
$icoPath = Join-Path $AssetDirectory 'app-tabs.ico'
$stream = [IO.File]::Create($icoPath)
$writer = New-Object IO.BinaryWriter $stream
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Length)
    $offset = 6 + 16 * $sizes.Length
    for ($i = 0; $i -lt $sizes.Length; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length)
        $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
}
finally { $writer.Dispose(); $stream.Dispose() }

$artwork::SaveSvg((Join-Path $AssetDirectory 'app-tabs.svg'))
$artwork::SaveContactSheet((Join-Path $PreviewDirectory 'icon-preview.png'), $sizes)

# Verify the actual file, not just the arrays used to construct it.
$ico = [IO.File]::ReadAllBytes($icoPath)
if ([BitConverter]::ToUInt16($ico, 0) -ne 0 -or [BitConverter]::ToUInt16($ico, 2) -ne 1 -or [BitConverter]::ToUInt16($ico, 4) -ne $sizes.Length) {
    throw 'Invalid ICO container header.'
}
$expectedOffset = 6 + 16 * $sizes.Length
for ($i = 0; $i -lt $sizes.Length; $i++) {
    $entry = 6 + $i * 16
    $width = if ($ico[$entry] -eq 0) { 256 } else { [int]$ico[$entry] }
    $height = if ($ico[$entry + 1] -eq 0) { 256 } else { [int]$ico[$entry + 1] }
    $length = [BitConverter]::ToUInt32($ico, $entry + 8)
    $offset = [BitConverter]::ToUInt32($ico, $entry + 12)
    if ($width -ne $sizes[$i] -or $height -ne $sizes[$i] -or $ico[$entry + 2] -ne 0 -or $ico[$entry + 3] -ne 0 -or
        [BitConverter]::ToUInt16($ico, $entry + 4) -ne 1 -or [BitConverter]::ToUInt16($ico, $entry + 6) -ne 32 -or
        $offset -ne $expectedOffset -or $length -lt 33 -or ([uint64]$offset + $length) -gt $ico.Length) {
        throw "Invalid ICO directory entry for $($sizes[$i]) px."
    }
    $maskStride = [int]([Math]::Ceiling($width / 32.0) * 4)
    $pixelBytes = $width * $height * 4
    if ($length -ne (40 + $pixelBytes + $maskStride * $height) -or
        [BitConverter]::ToUInt32($ico, $offset) -ne 40 -or [BitConverter]::ToInt32($ico, $offset + 4) -ne $width -or
        [BitConverter]::ToInt32($ico, $offset + 8) -ne ($height * 2) -or
        [BitConverter]::ToUInt16($ico, $offset + 12) -ne 1 -or [BitConverter]::ToUInt16($ico, $offset + 14) -ne 32 -or
        [BitConverter]::ToUInt32($ico, $offset + 16) -ne 0 -or [BitConverter]::ToUInt32($ico, $offset + 20) -ne ($pixelBytes + $maskStride * $height)) {
        throw "Invalid 32bpp DIB frame header for $($sizes[$i]) px."
    }
    $transparent = 0
    $opaque = 0
    $partial = 0
    for ($row = 0; $row -lt $height; $row++) {
        for ($x = 0; $x -lt $width; $x++) {
            $alpha = $ico[$offset + 40 + ($row * $width + $x) * 4 + 3]
            $maskByte = $ico[$offset + 40 + $pixelBytes + $row * $maskStride + [int][Math]::Floor($x / 8.0)]
            $masked = ($maskByte -band (128 -shr ($x % 8))) -ne 0
            if ($masked -ne ($alpha -eq 0)) { throw "DIB alpha/AND mask mismatch at $width px." }
            if ($alpha -eq 0) { $transparent++ } elseif ($alpha -eq 255) { $opaque++ } else { $partial++ }
        }
    }
    if ($transparent -eq 0 -or $opaque -eq 0 -or $partial -eq 0) { throw "Missing transparent, opaque, or antialiased pixels at $width px." }
    Write-Output ("Verified {0}x{0}: planes=1, bpp=32, BGRA DIB + AND mask, offset={1}, bytes={2}, alpha=0/255/partial:{3}/{4}/{5}" -f $width, $offset, $length, $transparent, $opaque, $partial)
    $expectedOffset += $length
}
if ($expectedOffset -ne $ico.Length) { throw 'Unexpected trailing ICO data.' }
Write-Output "Generated $icoPath and matching SVG; previews: $PreviewDirectory"
