# 6 varianten gebaseerd op #21 (Fluent download icon) en #22 (thin V-chevron)
# met indicator IN de front card én een nieuwe verticale stack layout (cards
# onder elkaar zonder overlap).

[CmdletBinding()]
param(
    [string]$OutDir = "$PSScriptRoot\..\data\icon-variants"
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }
Get-ChildItem $OutDir -Filter '*.png' -ErrorAction SilentlyContinue | Remove-Item -Force

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

# ===== Card stack layouts =====

# Straight stack — 3 cards overlappen elkaar, klassieke stacked look
function Draw-StraightStack {
    param([System.Drawing.Graphics]$G, [single]$f, [single]$YOffset = 0)
    $blueLight = [System.Drawing.Color]::FromArgb(255, 138, 188, 250)
    $blueMid1  = [System.Drawing.Color]::FromArgb(255,  74, 142, 226)
    $blueMid2  = [System.Drawing.Color]::FromArgb(255,  44, 105, 196)
    $blueDark1 = [System.Drawing.Color]::FromArgb(255,  24,  72, 160)
    [single]$cR = 2.5 * $f
    [single]$cW = 28.0 * $f; [single]$cH = 20.0 * $f

    [single]$c3X = 6.0 * $f; [single]$c3Y = (16.0 * $f) + $YOffset
    $c3Path = New-RoundedRectPath $c3X $c3Y $cW $cH $cR
    Add-DropShadow -G $G -Path $c3Path -OffsetX 0 -OffsetY ($f * 0.6) -Spread ($f * 1.5) -Opacity 110
    $c3Brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.RectangleF($c3X, $c3Y, $cW, $cH)), $blueMid1, $blueDark1, 120.0)
    $G.FillPath($c3Brush, $c3Path); $c3Brush.Dispose(); $c3Path.Dispose()

    [single]$c2X = 10.0 * $f; [single]$c2Y = (20.0 * $f) + $YOffset
    $c2Path = New-RoundedRectPath $c2X $c2Y $cW $cH $cR
    Add-DropShadow -G $G -Path $c2Path -OffsetX 0 -OffsetY ($f * 0.6) -Spread ($f * 1.5) -Opacity 110
    $c2Brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.RectangleF($c2X, $c2Y, $cW, $cH)), $blueLight, $blueMid2, 120.0)
    $G.FillPath($c2Brush, $c2Path); $c2Brush.Dispose(); $c2Path.Dispose()

    [single]$c1X = 14.0 * $f; [single]$c1Y = (24.0 * $f) + $YOffset
    $c1Path = New-RoundedRectPath $c1X $c1Y $cW $cH $cR
    Add-DropShadow -G $G -Path $c1Path -OffsetX 0 -OffsetY ($f * 0.7) -Spread ($f * 1.7) -Opacity 120
    $c1Brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.RectangleF($c1X, $c1Y, $cW, $cH)),
        ([System.Drawing.Color]::FromArgb(255, 230, 240, 254)), $blueLight, 120.0)
    $G.FillPath($c1Brush, $c1Path); $c1Brush.Dispose(); $c1Path.Dispose()

    return @{ FrontX = $c1X; FrontY = $c1Y; FrontW = $cW; FrontH = $cH }
}

