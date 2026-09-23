# Draws the app icons: a bonsai - a crooked trunk and two branches ending in the commit
# nodes this app is about, standing in the wide shallow pot that makes it read as a bonsai
# rather than as a tree. On a filled disc, because the old icon was thin strokes on nothing
# and someone with poor sight could not find it in the notification area.
#
# Two files, because the disc has to be the opposite of whatever is behind it:
#
#   app.ico        dark disc, pale foliage - for a light taskbar, and the icon compiled
#                  into the exe, which Explorer and SmartScreen show on light backgrounds
#   app-dark.ico   pale disc, dark foliage - for a dark taskbar
#
# The app picks between them at runtime and swaps when Windows changes theme; the one in
# the exe cannot follow the theme, which is why the darker disc is the one that goes there.
#
# Drawn natively at each size (rather than downscaled from one big bitmap) so the 16px
# tray/title-bar rendering stays sharp.
#
#   powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
param([string]$OutDir = "$PSScriptRoot\..")

Add-Type -AssemblyName System.Drawing

$sizes = 16, 20, 24, 32, 48, 64, 256

function C([int]$r, [int]$g, [int]$b) { [System.Drawing.Color]::FromArgb(255, $r, $g, $b) }

# Deep green reads against a light taskbar and under pale foliage; the near-white disc
# reads against a dark one. Measured at 3.25:1 or better against both taskbars and
# against the foliage on top, which is the bar WCAG sets for a graphic.
$deepGreen = C 20 83 45
$leaf      = C 134 239 172
$pale      = C 248 250 252

# The tree is drawn at its own size. Blowing it up towards the rim was tried and looked
# heavy: it reads as a bonsai because the pot is wide and the foliage sits above it, and
# both of those go when the drawing crowds the edge. The room came from the disc instead.
$anchorX = 0.50
$anchorY = 0.57
$fill     = 1.00

function New-IconBitmap([int]$s, [bool]$paleDisc) {
    $disc = if ($paleDisc) { $pale } else { $deepGreen }
    $tree = if ($paleDisc) { $deepGreen } else { $leaf }

    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # Normalised coords -> pixels, blown up about the anchor, so one design scales to every size.
    function SX([double]$v) { ($anchorX + ($v - $anchorX) * $fill) * $s }
    function SY([double]$v) { ($anchorY + ($v - $anchorY) * $fill) * $s }

    function Node([double]$cx, [double]$cy, [double]$r) {
        $b = New-Object System.Drawing.SolidBrush $tree
        $rr = $r * $fill * $s
        $g.FillEllipse($b, ((SX $cx) - $rr), ((SY $cy) - $rr), (2 * $rr), (2 * $rr))
        $b.Dispose()
    }
    function Limb([double]$w, $p) {
        $pen = New-Object System.Drawing.Pen $tree, ([Math]::Max(1.1, $w * $fill * $s))
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $g.DrawBezier($pen, (SX $p[0][0]), (SY $p[0][1]), (SX $p[1][0]), (SY $p[1][1]),
                            (SX $p[2][0]), (SY $p[2][1]), (SX $p[3][0]), (SY $p[3][1]))
        $pen.Dispose()
    }

    # Half a pixel of inset, not a fraction of the size. The renderer turns it into exactly
    # one pixel of clearance at every size - the disc spans 15 of 16, 63 of 64, 255 of 256 -
    # so the icon never paints right up against its own bounds. Measured: a quarter of a
    # pixel still reaches the edge, and the 2% it used to be cost a third of a pixel at 16
    # and five at 256, which is a gap nobody asked for at the size where it shows.
    $gap = 0.5
    $discBrush = New-Object System.Drawing.SolidBrush $disc
    $g.FillEllipse($discBrush, $gap, $gap, ($s - 2 * $gap), ($s - 2 * $gap))
    $discBrush.Dispose()

    # Trunk, leaning the way a bonsai's does, then a branch to each side.
    Limb 0.09 @(@(0.50,0.72), @(0.42,0.62), @(0.60,0.58), @(0.54,0.48))
    Limb 0.06 @(@(0.52,0.58), @(0.42,0.56), @(0.36,0.52), @(0.30,0.46))
    Limb 0.06 @(@(0.55,0.52), @(0.66,0.50), @(0.72,0.47), @(0.74,0.40))

    # The commit nodes, which are also the foliage.
    Node 0.55 0.33 0.135
    Node 0.27 0.42 0.115
    Node 0.76 0.36 0.115

    # The pot: a lip over a tapered body. Wide and shallow, which is the whole tell.
    $potBrush = New-Object System.Drawing.SolidBrush $tree
    $lipTop = SY 0.72
    $lipBottom = SY 0.775
    $g.FillRectangle($potBrush, (SX 0.24), $lipTop, ((SX 0.76) - (SX 0.24)), ($lipBottom - $lipTop))
    $body = @(
        (New-Object System.Drawing.PointF (SX 0.28), $lipBottom),
        (New-Object System.Drawing.PointF (SX 0.72), $lipBottom),
        (New-Object System.Drawing.PointF (SX 0.66), (SY 0.88)),
        (New-Object System.Drawing.PointF (SX 0.34), (SY 0.88)))
    $g.FillPolygon($potBrush, $body)
    $potBrush.Dispose()

    $g.Dispose()
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

function Write-Ico([string]$path, [bool]$paleDisc) {
    $frames = @()
    foreach ($s in $sizes) {
        $bmp = New-IconBitmap $s $paleDisc
        if ($s -ge 256) {
            $ms = New-Object System.IO.MemoryStream
            $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
            $bytes = $ms.ToArray(); $ms.Dispose()
        }
        else { $bytes = Get-Dib $bmp $s }
        $frames += , @{ Size = $s; Bytes = $bytes }
        $bmp.Dispose()
    }

    $fs = [System.IO.File]::Create($path)
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

    Write-Output "wrote $path ($($frames.Count) sizes, $((Get-Item $path).Length) bytes)"
}

Write-Ico (Join-Path $OutDir "app.ico") $false
Write-Ico (Join-Path $OutDir "app-dark.ico") $true
