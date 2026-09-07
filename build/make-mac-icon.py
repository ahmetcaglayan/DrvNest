#!/usr/bin/env python3
"""
Draws the Hexnest macOS icon and writes assets/icon-mac-1024.png.

Why this file exists
--------------------
macOS ships no SVG rasteriser that a build script may use. `qlmanage` looks like
one, but it produces a Quick Look *thumbnail*: the artwork is composited onto an
opaque white page, so the resulting icon is a white rounded square with the real
icon sitting inside it. That is exactly the "white border around the logo" that this
replaces.

Every other option - librsvg, Inkscape, ImageMagick, cairosvg, sharp - is something a
contributor would have to install first, and this project's whole deployment story is
that nothing has to be installed first. So the icon is drawn here instead, with the
standard library only: a small supersampled rasteriser and zlib for the PNG. It is the
same decision build/make-icon.ps1 makes on Windows, where the mark is drawn with GDI+
rather than converted from the SVG.

The geometry is the one in assets/icon-mac.svg, on the same 256pt grid as
assets/logo.svg, so all three stay in step.

    python3 build/make-mac-icon.py [output.png] [--size 1024]
"""

from __future__ import annotations

import math
import struct
import sys
import zlib
from pathlib import Path

# ---------------------------------------------------------------------------------
# Apple's icon grid.
#
# The canvas is 1024 and the plate is 824 of it, centred: a Dock full of icons all
# share that proportion, and an icon that fills its canvas edge to edge is the one
# that looks a size too big next to them.
#
# MARK_SCALE then decides how much of the plate the mark itself covers. The logo is
# drawn for a 256pt tile with generous padding, which is right on a website and too
# timid on a Dock icon, so it is enlarged about a fifth here.
# ---------------------------------------------------------------------------------
CANVAS = 1024
PLATE = 824
PLATE_RADIUS = 185
MARK_SCALE = 1.20

SUPERSAMPLE = 4

SURFACE_TOP = (0x1C, 0x23, 0x2D)
SURFACE_MID = (0x12, 0x17, 0x1E)
SURFACE_BOTTOM = (0x0B, 0x0E, 0x12)
EDGE = (0x2A, 0x32, 0x3D)

MINT_LIGHT = (0x5B, 0xF0, 0xBF)
MINT_BASE = (0x35, 0xD8, 0xA4)
MINT_DEEP = (0x22, 0xB9, 0x8A)

CHIP_LIGHT = (0x6B, 0xF3, 0xCA)
CHIP_DEEP = (0x2A, 0xC6, 0x94)
DIE = (0x0B, 0x1F, 0x19)

GLOW = (0x35, 0xD8, 0xA4)


# =====================================================================================
# A very small canvas
# =====================================================================================