# Tilted (Photos-style)
function Draw-TiltedStack {
    param([System.Drawing.Graphics]$G, [single]$f, [single]$YOffset = 0)
    $blueLight = [System.Drawing.Color]::FromArgb(255, 138, 188, 250)
    $blueMid1  = [System.Drawing.Color]::FromArgb(255,  74, 142, 226)
    $blueMid2  = [System.Drawing.Color]::FromArgb(255,  44, 105, 196)
    $blueDark1 = [System.Drawing.Color]::FromArgb(255,  24,  72, 160)
    [single]$cR = 2.5 * $f
    [single]$cW = 26.0 * $f; [single]$cH = 18.0 * $f

    $G.TranslateTransform([single](14 * $f), [single]((18 * $f) + $YOffset))
    $G.RotateTransform(-10.0)
    $p3 = New-RoundedRectPath 0 0 $cW $cH $cR
    Add-DropShadow -G $G -Path $p3 -OffsetX 0 -OffsetY ($f * 0.6) -Spread ($f * 1.5) -Opacity 110
    $b3 = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.RectangleF(0, 0, $cW, $cH)), $blueMid1, $blueDark1, 120.0)
    $G.FillPath($b3, $p3); $b3.Dispose(); $p3.Dispose()
    $G.ResetTransform()

    $G.TranslateTransform([single](16 * $f), [single]((22 * $f) + $YOffset))
    $G.RotateTransform(5.0)
    $p2 = New-RoundedRectPath 0 0 $cW $cH $cR
    Add-DropShadow -G $G -Path $p2 -OffsetX 0 -OffsetY ($f * 0.6) -Spread ($f * 1.5) -Opacity 110
    $b2 = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.RectangleF(0, 0, $cW, $cH)), $blueLight, $blueMid2, 120.0)
    $G.FillPath($b2, $p2); $b2.Dispose(); $p2.Dispose()
    $G.ResetTransform()

    [single]$c1X = 11.0 * $f; [single]$c1Y = (26.0 * $f) + $YOffset
    $p1 = New-RoundedRectPath $c1X $c1Y $cW $cH $cR
    Add-DropShadow -G $G -Path $p1 -OffsetX 0 -OffsetY ($f * 0.7) -Spread ($f * 1.7) -Opacity 120
    $b1 = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.RectangleF($c1X, $c1Y, $cW, $cH)),
        ([System.Drawing.Color]::FromArgb(255, 230, 240, 254)), $blueLight, 120.0)
    $G.FillPath($b1, $p1); $b1.Dispose(); $p1.Dispose()

    return @{ FrontX = $c1X; FrontY = $c1Y; FrontW = $cW; FrontH = $cH }
}

# Vertical stack — 3 cards onder elkaar zonder overlap, gecentreerd
# Top card = "highlight" (witter), eronder middel & onderste in donkerder blauw
function Draw-VerticalStack {
    param([System.Drawing.Graphics]$G, [single]$f, [single]$YOffset = 0)
    $blueLight = [System.Drawing.Color]::FromArgb(255, 138, 188, 250)
    $blueMid1  = [System.Drawing.Color]::FromArgb(255,  74, 142, 226)
    $blueMid2  = [System.Drawing.Color]::FromArgb(255,  44, 105, 196)
    $blueDark1 = [System.Drawing.Color]::FromArgb(255,  24,  72, 160)
    [single]$cR = 2.5 * $f
    [single]$cW = 30.0 * $f
    [single]$cH = 9.0 * $f
    [single]$gap = 2.0 * $f
    [single]$cX = (48 * $f - $cW) / 2.0  # gecentreerd

    # Top card (witter) — wordt de "front"/highlighted card
    [single]$y1 = (10.0 * $f) + $YOffset
    $p1 = New-RoundedRectPath $cX $y1 $cW $cH $cR
    Add-DropShadow -G $G -Path $p1 -OffsetX 0 -OffsetY ($f * 0.5) -Spread ($f * 1.3) -Opacity 110
    $b1 = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.RectangleF($cX, $y1, $cW, $cH)),
        ([System.Drawing.Color]::FromArgb(255, 230, 240, 254)), $blueLight, 120.0)
    $G.FillPath($b1, $p1); $b1.Dispose(); $p1.Dispose()

    # Middle card
    [single]$y2 = $y1 + $cH + $gap
    $p2 = New-RoundedRectPath $cX $y2 $cW $cH $cR
    Add-DropShadow -G $G -Path $p2 -OffsetX 0 -OffsetY ($f * 0.5) -Spread ($f * 1.3) -Opacity 110
    $b2 = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.RectangleF($cX, $y2, $cW, $cH)), $blueLight, $blueMid2, 120.0)
    $G.FillPath($b2, $p2); $b2.Dispose(); $p2.Dispose()

    # Bottom card (darkest)
    [single]$y3 = $y2 + $cH + $gap
    $p3 = New-RoundedRectPath $cX $y3 $cW $cH $cR
    Add-DropShadow -G $G -Path $p3 -OffsetX 0 -OffsetY ($f * 0.5) -Spread ($f * 1.3) -Opacity 110
    $b3 = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.RectangleF($cX, $y3, $cW, $cH)), $blueMid1, $blueDark1, 120.0)
    $G.FillPath($b3, $p3); $b3.Dispose(); $p3.Dispose()

    return @{ FrontX = $cX; FrontY = $y1; FrontW = $cW; FrontH = $cH;
              StackTopY = $y1; StackBotY = $y3 + $cH }
}

