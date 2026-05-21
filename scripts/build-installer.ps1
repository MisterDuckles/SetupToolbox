<#
  build-installer.ps1 — bouwt de SetupToolbox installer (setup.exe) end-to-end.

   1. dotnet publish -c Release (win-x64, self-contained folder)
   2. leest <Version> uit de csproj
   3. compileert installer\SetupToolbox.iss met ISCC (Inno Setup 6)

  Output: installer\Output\SetupToolbox-Setup-v<versie>.exe

  Gebruik:  pwsh scripts\build-installer.ps1            # volledige build
            pwsh scripts\build-installer.ps1 -SkipPublish   # alleen ISCC (hergebruik publish)
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [switch]$SkipPublish
)
$ErrorActionPreference = "Stop"

$repo       = Split-Path $PSScriptRoot -Parent
$csproj     = Join-Path $repo "src\SetupToolbox\SetupToolbox.csproj"
$iss        = Join-Path $repo "installer\SetupToolbox.iss"
$publishDir = Join-Path $repo "src\SetupToolbox\bin\$Configuration\net10.0-windows10.0.26100.0\win-x64\publish"

# --- Versie uit de csproj (single source of truth) ---
[xml]$xml = Get-Content $csproj
$version = $xml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "Kon <Version> niet uit de csproj lezen." }
Write-Host "Versie: $version" -ForegroundColor Cyan

# --- 1. Publish (self-contained folder, geen single-file) ---
if (-not $SkipPublish) {
    Write-Host "[1/2] dotnet publish ($Configuration, win-x64, self-contained)..." -ForegroundColor Yellow
    dotnet publish $csproj -c $Configuration -r win-x64 -p:Platform=x64 `
        -p:SelfContained=true -p:WindowsAppSDKSelfContained=true -p:PublishSingleFile=False
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish faalde (exit $LASTEXITCODE)." }
}
if (-not (Test-Path (Join-Path $publishDir "SetupToolbox.exe"))) {
    throw "Publish-output niet gevonden in $publishDir (draai zonder -SkipPublish)."
}

# --- 2. ISCC (Inno Setup compiler) ---
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "ISCC.exe (Inno Setup 6) niet gevonden. Installeer: winget install JRSoftware.InnoSetup" }

Write-Host "[2/2] Compileren met ISCC..." -ForegroundColor Yellow
& $iscc "/DPublishDir=$publishDir" "/DAppVersion=$version" $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC faalde (exit $LASTEXITCODE)." }

$setup = Join-Path $repo "installer\Output\SetupToolbox-v$version.exe"
Write-Host ""
if (Test-Path $setup) {
    Write-Host ("Klaar: {0}  ({1:N1} MB)" -f $setup, ((Get-Item $setup).Length / 1MB)) -ForegroundColor Green
} else {
    throw "Setup.exe niet gevonden op verwachte plek: $setup"
}
