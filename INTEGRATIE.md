# Integratie met Windows11-Unattended-Debloat

> **Status:** integratie wordt nog uitgewerkt voor de nieuwe WinUI app. De WPF
> launcher van de oude versie is gearchiveerd onder de git tag
> `wpf-final-v1.2.1`. De nieuwe WinUI launcher staat op de roadmap (zie
> [NEXT-STEPS.md](NEXT-STEPS.md) → v0.9.0+).

## Voorlopige aanpak (zonder launcher)

Tot de WinUI launcher er is kun je de complete `.exe` direct downloaden uit een
GitHub release. De exe is self-contained (~80 MB) en heeft geen runtime
dependencies — kopieer hem ergens en run.

```powershell
# In je debloat / autounattend setup script
$exeUrl = "https://github.com/MisterDuckles/WinGetAppDeployer/releases/latest/download/WingetAppDeployer.WinUI.exe"
$installDir = "$env:ProgramFiles\WingetAppDeployer"
$exePath = Join-Path $installDir "WingetAppDeployer.WinUI.exe"

New-Item -ItemType Directory -Path $installDir -Force | Out-Null
Invoke-WebRequest -Uri $exeUrl -OutFile $exePath -UseBasicParsing

# Desktop shortcut voor alle users
$publicDesktop = [Environment]::GetFolderPath('CommonDesktopDirectory')
$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut(
    (Join-Path $publicDesktop 'WingetAppDeployer.lnk'))
$shortcut.TargetPath = $exePath
$shortcut.Save()
```

## Geplande aanpak (na launcher port)

In v0.9.0+ komt er een kleine bootstrap-launcher die:

- ~5 KB groot is (download via firstlogon-script blijft snel)
- bij eerste run de full `.exe` downloadt naar `%ProgramFiles%`
- self-update check doet en automatisch de nieuwste versie downloadt

Daarna kan deze sectie het launcher-pad gebruiken in plaats van de full exe.

## Auto-update scheduled task

Onafhankelijk van de install-flow: zodra de WinUI app draait kan de gebruiker
in **Settings** → **Scheduled auto-updates** een Daily / Weekly / OnStartup
task aanmaken die `winget upgrade --all --silent` runt. Dit gaat via Windows
Task Scheduler met admin rights, geen extra setup vereist.

## Vragen?

Open een issue op GitHub.