class Canvas:
    """RGBA float canvas with source-over compositing, drawn at SUPERSAMPLE scale."""

    def __init__(self, size: int):
        self.size = size
        # Straight (non-premultiplied) RGBA, one list of four floats per pixel.
        self.pixels = [[0.0, 0.0, 0.0, 0.0] for _ in range(size * size)]

    def blend(self, x: int, y: int, colour, alpha: float) -> None:
        if alpha <= 0 or x < 0 or y < 0 or x >= self.size or y >= self.size:
            return

        alpha = min(1.0, alpha)
        pixel = self.pixels[y * self.size + x]

        out_a = alpha + pixel[3] * (1 - alpha)
        if out_a <= 0:
            return

        for i in range(3):
            pixel[i] = (colour[i] * alpha + pixel[i] * pixel[3] * (1 - alpha)) / out_a

        pixel[3] = out_a

    def fill(self, inside, shade, alpha_of=None, bounds=None) -> None:
        """
        Fills every pixel for which `inside(x, y)` is true.

        `inside` returns a signed distance in pixels: negative inside the shape,
        positive outside. That is what gives the edges their anti-aliasing without a
        second supersampling pass - the coverage of a pixel is simply how far its
        centre is from the boundary.

        `bounds` is the shape's bounding box. It is not an optimisation so much as the
        difference between this script taking two seconds and taking two minutes: the
        four chip pins are a thousandth of the canvas each, and without it every one of
        them would be tested against all sixteen million pixels.
        """
        if bounds is None:
            x0, y0, x1, y1 = 0, 0, self.size, self.size
        else:
            x0 = max(0, int(bounds[0]) - 2)
            y0 = max(0, int(bounds[1]) - 2)
            x1 = min(self.size, int(bounds[2]) + 2)
            y1 = min(self.size, int(bounds[3]) + 2)

        for y in range(y0, y1):
            for x in range(x0, x1):
                distance = inside(x + 0.5, y + 0.5)
                if distance > 1.0:
                    continue

                coverage = 1.0 if distance < 0 else 1.0 - distance
                if coverage <= 0:
                    continue

                colour = shade(x + 0.5, y + 0.5)
                weight = coverage * (alpha_of(x + 0.5, y + 0.5) if alpha_of else 1.0)
                self.blend(x, y, colour, weight)

    def to_png(self, path: Path, size: int) -> None:
        """Downsamples to `size` and writes an 8-bit RGBA PNG."""
        factor = self.size // size
        rows = bytearray()

        for y in range(size):
            rows.append(0)  # PNG filter type 0 (None)

            for x in range(size):
                r = g = b = a = 0.0

                for dy in range(factor):
                    for dx in range(factor):
                        pixel = self.pixels[(y * factor + dy) * self.size + x * factor + dx]
                        # Average in premultiplied space, or edge pixels pick up the
                        # colour of fully transparent neighbours as a dark halo.
                        r += pixel[0] * pixel[3]
                        g += pixel[1] * pixel[3]
                        b += pixel[2] * pixel[3]
                        a += pixel[3]

                count = factor * factor
                a /= count

                if a > 0:
                    r = r / count / a
                    g = g / count / a
                    b = b / count / a

                rows += bytes((
                    max(0, min(255, round(r))),
                    max(0, min(255, round(g))),
                    max(0, min(255, round(b))),
                    max(0, min(255, round(a * 255))),
                ))

        def chunk(tag: bytes, payload: bytes) -> bytes:
            return (struct.pack(">I", len(payload)) + tag + payload
                    + struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF))

        header = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)

        path.write_bytes(
            b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", header)
            + chunk(b"IDAT", zlib.compress(bytes(rows), 9))
            + chunk(b"IEND", b"")
        )


# =====================================================================================
# Geometry
# =====================================================================================

def bbox_of(x: float, y: float, w: float, h: float):
    return (x, y, x + w, y + h)


def rounded_rect(x: float, y: float, w: float, h: float, r: float):
    """Signed distance to a rounded rectangle: negative inside."""
    cx, cy = x + w / 2, y + h / 2
    hw, hh = w / 2 - r, h / 2 - r

    def distance(px: float, py: float) -> float:
        dx = abs(px - cx) - hw
        dy = abs(py - cy) - hh
        outside = math.hypot(max(dx, 0), max(dy, 0))
        return outside + min(max(dx, dy), 0) - r

    return distance


def polygon_ring(points, width: float):
    """Signed distance to the outline of a polygon, `width` pixels thick."""
    half = width / 2

    def distance(px: float, py: float) -> float:
        best = float("inf")

        for i in range(len(points)):
            ax, ay = points[i]
            bx, by = points[(i + 1) % len(points)]

            vx, vy = bx - ax, by - ay
            length2 = vx * vx + vy * vy
            t = 0.0 if length2 == 0 else max(0.0, min(1.0, ((px - ax) * vx + (py - ay) * vy) / length2))

            best = min(best, math.hypot(px - (ax + t * vx), py - (ay + t * vy)))

        return best - half

    return distance


def linear(c0, c1, x0, y0, x1, y1):
    """A linear gradient between two points."""
    dx, dy = x1 - x0, y1 - y0
    length2 = dx * dx + dy * dy or 1

    def shade(px: float, py: float):
        t = max(0.0, min(1.0, ((px - x0) * dx + (py - y0) * dy) / length2))
        return [c0[i] + (c1[i] - c0[i]) * t for i in range(3)]

    return shade


def linear3(c0, c1, c2, x0, y0, x1, y1, stop: float):
    """A three-stop linear gradient, matching the SVG's surface fill."""
    dx, dy = x1 - x0, y1 - y0
    length2 = dx * dx + dy * dy or 1

    def shade(px: float, py: float):
        t = max(0.0, min(1.0, ((px - x0) * dx + (py - y0) * dy) / length2))

        if t <= stop:
            u = t / stop if stop else 0
            return [c0[i] + (c1[i] - c0[i]) * u for i in range(3)]

        u = (t - stop) / (1 - stop) if stop < 1 else 0
        return [c1[i] + (c2[i] - c1[i]) * u for i in range(3)]

    return shade


