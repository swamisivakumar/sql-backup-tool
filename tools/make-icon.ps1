param([string]$OutIco, [string]$OutPng)
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

function New-RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Draw-Icon([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.Clear([System.Drawing.Color]::Transparent)

    # --- tile background (blue gradient)
    $m = [Math]::Max(0.5, $s * 0.03)
    $tile = New-RoundRect $m $m ($s - 2 * $m) ($s - 2 * $m) ($s * 0.2)
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new(0, 0)), ([System.Drawing.PointF]::new($s, $s)),
        ([System.Drawing.Color]::FromArgb(255, 59, 130, 246)), ([System.Drawing.Color]::FromArgb(255, 30, 58, 138))
    $g.FillPath($bg, $tile)

    # --- database cylinder (white)
    $cx = $s * 0.43; $cw = $s * 0.54; $x = $cx - $cw / 2
    $top = $s * 0.17; $eh = $s * 0.15; $bottom = $s * 0.76
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $g.FillRectangle($white, $x, $top + $eh / 2, $cw, $bottom - $top - $eh / 2)
    $g.FillEllipse($white, $x, $bottom - $eh / 2, $cw, $eh)
    $lid = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 191, 219, 254))
    $g.FillEllipse($lid, $x, $top, $cw, $eh)
    $band = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 37, 99, 235)), ([Math]::Max(1.0, $s * 0.035))
    $g.DrawEllipse($band, $x, $top, $cw, $eh)
    $bands = if ($s -ge 32) { 2 } else { 1 }
    for ($i = 1; $i -le $bands; $i++) {
        $yy = $top + ($bottom - $top) * $i / ($bands + 1)
        $g.DrawArc($band, $x, $yy, $cw, $eh, 0, 180)
    }

    # --- green badge with down arrow (backup)
    $br = $s * 0.215; $bx = $s * 0.73; $by = $s * 0.73
    $ring = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $rw = [Math]::Max(1.0, $s * 0.04)
    $g.FillEllipse($ring, $bx - $br - $rw, $by - $br - $rw, 2 * ($br + $rw), 2 * ($br + $rw))
    $green = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new($bx, $by - $br)), ([System.Drawing.PointF]::new($bx, $by + $br)),
        ([System.Drawing.Color]::FromArgb(255, 52, 211, 153)), ([System.Drawing.Color]::FromArgb(255, 5, 150, 105))
    $g.FillEllipse($green, $bx - $br, $by - $br, 2 * $br, 2 * $br)
    $aw = $br * 0.42; $ah = $br * 0.55; $sw = $br * 0.2
    $pts = @(
        [System.Drawing.PointF]::new($bx - $sw, $by - $br * 0.6),
        [System.Drawing.PointF]::new($bx + $sw, $by - $br * 0.6),
        [System.Drawing.PointF]::new($bx + $sw, $by - $br * 0.05),
        [System.Drawing.PointF]::new($bx + $aw * 1.25, $by - $br * 0.05),
        [System.Drawing.PointF]::new($bx, $by + $ah),
        [System.Drawing.PointF]::new($bx - $aw * 1.25, $by - $br * 0.05),
        [System.Drawing.PointF]::new($bx - $sw, $by - $br * 0.05)
    )
    $g.FillPolygon($white, $pts)
    $g.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$frames = foreach ($s in $sizes) {
    $bmp = Draw-Icon $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($s -eq 256 -and $OutPng) { $bmp.Save($OutPng, [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
    , $ms.ToArray()
}

$fs = [System.IO.File]::Create($OutIco)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $frames[$i].Length
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$len); $w.Write([UInt32]$offset)
    $offset += $len
}
foreach ($f in $frames) { $w.Write($f) }
$w.Close()
"Wrote $OutIco ($((Get-Item $OutIco).Length) bytes)"
