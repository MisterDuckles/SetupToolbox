# Fetches app icons via Google favicon API and normalizes them to 128x128 PNG.
# Output: data/icons/<wingetId>.png (transparent canvas, image centered, aspect preserved).
#
# Usage:
#   pwsh scripts/fetch-icons.ps1                  # fetch all
#   pwsh scripts/fetch-icons.ps1 -Force           # re-fetch existing
#   pwsh scripts/fetch-icons.ps1 -Only "Discord.Discord","Valve.Steam"

[CmdletBinding()]
param(
    [switch]$Force,
    [string[]]$Only
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repoRoot   = Split-Path -Parent $PSScriptRoot
$iconsDir   = Join-Path $repoRoot 'data\icons'
$reportPath = Join-Path $repoRoot 'data\icons-report.txt'

if (-not (Test-Path $iconsDir)) {
    New-Item -ItemType Directory -Path $iconsDir | Out-Null
}

# wingetId -> @{ domain = '...'; iconUrl = '...' (optional override) }
$sources = @{
    # Browsers
    'Google.Chrome'                       = @{ domain = 'google.com/chrome' }
    'Mozilla.Firefox'                     = @{ domain = 'firefox.com' }
    'Brave.Brave'                         = @{ domain = 'brave.com' }
    'Microsoft.Edge'                      = @{ domain = 'microsoft.com/edge' }
    'Opera.Opera'                         = @{ domain = 'opera.com'; iconUrl = 'https://cdn.jsdelivr.net/gh/homarr-labs/dashboard-icons/png/opera.png' }
    'Opera.OperaGX'                       = @{ domain = 'opera.com/gx'; iconUrl = 'https://commons.wikimedia.org/wiki/Special:FilePath/Opera_GX.svg?width=512' }
    'VivaldiTechnologies.Vivaldi'         = @{ domain = 'vivaldi.com' }
    'TheBrowserCompany.Arc'               = @{ domain = 'arc.net'; iconUrl = 'https://upload.wikimedia.org/wikipedia/commons/thumb/3/37/Arc_%28browser%29_logo.svg/960px-Arc_%28browser%29_logo.svg.png' }
    'TorProject.TorBrowser'               = @{ domain = 'torproject.org'; iconUrl = 'https://upload.wikimedia.org/wikipedia/commons/thumb/1/15/Tor-logo-2011-flat.svg/512px-Tor-logo-2011-flat.svg.png' }
    'LibreWolf.LibreWolf'                 = @{ domain = 'librewolf.net'; iconUrl = 'https://upload.wikimedia.org/wikipedia/commons/thumb/d/d0/LibreWolf_icon.svg/512px-LibreWolf_icon.svg.png' }

    # Development - IDE & Editors
    'Microsoft.VisualStudioCode'          = @{ domain = 'code.visualstudio.com' }
    'Microsoft.VisualStudio.Community'    = @{ domain = 'visualstudio.microsoft.com'; iconUrl = 'https://upload.wikimedia.org/wikipedia/commons/thumb/2/20/Visual_Studio_Icon_2026.svg/512px-Visual_Studio_Icon_2026.svg.png' }
    'JetBrains.IntelliJIDEA.Community'    = @{ domain = 'jetbrains.com/idea' }
    'JetBrains.PyCharm.Community'         = @{ domain = 'jetbrains.com/pycharm'; iconUrl = 'https://commons.wikimedia.org/wiki/Special:FilePath/PyCharm_Icon.svg?width=512' }
    'Google.Antigravity'                  = @{ domain = 'antigravity.google' }

    # Version Control
    'Git.Git'                             = @{ domain = 'git-scm.com'; iconUrl = 'https://cdn.jsdelivr.net/gh/selfhst/icons/png/git.png' }
    'GitHub.GitHubDesktop'                = @{ domain = 'desktop.github.com'; iconUrl = 'https://upload.wikimedia.org/wikipedia/commons/thumb/c/c2/GitHub_Invertocat_Logo.svg/512px-GitHub_Invertocat_Logo.svg.png' }

    # Runtimes
    'OpenJS.NodeJS.LTS'                   = @{ domain = 'nodejs.org'; iconUrl = 'https://cdn.jsdelivr.net/gh/homarr-labs/dashboard-icons/png/nodejs.png' }
    'Python.Python.3.13'                  = @{ domain = 'python.org' }
    'Microsoft.DotNet.SDK.9'              = @{ domain = 'dotnet.microsoft.com' }
    'Microsoft.OpenJDK.21'                = @{ domain = 'microsoft.com/openjdk' }

    # VMs
    'Docker.DockerDesktop'                = @{ domain = 'docker.com' }
    'Oracle.VirtualBox'                   = @{ domain = 'virtualbox.org' }

    # Databases
    'Oracle.MySQLWorkbench'               = @{ domain = 'mysql.com'; iconUrl = 'https://cdn.jsdelivr.net/gh/homarr-labs/dashboard-icons/png/mysql.png' }
    'PostgreSQL.PostgreSQL.17'            = @{ domain = 'postgresql.org'; iconUrl = 'https://upload.wikimedia.org/wikipedia/commons/thumb/2/29/Postgresql_elephant.svg/960px-Postgresql_elephant.svg.png' }

    # API Tools
    'Postman.Postman'                     = @{ domain = 'postman.com' }
    'Insomnia.Insomnia'                   = @{ domain = 'insomnia.rest' }

    # Security - Password Managers
    'Bitwarden.Bitwarden'                 = @{ domain = 'bitwarden.com'; iconUrl = 'https://cdn.jsdelivr.net/gh/homarr-labs/dashboard-icons/png/bitwarden.png' }
    'AgileBits.1Password'                 = @{ domain = '1password.com'; iconUrl = 'https://cdn.jsdelivr.net/gh/homarr-labs/dashboard-icons/png/1password.png' }
    'KeePassXCTeam.KeePassXC'             = @{ domain = 'keepassxc.org'; iconUrl = 'https://cdn.jsdelivr.net/gh/homarr-labs/dashboard-icons/png/keepassxc.png' }
    'NordSecurity.NordPass'               = @{ domain = 'nordpass.com'; iconUrl = 'https://cdn.brandfetch.io/idjaScGQhv/w/400/h/400/theme/dark/icon.png?c=1bxid64Mup7aczewSAYMX&t=1777264321556' }
    'SiberSystems.RoboForm'               = @{ domain = 'roboform.com'; iconUrl = 'https://downloadr2.apkmirror.com/wp-content/uploads/2019/06/5d09ecd4b7740.png' }
    'KeeperSecurity.KeeperDesktop'        = @{ domain = 'keepersecurity.com'; iconUrl = 'https://cdn.jsdelivr.net/gh/homarr-labs/dashboard-icons/png/keeper-security.png' }

    # Security - VPN
    'NordVPN.NordVPN'                     = @{ domain = 'nordvpn.com' }
    'Proton.ProtonVPN'                    = @{ domain = 'protonvpn.com'; iconUrl = 'https://cdn.jsdelivr.net/gh/homarr-labs/dashboard-icons/png/proton-vpn.png' }
    'Surfshark.Surfshark'                 = @{ domain = 'surfshark.com'; iconUrl = 'https://cdn.jsdelivr.net/gh/homarr-labs/dashboard-icons/png/surfshark.png' }
    'IPVanish.IPVanish'                   = @{ domain = 'ipvanish.com'; iconUrl = 'https://cdn.jsdelivr.net/gh/selfhst/icons/png/ipvanish.png' }

    # Security - Antivirus
    'Malwarebytes.Malwarebytes'           = @{ domain = 'malwarebytes.com' }
    'XPFNZKWN35KD6Z'                      = @{ domain = 'norton.com'; label = 'Norton360'; iconUrl = 'https://s2.qwant.com/thumbr/474x474/e/4/6a5b0899e64a3e9cc2348accae312630f16a2c55d2c4699c04915dbcdf9468/OIP.hn5ixoEuObst6pL3KZFQlwHaHa.jpg?u=https%3A%2F%2Fthf.bing.com%2Fth%2Fid%2FOIP.hn5ixoEuObst6pL3KZFQlwHaHa%3Fcb%3Dthfc1%26pid%3DApi&q=0&b=1&p=0&a=0' }
    'XP9K931FWBP5V5'                      = @{ domain = 'bitdefender.com'; label = 'Bitdefender'; iconUrl = 'https://s1.qwant.com/thumbr/474x473/3/3/abbf5793eae825c5cf2db8f14e45632659f53ef6260fb8cc1544d2810706f0/OIP.P63wlYnTio4Cik9kubI0ogHaHZ.jpg?u=https%3A%2F%2Ftse.mm.bing.net%2Fth%2Fid%2FOIP.P63wlYnTio4Cik9kubI0ogHaHZ%3Fpid%3DApi&q=0&b=1&p=0&a=0' }

    # Productivity - Office
    'Microsoft.Office'                    = @{ domain = 'microsoft.com/microsoft-365'; iconUrl = 'https://upload.wikimedia.org/wikipedia/commons/thumb/0/0e/Microsoft_365_%282022%29.svg/960px-Microsoft_365_%282022%29.svg.png' }
    'TheDocumentFoundation.LibreOffice'   = @{ domain = 'libreoffice.org' }
    'ONLYOFFICE.DesktopEditors'           = @{ domain = 'onlyoffice.com' }

    # Productivity - Cloud Storage
    'Microsoft.OneDrive'                  = @{ domain = 'onedrive.com'; iconUrl = 'https://upload.wikimedia.org/wikipedia/commons/thumb/e/e7/Microsoft_OneDrive_Icon_%282025_-_present%29.svg/512px-Microsoft_OneDrive_Icon_%282025_-_present%29.svg.png' }
    'Google.GoogleDrive'                  = @{ domain = 'drive.google.com'; iconUrl = 'https://cdn.jsdelivr.net/gh/selfhst/icons/png/google-drive.png' }
    'Dropbox.Dropbox'                     = @{ domain = 'dropbox.com'; iconUrl = 'https://cdn.jsdelivr.net/gh/homarr-labs/dashboard-icons/png/dropbox.png' }
    '9PKTQ5699M62'                        = @{ domain = 'icloud.com'; label = 'iCloud'; iconUrl = 'https://cdn.jsdelivr.net/gh/homarr-labs/dashboard-icons/png/icloud.png' }
    'Internxt.Drive'                      = @{ domain = 'internxt.com'; iconUrl = 'https://s1.qwant.com/thumbr/474x474/a/1/32f3aecbddf7e7a245fcaf50ddaeec693b5144a88c1b5bf20c49425e2fe0de/OIP.81Qqxk9asg7sv5_yYy32DwHaHa.jpg?u=https%3A%2F%2Ftse.mm.bing.net%2Fth%2Fid%2FOIP.81Qqxk9asg7sv5_yYy32DwHaHa%3Fpid%3DApi&q=0&b=1&p=0&a=0' }

    # Productivity - Notes
    'Notion.Notion'                       = @{ domain = 'notion.so' }
    'Obsidian.Obsidian'                   = @{ domain = 'obsidian.md' }

    # Productivity - PDF
    'Adobe.Acrobat.Reader.64-bit'         = @{ domain = 'adobe.com/acrobat'; iconUrl = 'https://upload.wikimedia.org/wikipedia/commons/thumb/4/42/Adobe_Acrobat_DC_logo_2020.svg/512px-Adobe_Acrobat_DC_logo_2020.svg.png' }
    'SumatraPDF.SumatraPDF'               = @{ domain = 'sumatrapdfreader.org'; iconUrl = 'https://upload.wikimedia.org/wikipedia/commons/thumb/c/cb/Sumatra_PDF_logo.svg/512px-Sumatra_PDF_logo.svg.png' }
    'Foxit.FoxitReader'                   = @{ domain = 'foxit.com' }

    # Productivity - AI
    '9NT1R1C2HH7J'                        = @{ domain = 'openai.com'; label = 'ChatGPT'; iconUrl = 'https://cdn.jsdelivr.net/gh/homarr-labs/dashboard-icons/png/chatgpt.png' }
    'Anthropic.Claude'                    = @{ domain = 'claude.ai'; iconUrl = 'https://cdn.jsdelivr.net/gh/selfhst/icons/png/claude.png' }
    'XP8JNQFBQH6PVF'                      = @{ domain = 'perplexity.ai'; label = 'Perplexity'; iconUrl = 'https://cdn.jsdelivr.net/gh/homarr-labs/dashboard-icons/png/perplexity.png' }
    'Ollama.Ollama'                       = @{ domain = 'ollama.com'; iconUrl = 'https://cdn.jsdelivr.net/gh/homarr-labs/dashboard-icons/png/ollama.png' }

    # Communication
    'Discord.Discord'                     = @{ domain = 'discord.com' }
    'SlackTechnologies.Slack'             = @{ domain = 'slack.com'; iconUrl = 'https://commons.wikimedia.org/wiki/Special:FilePath/Slack_icon_2019.svg?width=512' }
    'Microsoft.Teams'                     = @{ domain = 'teams.microsoft.com'; iconUrl = 'https://upload.wikimedia.org/wikipedia/commons/thumb/0/07/Microsoft_Office_Teams_%282025%E2%80%93present%29.svg/960px-Microsoft_Office_Teams_%282025%E2%80%93present%29.svg.png' }
    'OpenWhisperSystems.Signal'           = @{ domain = 'signal.org'; iconUrl = 'https://upload.wikimedia.org/wikipedia/commons/thumb/6/60/Signal-Logo-Ultramarine_%282024%29.svg/512px-Signal-Logo-Ultramarine_%282024%29.svg.png' }
    '9NKSQGP7F2NH'                        = @{ domain = 'whatsapp.com'; label = 'WhatsApp'; iconUrl = 'https://upload.wikimedia.org/wikipedia/commons/thumb/4/4c/WhatsApp_Logo_green.svg/500px-WhatsApp_Logo_green.svg.png' }

    # Media - Music
    'Spotify.Spotify'                     = @{ domain = 'spotify.com'; iconUrl = 'https://cdn.jsdelivr.net/gh/homarr-labs/dashboard-icons/png/spotify.png' }
    '9PFHDD62MXS1'                        = @{ domain = 'apple.com/apple-music'; label = 'AppleMusic'; iconUrl = 'https://cdn.jsdelivr.net/gh/homarr-labs/dashboard-icons/png/apple-music.png' }

    # Media - Audio/Video
    'VideoLAN.VLC'                        = @{ domain = 'videolan.org'; iconUrl = 'https://commons.wikimedia.org/wiki/Special:FilePath/VLC_Icon.svg?width=512' }
    'Audacity.Audacity'                   = @{ domain = 'audacityteam.org' }

    # Media - Streaming
    'OBSProject.OBSStudio'                = @{ domain = 'obsproject.com' }
    'Streamlabs.Streamlabs'               = @{ domain = 'streamlabs.com' }

    # Gaming
    'Valve.Steam'                         = @{ domain = 'store.steampowered.com' }
    'EpicGames.EpicGamesLauncher'         = @{ domain = 'epicgames.com'; iconUrl = 'https://commons.wikimedia.org/wiki/Special:FilePath/Epic_Games_logo.svg?width=512' }
    'ElectronicArts.EADesktop'            = @{ domain = 'ea.com'; iconUrl = 'https://cdn.jsdelivr.net/gh/homarr-labs/dashboard-icons/png/electronic-arts.png' }
    'Blizzard.BattleNet'                  = @{ domain = 'battle.net' }
    'Ubisoft.Connect'                     = @{ domain = 'ubisoftconnect.com'; iconUrl = 'https://cdn2.steamgriddb.com/icon/510b67b97266d086ba20a6e589756f39/32/1024x1024.png' }

    # Utilities - System
    'Microsoft.PowerToys'                 = @{ domain = 'learn.microsoft.com/windows/powertoys'; iconUrl = 'https://upload.wikimedia.org/wikipedia/commons/thumb/2/2b/2020_PowerToys_Icon.svg/960px-2020_PowerToys_Icon.svg.png' }
    'voidtools.Everything'                = @{ domain = 'voidtools.com'; iconFile = 'local-icons/everything.png'; autoCrop = $true }
    '7zip.7zip'                           = @{ domain = '7-zip.org'; iconUrl = 'https://upload.wikimedia.org/wikipedia/commons/thumb/f/f2/7ziplogo.svg/960px-7ziplogo.svg.png' }
    'RARLab.WinRAR'                       = @{ domain = 'win-rar.com'; iconUrl = 'https://img.icons8.com/?size=200&id=tqHCLM4kzQy0&format=png'; autoCrop = $true }
    'JAMSoftware.TreeSize.Free'           = @{ domain = 'jam-software.com'; iconUrl = 'https://upload.wikimedia.org/wikipedia/commons/b/bd/TreeSize-Icon-256.png' }
    'Piriform.CCleaner'                   = @{ domain = 'ccleaner.com'; iconUrl = 'https://img.icons8.com/?size=512&id=36508&format=png' }
    'RevoUninstaller.RevoUninstaller'     = @{ domain = 'revouninstaller.com'; iconUrl = 'https://upload.wikimedia.org/wikipedia/commons/8/83/Revouninstallerpro_icon.png' }
    'PuTTY.PuTTY'                         = @{ domain = 'chiark.greenend.org.uk/~sgtatham/putty'; iconUrl = 'https://cdn.jsdelivr.net/gh/selfhst/icons/png/putty.png' }
    'CharlesMilette.TranslucentTB'        = @{ domain = 'translucenttb.com' }
    'xanderfrangos.twinkletray'           = @{ domain = 'twinkletray.com' }

    # Utilities - Remote
    'TeamViewer.TeamViewer'               = @{ domain = 'teamviewer.com' }
    'Parsec.Parsec'                       = @{ domain = 'parsec.app' }

    # Creative - Graphics
    'GIMP.GIMP'                           = @{ domain = 'gimp.org'; iconUrl = 'https://cdn.jsdelivr.net/gh/homarr-labs/dashboard-icons/png/gimp.png' }
    'dotPDN.PaintDotNet'                  = @{ domain = 'getpaint.net'; iconUrl = 'https://img.icons8.com/?size=512&id=60851&format=png' }
    'darktable.darktable'                 = @{ domain = 'darktable.org'; iconUrl = 'https://upload.wikimedia.org/wikipedia/commons/thumb/7/7b/Darktable_icon.svg/960px-Darktable_icon.svg.png' }

    # Creative - Video
    'BlackmagicDesign.DaVinciResolve'     = @{ domain = 'blackmagicdesign.com'; iconUrl = 'https://vectorified.com/images/davinci-resolve-icon-6.jpg' }
    'ByteDance.CapCut'                    = @{ domain = 'capcut.com' }
    'HandBrake.HandBrake'                 = @{ domain = 'handbrake.fr'; iconUrl = 'https://upload.wikimedia.org/wikipedia/commons/d/d9/HandBrake_Icon.png' }

    # Creative - 3D
    'BlenderFoundation.Blender'           = @{ domain = 'blender.org' }
    'Bambulab.Bambustudio'                = @{ domain = 'bambulab.com' }

    # Suites - Proton — alle product-iconen via dashboard-icons (uniform, hoge res)
    'Proton.ProtonMail'                   = @{ domain = 'mail.proton.me';     iconUrl = 'https://cdn.jsdelivr.net/gh/homarr-labs/dashboard-icons/png/proton-mail.png' }
    'Proton.ProtonMailBridge'             = @{ domain = 'mail.proton.me';     iconUrl = 'https://cdn.jsdelivr.net/gh/homarr-labs/dashboard-icons/png/proton-mail.png' }
    'Proton.ProtonDrive'                  = @{ domain = 'drive.proton.me' }
    'Proton.ProtonPass'                   = @{ domain = 'pass.proton.me' }
    'Proton.ProtonCalendar'               = @{ domain = 'calendar.proton.me'; iconUrl = 'https://cdn.jsdelivr.net/gh/homarr-labs/dashboard-icons/png/proton-calendar.png' }
    'Proton.ProtonWallet'                 = @{ domain = 'wallet.proton.me';   iconUrl = 'https://cdn.jsdelivr.net/gh/homarr-labs/dashboard-icons/png/proton-wallet.png' }
    'Proton.ProtonAuthenticator'          = @{ domain = 'proton.me/authenticator'; iconUrl = 'https://uxwing.com/wp-content/themes/uxwing/download/brands-and-social-media/proton-authenticator-icon.png' }
    'Proton.ProtonSheets'                 = @{ domain = 'proton.me/sheets';   iconUrl = 'https://cdn.jsdelivr.net/gh/homarr-labs/dashboard-icons/png/proton-sheets.png' }
    'Proton.ProtonDocs'                   = @{ domain = 'proton.me/docs';     iconUrl = 'https://cdn.jsdelivr.net/gh/homarr-labs/dashboard-icons/png/proton-docs.png' }
    'Proton.ProtonMeet'                   = @{ domain = 'proton.me/meet';     iconUrl = 'https://play-lh.googleusercontent.com/iCSbycC--Gj70n6n23vs4rp8a_WLoKIJ8E4dDX39AYSfiTCyPcX5hlOZ5mjOEqtWQaP5420cRqnnuG1WZf0xIFE=w480-h960'; autoCrop = $true }

    # Suites - Adobe
    'Adobe.CreativeCloud'                 = @{ domain = 'adobe.com/creativecloud'; iconUrl = 'https://upload.wikimedia.org/wikipedia/commons/thumb/4/4c/Adobe_Creative_Cloud_rainbow_icon.svg/512px-Adobe_Creative_Cloud_rainbow_icon.svg.png' }
    'Adobe.Acrobat.Pro'                   = @{ domain = 'adobe.com/acrobat';       iconUrl = 'https://upload.wikimedia.org/wikipedia/commons/thumb/4/42/Adobe_Acrobat_DC_logo_2020.svg/512px-Adobe_Acrobat_DC_logo_2020.svg.png' }
}

function Get-GoogleFaviconUrl {
    param([string]$Domain, [int]$Size = 128)
    return "https://www.google.com/s2/favicons?domain=$Domain&sz=$Size"
}

function Get-IconHorseUrl {
    param([string]$Domain)
    # icon.horse expects bare hostname (no path)
    $hostname = ($Domain -split '/')[0]
    return "https://icon.horse/icon/$hostname"
}

function Invoke-IconFetch {
    param([string]$Url)
    try {
        $headers = @{ 'User-Agent' = 'WingetAppDeployer/0.5 (https://github.com/MisterDuckles/WinAppInstaller; nlmulitigaming123@gmail.com) icon-fetcher' }
        $resp = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 15 -Headers $headers -ErrorAction Stop
        if ($resp.StatusCode -ne 200) { return $null }
        $bytes = $resp.Content
        if ($bytes -is [string]) { $bytes = [System.Text.Encoding]::UTF8.GetBytes($bytes) }
        if ($bytes.Length -lt 100) { return $null }
        # Reject SVG (System.Drawing can't render it).
        $ct = $resp.Headers['Content-Type']
        if ($ct -and ($ct -match 'svg')) { return $null }
        # Sniff first bytes for SVG anyway (some servers don't set the type).
        $head = [System.Text.Encoding]::ASCII.GetString($bytes[0..[Math]::Min(127, $bytes.Length-1)])
        if ($head -match '<svg' -or $head -match '<\?xml') { return $null }
        return ,$bytes
    } catch {
        return $null
    }
}

function Measure-ImageSource {
    param([byte[]]$Bytes)
    try {
        $ms = New-Object System.IO.MemoryStream(,$Bytes)
        $img = [System.Drawing.Image]::FromStream($ms)
        $w = $img.Width; $h = $img.Height
        $img.Dispose(); $ms.Dispose()
        return @{ Width = $w; Height = $h }
    } catch {
        return $null
    }
}

# Maakt witte (en near-white) achtergrond transparant via flood-fill vanaf de
# 4 hoeken. Stopt zodra een pixel "te ver van wit" is (= het logo). Pakt typische
# favicon-witte-blok backgrounds (Slack, Signal, Proton Mail, WinRAR) zonder
# logos met witte details kapot te maken — die zijn niet vanaf de buitenrand
# bereikbaar.
function Invoke-WhiteToTransparent {
    param([System.Drawing.Bitmap]$Bmp, [int]$Threshold = 225)

    $w = $Bmp.Width; $h = $Bmp.Height
    $rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
    $data = $Bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadWrite, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $stride = $data.Stride
        $bytes = [byte[]]::new($stride * $h)
        [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)

        # Format32bppArgb in memory: BGRA per pixel.
        # Vlakke 1D arrays met index = y*w+x — vermijdt PowerShell 2D-array
        # indexing kwirks (`$arr[$x,$y]` wordt geparsed als index-array).
        $size = $w * $h
        $isWhite = [bool[]]::new($size)
        $visited = [bool[]]::new($size)
        for ($y = 0; $y -lt $h; $y++) {
            $rowOff = $y * $stride
            $idxOff = $y * $w
            for ($x = 0; $x -lt $w; $x++) {
                $i = $rowOff + $x * 4
                if ($bytes[$i + 3] -gt 0 -and $bytes[$i] -ge $Threshold -and $bytes[$i + 1] -ge $Threshold -and $bytes[$i + 2] -ge $Threshold) {
                    $isWhite[$idxOff + $x] = $true
                }
            }
        }

        # BFS vanaf 4 hoek-pixels (als die wit zijn).
        $queue = [System.Collections.Generic.Queue[int]]::new()
        $corners = @(0, ($w - 1), (($h - 1) * $w), (($h - 1) * $w + $w - 1))
        foreach ($idx in $corners) {
            if ($isWhite[$idx]) {
                $queue.Enqueue($idx)
                $visited[$idx] = $true
            }
        }
        while ($queue.Count -gt 0) {
            $idx = $queue.Dequeue()
            $px = $idx % $w
            $py = [int]([Math]::Floor($idx / $w))
            $bytes[$py * $stride + $px * 4 + 3] = 0   # alpha = 0

            # 4 neighbours
            if ($px -gt 0)        { $n = $idx - 1;  if (-not $visited[$n] -and $isWhite[$n]) { $visited[$n] = $true; $queue.Enqueue($n) } }
            if ($px -lt ($w - 1)) { $n = $idx + 1;  if (-not $visited[$n] -and $isWhite[$n]) { $visited[$n] = $true; $queue.Enqueue($n) } }
            if ($py -gt 0)        { $n = $idx - $w; if (-not $visited[$n] -and $isWhite[$n]) { $visited[$n] = $true; $queue.Enqueue($n) } }
            if ($py -lt ($h - 1)) { $n = $idx + $w; if (-not $visited[$n] -and $isWhite[$n]) { $visited[$n] = $true; $queue.Enqueue($n) } }
        }

        [System.Runtime.InteropServices.Marshal]::Copy($bytes, 0, $data.Scan0, $bytes.Length)
    } finally {
        $Bmp.UnlockBits($data)
    }
}

