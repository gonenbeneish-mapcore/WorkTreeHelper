# Draws app.ico: a git-branch tree - a trunk splitting into branches that end in
# commit nodes. Drawn natively at each size (rather than downscaled from one big
# bitmap) so the 16px tray/title-bar rendering stays sharp.
#
#   powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
param([string]$OutFile = "$PSScriptRoot\..\app.ico")

Add-Type -AssemblyName System.Drawing

$sizes = 16, 20, 24, 32, 48, 64, 256

# Mid-tone green: saturated enough to read on a light taskbar, bright enough on a dark one.
$limb = [System.Drawing.Color]::FromArgb(255, 46, 163, 107)
$node = [System.Drawing.Color]::FromArgb(255, 74, 222, 128)

function New-IconBitmap([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # Normalised coords -> pixels, so one design scales to every size.
    function X([double]$v) { $v * $s }

    $limbW = [Math]::Max(1.5, 0.09 * $s)
    $pen = New-Object System.Drawing.Pen $limb, $limbW
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    # Trunk
    $g.DrawLine($pen, (X 0.5), (X 0.90), (X 0.5), (X 0.22))
    # Left branch, curving out of the trunk
    $g.DrawBezier($pen, (X 0.5), (X 0.68), (X 0.5), (X 0.54), (X 0.24), (X 0.62), (X 0.20), (X 0.46))
    # Right branch
    $g.DrawBezier($pen, (X 0.5), (X 0.56), (X 0.5), (X 0.42), (X 0.78), (X 0.50), (X 0.80), (X 0.34))

    # Commit nodes at each branch tip
    $brush = New-Object System.Drawing.SolidBrush $node
    $r = 0.115 * $s
    foreach ($p in @(@(0.5, 0.22), @(0.20, 0.46), @(0.80, 0.34))) {
        $cx = X $p[0]; $cy = X $p[1]
        $g.FillEllipse($brush, ($cx - $r), ($cy - $r), (2 * $r), (2 * $r))
    }

    $brush.Dispose(); $pen.Dispose(); $g.Dispose()
    return $bmp
}

# Pack the frames into a multi-resolution .ico.
# Sub-256 frames must be DIBs (BITMAPINFOHEADER + bottom-up BGRA + AND mask):
# GDI+ reads those entries as raw DIB regardless of content, so a PNG there
# decodes as noise. Only the 256 frame is PNG, which every modern shell reads.
function Get-Dib([System.Drawing.Bitmap]$bmp, [int]$s) {
    $rect = New-Object System.Drawing.Rectangle 0, 0, $s, $s
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
                          [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $buf = New-Object byte[] ($data.Stride * $s)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $buf, 0, $buf.Length)
    $stride = $data.Stride
    $bmp.UnlockBits($data)

    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $ms
    $w.Write([uint32]40); $w.Write([int32]$s); $w.Write([int32](2 * $s))
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]0)
    $w.Write([uint32]0); $w.Write([int32]0); $w.Write([int32]0)
    $w.Write([uint32]0); $w.Write([uint32]0)
    # XOR bitmap, bottom-up
    for ($y = $s - 1; $y -ge 0; $y--) { $w.Write($buf, $y * $stride, $s * 4) }
    # AND mask: unused with 32bpp alpha, but the rows must still be present
    $maskStride = [int][Math]::Ceiling($s / 32.0) * 4
    $w.Write((New-Object byte[] ($maskStride * $s)))
    $w.Flush()
    $bytes = $ms.ToArray()
    $w.Dispose(); $ms.Dispose()
    # Comma-wrapped: PowerShell unrolls a returned byte[] into the pipeline.
    return , $bytes
}

$frames = @()
foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    if ($s -ge 256) {
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bytes = $ms.ToArray(); $ms.Dispose()
    }
    else { $bytes = Get-Dib $bmp $s }
    $frames += , @{ Size = $s; Bytes = $bytes }
    $bmp.Dispose()
}

$fs = [System.IO.File]::Create($OutFile)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $dim = if ($f.Size -ge 256) { 0 } else { $f.Size }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim)
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$f.Bytes.Length); $bw.Write([uint32]$offset)
    $offset += $f.Bytes.Length
}
foreach ($f in $frames) { $bw.Write($f.Bytes) }
$bw.Flush(); $bw.Dispose(); $fs.Dispose()

Write-Output "wrote $OutFile ($($frames.Count) sizes, $((Get-Item $OutFile).Length) bytes)"