function New-Canvas {
    param([int]$Size)
    $bmp = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    return ,@($bmp, $g)
}

# Kleuren
$accentDeepBlue   = [System.Drawing.Color]::FromArgb(255,  10,  60, 160)
$accentBrightBlue = [System.Drawing.Color]::FromArgb(255,  20, 110, 230)

# ===== Indicator helpers =====

# Fluent download icon (verticale shaft + V-tip + horizontale tray-lijn)
function Draw-FluentDownloadIcon {
    param([System.Drawing.Graphics]$G, [single]$Cx, [single]$Cy, [single]$Size, [single]$f, [System.Drawing.Color]$Color, [single]$StrokeFactor = 1.6)
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
    $G.DrawLine($pen, $Cx, $arrowTopY, $Cx, $arrowTipY)
    $G.DrawLines($pen, [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF([single]($Cx - $arrowHalfW), [single]($arrowTipY - $arrowHalfW * 0.85))),
        (New-Object System.Drawing.PointF($Cx, $arrowTipY)),
        (New-Object System.Drawing.PointF([single]($Cx + $arrowHalfW), [single]($arrowTipY - $arrowHalfW * 0.85)))
    ))
    $G.DrawLine($pen, [single]($Cx - $trayHalfW), $trayY, [single]($Cx + $trayHalfW), $trayY)
    $pen.Dispose()
}

# Modern flat solid down-arrow (polygon shaft + chevron tip), gevuld zonder outline
function Draw-FlatDownArrow {
    param([System.Drawing.Graphics]$G, [single]$Cx, [single]$TopY, [single]$BotY, [single]$Width, [single]$f, [System.Drawing.Color]$ColorA, [System.Drawing.Color]$ColorB, [bool]$Shadow = $true)
    [single]$shaftHW = $Width * 0.16
    [single]$chevHW  = $Width * 0.5
    [single]$shaftBotY = $TopY + ($BotY - $TopY) * 0.55
    $pts = [System.Drawing.PointF[]]::new(7)
    $pts[0] = New-Object System.Drawing.PointF([single]($Cx - $shaftHW), $TopY)
    $pts[1] = New-Object System.Drawing.PointF([single]($Cx + $shaftHW), $TopY)
    $pts[2] = New-Object System.Drawing.PointF([single]($Cx + $shaftHW), $shaftBotY)
    $pts[3] = New-Object System.Drawing.PointF([single]($Cx + $chevHW),  $shaftBotY)
    $pts[4] = New-Object System.Drawing.PointF($Cx, $BotY)
    $pts[5] = New-Object System.Drawing.PointF([single]($Cx - $chevHW),  $shaftBotY)
    $pts[6] = New-Object System.Drawing.PointF([single]($Cx - $shaftHW), $shaftBotY)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddPolygon($pts)
    if ($Shadow) {
        Add-DropShadow -G $G -Path $path -OffsetX 0 -OffsetY ($f * 0.4) -Spread ($f * 1.2) -Opacity 130
    }
    $rect = New-Object System.Drawing.RectangleF([single]($Cx - $chevHW), $TopY, [single]($chevHW * 2), [single]($BotY - $TopY))
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $ColorA, $ColorB, 120.0)
    $G.FillPath($brush, $path); $brush.Dispose(); $path.Dispose()
}

# Thin V-chevron met horizontale lijn eronder (geen verticale shaft)
# Cleaner / simpeler dan Fluent download icon — alleen V + tray-lijn
function Draw-ChevronWithLine {
    param([System.Drawing.Graphics]$G, [single]$Cx, [single]$Cy, [single]$Size, [single]$f, [System.Drawing.Color]$Color, [single]$StrokeFactor = 2.0)
    [single]$strokeW = $f * $StrokeFactor
    $pen = New-Object System.Drawing.Pen $Color, $strokeW
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    [single]$chevHalfW = $Size * 0.32
    [single]$lineHalfW = $Size * 0.42
    [single]$chevTopY  = $Cy - $Size * 0.32
    [single]$chevTipY  = $Cy + $Size * 0.10
    [single]$lineY     = $Cy + $Size * 0.38
    $G.DrawLines($pen, [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF([single]($Cx - $chevHalfW), $chevTopY)),
        (New-Object System.Drawing.PointF($Cx, $chevTipY)),
        (New-Object System.Drawing.PointF([single]($Cx + $chevHalfW), $chevTopY))
    ))
    $G.DrawLine($pen, [single]($Cx - $lineHalfW), $lineY, [single]($Cx + $lineHalfW), $lineY)
    $pen.Dispose()
}

