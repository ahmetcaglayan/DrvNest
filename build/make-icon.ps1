<#
.SYNOPSIS
    Generates src/Hexnest.App/Assets/hexnest.ico from scratch.

.DESCRIPTION
    Draws the Hexnest mark programmatically with System.Drawing (GDI+) at
    16, 24, 32, 48, 64, 128 and 256 px, then writes a single multi-resolution
    .ico file by hand (ICONDIR + ICONDIRENTRY records + PNG payloads).

    No external tooling is required -- only Windows and the .NET Framework
    assemblies that ship with it. PowerShell 5.1 compatible.

.NOTES
    Every entry is stored PNG-compressed. Windows has supported PNG-compressed
    icon entries since Vista.
#>

[CmdletBinding()]
param(
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

Add-Type -AssemblyName System.Drawing

# ---------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $repoRoot   = Split-Path -Parent $PSScriptRoot
    $OutputPath = Join-Path $repoRoot 'src\Hexnest.App\Assets\hexnest.ico'
}

$outputDir = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

# ---------------------------------------------------------------------------
# Brand palette
# ---------------------------------------------------------------------------

function ConvertTo-Color([string] $hex) {
    return [System.Drawing.ColorTranslator]::FromHtml($hex)
}

$SurfaceTop    = ConvertTo-Color '#1C232D'
$SurfaceMid    = ConvertTo-Color '#12171E'
$SurfaceBottom = ConvertTo-Color '#0B0E12'
$BorderColor   = ConvertTo-Color '#2A323D'
$MintLight     = ConvertTo-Color '#5BF0BF'
$MintBase      = ConvertTo-Color '#35D8A4'
$MintDeep      = ConvertTo-Color '#22B98A'
$ChipLight     = ConvertTo-Color '#6BF3CA'
$ChipDeep      = ConvertTo-Color '#2AC694'
$DieColor      = ConvertTo-Color '#0B1F19'

# ---------------------------------------------------------------------------
# Geometry helpers -- all coordinates are expressed on the 256x256 design grid
# used by assets/logo.svg, then scaled to the requested canvas.
# ---------------------------------------------------------------------------

function New-RoundedRectPath {
    param(
        [single] $X, [single] $Y, [single] $W, [single] $H, [single] $R
    )
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [single]($R * 2.0)
    if ($d -le 0) {
        $path.AddRectangle((New-Object System.Drawing.RectangleF($X, $Y, $W, $H)))
        return $path
    }
    $path.AddArc([single]$X,             [single]$Y,             $d, $d, [single]180, [single]90)
    $path.AddArc([single]($X + $W - $d), [single]$Y,             $d, $d, [single]270, [single]90)
    $path.AddArc([single]($X + $W - $d), [single]($Y + $H - $d), $d, $d, [single]0,   [single]90)
    $path.AddArc([single]$X,             [single]($Y + $H - $d), $d, $d, [single]90,  [single]90)
    $path.CloseFigure()
    return $path
}

function New-LinearBrush {
    param(
        [System.Drawing.RectangleF] $Bounds,
        [System.Drawing.Color] $From,
        [System.Drawing.Color] $To,
        [System.Drawing.Color] $Mid = ([System.Drawing.Color]::Empty),
        [single] $Angle = 45
    )
    # Inflate slightly: GDI+ linear gradients can wrap at the exact edge pixel.
    $b = $Bounds
    $b.Inflate([single]2, [single]2)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($b, $From, $To, $Angle)
    $brush.WrapMode = [System.Drawing.Drawing2D.WrapMode]::TileFlipXY
    if (-not $Mid.IsEmpty) {
        $blend = New-Object System.Drawing.Drawing2D.ColorBlend(3)
        $blend.Colors    = @($From, $Mid, $To)
        $blend.Positions = @([single]0.0, [single]0.55, [single]1.0)
        $brush.InterpolationColors = $blend
    }
    return $brush
}

function New-HexagonPath {
    # Flat-top hexagon on the 256 grid, centred at (128,128).
    param([single] $Radius)

    $dx = [single]($Radius * 0.5)
    $dy = [single]($Radius * 0.8660254)
    $pts = @(
        (New-Object System.Drawing.PointF([single](128 + $Radius), [single]128)),
        (New-Object System.Drawing.PointF([single](128 + $dx),     [single](128 + $dy))),
        (New-Object System.Drawing.PointF([single](128 - $dx),     [single](128 + $dy))),
        (New-Object System.Drawing.PointF([single](128 - $Radius), [single]128)),
        (New-Object System.Drawing.PointF([single](128 - $dx),     [single](128 - $dy))),
        (New-Object System.Drawing.PointF([single](128 + $dx),     [single](128 - $dy)))
    )
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddPolygon([System.Drawing.PointF[]]$pts)
    return $path
}

