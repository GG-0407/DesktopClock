# Build app.ico from clock.png (keep white bg, hand-drawn style, hands at 8:18)
Add-Type -AssemblyName System.Drawing
[System.Reflection.Assembly]::LoadWithPartialName('System.Drawing') | Out-Null

$src = Join-Path (Split-Path $PSScriptRoot -Parent) 'clock.png'
$icoPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'app.ico'
if (-not (Test-Path $src)) { Write-Output "missing $src"; exit 1 }

$srcBmp = New-Object System.Drawing.Bitmap($src)
$g = [System.Drawing.Graphics]::FromImage($srcBmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

# 1) content bbox (non-white) -> center + radius
$minX = $srcBmp.Width; $minY = $srcBmp.Height; $maxX = 0; $maxY = 0
for ($y = 0; $y -lt $srcBmp.Height; $y++) {
    for ($x = 0; $x -lt $srcBmp.Width; $x++) {
        $c = $srcBmp.GetPixel($x, $y)
        if (($c.R -lt 200) -or ($c.G -lt 200) -or ($c.B -lt 200)) {
            if ($x -lt $minX) { $minX = $x }; if ($x -gt $maxX) { $maxX = $x }
            if ($y -lt $minY) { $minY = $y }; if ($y -gt $maxY) { $maxY = $y }
        }
    }
}
$cx = ($minX + $maxX) / 2.0
$cy = ($minY + $maxY) / 2.0
$radius = ([Math]::Max(($maxX - $minX), ($maxY - $minY)) / 2.0)
Write-Output ("bbox ({0},{1})-({2},{3}) center=({4:F0},{5:F0}) radius={6:F0}" -f $minX,$minY,$maxX,$maxY,$cx,$cy,$radius)

# 2) erase old hands (dark pixels within 0.72*radius -> white), keep rim ticks
$eraseR = $radius * 0.72
for ($y = 0; $y -lt $srcBmp.Height; $y++) {
    for ($x = 0; $x -lt $srcBmp.Width; $x++) {
        $dx = $x - $cx; $dy = $y - $cy
        if (($dx*$dx + $dy*$dy) -lt ($eraseR*$eraseR)) {
            $c = $srcBmp.GetPixel($x, $y)
            if (($c.R -lt 200) -or ($c.G -lt 200) -or ($c.B -lt 200)) {
                $srcBmp.SetPixel($x, $y, [System.Drawing.Color]::White)
            }
        }
    }
}

# 3) redraw hand-drawn hands pointing to 8:18
$black = [System.Drawing.Color]::FromArgb(40, 30, 30)
function DrawHand2([float]$deg, [double]$lenR, [double]$thickR) {
    $rad = ($deg - 90) * [Math]::PI / 180.0
    $L = $radius * $lenR
    $ex = $cx + $L * [Math]::Cos($rad)
    $ey = $cy + $L * [Math]::Sin($rad)
    $pen = New-Object System.Drawing.Pen($black, [float]($radius * $thickR))
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($pen, [float]$cx, [float]$cy, [float]$ex, [float]$ey)
    $pen.Dispose()
}
$hourDeg   = (8 + 18.0 / 60.0) / 12.0 * 360.0
$minuteDeg = (18.0 / 60.0) * 360.0
DrawHand2 $hourDeg   0.40 0.075
DrawHand2 $minuteDeg 0.58 0.055
$hubR = $radius * 0.06
$hub = New-Object System.Drawing.SolidBrush($black)
$g.FillEllipse($hub, [float]($cx - $hubR), [float]($cy - $hubR), [float](2 * $hubR), [float](2 * $hubR))
$hub.Dispose(); $g.Dispose()

# 4) crop away outer whitespace (keep small pad)
$pad = [int]($radius * 0.06)
$cropX = [Math]::Max(0, [int]($minX - $pad)); $cropY = [Math]::Max(0, [int]($minY - $pad))
$cropW = [Math]::Min($srcBmp.Width - $cropX, [int]($maxX - $minX + 2 * $pad))
$cropH = [Math]::Min($srcBmp.Height - $cropY, [int]($maxY - $minY + 2 * $pad))
$face = $srcBmp.Clone([System.Drawing.Rectangle]::new($cropX, $cropY, $cropW, $cropH), $srcBmp.PixelFormat)
Write-Output ("crop {0}x{1}" -f $cropW, $cropH)

# 5) pack multi-size ico
$sizes = @(16, 32, 48, 64, 128, 256)
$pngs = @{}
foreach ($size in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $gg = [System.Drawing.Graphics]::FromImage($bmp)
    $gg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $gg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $gg.DrawImage($face, 0, 0, $size, $size)
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs[$size] = $ms.ToArray()
    $ms.Dispose(); $gg.Dispose(); $bmp.Dispose()
}
$count = $sizes.Count
$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($out)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$count)
$offsets = @(); $offset = 6 + 16 * $count
foreach ($size in $sizes) { $offsets += $offset; $offset += $pngs[$size].Length }
for ($i = 0; $i -lt $count; $i++) {
    $size = $sizes[$i]
    $dim = if ($size -ge 256) { 0 } else { $size }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$pngs[$size].Length); $bw.Write([uint32]$offsets[$i])
}
foreach ($size in $sizes) { $bw.Write($pngs[$size]) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($icoPath, $out.ToArray())
$bw.Dispose(); $out.Dispose(); $face.Dispose(); $srcBmp.Dispose()
Write-Output ("Generated {0}  ({1} bytes, {2} sizes)" -f $icoPath, (Get-Item $icoPath).Length, $count)
