# DrvNest brand assets

Everything here is hand-authored, self-contained SVG — no external references, no
embedded raster images, no `<script>`, no `<foreignObject>`, no webfonts. That keeps
the files safe from GitHub's SVG sanitiser and renders identically whether they are
inlined, linked from Markdown, or opened directly.

## Files

| File | Size / viewBox | Use it for |
| --- | --- | --- |
| `logo.svg` | `256 × 256` | The square app mark on **dark** backgrounds. Legible down to 16 px. |
| `logo-light.svg` | `256 × 256` | The same mark tuned for **light** backgrounds — dark nest ring, deepened mint chip, white die. |
| `banner.svg` | `1280 × 320` | The README hero: mark, wordmark, tagline and feature line on a dark gradient. |
| `download-button.svg` | `320 × 72` | A standalone download button, meant to be wrapped in a Markdown link. Fallback for when shields.io is unavailable. |
| `screenshots/` | — | Application screenshots used by the docs. |

### The mark

A flat-top hexagon — the *nest*, a honeycomb cell — cradling a rounded-square chip
with a dark die at its centre, with four chip pins breaking out through the top and
bottom edges. `Drv` + `Nest`: the safe place a machine's drivers live.

All three mark-bearing files share one geometry defined on a **256 × 256 grid**, and
`build/make-icon.ps1` reproduces that same grid in GDI+. If you move a coordinate in
`logo.svg`, mirror it in `logo-light.svg`, in the `<g transform="translate(76 84) scale(0.59375)">`
group inside `banner.svg`, and in the script.

### Typography

`banner.svg` and `download-button.svg` set live `<text>` with the stack:

```
font-family="Segoe UI, Inter, Helvetica, Arial, sans-serif"
```

Fonts are deliberately *not* converted to paths, so the text stays selectable and the
files stay small. The layouts are built with slack around every string — the button
label is centre-anchored with generous padding, and the banner's text block is
left-anchored with roughly 400 px of clear space to its right — so a substituted font
shifts the metrics without ever clipping or colliding. Both were checked against a
forced serif fallback.

## Brand colours

| Role | Hex | Notes |
| --- | --- | --- |
| Background | `#0D1014` | App window background. |
| Surface | `#171C23` | Cards, panels, the icon tile. |
| Border | `#2A323D` | Hairline dividers and the icon tile's edge. |
| Text | `#E8EDF4` | Primary foreground. |
| Text muted | `#94A3B4` | Secondary copy, the banner tagline. |
| **Accent** | **`#35D8A4`** | **The brand mint.** Primary buttons, the mark, `Nest` in the wordmark. |
| Accent pressed | `#22B98A` | Active/pressed state; the deep end of the mint gradient. |
| On-accent text | `#06231A` | Text and glyphs sitting *on* mint, e.g. the download button label. |
| Info | `#4C8DFF` | Informational badges and states. |
| Warning | `#F5A623` | Warnings, "update available". |
| Danger | `#FF5F6B` | Errors, destructive actions. |

Gradient stops used inside the artwork, derived from the accent:

| Purpose | Stops |
| --- | --- |
| Mint (nest ring, pins) | `#5BF0BF` → `#35D8A4` → `#22B98A` |
| Chip body | `#6BF3CA` → `#2AC694` |
| Icon tile | `#1C232D` → `#12171E` → `#0B0E12` |
| Chip die | `#0B1F19` (flat) |
| Mint on light backgrounds | `#2ADCA6` → `#12A277` |

`#35D8A4` on `#0D1014` clears WCAG AA for large text and UI components. On white it
does **not** — that is why `logo-light.svg` swaps to the darker `#12A277` end of the
ramp and carries the structure in near-black rather than mint.

## Regenerating the .ico

`src/DrvNest.App/Assets/drvnest.ico` is generated, not committed by hand. The app's
csproj picks it up automatically:

```xml
<PropertyGroup Condition="Exists('Assets\drvnest.ico')">
  <ApplicationIcon>Assets\drvnest.ico</ApplicationIcon>
</PropertyGroup>
```

To rebuild it:

```powershell
powershell -ExecutionPolicy Bypass -File build\make-icon.ps1
```

The script needs **nothing but Windows** — no ImageMagick, no Inkscape, no `.svg`
input. It draws the mark directly with `System.Drawing`, renders it at 16, 24, 32, 48,
64, 128 and 256 px, and writes the ICO container itself (`ICONDIR` + one 16-byte
`ICONDIRENTRY` per image + PNG payloads). Every entry is PNG-compressed, which Windows
has accepted since Vista. It is PowerShell 5.1 compatible, so it runs on a stock box
with no extra modules installed.

Pass `-OutputPath` to write somewhere else:

```powershell
powershell -ExecutionPolicy Bypass -File build\make-icon.ps1 -OutputPath C:\tmp\drvnest.ico
```

Each size is drawn at 4× and downsampled bicubically, and the small entries are
optically scaled rather than linearly reduced: below 32 px the nest stroke is
thickened and the chip pins are dropped, and at 16 px the die is dropped too, because
at that scale those details land on well under a pixel and turn the mark to mush.
That tiering lives in `Get-MarkGeometry` — adjust it there if you change the mark.