function Get-MarkGeometry {
    <#
        Optical scaling: below 32 px the 15-unit nest stroke lands on less than
        one physical pixel and the whole mark turns to mud. Small entries get a
        bolder, progressively simpler mark instead of a linear reduction.
    #>
    param([int] $Size)

    if ($Size -le 16) {
        return @{
            HexRadius = [single]76;  HexStroke = [single]26
            Pins = $false;           Border    = $false
            Chip = @(107, 107, 42, 10)
            Die  = $null
        }
    }
    if ($Size -lt 32) {
        return @{
            HexRadius = [single]72;  HexStroke = [single]22
            Pins = $false;           Border    = $false
            Chip = @(103, 103, 50, 12)
            Die  = @(118, 118, 20, 5)
        }
    }
    return @{
        HexRadius = [single]68;      HexStroke = [single]15
        Pins = $true;                Border    = ($Size -ge 48)
        Chip = @(99, 99, 58, 14)
        Die  = @(116, 116, 24, 6)
    }
}

function New-MarkBitmap {
    <#
        Renders the mark into a square bitmap of the requested pixel size.
        $Simplify drops hairline detail that only turns to mud below 32 px.
    #>
    param(
        [int] $Size,
        [hashtable] $Geometry
    )

    $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $g.Clear([System.Drawing.Color]::Transparent)

        $scale = [single]($Size / 256.0)
        $g.ScaleTransform($scale, $scale)

        # --- container -----------------------------------------------------
        $containerRect = New-Object System.Drawing.RectangleF([single]6, [single]6, [single]244, [single]244)
        $containerPath = New-RoundedRectPath -X 6 -Y 6 -W 244 -H 244 -R 56
        $surfaceBrush  = New-LinearBrush -Bounds $containerRect -From $SurfaceTop -To $SurfaceBottom -Mid $SurfaceMid
        $g.FillPath($surfaceBrush, $containerPath)
        $surfaceBrush.Dispose()

        if ($Geometry.Border) {
            $borderPath = New-RoundedRectPath -X 7 -Y 7 -W 242 -H 242 -R 55
            $borderPen  = New-Object System.Drawing.Pen($BorderColor, [single]2)
            $g.DrawPath($borderPen, $borderPath)
            $borderPen.Dispose()
            $borderPath.Dispose()
        }
        $containerPath.Dispose()

        # --- mint gradient (shared by pins + nest ring) --------------------
        $markRect  = New-Object System.Drawing.RectangleF([single]46, [single]46, [single]164, [single]164)
        $mintBrush = New-LinearBrush -Bounds $markRect -From $MintLight -To $MintDeep -Mid $MintBase

        # --- chip pins -----------------------------------------------------
        if ($Geometry.Pins) {
            $pins = @(
                @(105, 46), @(137, 46), @(105, 190), @(137, 190)
            )
            foreach ($pin in $pins) {
                $pinPath = New-RoundedRectPath -X $pin[0] -Y $pin[1] -W 14 -H 20 -R 5
                $g.FillPath($mintBrush, $pinPath)
                $pinPath.Dispose()
            }
        }

        # --- nest cell -----------------------------------------------------
        $hexPath = New-HexagonPath -Radius $Geometry.HexRadius
        $hexPen  = New-Object System.Drawing.Pen($mintBrush, $Geometry.HexStroke)
        $hexPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $g.DrawPath($hexPen, $hexPath)
        $hexPen.Dispose()
        $hexPath.Dispose()
        $mintBrush.Dispose()

        # --- chip body -----------------------------------------------------
        $c = $Geometry.Chip
        $chipRect  = New-Object System.Drawing.RectangleF([single]$c[0], [single]$c[1], [single]$c[2], [single]$c[2])
        $chipPath  = New-RoundedRectPath -X $c[0] -Y $c[1] -W $c[2] -H $c[2] -R $c[3]
        $chipBrush = New-LinearBrush -Bounds $chipRect -From $ChipLight -To $ChipDeep
        $g.FillPath($chipBrush, $chipPath)
        $chipBrush.Dispose()
        $chipPath.Dispose()

        # --- die -----------------------------------------------------------
        if ($null -ne $Geometry.Die) {
            $d = $Geometry.Die
            $diePath  = New-RoundedRectPath -X $d[0] -Y $d[1] -W $d[2] -H $d[2] -R $d[3]
            $dieBrush = New-Object System.Drawing.SolidBrush($DieColor)
            $g.FillPath($dieBrush, $diePath)
            $dieBrush.Dispose()
            $diePath.Dispose()
        }
    }
    finally {
        $g.Dispose()
    }

    return $bmp
}

