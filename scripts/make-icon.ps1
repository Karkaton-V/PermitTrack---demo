# Draws the PermitTrack app icon (navy tile, "PT", red "DEMO" corner ribbon) and writes it as a
# multi-size .ico to wwwroot/favicon.ico, used by the browser tab, the app window and the desktop shortcut.
#   powershell -ExecutionPolicy Bypass -File scripts\make-icon.ps1 [-PreviewPng path.png]

param([string]$PreviewPng)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$repo = Split-Path $PSScriptRoot -Parent
$out = Join-Path $repo 'src\PermitTrack.Web\wwwroot\favicon.ico'

function New-Brush([int]$r, [int]$g, [int]$b) { New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb($r, $g, $b)) }

# Master artwork at 256 px.
$size = 256
$master = New-Object System.Drawing.Bitmap $size, $size
$g = [System.Drawing.Graphics]::FromImage($master)
$g.SmoothingMode = 'AntiAlias'
$g.TextRenderingHint = 'AntiAliasGridFit'

$tile = New-Object System.Drawing.Drawing2D.GraphicsPath
$r = 56; $m = 6; $e = $size - $m
$tile.AddArc($m, $m, $r, $r, 180, 90); $tile.AddArc($e - $r, $m, $r, $r, 270, 90)
$tile.AddArc($e - $r, $e - $r, $r, $r, 0, 90); $tile.AddArc($m, $e - $r, $r, $r, 90, 90); $tile.CloseFigure()
$g.FillPath((New-Brush 31 78 121), $tile)

$center = New-Object System.Drawing.StringFormat
$center.Alignment = 'Center'; $center.LineAlignment = 'Center'
$ptFont = New-Object System.Drawing.Font 'Segoe UI', 108, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
$g.DrawString('PT', $ptFont, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF 0, 0, 216, 200), $center)

# Corner ribbon: a band rotated 45° across the bottom-right corner, clipped to the tile.
$g.SetClip($tile)
$g.TranslateTransform(182, 182)
$g.RotateTransform(-45)
$g.FillRectangle((New-Brush 200 16 46), -160, -27, 320, 54)
$g.FillRectangle((New-Brush 255 255 255), -160, -27, 320, 3)
$g.FillRectangle((New-Brush 255 255 255), -160, 24, 320, 3)
$ribbonFont = New-Object System.Drawing.Font 'Segoe UI', 30, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
$g.DrawString('DEMO', $ribbonFont, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF -100, -27, 200, 56), $center)
$g.Dispose()

if ($PreviewPng) { $master.Save($PreviewPng, [System.Drawing.Imaging.ImageFormat]::Png) }

# Downscale for the smaller icon sizes and store each as a PNG frame in the .ico container.
$frames = foreach ($s in 256, 64, 48, 32, 16) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $sg = [System.Drawing.Graphics]::FromImage($bmp)
    $sg.InterpolationMode = 'HighQualityBicubic'; $sg.PixelOffsetMode = 'HighQuality'
    $sg.DrawImage($master, 0, 0, $s, $s); $sg.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    [pscustomobject]@{ Size = $s; Bytes = $ms.ToArray() }
}

$ico = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $ico
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $dim = if ($f.Size -ge 256) { 0 } else { $f.Size }   # 0 means 256 in the ICO format
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$f.Bytes.Length); $w.Write([UInt32]$offset)
    $offset += $f.Bytes.Length
}
foreach ($f in $frames) { $w.Write($f.Bytes) }
$w.Flush()
[IO.File]::WriteAllBytes($out, $ico.ToArray())
Write-Host "Wrote $out"
