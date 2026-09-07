<#
.SYNOPSIS
    Generates docs/site/og-image.png -- the 1280x640 social preview card.

.DESCRIPTION
    Draws the Hexnest social card programmatically with System.Drawing (GDI+):
    the dark brand background, the honeycomb motif, the app mark reproduced from
    the same 256x256 design grid as assets/logo.svg, the wordmark, the tagline and
    a short feature line.

    No external tooling is required -- only Windows and the .NET Framework
    assemblies that ship with it. PowerShell 5.1 compatible.

    The same image is referenced by <meta property="og:image"> on both landing
    pages and is what should be uploaded under
    Settings -> General -> Social preview on GitHub.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build\make-og-image.ps1
#>

[CmdletBinding()]
param(
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

# ---------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $repoRoot   = Split-Path -Parent $PSScriptRoot
    $OutputPath = Join-Path $repoRoot 'docs\site\og-image.png'
}

$outputDir = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

# ---------------------------------------------------------------------------
# Canvas + brand palette (assets/README.md is the source of truth)
# ---------------------------------------------------------------------------

$W = 1280
$H = 640

function ConvertTo-Color([string] $hex) {
    return [System.Drawing.ColorTranslator]::FromHtml($hex)
}

$BgTop         = ConvertTo-Color '#131A22'
$BgBottom      = ConvertTo-Color '#090C10'
$SurfaceTop    = ConvertTo-Color '#1C232D'
$SurfaceBottom = ConvertTo-Color '#0B0E12'
$BorderColor   = ConvertTo-Color '#2A323D'
$MintLight     = ConvertTo-Color '#5BF0BF'
$MintDeep      = ConvertTo-Color '#22B98A'
$MintBase      = ConvertTo-Color '#35D8A4'
$ChipLight     = ConvertTo-Color '#6BF3CA'
$ChipDeep      = ConvertTo-Color '#2AC694'
$DieColor      = ConvertTo-Color '#0B1F19'
$TextColor     = ConvertTo-Color '#E8EDF4'
$MutedColor    = ConvertTo-Color '#94A3B4'
$DimColor      = ConvertTo-Color '#7A8899'

# ---------------------------------------------------------------------------
# Geometry helpers
# ---------------------------------------------------------------------------

function New-RoundedRectPath {
    param([single] $X, [single] $Y, [single] $Wd, [single] $Ht, [single] $R)

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [single]($R * 2.0)
    if ($d -le 0) {
        $path.AddRectangle((New-Object System.Drawing.RectangleF($X, $Y, $Wd, $Ht)))
        return $path
    }
    $path.AddArc([single]$X,              [single]$Y,               $d, $d, [single]180, [single]90)
    $path.AddArc([single]($X + $Wd - $d), [single]$Y,               $d, $d, [single]270, [single]90)
    $path.AddArc([single]($X + $Wd - $d), [single]($Y + $Ht - $d),  $d, $d, [single]0,   [single]90)
    $path.AddArc([single]$X,              [single]($Y + $Ht - $d),  $d, $d, [single]90,  [single]90)
    $path.CloseFigure()
    return $path
}

# Regular hexagon with vertices at 0/60/.../300 degrees -- the same "nest cell"
# orientation used by assets/banner.svg.
function New-HexPath {
    param([single] $Cx, [single] $Cy, [single] $R)

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $pts = New-Object 'System.Collections.Generic.List[System.Drawing.PointF]'
    for ($i = 0; $i -lt 6; $i++) {
        $a = [Math]::PI / 3.0 * $i
        $pts.Add((New-Object System.Drawing.PointF(
            [single]($Cx + $R * [Math]::Cos($a)),
            [single]($Cy + $R * [Math]::Sin($a)))))
    }
    $path.AddPolygon($pts.ToArray())
    return $path
}

function New-PolyPath {
    param([single[][]] $Points)

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $pts = New-Object 'System.Collections.Generic.List[System.Drawing.PointF]'
    foreach ($p in $Points) {
        $pts.Add((New-Object System.Drawing.PointF([single]$p[0], [single]$p[1])))
    }
    $path.AddPolygon($pts.ToArray())
    return $path
}

function New-LinearBrush {
    param(
        [single] $X1, [single] $Y1, [single] $X2, [single] $Y2,
        [System.Drawing.Color] $C1, [System.Drawing.Color] $C2
    )
    return New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF($X1, $Y1)),
        (New-Object System.Drawing.PointF($X2, $Y2)),
        $C1, $C2)
}

# ---------------------------------------------------------------------------
# The app mark, drawn on the shared 256x256 grid and scaled into place
# ---------------------------------------------------------------------------

