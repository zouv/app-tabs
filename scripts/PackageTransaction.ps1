# Shared package helpers. Every recursive move/delete stays inside this workspace.
function Assert-PackagePath([string]$path, [string]$workspace) {
    $absolute = [IO.Path]::GetFullPath($path)
    $base = [IO.Path]::GetFullPath($workspace).TrimEnd('\') + '\'
    if (-not $absolute.StartsWith($base, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Package target escapes workspace: $absolute"
    }
    return $absolute
}

function New-PackageZip([string]$source, [string]$zipPath, [string]$prefix) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    $archive = [IO.Compression.ZipFile]::Open($zipPath, 'Create')
    try {
        $base = [IO.Path]::GetFullPath($source).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        foreach ($file in Get-ChildItem -LiteralPath $source -Recurse -File) {
            $relative = $file.FullName.Substring($base.Length).Replace('\', '/')
            $entry = if ([string]::IsNullOrEmpty($prefix)) { $relative } else { "$prefix/$relative" }
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entry) | Out-Null
        }
    }
    finally { $archive.Dispose() }
}

function Test-PackageZip([string]$source, [string]$zipPath, [string]$prefix) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $base = [IO.Path]::GetFullPath($source).TrimEnd('\') + '\'
        $files = @(Get-ChildItem -LiteralPath $source -Recurse -File)
        if ($zip.Entries.Count -ne $files.Count) { throw "Zip entry count mismatch: $zipPath" }
        foreach ($file in $files) {
            $relative = $file.FullName.Substring($base.Length).Replace('\', '/')
            $name = if ([string]::IsNullOrEmpty($prefix)) { $relative } else { "$prefix/$relative" }
            $entry = $zip.GetEntry($name)
            if ($null -eq $entry -or $entry.Length -ne $file.Length) { throw "Zip entry missing/invalid: $name" }
            $input = $entry.Open()
            $disk = [IO.File]::OpenRead($file.FullName)
            $sha = [Security.Cryptography.SHA256]::Create()
            try {
                $actual = [Convert]::ToBase64String($sha.ComputeHash($input))
                $expected = [Convert]::ToBase64String($sha.ComputeHash($disk))
                if ($actual -ne $expected) { throw "Zip content mismatch: $name" }
            }
            finally { $sha.Dispose(); $input.Dispose(); $disk.Dispose() }
        }
    }
    finally { $zip.Dispose() }
}

function Publish-PackageSet([string]$workspace, [string]$stage, [string]$dist) {
    $backup = Assert-PackagePath (Join-Path $stage 'previous') $workspace
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    $saved = @()
    $promoted = @()
    try {
        foreach ($name in @('dev', 'release')) {
            $source = Assert-PackagePath (Join-Path $stage $name) $workspace
            $target = Assert-PackagePath (Join-Path $dist $name) $workspace
            $old = Assert-PackagePath (Join-Path $backup $name) $workspace
            if (Test-Path -LiteralPath $target) {
                Move-Item -LiteralPath $target -Destination $old
                $saved += $name
            }
            Move-Item -LiteralPath $source -Destination $target
            $promoted += $name
        }
    }
    catch {
        $failure = $_
        $rollback = @()
        foreach ($name in @('release', 'dev')) {
            $source = Assert-PackagePath (Join-Path $stage $name) $workspace
            $target = Assert-PackagePath (Join-Path $dist $name) $workspace
            $old = Assert-PackagePath (Join-Path $backup $name) $workspace
            try {
                if ($promoted -contains $name) { Move-Item -LiteralPath $target -Destination $source }
                if ($saved -contains $name) { Move-Item -LiteralPath $old -Destination $target }
            }
            catch { $rollback += $_.Exception.Message }
        }
        if ($rollback.Count -gt 0) { throw "Publish failed: $failure; rollback incomplete, retained at $stage : $($rollback -join '; ')" }
        throw $failure
    }
}
