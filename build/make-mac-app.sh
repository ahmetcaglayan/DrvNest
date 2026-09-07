#!/usr/bin/env bash
#
# Builds Hexnest.app, and optionally Hexnest.dmg, for macOS.
#
# The Windows build publishes one self-contained .exe because that is the shape of
# thing a Windows user can be handed. The equivalent on a Mac is an .app bundle: a
# folder the Finder treats as one object, which the user drags into /Applications.
# This script is the counterpart of build/publish.ps1.
#
#   ./build/make-mac-app.sh                       both architectures, .app only
#   ./build/make-mac-app.sh --arch arm64          just Apple silicon
#   ./build/make-mac-app.sh --dmg                 also build the disk image
#   ./build/make-mac-app.sh --version 1.3.0       stamp a version
#
# A universal binary is deliberately not produced. `lipo`-ing two self-contained .NET
# runtimes together doubles the download for every user in order to save one click on
# the download page, and the release publishes the two disk images side by side
# instead.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/src/Hexnest.Mac/Hexnest.Mac.csproj"
OUT="$ROOT/artifacts/mac"
ASSETS="$ROOT/assets"

ARCHES=("arm64" "x64")
VERSION=""
MAKE_DMG=0
CONFIG="Release"

while [[ $# -gt 0 ]]; do
    case "$1" in
        --arch)    ARCHES=("$2"); shift 2 ;;
        --version) VERSION="$2";  shift 2 ;;
        --dmg)     MAKE_DMG=1;    shift ;;
        --debug)   CONFIG="Debug"; shift ;;
        *) echo "Unknown option: $1" >&2; exit 2 ;;
    esac
done

DOTNET="${DOTNET:-dotnet}"
command -v "$DOTNET" >/dev/null 2>&1 || DOTNET="$HOME/.dotnet/dotnet"
command -v "$DOTNET" >/dev/null 2>&1 || { echo "The .NET 8 SDK was not found." >&2; exit 1; }

# ---------------------------------------------------------------------------------
# The icon.
#
# macOS wants an .icns holding every size from 16 to 1024 points, which iconutil
# assembles from one master PNG. The master is committed rather than converted from
# the SVG at build time: see build/make-mac-icon.py for why there is no rasteriser
# here to convert it with.
# ---------------------------------------------------------------------------------
make_icon() {
    local iconset="$1/Hexnest.iconset"
    local master="$ASSETS/icon-mac-1024.png"

    # Drawn by build/make-mac-icon.py and committed, because macOS has no SVG
    # rasteriser a build may rely on: qlmanage composites onto an opaque white page,
    # which is where the white ring around the icon came from.
    if [[ ! -f "$master" ]]; then
        echo "  no icon-mac-1024.png (run build/make-mac-icon.py); using the default icon"
        return 1
    fi

    rm -rf "$iconset"; mkdir -p "$iconset"

    for size in 16 32 128 256 512; do
        sips -z $size $size "$master" --out "$iconset/icon_${size}x${size}.png" >/dev/null 2>&1
        sips -z $((size * 2)) $((size * 2)) "$master" \
            --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null 2>&1
    done

    iconutil -c icns "$iconset" -o "$1/Hexnest.icns" 2>/dev/null || return 1
    rm -rf "$iconset"
    return 0
}

# ---------------------------------------------------------------------------------
# One bundle per architecture.
# ---------------------------------------------------------------------------------
build_one() {
    local arch="$1"
    local rid="osx-$arch"
    local stage="$OUT/$arch"
    local app="$stage/Hexnest.app"

    echo ""
    echo "==> $rid"

    rm -rf "$stage"
    mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"

    local args=(publish "$PROJECT" -c "$CONFIG" -r "$rid" --self-contained true
                -p:PublishReadyToRun=true -p:DebugType=embedded
                -o "$stage/publish")

    [[ -n "$VERSION" ]] && args+=("-p:Version=$VERSION")

    "$DOTNET" "${args[@]}" >/dev/null

    cp -R "$stage/publish/." "$app/Contents/MacOS/"
    rm -rf "$stage/publish"

    if make_icon "$stage"; then
        mv "$stage/Hexnest.icns" "$app/Contents/Resources/Hexnest.icns"
        echo "  icon built from icon-mac-1024.png"
    fi

    local display="${VERSION:-1.3.0}"

    cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>              <string>Hexnest</string>
    <key>CFBundleDisplayName</key>       <string>Hexnest</string>
    <key>CFBundleExecutable</key>        <string>Hexnest</string>
    <key>CFBundleIdentifier</key>        <string>com.ahmetcaglayan.hexnest</string>
    <key>CFBundleIconFile</key>          <string>Hexnest</string>
    <key>CFBundlePackageType</key>       <string>APPL</string>
    <key>CFBundleShortVersionString</key><string>$display</string>
    <key>CFBundleVersion</key>           <string>$display</string>
    <key>LSMinimumSystemVersion</key>    <string>12.0</string>
    <key>NSHighResolutionCapable</key>   <true/>

    <!--
        Hexnest has no document types, no URL schemes and no background mode. It is a
        window the user opens, and it should leave nothing behind when it is closed.
    -->
    <key>LSApplicationCategoryType</key> <string>public.app-category.utilities</string>
    <key>NSHumanReadableCopyright</key>  <string>MIT licensed. Copyright (c) Hexnest contributors.</string>
</dict>
</plist>
PLIST

    chmod +x "$app/Contents/MacOS/Hexnest"

    # An ad-hoc signature is not notarisation and does not remove Gatekeeper's first
    # run prompt, but without any signature at all an Apple silicon Mac refuses to
    # launch the binary outright rather than asking.
    codesign --force --deep --sign - "$app" >/dev/null 2>&1 \
        && echo "  ad-hoc signed" \
        || echo "  could not sign (the app will still run after Control-clicking Open)"

    local size
    size="$(du -sh "$app" | cut -f1)"
    echo "  $app  ($size)"

    if [[ $MAKE_DMG -eq 1 ]]; then
        local dmg="$OUT/Hexnest-$arch.dmg"
        rm -f "$dmg"

        local staging="$stage/dmg"
        rm -rf "$staging"; mkdir -p "$staging"
        cp -R "$app" "$staging/"
        ln -s /Applications "$staging/Applications"

        hdiutil create -volname "Hexnest" -srcfolder "$staging" \
            -ov -format UDZO "$dmg" >/dev/null

        rm -rf "$staging"
        echo "  $dmg  ($(du -sh "$dmg" | cut -f1))"
    fi
}

echo "Hexnest for macOS"
echo "  configuration : $CONFIG"
echo "  architectures : ${ARCHES[*]}"
[[ -n "$VERSION" ]] && echo "  version       : $VERSION"

mkdir -p "$OUT"

for arch in "${ARCHES[@]}"; do
    build_one "$arch"
done

# The checksum file the About page tells people to verify against.
if [[ $MAKE_DMG -eq 1 ]]; then
    ( cd "$OUT" && shasum -a 256 ./*.dmg > checksums.txt )
    echo ""
    cat "$OUT/checksums.txt"
fi

echo ""
echo "Done."