function Draw-Mark {
    param(
        [System.Drawing.Graphics] $G,
        [single] $X, [single] $Y, [single] $Size
    )

    $state = $G.Save()
    $G.TranslateTransform($X, $Y)
    $s = [single]($Size / 256.0)
    $G.ScaleTransform($s, $s)

    # tile
    $tile = New-RoundedRectPath 6 6 244 244 56
    $tileBrush = New-LinearBrush 6 6 250 250 $SurfaceTop $SurfaceBottom
    $G.FillPath($tileBrush, $tile)
    $tileBrush.Dispose()
    $tile.Dispose()

    # tile hairline
    $edge = New-RoundedRectPath 7 7 242 242 55
    $edgePen = New-Object System.Drawing.Pen($BorderColor, [single]2)
    $G.DrawPath($edgePen, $edge)
    $edgePen.Dispose()
    $edge.Dispose()

    # mint gradient shared by the pins and the nest ring
    $mint = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF([single]46, [single]46)),
        (New-Object System.Drawing.PointF([single]210, [single]210)),
        $MintLight, $MintDeep)

    # chip pins
    foreach ($pin in @(@(105, 46), @(137, 46), @(105, 190), @(137, 190))) {
        $p = New-RoundedRectPath ([single]$pin[0]) ([single]$pin[1]) 14 20 5
        $G.FillPath($mint, $p)
        $p.Dispose()
    }

    # nest cell
    $hex = New-PolyPath @(
        @([single]196, [single]128),
        @([single]162, [single]186.89),
        @([single]94,  [single]186.89),
        @([single]60,  [single]128),
        @([single]94,  [single]69.11),
        @([single]162, [single]69.11))
    $hexPen = New-Object System.Drawing.Pen($mint, [single]15)
    $hexPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $G.DrawPath($hexPen, $hex)
    $hexPen.Dispose()
    $hex.Dispose()
    $mint.Dispose()

    # chip body + die
    $chip = New-RoundedRectPath 99 99 58 58 14
    $chipBrush = New-LinearBrush 99 99 157 157 $ChipLight $ChipDeep
    $G.FillPath($chipBrush, $chip)
    $chipBrush.Dispose()
    $chip.Dispose()

    $die = New-RoundedRectPath 116 116 24 24 6
    $dieBrush = New-Object System.Drawing.SolidBrush($DieColor)
    $G.FillPath($dieBrush, $die)
    $dieBrush.Dispose()
    $die.Dispose()

    $G.Restore($state) | Out-Null
}

# ---------------------------------------------------------------------------
# Compose
# ---------------------------------------------------------------------------

$bmp = New-Object System.Drawing.Bitmap($W, $H, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$bmp.SetResolution(96, 96)

$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias
$g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

# background wash
$bg = New-LinearBrush 0 0 $W $H $BgTop $BgBottom
$g.FillRectangle($bg, 0, 0, $W, $H)
$bg.Dispose()

# soft mint glow behind the mark
$glowPath = New-Object System.Drawing.Drawing2D.GraphicsPath
$glowPath.AddEllipse([single](-180), [single](20), [single]840, [single]680)
$glow = New-Object System.Drawing.Drawing2D.PathGradientBrush($glowPath)
$glow.CenterPoint  = New-Object System.Drawing.PointF([single]230, [single]340)
$glow.CenterColor  = [System.Drawing.Color]::FromArgb(17, $MintBase)
$glow.SurroundColors = @([System.Drawing.Color]::FromArgb(0, $MintBase))
$g.FillPath($glow, $glowPath)
$glow.Dispose()
$glowPath.Dispose()

# honeycomb motif, top right
$hexR  = [single]52
$stepX = [single]($hexR * 1.5)
$stepY = [single]($hexR * [Math]::Sqrt(3))
$cells = @(
    @(0,  0,  11),
    @(1,  -0.5, 7), @(1,  0.5, 7),
    @(-1, -0.5, 6), @(-1, 0.5, 6),
    @(0,  -1,  5),  @(0,  1,  5),
    @(2,  0,   4),  @(2,  -1, 4), @(2, 1, 4)
)
$originX = [single]1096
$originY = [single]224
foreach ($c in $cells) {
    $cx = [single]($originX + $stepX * $c[0])
    $cy = [single]($originY + $stepY * $c[1])
    $hp = New-HexPath $cx $cy $hexR
    $pen = New-Object System.Drawing.Pen(
        [System.Drawing.Color]::FromArgb([int]([single]$c[2] * 2.55 * 1.0), $MintBase), [single]2)
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawPath($pen, $hp)
    $pen.Dispose()
    $hp.Dispose()
}
# the centre cell gets a whisper of fill
$centreHex = New-HexPath $originX $originY $hexR
$centreFill = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(10, $MintBase))
$g.FillPath($centreFill, $centreHex)
$centreFill.Dispose()
$centreHex.Dispose()

# ---------------------------------------------------------------------------
# Fonts
# ---------------------------------------------------------------------------

function Get-Family([string[]] $Names) {
    foreach ($n in $Names) {
        try { return (New-Object System.Drawing.FontFamily($n)) } catch { }
    }
    return [System.Drawing.FontFamily]::GenericSansSerif
}