# ===== 6 Varianten =====

# 29: Tilted stack + Fluent download icon (arrow+shaft+tray) IN front card
# Size matched to #36 (14 * f absoluut) zodat de pijl even groot oogt op
# beide layouts ondanks verschillende front-card hoogtes (tilted=18f, straight=20f).
function Draw-V29 {
    param([System.Drawing.Graphics]$G, [single]$f)
    $info = Draw-TiltedStack -G $G -f $f -YOffset ([single](-2 * $f))
    [single]$cx = $info.FrontX + $info.FrontW / 2.0
    [single]$cy = $info.FrontY + $info.FrontH / 2.0
    Draw-FluentDownloadIcon -G $G -Cx $cx -Cy $cy -Size ([single](14.0 * $f)) -f $f -Color $accentDeepBlue -StrokeFactor 1.7
}

# 30: Tilted stack + V-chevron + tray-lijn IN front card
function Draw-V30 {
    param([System.Drawing.Graphics]$G, [single]$f)
    $info = Draw-TiltedStack -G $G -f $f -YOffset ([single](-2 * $f))
    [single]$cx = $info.FrontX + $info.FrontW / 2.0
    [single]$cy = $info.FrontY + $info.FrontH / 2.0
    Draw-ChevronWithLine -G $G -Cx $cx -Cy $cy -Size ([single]($info.FrontH * 0.85)) -f $f -Color $accentDeepBlue -StrokeFactor 2.0
}

# 31: Vertical stack (3 cards onder elkaar) + Fluent download icon ABOVE
function Draw-V31 {
    param([System.Drawing.Graphics]$G, [single]$f)
    Draw-VerticalStack -G $G -f $f -YOffset ([single](2 * $f)) | Out-Null
    Draw-FluentDownloadIcon -G $G -Cx ([single](24 * $f)) -Cy ([single](6 * $f)) -Size ([single](7 * $f)) -f $f -Color $accentBrightBlue -StrokeFactor 1.7
}

# 32: Vertical stack + Fluent download icon IN top card
function Draw-V32 {
    param([System.Drawing.Graphics]$G, [single]$f)
    $info = Draw-VerticalStack -G $G -f $f -YOffset ([single](-2 * $f))
    [single]$cx = $info.FrontX + $info.FrontW / 2.0
    [single]$cy = $info.FrontY + $info.FrontH / 2.0
    Draw-FluentDownloadIcon -G $G -Cx $cx -Cy $cy -Size ([single]($info.FrontH * 0.95)) -f $f -Color $accentDeepBlue -StrokeFactor 1.7
}

# 33: Vertical stack + V-chevron + tray-lijn ABOVE
function Draw-V33 {
    param([System.Drawing.Graphics]$G, [single]$f)
    Draw-VerticalStack -G $G -f $f -YOffset ([single](2 * $f)) | Out-Null
    Draw-ChevronWithLine -G $G -Cx ([single](24 * $f)) -Cy ([single](5.5 * $f)) -Size ([single](7 * $f)) -f $f -Color $accentBrightBlue -StrokeFactor 2.0
}

# 34: Vertical stack + V-chevron + tray-lijn IN top card
function Draw-V34 {
    param([System.Drawing.Graphics]$G, [single]$f)
    $info = Draw-VerticalStack -G $G -f $f -YOffset ([single](-2 * $f))
    [single]$cx = $info.FrontX + $info.FrontW / 2.0
    [single]$cy = $info.FrontY + $info.FrontH / 2.0
    Draw-ChevronWithLine -G $G -Cx $cx -Cy $cy -Size ([single]($info.FrontH * 0.95)) -f $f -Color $accentDeepBlue -StrokeFactor 2.0
}

