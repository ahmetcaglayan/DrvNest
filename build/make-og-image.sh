#!/usr/bin/env bash
#
# Renders assets/og-image.svg into docs/site/og-image.png (1280x640).
#
# The macOS counterpart of build/make-og-image.ps1, which draws the same card with
# GDI+ for anyone regenerating it on Windows.
#
# Two quirks of qlmanage, which is the only SVG rasteriser macOS ships, are worked
# around in assets/og-image.svg rather than here:
#
#   It scales a non-square SVG to fit a square thumbnail and clips the overflow, so
#   the file declares a 1280x1280 canvas.
#
#   sips crops from the centre, so the card is drawn into the middle of that canvas
#   and the middle 1280x640 is taken back out below.
#
# qlmanage also composites onto an opaque white page. That is harmless here because
# the card covers every pixel - it is why the transparent application icon needs
# build/make-mac-icon.py instead.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SOURCE="$ROOT/assets/og-image.svg"
OUTPUT="${1:-$ROOT/docs/site/og-image.png}"

[[ -f "$SOURCE" ]] || { echo "Missing $SOURCE" >&2; exit 1; }

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

qlmanage -t -s 1280 -o "$WORK" "$SOURCE" >/dev/null 2>&1

RENDER="$(find "$WORK" -name '*.png' | head -1)"
[[ -n "$RENDER" ]] || { echo "qlmanage produced no image." >&2; exit 1; }

sips -c 640 1280 "$RENDER" --out "$OUTPUT" >/dev/null

echo "Wrote $OUTPUT ($(sips -g pixelWidth -g pixelHeight "$OUTPUT" | tail -2 | tr -d ' \n'))"
