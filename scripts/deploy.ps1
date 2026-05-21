# Deploy script voor SetupToolbox
# Dit script download en installeert SetupToolbox op een nieuwe Windows installatie

param(
    [string]$GitHubUser = "MisterDuckles",
    [string]$RepoName = "SetupToolbox",
    [switch]$CreateDesktopShortcut = $true,
    [switch]$AutoLaunch = $true
)

Write-Host "==================================" -ForegroundColor Cyan
Write-Host "  Winget App Deployer Deployment" -ForegroundColor Cyan
Write-Host "==================================" -ForegroundColor Cyan
Write-Host ""

# Setup paths
$installDir = "$env:ProgramFiles\SetupToolbox"
$launcherPath = Join-Path $installDir "SetupToolbox-Launcher.exe"
$launcherUrl = "https://github.com/$GitHubUser/$RepoName/releases/latest/download/Launcher.exe"

try {
    # Create installation directory
    Write-Host "[1/4] Creating installation directory..." -ForegroundColor Yellow
    if (!(Test-Path $installDir)) {
        New-Item -ItemType Directory -Path $installDir -Force | Out-Null
        Write-Host "      Created: $installDir" -ForegroundColor Green
    } else {
        Write-Host "      Directory already exists" -ForegroundColor Gray
    }

    # Download launcher
    Write-Host "[2/4] Downloading launcher from GitHub..." -ForegroundColor Yellow
    try {
        Invoke-WebRequest -Uri $launcherUrl -OutFile $launcherPath -UseBasicParsing
        Write-Host "      Downloaded: $launcherPath" -ForegroundColor Green
    } catch {
        Write-Host "      ERROR: Failed to download launcher" -ForegroundColor Red
        Write-Host "      URL: $launcherUrl" -ForegroundColor Gray
        Write-Host "      Error: $($_.Exception.Message)" -ForegroundColor Red
        exit 1
    }

    # Create desktop shortcut
    if ($CreateDesktopShortcut) {
        Write-Host "[3/4] Creating desktop shortcut..." -ForegroundColor Yellow

        # Get public desktop path (all users)
        $publicDesktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonDesktopDirectory)

        # Try current user desktop as fallback
        if (!(Test-Path $publicDesktop)) {
            $publicDesktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::Desktop)
        }

        $shortcutPath = Join-Path $publicDesktop "Winget App Deployer.lnk"

        $WshShell = New-Object -ComObject WScript.Shell
        $Shortcut = $WshShell.CreateShortcut($shortcutPath)
        $Shortcut.TargetPath = $launcherPath
        $Shortcut.WorkingDirectory = $installDir
        $Shortcut.Description = "Install Windows applications using Winget"
        $Shortcut.IconLocation = "$launcherPath,0"
        $Shortcut.Save()

        Write-Host "      Created shortcut: $shortcutPath" -ForegroundColor Green
    } else {
        Write-Host "[3/4] Skipping desktop shortcut" -ForegroundColor Gray
    }

    # Auto-launch
    if ($AutoLaunch) {
        Write-Host "[4/4] Launching Winget App Deployer..." -ForegroundColor Yellow
        Start-Process $launcherPath
        Write-Host "      Launched successfully!" -ForegroundColor Green
    } else {
        Write-Host "[4/4] Skipping auto-launch" -ForegroundColor Gray
    }

    Write-Host ""
    Write-Host "==================================" -ForegroundColor Green
    Write-Host "  Installation Complete! ✓" -ForegroundColor Green
    Write-Host "==================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Winget App Deployer has been installed to:" -ForegroundColor White
    Write-Host "  $installDir" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "The launcher will automatically download the latest version from GitHub" -ForegroundColor Gray

} catch {
    Write-Host ""
    Write-Host "==================================" -ForegroundColor Red
    Write-Host "  Installation Failed!" -ForegroundColor Red
    Write-Host "==================================" -ForegroundColor Red
    Write-Host ""
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    exit 1
}