function New-ScaledBitmap {
    param(
        [System.Drawing.Bitmap] $Source,
        [int] $Size
    )
    $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $attr = New-Object System.Drawing.Imaging.ImageAttributes
    try {
        $g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $g.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.Clear([System.Drawing.Color]::Transparent)

        # TileFlipXY stops the bicubic kernel sampling transparent black
        # from outside the source, which would halo the rounded corners.
        $attr.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)

        $dest = New-Object System.Drawing.Rectangle(0, 0, $Size, $Size)
        $g.DrawImage($Source, $dest, 0, 0, $Source.Width, $Source.Height,
                     [System.Drawing.GraphicsUnit]::Pixel, $attr)
    }
    finally {
        $attr.Dispose()
        $g.Dispose()
    }
    return $bmp
}

function ConvertTo-PngBytes {
    param([System.Drawing.Bitmap] $Bitmap)
    $ms = New-Object System.IO.MemoryStream
    try {
        $Bitmap.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        return $ms.ToArray()
    }
    finally {
        $ms.Dispose()
    }
}

# ---------------------------------------------------------------------------
# Render every size
# ---------------------------------------------------------------------------

$sizes      = @(16, 24, 32, 48, 64, 128, 256)
$supersample = 4
$payloads   = New-Object System.Collections.Generic.List[object]

foreach ($size in $sizes) {
    $geometry = Get-MarkGeometry -Size $size

    # Draw 4x oversized, then bicubic down to the exact target. Drawing tiny
    # geometry directly leaves the hexagon stroke ragged.
    $big    = New-MarkBitmap -Size ($size * $supersample) -Geometry $geometry
    $scaled = New-ScaledBitmap -Source $big -Size $size
    $big.Dispose()

    $bytes = ConvertTo-PngBytes -Bitmap $scaled
    $scaled.Dispose()

    $payloads.Add([pscustomobject]@{ Size = $size; Bytes = $bytes }) | Out-Null
    Write-Host ("  {0,3} x {1,-3}  {2,7:N0} bytes" -f $size, $size, $bytes.Length)
}

# ---------------------------------------------------------------------------
# Write the .ico container
#
#   ICONDIR       reserved(2)=0  type(2)=1  count(2)=N
#   ICONDIRENTRY  width(1) height(1) colorCount(1)=0 reserved(1)=0
#                 planes(2)=1 bitCount(2)=32 bytesInRes(4) imageOffset(4)
#   ...then the PNG payloads, in the same order as the entries.
# ---------------------------------------------------------------------------

$count      = $payloads.Count
$headerSize = 6 + (16 * $count)

$fs = New-Object System.IO.FileStream($OutputPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
$bw = New-Object System.IO.BinaryWriter($fs)
try {
    # ICONDIR
    $bw.Write([uint16]0)       # reserved
    $bw.Write([uint16]1)       # type: 1 = icon
    $bw.Write([uint16]$count)  # image count

    # ICONDIRENTRY records
    $offset = $headerSize
    foreach ($item in $payloads) {
        # 256 is encoded as 0 in the single-byte width/height fields.
        $dim = [byte]($(if ($item.Size -ge 256) { 0 } else { $item.Size }))

        $bw.Write([byte]$dim)                  # width
        $bw.Write([byte]$dim)                  # height
        $bw.Write([byte]0)                     # colorCount (0 = truecolour)
        $bw.Write([byte]0)                     # reserved
        $bw.Write([uint16]1)                   # colour planes
        $bw.Write([uint16]32)                  # bits per pixel
        $bw.Write([uint32]$item.Bytes.Length)  # bytesInRes
        $bw.Write([uint32]$offset)             # imageOffset

        $offset += $item.Bytes.Length
    }

    # PNG payloads
    foreach ($item in $payloads) {
        $bw.Write($item.Bytes, 0, $item.Bytes.Length)
    }

    $bw.Flush()
}
finally {
    $bw.Dispose()
    $fs.Dispose()
}

$file = Get-Item -LiteralPath $OutputPath
Write-Host ''
Write-Host ("Wrote {0}" -f $file.FullName)
Write-Host ("Size  {0:N0} bytes ({1:N1} KB), {2} images" -f $file.Length, ($file.Length / 1KB), $count)
