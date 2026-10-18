# Downloads the third-party binaries the Windows app ships with, and verifies them.
# They are not committed to the repository (see THIRD_PARTY_NOTICES.md).
#
#   scrcpy 5.0.1 (Apache-2.0): scrcpy-server, plus the FFmpeg DLLs (LGPL build) and the adb
#   binaries from the same official release.
#
# Usage: .\tools\fetch-thirdparty.ps1 [-Force]
param([switch]$Force)
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$root = Split-Path $PSScriptRoot -Parent
$tp = Join-Path $root "windows\Orbit.Windows\ThirdParty"

$scrcpyVersion = "5.0.1"
$scrcpyUrl = "https://github.com/Genymobile/scrcpy/releases/download/v$scrcpyVersion/scrcpy-win64-v$scrcpyVersion.zip"
$scrcpySha256 = "B12A2C4EE8BE317422451FC7DCF8EE20A71B5EA7EF9AD73DDD825A25316227A5"

$scrcpyFiles = "scrcpy-server", "avcodec-63.dll", "avformat-63.dll", "avutil-61.dll", "swresample-7.dll", "LICENSE.txt"
$adbFiles = "adb.exe", "AdbWinApi.dll", "AdbWinUsbApi.dll"

$scrcpyDir = Join-Path $tp "scrcpy"
$adbDir = Join-Path $tp "platform-tools"
$have = ($scrcpyFiles | ForEach-Object { Test-Path (Join-Path $scrcpyDir $_) }) + ($adbFiles | ForEach-Object { Test-Path (Join-Path $adbDir $_) })
if (-not $Force -and $have -notcontains $false) {
    Write-Host "Third-party files already present (use -Force to re-download)."
    return
}

$tmp = Join-Path ([IO.Path]::GetTempPath()) ("orbit-thirdparty-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $tmp | Out-Null
try {
    $zip = Join-Path $tmp "scrcpy.zip"
    Write-Host "Downloading scrcpy $scrcpyVersion..."
    Invoke-WebRequest $scrcpyUrl -OutFile $zip
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash
    if ($hash -ne $scrcpySha256) { throw "scrcpy download checksum mismatch: $hash" }

    Expand-Archive $zip (Join-Path $tmp "x")
    $src = Join-Path $tmp "x\scrcpy-win64-v$scrcpyVersion"
    New-Item -ItemType Directory -Force $scrcpyDir, $adbDir | Out-Null
    foreach ($f in $scrcpyFiles) { Copy-Item (Join-Path $src $f) $scrcpyDir -Force }
    foreach ($f in $adbFiles) { Copy-Item (Join-Path $src $f) $adbDir -Force }
    Write-Host "OK: $scrcpyDir"
    Write-Host "OK: $adbDir"
}
finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