def solid(colour):
    return lambda px, py: list(colour)


# =====================================================================================

def draw(size: int) -> Canvas:
    scale = size / CANVAS
    canvas = Canvas(size)

    plate_x = (CANVAS - PLATE) / 2 * scale
    plate_w = PLATE * scale
    plate = rounded_rect(plate_x, plate_x, plate_w, plate_w, PLATE_RADIUS * scale)

    plate_bounds = bbox_of(plate_x, plate_x, plate_w, plate_w)

    canvas.fill(plate, linear3(
        SURFACE_TOP, SURFACE_MID, SURFACE_BOTTOM,
        plate_x, plate_x, plate_x + plate_w, plate_x + plate_w, 0.55), bounds=plate_bounds)

    # The mint bloom behind the mark, clipped to the plate.
    glow_cx, glow_cy = size / 2, size * 0.46
    glow_r = size * 0.40

    def glow_alpha(px, py):
        t = math.hypot(px - glow_cx, py - glow_cy) / glow_r
        if t >= 1:
            return 0.0
        return (0.24 * (1 - t / 0.6)) if t < 0.6 else (0.07 * (1 - (t - 0.6) / 0.4))

    canvas.fill(plate, solid(GLOW), glow_alpha, bounds=plate_bounds)

    # A hairline just inside the edge, so the plate does not dissolve into a dark Dock.
    edge_outer = rounded_rect(plate_x + 2 * scale, plate_x + 2 * scale,
                              plate_w - 4 * scale, plate_w - 4 * scale,
                              (PLATE_RADIUS - 1) * scale)
    edge_inner = rounded_rect(plate_x + 6 * scale, plate_x + 6 * scale,
                              plate_w - 12 * scale, plate_w - 12 * scale,
                              (PLATE_RADIUS - 5) * scale)

    canvas.fill(lambda px, py: max(edge_outer(px, py), -edge_inner(px, py)),
                solid(EDGE), bounds=plate_bounds)

    # ---- the mark, from the 256pt logo grid ------------------------------------
    #
    # Centred on the plate and enlarged by MARK_SCALE, so it carries the icon at
    # Dock size instead of floating in the middle of it.
    unit = plate_w / 256 * MARK_SCALE
    origin = size / 2 - 128 * unit

    def p(x: float, y: float):
        return origin + x * unit, origin + y * unit

    def rect(x, y, w, h, r):
        px, py = p(x, y)
        return rounded_rect(px, py, w * unit, h * unit, r * unit), \
               bbox_of(px, py, w * unit, h * unit)

    mint = linear(MINT_LIGHT, MINT_DEEP, *p(30, 0), *p(226, 256))

    for x in (105, 137):
        for y in (46, 190):
            shape, bounds = rect(x, y, 14, 20, 5)
            canvas.fill(shape, mint, bounds=bounds)

    hexagon = [p(196, 128), p(162, 186.89), p(94, 186.89),
               p(60, 128), p(94, 69.11), p(162, 69.11)]

    hex_bounds = (min(x for x, _ in hexagon) - 10 * unit, min(y for _, y in hexagon) - 10 * unit,
                  max(x for x, _ in hexagon) + 10 * unit, max(y for _, y in hexagon) + 10 * unit)

    canvas.fill(polygon_ring(hexagon, 15 * unit), mint, bounds=hex_bounds)

    shape, bounds = rect(99, 99, 58, 58, 14)
    canvas.fill(shape, linear(CHIP_LIGHT, CHIP_DEEP, *p(99, 99), *p(157, 157)), bounds=bounds)

    shape, bounds = rect(116, 116, 24, 24, 6)
    canvas.fill(shape, solid(DIE), bounds=bounds)

    return canvas


def main() -> int:
    args = [a for a in sys.argv[1:]]
    size = 1024

    if "--size" in args:
        i = args.index("--size")
        size = int(args[i + 1])
        del args[i:i + 2]

    root = Path(__file__).resolve().parent.parent
    output = Path(args[0]) if args else root / "assets" / "icon-mac-1024.png"
    output.parent.mkdir(parents=True, exist_ok=True)

    print(f"Drawing {size}x{size} at {SUPERSAMPLE}x supersampling...")

    canvas = draw(size * SUPERSAMPLE)
    canvas.to_png(output, size)

    print(f"Wrote {output}  ({output.stat().st_size:,} bytes)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