# Apple-style squircle: rounded-rect mask met radius ~22% van canvas. Skipt
# automatisch als alle 4 hoek-pixels al transparant zijn (icon was al rond).
function Invoke-RoundedCorners {
    param([System.Drawing.Bitmap]$Bmp, [double]$RadiusFraction = 0.224)

    $w = $Bmp.Width; $h = $Bmp.Height
    # Squircle mask alleen toepassen op iOS-style "filled square" icons —
    # vereist dat ALLE 4 hoek-pixels (bijna) volledig opaque zijn. Zodra ook
    # maar één hoek transparant of semi-transparant is, heeft het ontwerp
    # bewuste padding / rays / een ronde vorm en zou masking content afsnijden
    # (Claude burst, CCleaner broom) of redundant zijn (Discord, Spotify rond).
    $c1 = $Bmp.GetPixel(0, 0).A
    $c2 = $Bmp.GetPixel($w - 1, 0).A
    $c3 = $Bmp.GetPixel(0, $h - 1).A
    $c4 = $Bmp.GetPixel($w - 1, $h - 1).A
    if ($c1 -lt 250 -or $c2 -lt 250 -or $c3 -lt 250 -or $c4 -lt 250) { return $false }

    $r = [int]([Math]::Round([Math]::Min($w, $h) * $RadiusFraction))
    # Maak pad voor rounded rect.
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $r * 2, $r * 2, 180, 90)
    $path.AddArc($w - $r * 2, 0, $r * 2, $r * 2, 270, 90)
    $path.AddArc($w - $r * 2, $h - $r * 2, $r * 2, $r * 2, 0, 90)
    $path.AddArc(0, $h - $r * 2, $r * 2, $r * 2, 90, 90)
    $path.CloseFigure()

    # Render via mask: copy bestaande pixels alleen binnen pad. Doen we door
    # een nieuwe transparante bitmap te maken, mask toe te passen, en de oude
    # te overschrijven.
    $masked = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $mg = [System.Drawing.Graphics]::FromImage($masked)
    $mg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $mg.Clear([System.Drawing.Color]::Transparent)
    $mg.SetClip($path)
    $mg.DrawImage($Bmp, 0, 0)
    $mg.Dispose()
    $path.Dispose()

    # Copy masked back into $Bmp.
    $g = [System.Drawing.Graphics]::FromImage($Bmp)
    $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($masked, 0, 0)
    $g.Dispose()
    $masked.Dispose()
    return $true
}

