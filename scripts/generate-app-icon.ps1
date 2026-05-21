# Genereert AppIcon.ico — design = "Variant 29": tilted (Photos-style) stack
# van 3 blauwe cards met Fluent download icon (verticale shaft + V-chevron tip
# + horizontale tray-lijn) gecentreerd IN de witte front card.
#
# Volgt Microsoft Windows 11 Fluent Design guidelines:
# - Single literal metaphor (apps + downloaden naar collection)
# - Analogous monochrome blauw palette (3 ranges: licht/medium/donker)
# - Subtle 120° gradient
# - Layered flat shapes met drop shadows
# - Light source from top-left
# - Thin stroked icon met round caps (Segoe Fluent stijl)
# - Geen typografie, geen background tile
#
# Reproduceerbaar — gewoon dit script opnieuw runnen om AppIcon.ico te updaten.
# Backup van afgewezen alternatives in data/app-icon-backups/.
#
# Output: multi-resolution ICO (16/24/32/48/64/128/256 px PNG-encoded entries).

[CmdletBinding()]
param(
    [string]$OutPath = "$PSScriptRoot\..\src\SetupToolbox\Assets\AppIcon.ico",
    [string]$PreviewPng = "$PSScriptRoot\..\data\app-icon-preview.png"
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# ===== Helpers =====
function New-RoundedRectPath {
    param([single]$X, [single]$Y, [single]$W, [single]$H, [single]$R)
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    if ($R -le 0) { $p.AddRectangle((New-Object System.Drawing.RectangleF($X, $Y, $W, $H))); return $p }
    $d = $R * 2
    $p.AddArc($X, $Y, $d, $d, 180, 90)
    $p.AddArc($X + $W - $d, $Y, $d, $d, 270, 90)
    $p.AddArc($X + $W - $d, $Y + $H - $d, $d, $d, 0, 90)
    $p.AddArc($X, $Y + $H - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

# Approximate drop shadow via multi-pass widening met afnemende alpha.
# System.Drawing heeft geen native blur — dit benadert het.
function Add-DropShadow {
    param([System.Drawing.Graphics]$G, [System.Drawing.Drawing2D.GraphicsPath]$Path, [single]$OffsetX = 0, [single]$OffsetY = 2, [single]$Spread = 4, [byte]$Opacity = 80)
    $passes = 6
    for ($i = $passes; $i -ge 1; $i--) {
        $a = [byte]([Math]::Round($Opacity * ($i / [double]$passes) * ($i / [double]$passes) * 0.35))
        $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb($a, 8, 24, 64))
        $matrix = New-Object System.Drawing.Drawing2D.Matrix
        $matrix.Translate($OffsetX, $OffsetY)
        $clone = $Path.Clone()
        $clone.Transform($matrix)
        $pen = New-Object System.Drawing.Pen $brush, ($Spread * $i / [double]$passes)
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $G.DrawPath($pen, $clone)
        $pen.Dispose(); $brush.Dispose(); $clone.Dispose(); $matrix.Dispose()
    }
}

# Draws Fluent-style download icon: verticale shaft + V-chevron tip + horizontale
# tray-lijn. Round caps en joins voor true Segoe Fluent feel.
function Draw-FluentDownloadIcon {
    param([System.Drawing.Graphics]$G, [single]$Cx, [single]$Cy, [single]$Size, [single]$f, [System.Drawing.Color]$Color, [single]$StrokeFactor = 1.7)
    [single]$strokeW = $f * $StrokeFactor
    $pen = New-Object System.Drawing.Pen $Color, $strokeW
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    [single]$arrowHalfW = $Size * 0.3
    [single]$trayHalfW  = $Size * 0.42
    [single]$arrowTopY  = $Cy - $Size * 0.42
    [single]$arrowTipY  = $Cy + $Size * 0.18
    [single]$trayY      = $Cy + $Size * 0.42

    # Verticale shaft
    $G.DrawLine($pen, $Cx, $arrowTopY, $Cx, $arrowTipY)
    # V chevron tip onderaan shaft
    $G.DrawLines($pen, [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF([single]($Cx - $arrowHalfW), [single]($arrowTipY - $arrowHalfW * 0.85))),
        (New-Object System.Drawing.PointF($Cx, $arrowTipY)),
        (New-Object System.Drawing.PointF([single]($Cx + $arrowHalfW), [single]($arrowTipY - $arrowHalfW * 0.85)))
    ))
    # Horizontale tray-lijn (de "install destination")
    $G.DrawLine($pen, [single]($Cx - $trayHalfW), $trayY, [single]($Cx + $trayHalfW), $trayY)

    $pen.Dispose()
}

function New-AppIcon {
    param([int]$Size = 256)

    $bmp = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    [single]$f = [single]$Size / 48.0   # MS 48-grid scale factor

    # Analogous monochrome blauw palette (3 ranges per MS guidelines)
    $blueLight    = [System.Drawing.Color]::FromArgb(255, 138, 188, 250)
    $blueMid1     = [System.Drawing.Color]::FromArgb(255,  74, 142, 226)
    $blueMid2     = [System.Drawing.Color]::FromArgb(255,  44, 105, 196)
    $blueDark1    = [System.Drawing.Color]::FromArgb(255,  24,  72, 160)
    $accentDeep   = [System.Drawing.Color]::FromArgb(255,  10,  60, 160)

    # V36 design: straight stack (geen rotatie), 3 cards diagonaal gestapeld
    # met SYMMETRIC offsets zoals de originele V36 layout. Cards landscape
    # (38:26 ≈ 1.46:1). Offset 5 geeft duidelijker zichtbare "stap" tussen
    # cards (19% van card-hoogte zichtbaar) — niet zo krap als offset 4.
    [single]$cR  = 4.0 * $f
    [single]$cW  = 38.0 * $f
    [single]$cH  = 26.0 * $f
    [single]$offset  = 5.0 * $f    # symmetric X+Y offset
    [single]$startY  = 6.0 * $f    # verticaal gecentreerd (totaal hoogte=36)

    # ============================================================
    # Card 3 (back) — donker blauw
    # ============================================================
    [single]$c3X = 0.0; [single]$c3Y = $startY
    $p3 = New-RoundedRectPath $c3X $c3Y $cW $cH $cR
    Add-DropShadow -G $g -Path $p3 -OffsetX 0 -OffsetY ($f * 0.9) -Spread ($f * 2.1) -Opacity 110
    $b3 = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.RectangleF($c3X, $c3Y, $cW, $cH)), $blueMid1, $blueDark1, 120.0)
    $g.FillPath($b3, $p3); $b3.Dispose(); $p3.Dispose()

    # ============================================================
    # Card 2 (middle) — medium blauw, diagonaal offset
    # ============================================================
    [single]$c2X = $offset; [single]$c2Y = $startY + $offset
    $p2 = New-RoundedRectPath $c2X $c2Y $cW $cH $cR
    Add-DropShadow -G $g -Path $p2 -OffsetX 0 -OffsetY ($f * 0.9) -Spread ($f * 2.1) -Opacity 110
    $b2 = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.RectangleF($c2X, $c2Y, $cW, $cH)), $blueLight, $blueMid2, 120.0)
    $g.FillPath($b2, $p2); $b2.Dispose(); $p2.Dispose()

    # ============================================================
    # Card 1 (front) — wit/lichtblauw
    # ============================================================
    [single]$c1X = $offset * 2; [single]$c1Y = $startY + $offset * 2
    $p1 = New-RoundedRectPath $c1X $c1Y $cW $cH $cR
    Add-DropShadow -G $g -Path $p1 -OffsetX 0 -OffsetY ($f * 1.0) -Spread ($f * 2.4) -Opacity 130
    $b1 = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.RectangleF($c1X, $c1Y, $cW, $cH)),
        ([System.Drawing.Color]::FromArgb(255, 230, 240, 254)), $blueLight, 120.0)
    $g.FillPath($b1, $p1); $b1.Dispose(); $p1.Dispose()

    # ============================================================
    # Fluent download icon gecentreerd in front card.
    # ============================================================
    [single]$cx = $c1X + $cW / 2.0
    [single]$cy = $c1Y + $cH / 2.0
    Draw-FluentDownloadIcon -G $g -Cx $cx -Cy $cy -Size ([single](22.0 * $f)) -f $f -Color $accentDeep -StrokeFactor 2.4

    $g.Dispose()
    return $bmp
}