$family = Get-Family @('Segoe UI', 'Selawik', 'Tahoma', 'Arial')

function New-PixelFont([single] $Size, [System.Drawing.FontStyle] $Style) {
    return New-Object System.Drawing.Font(
        $family, $Size, $Style, [System.Drawing.GraphicsUnit]::Pixel)
}

$fontWord    = New-PixelFont 96 ([System.Drawing.FontStyle]::Bold)
$fontTagline = New-PixelFont 30 ([System.Drawing.FontStyle]::Regular)
$fontFeature = New-PixelFont 21 ([System.Drawing.FontStyle]::Regular)
$fontSmall   = New-PixelFont 20 ([System.Drawing.FontStyle]::Regular)
$fontPill    = New-PixelFont 18 ([System.Drawing.FontStyle]::Regular)

[System.Drawing.StringFormat] $fmt = [System.Drawing.StringFormat]::GenericTypographic.Clone()
$fmt.FormatFlags = $fmt.FormatFlags -bor [System.Drawing.StringFormatFlags]::MeasureTrailingSpaces

function Measure-Text([string] $Text, [System.Drawing.Font] $Font) {
    return $g.MeasureString($Text, $Font,
        (New-Object System.Drawing.SizeF([single]4000, [single]800)), $fmt)
}

function Draw-Text {
    param(
        [string] $Text,
        [System.Drawing.Font] $Font,
        [System.Drawing.Color] $Color,
        [single] $X, [single] $Y
    )
    $b = New-Object System.Drawing.SolidBrush($Color)
    $g.DrawString($Text, $Font, $b, (New-Object System.Drawing.PointF($X, $Y)), $fmt)
    $b.Dispose()
}

# ---------------------------------------------------------------------------
# Content
# ---------------------------------------------------------------------------

$left = [single]100

# The interpunct is built from its code point rather than typed literally: this
# script is read as ANSI by Windows PowerShell 5.1 and a literal U+00B7 would
# arrive on the canvas as mojibake.
$dot = [string][char]0x00B7

# repository line
Draw-Text 'github.com/ahmetcaglayan/Hexnest' $fontSmall $DimColor $left 94

# app mark
Draw-Mark $g $left 224 208

$textX = [single]370

# wordmark: "Drv" in foreground, "Nest" in mint
Draw-Text 'Drv' $fontWord $TextColor $textX 220
$drvWidth = (Measure-Text 'Drv' $fontWord).Width
Draw-Text 'Nest' $fontWord $MintBase ([single]($textX + $drvWidth)) 220

Draw-Text 'Free, open-source Windows driver updater' $fontTagline $MutedColor $textX 356
Draw-Text ('Missing drivers  {0}  Post-format recovery  {0}  Offline USB restore' -f $dot) `
    $fontFeature $DimColor $textX 406

# capability pills
$pills = @(
    'Windows 10 / 11',
    'Single .exe, no .NET needed',
    'MIT licensed, no adware'
)
$pillX = $left
$pillY = [single]502
$pillH = [single]52
foreach ($label in $pills) {
    $tw = (Measure-Text $label $fontPill).Width
    $pw = [single]($tw + 46)
    $path = New-RoundedRectPath $pillX $pillY $pw $pillH ([single]($pillH / 2))
    $fill = New-Object System.Drawing.SolidBrush((ConvertTo-Color '#171C23'))
    $g.FillPath($fill, $path)
    $fill.Dispose()
    $pen = New-Object System.Drawing.Pen($BorderColor, [single]1.5)
    $g.DrawPath($pen, $path)
    $pen.Dispose()
    $path.Dispose()

    $th = (Measure-Text $label $fontPill).Height
    Draw-Text $label $fontPill $MutedColor ([single]($pillX + 23)) ([single]($pillY + ($pillH - $th) / 2))

    $pillX = [single]($pillX + $pw + 16)
}

# bottom rule -- mint fading to nothing, mirroring assets/banner.svg
$rule = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.PointF([single]0, [single]0)),
    (New-Object System.Drawing.PointF([single]$W, [single]0)),
    [System.Drawing.Color]::FromArgb(215, $MintBase),
    [System.Drawing.Color]::FromArgb(0, $MintBase))
$g.FillRectangle($rule, 0, ($H - 5), $W, 5)
$rule.Dispose()

# ---------------------------------------------------------------------------
# Save
# ---------------------------------------------------------------------------

$g.Dispose()
$bmp.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

foreach ($f in @($fontWord, $fontTagline, $fontFeature, $fontSmall, $fontPill)) { $f.Dispose() }
$fmt.Dispose()

$size = (Get-Item -LiteralPath $OutputPath).Length
Write-Host ("Wrote {0} ({1:N0} bytes, {2}x{3})" -f $OutputPath, $size, $W, $H)