# Vind bounding-box van non-witte, non-transparante pixels. Gebruikt om
# whitespace-borders rond een logo automatisch te trimmen vóór schaling
# (anders zit een logo met veel padding straks klein in 128x128).
function Get-ContentBoundingBox {
    param([System.Drawing.Bitmap]$Bmp, [int]$Threshold = 225)

    $w = $Bmp.Width; $h = $Bmp.Height
    $rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
    $data = $Bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $minX = $w; $maxX = -1; $minY = $h; $maxY = -1
    try {
        $stride = $data.Stride
        $bytes = [byte[]]::new($stride * $h)
        [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)

        for ($y = 0; $y -lt $h; $y++) {
            $rowOff = $y * $stride
            for ($x = 0; $x -lt $w; $x++) {
                $i = $rowOff + $x * 4
                $a = $bytes[$i + 3]
                if ($a -lt 32) { continue }                                        # transparent
                if ($bytes[$i] -ge $Threshold -and $bytes[$i + 1] -ge $Threshold -and $bytes[$i + 2] -ge $Threshold) { continue } # near-white
                if ($x -lt $minX) { $minX = $x }
                if ($x -gt $maxX) { $maxX = $x }
                if ($y -lt $minY) { $minY = $y }
                if ($y -gt $maxY) { $maxY = $y }
            }
        }
    } finally {
        $Bmp.UnlockBits($data)
    }

    if ($maxX -lt 0) { return $null }
    return @{ X = $minX; Y = $minY; Width = ($maxX - $minX + 1); Height = ($maxY - $minY + 1) }
}

