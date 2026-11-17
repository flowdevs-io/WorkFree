# Draws the WorkFree frog and writes the app icon (Windows only; uses System.Drawing).
#   pwsh tools/make-icon.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$assets = Join-Path $PSScriptRoot '..\src\WorkFree.Gui\Assets'
New-Item -ItemType Directory -Force $assets | Out-Null

function Brush([string]$hex) { [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml($hex)) }

# Master drawing on a 1024 canvas; every icon size is scaled down from it.
$size = 1024
$master = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($master)
$g.SmoothingMode = 'AntiAlias'
$g.Clear([System.Drawing.Color]::Transparent)

$dark = Brush '#1F7A3D'
$green = Brush '#4CC26A'
$light = Brush '#8BE29C'
$white = Brush '#FFFFFF'
$black = Brush '#10201A'
$pink = Brush '#F27A9B'

function Ellipse($brush, [float]$cx, [float]$cy, [float]$rx, [float]$ry) {
    $g.FillEllipse($brush, $cx - $rx, $cy - $ry, 2 * $rx, 2 * $ry)
}

# Outline layer, then the body on top of it.
Ellipse $dark 300 330 196 196
Ellipse $dark 724 330 196 196
Ellipse $dark 512 610 476 356
Ellipse $green 300 330 164 164
Ellipse $green 724 330 164 164
Ellipse $green 512 610 444 324
Ellipse $light 512 720 300 170

# Eyes
Ellipse $white 300 322 118 118
Ellipse $white 724 322 118 118
Ellipse $black 318 334 62 62
Ellipse $black 706 334 62 62
Ellipse $white 340 308 20 20
Ellipse $white 728 308 20 20

# Cheeks, nostrils and a wide smile
Ellipse $pink 180 640 56 36
Ellipse $pink 844 640 56 36
Ellipse $dark 468 520 13 13
Ellipse $dark 556 520 13 13
$pen = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml('#10201A'), 30)
$pen.StartCap = 'Round'
$pen.EndCap = 'Round'
$g.DrawArc($pen, 272, 400, 480, 330, 25, 130)
$g.Dispose()

function Scaled([int]$px) {
    $bmp = [System.Drawing.Bitmap]::new($px, $px, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $gg = [System.Drawing.Graphics]::FromImage($bmp)
    $gg.InterpolationMode = 'HighQualityBicubic'
    $gg.PixelOffsetMode = 'HighQuality'
    $gg.CompositingQuality = 'HighQuality'
    $gg.DrawImage($master, 0, 0, $px, $px)
    $gg.Dispose()
    $ms = [System.IO.MemoryStream]::new()
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}

[System.IO.File]::WriteAllBytes((Join-Path $assets 'workfree.png'), (Scaled 256))

# .ico container holding one PNG per size.
$sizes = 16, 24, 32, 48, 64, 128, 256
$images = $sizes | ForEach-Object { , (Scaled $_) }
$out = [System.IO.MemoryStream]::new()
$w = [System.IO.BinaryWriter]::new($out)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $assets 'workfree.ico'), $out.ToArray())
$master.Dispose()
"Wrote workfree.ico and workfree.png to $assets"
