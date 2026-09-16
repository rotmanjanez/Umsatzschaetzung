#!/bin/sh
# Schreibt AppIcon.icns in das Resources-Verzeichnis $1, aus der Icon-Composer-Datei
# wenn Xcode da ist (dann auch Assets.car, erst damit zeichnet macOS 26 das Glas),
# sonst als Ersatz aus logo.svg bzw. logo-512.png.
set -eu

assets=$(cd "$(dirname "$0")/../../src/Umsatzschätzung.App/Ui/Assets" && pwd)
icon=$assets/Umsatzschätzung.icon
out=$1

if [ -d "$icon" ] && xcrun --find actool >/dev/null 2>&1; then
    work=$(mktemp -d)
    cp -R "$icon" "$work/AppIcon.icon"
    xcrun actool "$work/AppIcon.icon" --compile "$out" --app-icon AppIcon \
        --output-partial-info-plist "$work/icon.plist" --platform macosx \
        --minimum-deployment-target 13.0 --target-device mac > /dev/null
    rm -rf "$work"
    exit 0
fi

echo "kein Xcode: Symbol aus logo.svg statt aus $icon" >&2
set=$(mktemp -d)/icon.iconset
mkdir -p "$set"
for size in 16 32 128 256 512; do
    for scale in 1 2; do
        px=$((size * scale))
        [ "$scale" -eq 1 ] && name=icon_${size}x${size}.png || name=icon_${size}x${size}@2x.png
        if command -v rsvg-convert >/dev/null 2>&1; then
            rsvg-convert -w "$px" -h "$px" "$assets/logo.svg" -o "$set/$name"
        else
            sips -s format png -z "$px" "$px" "$assets/logo-512.png" --out "$set/$name" >/dev/null
        fi
    done
done
iconutil -c icns "$set" -o "$out/AppIcon.icns"
rm -rf "$(dirname "$set")"