function Save-NormalizedIcon {
    param(
        [byte[]]$ImageBytes,
        [string]$OutPath,
        [int]$Canvas = 128,
        [bool]$WhiteToTransparent = $true,
        [bool]$RoundCorners = $true,
        # Auto-crop default OFF: veel designed icons hebben bewuste padding
        # (Claude, PowerToys, Office, Teams) — die wegnemen breekt het ontwerp.
        # Opt-in per app via `autoCrop = $true` in $sources voor random PNGs
        # met veel whitespace rondom (bv. de Gemini-render voor Everything).
        [bool]$AutoCrop = $false
    )

    $ms  = New-Object System.IO.MemoryStream(,$ImageBytes)
    $loaded = [System.Drawing.Image]::FromStream($ms)
    $origW = $loaded.Width
    $origH = $loaded.Height

    # Clone naar 32bppArgb voor LockBits-scans. DrawImage-based conversion gaf
    # bugs met semi-transparante PNGs (Proton Sheets verloor zijn content);
    # Clone(Rectangle, PixelFormat) gebruikt GDI+ native format-conversie die
    # alpha correct preserveert.
    $cloneRect = New-Object System.Drawing.Rectangle(0, 0, $loaded.Width, $loaded.Height)
    $src = $loaded.Clone($cloneRect, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $loaded.Dispose()

    # Auto-crop whitespace borders op de source — kleine logos met veel padding
    # vullen daarna 128x128 fatsoenlijk i.p.v. wegvallen in een kleine middencel.
    if ($AutoCrop) {
        $bbox = Get-ContentBoundingBox -Bmp $src
        if ($bbox -and ($bbox.X -gt 0 -or $bbox.Y -gt 0 -or $bbox.Width -lt $src.Width -or $bbox.Height -lt $src.Height)) {
            $cropped = New-Object System.Drawing.Bitmap $bbox.Width, $bbox.Height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            $cg = [System.Drawing.Graphics]::FromImage($cropped)
            $destRect = New-Object System.Drawing.Rectangle(0, 0, $bbox.Width, $bbox.Height)
            $cg.DrawImage($src, $destRect, $bbox.X, $bbox.Y, $bbox.Width, $bbox.Height, [System.Drawing.GraphicsUnit]::Pixel)
            $cg.Dispose()
            $src.Dispose()
            $src = $cropped
        }
    }

    $srcW = $src.Width
    $srcH = $src.Height

    # Scale to fit inside canvas, preserve aspect ratio.
    $ratio  = [Math]::Min($Canvas / $srcW, $Canvas / $srcH)
    $newW   = [int]([Math]::Round($srcW * $ratio))
    $newH   = [int]([Math]::Round($srcH * $ratio))
    $offX   = [int](($Canvas - $newW) / 2)
    $offY   = [int](($Canvas - $newH) / 2)

    $bmp = New-Object System.Drawing.Bitmap $Canvas, $Canvas, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

    $g.DrawImage($src, $offX, $offY, $newW, $newH)
    $g.Dispose()

    if ($WhiteToTransparent) { Invoke-WhiteToTransparent -Bmp $bmp }
    if ($RoundCorners)       { [void](Invoke-RoundedCorners -Bmp $bmp) }

    $bmp.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $src.Dispose()
    $ms.Dispose()

    return @{ SourceWidth = $origW; SourceHeight = $origH }
}

# Fetch loop
$results = [System.Collections.Generic.List[object]]::new()
$keys = if ($Only) { $Only } else { $sources.Keys }

foreach ($wingetId in $keys) {
    if (-not $sources.ContainsKey($wingetId)) {
        Write-Warning "Geen source gemapped voor: $wingetId"
        continue
    }

    $entry    = $sources[$wingetId]
    $label    = if ($entry.label) { $entry.label } else { $wingetId }
    # Filename met dots vervangen door hyphens — Windows PRI parser ziet
    # anders bv. ".64-bit.png" als een scale qualifier (zoals .scale-200).
    $safeName = $wingetId -replace '\.', '-'
    $outFile  = Join-Path $iconsDir "$safeName.png"

    if ((Test-Path $outFile) -and -not $Force) {
        Write-Host "[SKIP] $label (bestaat al)" -ForegroundColor DarkGray
        $results.Add(@{ Id = $wingetId; Status = 'skipped'; Path = $outFile }) | Out-Null
        continue
    }

    # Local-file shortcut: gebruiker dropt PNG handmatig in scripts/local-icons/.
    if ($entry.iconFile) {
        $localPath = if ([System.IO.Path]::IsPathRooted($entry.iconFile)) { $entry.iconFile } else { Join-Path $PSScriptRoot $entry.iconFile }
        if (Test-Path $localPath) {
            try {
                Write-Host "[GET ] $label  <-  [local] $localPath" -ForegroundColor Cyan
                $bytes = [System.IO.File]::ReadAllBytes($localPath)
                $autoCropFlag = if ($entry.autoCrop) { [bool]$entry.autoCrop } else { $false }
                $info = Save-NormalizedIcon -ImageBytes $bytes -OutPath $outFile -Canvas 128 -AutoCrop $autoCropFlag
                $sizeKb = [Math]::Round((Get-Item $outFile).Length / 1KB, 1)
                $quality = if ($info.SourceWidth -lt 64) { 'LAAG' } elseif ($info.SourceWidth -lt 128) { 'OK' } else { 'GOED' }
                Write-Host "       => gekozen: local ($($info.SourceWidth)x$($info.SourceHeight)) -> 128x128 ($sizeKb KB) [$quality]" -ForegroundColor Green
                $results.Add(@{ Id = $wingetId; Status = 'ok'; Source = 'local'; Quality = $quality; SourceWidth = $info.SourceWidth; SourceHeight = $info.SourceHeight; Path = $outFile }) | Out-Null
                continue
            } catch {
                Write-Host "[FAIL] $label - lokaal bestand kon niet verwerkt worden: $($_.Exception.Message)" -ForegroundColor Red
                $results.Add(@{ Id = $wingetId; Status = 'failed'; Error = $_.Exception.Message }) | Out-Null
                continue
            }
        } else {
            Write-Host "[WARN] $label - iconFile niet gevonden: $localPath, fallback naar URL" -ForegroundColor DarkYellow
        }
    }

    # Build candidate URL list (in priority order):
    # 1. Explicit override
    # 2. Google favicon API
    # 3. icon.horse (often returns apple-touch-icon, much higher res)
    $candidates = @()
    if ($entry.iconUrl) {
        $candidates += @{ Source = 'override'; Url = $entry.iconUrl }
    } else {
        $candidates += @{ Source = 'google';     Url = (Get-GoogleFaviconUrl -Domain $entry.domain -Size 128) }
        $candidates += @{ Source = 'icon.horse'; Url = (Get-IconHorseUrl -Domain $entry.domain) }
    }

    $best     = $null
    $bestSrc  = $null
    $bestSize = 0
    $bestBytes = $null

    foreach ($c in $candidates) {
        Write-Host "[GET ] $label  <-  [$($c.Source)] $($c.Url)" -ForegroundColor Cyan
        $bytes = Invoke-IconFetch -Url $c.Url
        if (-not $bytes) {
            Write-Host "       (geen antwoord / leeg)" -ForegroundColor DarkYellow
            continue
        }
        $dim = Measure-ImageSource -Bytes $bytes
        if (-not $dim) {
            Write-Host "       (kan beeld niet lezen)" -ForegroundColor DarkYellow
            continue
        }
        $maxDim = [Math]::Max($dim.Width, $dim.Height)
        Write-Host "       bron $($dim.Width)x$($dim.Height)" -ForegroundColor DarkGray

        if ($maxDim -gt $bestSize) {
            $best     = $dim
            $bestSrc  = $c.Source
            $bestSize = $maxDim
            $bestBytes = $bytes
        }

        # Override altijd accepteren als enige; voor andere stoppen zodra we 128+ hebben.
        if ($c.Source -eq 'override' -or $maxDim -ge 128) { break }
    }

    if (-not $bestBytes) {
        Write-Host "[FAIL] $label - geen bruikbare bron" -ForegroundColor Red
        $results.Add(@{ Id = $wingetId; Status = 'failed'; Error = 'no usable source' }) | Out-Null
        continue
    }

    try {
        $autoCropFlag = if ($entry.autoCrop) { [bool]$entry.autoCrop } else { $false }
        $info = Save-NormalizedIcon -ImageBytes $bestBytes -OutPath $outFile -Canvas 128 -AutoCrop $autoCropFlag
        $sizeKb = [Math]::Round((Get-Item $outFile).Length / 1KB, 1)
        $quality = if ($info.SourceWidth -lt 64) { 'LAAG' } elseif ($info.SourceWidth -lt 128) { 'OK' } else { 'GOED' }
        Write-Host "       => gekozen: $bestSrc ($($info.SourceWidth)x$($info.SourceHeight)) -> 128x128 ($sizeKb KB) [$quality]" -ForegroundColor Green
        $results.Add(@{
            Id           = $wingetId
            Status       = 'ok'
            Source       = $bestSrc
            Quality      = $quality
            SourceWidth  = $info.SourceWidth
            SourceHeight = $info.SourceHeight
            Path         = $outFile
        }) | Out-Null
    } catch {
        Write-Host "[FAIL] $label - $($_.Exception.Message)" -ForegroundColor Red
        $results.Add(@{ Id = $wingetId; Status = 'failed'; Error = $_.Exception.Message }) | Out-Null
    }
}

# Report
$ok      = $results | Where-Object { $_.Status -eq 'ok' }
$skipped = $results | Where-Object { $_.Status -eq 'skipped' }
$failed  = $results | Where-Object { $_.Status -eq 'failed' }
$lowQ    = $ok      | Where-Object { $_.Quality -eq 'LAAG' }

$report  = @()
$report += "=== Icon fetch report ($(Get-Date -Format 'yyyy-MM-dd HH:mm')) ==="
$report += "OK:      $($ok.Count)"
$report += "Skipped: $($skipped.Count)"
$report += "Failed:  $($failed.Count)"
$report += "Lage kwaliteit (bron <64px, candidate voor handmatige vervang): $($lowQ.Count)"
$report += ''
if ($failed.Count -gt 0) {
    $report += '--- FAILED (handmatig oppakken) ---'
    foreach ($f in $failed) { $report += "  $($f.Id) - $($f.Error)" }
    $report += ''
}
if ($lowQ.Count -gt 0) {
    $report += '--- LAGE KWALITEIT (bron was klein, overweeg handmatige vervang) ---'
    foreach ($l in $lowQ) { $report += "  $($l.Id) - bron $($l.SourceWidth)x$($l.SourceHeight)" }
    $report += ''
}

$report -join "`r`n" | Set-Content -Path $reportPath -Encoding UTF8

Write-Host ''
Write-Host '=========================================' -ForegroundColor Yellow
Write-Host "OK: $($ok.Count) | Skipped: $($skipped.Count) | Failed: $($failed.Count) | Laag: $($lowQ.Count)" -ForegroundColor Yellow
Write-Host "Report: $reportPath" -ForegroundColor Yellow
