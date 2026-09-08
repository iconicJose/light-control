param([string]$Out = (Join-Path $PSScriptRoot '..\src\app.ico'))

Add-Type -AssemblyName System.Drawing

function New-BulbDib([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $cx = $s * 0.5
    $glass = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(245, 190, 66))
    $base  = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(198, 202, 210))

    $r = $s * 0.30
    $g.FillEllipse($glass, [float]($cx - $r), [float]($s * 0.10), [float]($r * 2), [float]($r * 2))
    $pts = @(
        (New-Object System.Drawing.PointF([float]($cx - $s * 0.20), [float]($s * 0.60))),
        (New-Object System.Drawing.PointF([float]($cx + $s * 0.20), [float]($s * 0.60))),
        (New-Object System.Drawing.PointF([float]($cx + $s * 0.14), [float]($s * 0.74))),
        (New-Object System.Drawing.PointF([float]($cx - $s * 0.14), [float]($s * 0.74)))
    )
    $g.FillPolygon($glass, $pts)

    $w = $s * 0.28
    $h = [Math]::Max(1.0, $s * 0.07)
    $g.FillRectangle($base, [float]($cx - $w / 2), [float]($s * 0.75), [float]$w, [float]$h)
    $g.FillRectangle($base, [float]($cx - $w / 2), [float]($s * 0.75 + $h * 1.9), [float]$w, [float]$h)
    $g.Dispose(); $glass.Dispose(); $base.Dispose()

    $rect = New-Object System.Drawing.Rectangle(0, 0, $s, $s)
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $pixels = New-Object byte[] ($data.Stride * $s)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    $bmp.UnlockBits($data)
    $stride = $data.Stride
    $bmp.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)

    $bw.Write([UInt32]40)
    $bw.Write([Int32]$s)
    $bw.Write([Int32]($s * 2))
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]0)
    $bw.Write([UInt32]($s * $s * 4))
    $bw.Write([Int32]0); $bw.Write([Int32]0); $bw.Write([UInt32]0); $bw.Write([UInt32]0)

    for ($y = $s - 1; $y -ge 0; $y--) { $bw.Write($pixels, $y * $stride, $s * 4) }

    $maskRow = [int][Math]::Ceiling($s / 8.0)
    if ($maskRow % 4 -ne 0) { $maskRow += 4 - ($maskRow % 4) }
    $bw.Write((New-Object byte[] ($maskRow * $s)))

    $bw.Flush()
    return ,$ms.ToArray()
}

$sizes = @(16, 20, 24, 32, 48, 64, 128)
$images = New-Object System.Collections.ArrayList
foreach ($size in $sizes) { [void]$images.Add((New-BulbDib $size)) }

$fs = [System.IO.File]::Create($Out)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $d = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $bw.Write([Byte]$d); $bw.Write([Byte]$d)
    $bw.Write([Byte]0); $bw.Write([Byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$images[$i].Length)
    $bw.Write([UInt32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $bw.Write($img) }
$bw.Flush(); $bw.Close(); $fs.Close()

Write-Host "icon -> $Out"