function Get-PngBytes {
    param([System.Drawing.Bitmap]$Bitmap)
    $ms = New-Object System.IO.MemoryStream
    $Bitmap.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    $ms.Dispose()
    return ,$bytes
}

# Genereer master 256 + downscaled variants voor multi-res ICO.
$sizes  = @(16, 24, 32, 48, 64, 128, 256)
$master = New-AppIcon -Size 256
$pngBytesPerSize = @{}
foreach ($s in $sizes) {
    if ($s -eq 256) {
        $pngBytesPerSize[$s] = Get-PngBytes -Bitmap $master
    } else {
        $scaled = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $sg = [System.Drawing.Graphics]::FromImage($scaled)
        $sg.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $sg.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $sg.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $sg.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $sg.DrawImage($master, 0, 0, $s, $s)
        $sg.Dispose()
        $pngBytesPerSize[$s] = Get-PngBytes -Bitmap $scaled
        $scaled.Dispose()
    }
}

# Save preview PNG
$previewDir = Split-Path -Parent $PreviewPng
if (-not (Test-Path $previewDir)) { New-Item -ItemType Directory -Path $previewDir -Force | Out-Null }
$master.Save($PreviewPng, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Host "Preview PNG: $PreviewPng" -ForegroundColor Cyan

# ICO file format (Vista+ PNG-encoded):
$count = $sizes.Count
$headerSize = 6 + 16 * $count
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $ms
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$count)
$offsets = @{}
$cursor = $headerSize
foreach ($s in $sizes) { $offsets[$s] = $cursor; $cursor += $pngBytesPerSize[$s].Length }
foreach ($s in $sizes) {
    $w = $s; $h = $s
    if ($s -eq 256) { $w = 0; $h = 0 }
    $bw.Write([byte]$w); $bw.Write([byte]$h); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$pngBytesPerSize[$s].Length)
    $bw.Write([uint32]$offsets[$s])
}
foreach ($s in $sizes) { $bw.Write($pngBytesPerSize[$s]) }
$bw.Flush()
$icoBytes = $ms.ToArray()
$bw.Dispose(); $ms.Dispose()

$outDir = Split-Path -Parent $OutPath
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
[System.IO.File]::WriteAllBytes($OutPath, $icoBytes)

$master.Dispose()

Write-Host ""
Write-Host "=========================================" -ForegroundColor Yellow
Write-Host "ICO geschreven: $OutPath" -ForegroundColor Green
Write-Host "Resoluties: $($sizes -join ', ') px" -ForegroundColor Yellow
Write-Host "Totale grootte: $([Math]::Round($icoBytes.Length / 1KB, 1)) KB" -ForegroundColor Yellow
