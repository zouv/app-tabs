$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class AppTabsIconHandle { [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr handle); }'
$taskBitmap = New-Object Drawing.Bitmap 64,64
$taskGraphics = [Drawing.Graphics]::FromImage($taskBitmap)
$taskGraphics.Clear([Drawing.Color]::Transparent)
$taskBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(79,70,229))
$taskGraphics.FillRectangle($taskBrush,4,12,56,46)
$taskGraphics.FillRectangle($taskBrush,4,5,23,12)
$taskWhite = New-Object Drawing.SolidBrush ([Drawing.Color]::White)
$taskGraphics.FillRectangle($taskWhite,13,24,17,24)
$taskGraphics.FillRectangle($taskWhite,35,24,16,24)
$taskHandle = $taskBitmap.GetHicon()
$taskIcon = [Drawing.Icon]::FromHandle($taskHandle)
$taskStream = [IO.File]::Create((Join-Path (Split-Path -Parent $PSScriptRoot) 'assets\app-tabs.ico'))
try { $taskIcon.Save($taskStream) }
finally { $taskStream.Dispose(); $taskIcon.Dispose(); [AppTabsIconHandle]::DestroyIcon($taskHandle) | Out-Null; $taskBrush.Dispose(); $taskWhite.Dispose(); $taskGraphics.Dispose(); $taskBitmap.Dispose() }