# 35: Straight stack + Fluent download icon ABOVE
function Draw-V35 {
    param([System.Drawing.Graphics]$G, [single]$f)
    Draw-StraightStack -G $G -f $f -YOffset ([single](2 * $f)) | Out-Null
    Draw-FluentDownloadIcon -G $G -Cx ([single](24 * $f)) -Cy ([single](9 * $f)) -Size ([single](11 * $f)) -f $f -Color $accentBrightBlue -StrokeFactor 1.7
}

# 36: Straight stack + Fluent download icon IN front card
function Draw-V36 {
    param([System.Drawing.Graphics]$G, [single]$f)
    $info = Draw-StraightStack -G $G -f $f
    [single]$cx = $info.FrontX + $info.FrontW / 2.0
    [single]$cy = $info.FrontY + $info.FrontH / 2.0
    Draw-FluentDownloadIcon -G $G -Cx $cx -Cy $cy -Size ([single]($info.FrontH * 0.7)) -f $f -Color $accentDeepBlue -StrokeFactor 1.7
}

# 37: Straight stack + V-chevron + tray-lijn ABOVE
function Draw-V37 {
    param([System.Drawing.Graphics]$G, [single]$f)
    Draw-StraightStack -G $G -f $f -YOffset ([single](2 * $f)) | Out-Null
    Draw-ChevronWithLine -G $G -Cx ([single](24 * $f)) -Cy ([single](9 * $f)) -Size ([single](11 * $f)) -f $f -Color $accentBrightBlue -StrokeFactor 2.0
}

# 38: Straight stack + V-chevron + tray-lijn IN front card
function Draw-V38 {
    param([System.Drawing.Graphics]$G, [single]$f)
    $info = Draw-StraightStack -G $G -f $f
    [single]$cx = $info.FrontX + $info.FrontW / 2.0
    [single]$cy = $info.FrontY + $info.FrontH / 2.0
    Draw-ChevronWithLine -G $G -Cx $cx -Cy $cy -Size ([single]($info.FrontH * 0.7)) -f $f -Color $accentDeepBlue -StrokeFactor 2.0
}

# 20: Straight stack + filled polygon arrow IN front card (uit eerdere batch)
function Draw-V20 {
    param([System.Drawing.Graphics]$G, [single]$f)
    $info = Draw-StraightStack -G $G -f $f
    [single]$cx = $info.FrontX + $info.FrontW / 2.0
    [single]$cy = $info.FrontY + $info.FrontH / 2.0
    [single]$h = $info.FrontH * 0.55
    Draw-FlatDownArrow -G $G -Cx $cx -TopY ([single]($cy - $h/2)) -BotY ([single]($cy + $h/2)) -Width ([single]($info.FrontW * 0.4)) -f $f -ColorA $accentBrightBlue -ColorB $accentDeepBlue -Shadow $false
}

# ===== Genereer =====
$variants = @(
    @{ Name = '20_straight-flat-arrow-on-front'; Draw = ${function:Draw-V20}; Desc = 'Straight + filled polygon arrow IN front' },
    @{ Name = '29_tilt-fluent-in-front';        Draw = ${function:Draw-V29}; Desc = 'Tilted + Fluent (arrow+shaft+tray) IN front' },
    @{ Name = '36_straight-fluent-in-front';    Draw = ${function:Draw-V36}; Desc = 'Straight + Fluent (arrow+shaft+tray) IN front' },
    @{ Name = '38_straight-chev-line-in-front'; Draw = ${function:Draw-V38}; Desc = 'Straight + V-chevron + tray-lijn IN front' }
)

[int]$Size = 256
foreach ($v in $variants) {
    $arr = New-Canvas -Size $Size
    $bmp = $arr[0]; $g = $arr[1]
    [single]$f = [single]$Size / 48.0
    & $v.Draw -G $g -f $f
    $g.Dispose()
    $outFile = Join-Path $OutDir "$($v.Name).png"
    $bmp.Save($outFile, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "  $($v.Name): $($v.Desc)" -ForegroundColor Green
}
Write-Host ""
Write-Host "6 varianten in $OutDir" -ForegroundColor Yellow
