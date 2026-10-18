# Builds both apps into .\dist
#   .\build.ps1                  release builds (Windows needs the .NET 8 Desktop Runtime installed)
#   .\build.ps1 -SelfContained   Windows build that bundles .NET (what releases ship)
#   .\build.ps1 -Installer       also builds dist\OrbitSetup-<version>.exe (needs Inno Setup 6)
#   .\build.ps1 -Install         also installs the APK on the USB-connected phone
#
# Android release signing: android/keystore.properties or ORBIT_KEYSTORE* env vars (see docs/RELEASING.md).
param([switch]$Install, [switch]$SelfContained, [switch]$Installer)
# Native tools write progress to stderr; rely on exit codes instead of "Stop".
$ErrorActionPreference = "Continue"
$root = $PSScriptRoot
$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force $dist | Out-Null

& "$root\tools\fetch-thirdparty.ps1"
if (-not $?) { throw "Could not fetch third-party files" }

# Android Gradle Plugin needs JDK 17–21. Use a portable one if JAVA_HOME isn't set.
if (-not $env:JAVA_HOME) {
    $jdk = Get-ChildItem "$env:LOCALAPPDATA\OrbitDevTools\jdk" -Directory -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($jdk) { $env:JAVA_HOME = $jdk.FullName }
}

$version = ([xml](Get-Content "$root\windows\Orbit.Windows\Orbit.Windows.csproj")).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1

Write-Host "== Windows app $version" -ForegroundColor Cyan
$sc = if ($SelfContained -or $Installer) { "true" } else { "false" }
dotnet publish "$root\windows\Orbit.Windows" -c Release -r win-x64 --self-contained $sc `
    -p:PublishReadyToRun=true -o "$dist\windows"
if ($LASTEXITCODE) { throw "Windows build failed" }

Write-Host "== Android app" -ForegroundColor Cyan
Push-Location "$root\android"
try {
    .\gradlew.bat assembleRelease --console=plain
    if ($LASTEXITCODE) { throw "Android build failed" }
} finally { Pop-Location }
Copy-Item "$root\android\app\build\outputs\apk\release\app-release.apk" "$dist\Orbit.apk" -Force

if ($Installer) {
    $iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") |
        Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) { throw "Inno Setup 6 not found (winget install JRSoftware.InnoSetup)" }
    & $iscc "/DAppVersion=$version" "/DSourceDir=$dist\windows" "/O$dist" "$root\windows\installer\Orbit.iss"
    if ($LASTEXITCODE) { throw "Installer build failed" }
}

if ($Install) {
    $adb = "$dist\windows\adb\adb.exe"
    if (-not (Test-Path $adb)) { $adb = "adb" }
    & $adb install -r "$dist\Orbit.apk"
}

Write-Host ""
Write-Host "Done:" -ForegroundColor Green
Write-Host "  Windows: $dist\windows\Orbit.exe"
Write-Host "  Android: $dist\Orbit.apk"
if ($Installer) { Write-Host "  Installer: $dist\OrbitSetup-$version.exe" }
