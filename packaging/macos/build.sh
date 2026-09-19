#!/bin/sh
# Baut Umsatzschätzung.app und daraus ein DMG. Ohne -i wird ad-hoc signiert:
# das läuft lokal, taugt aber nicht zur Weitergabe.
#
#   packaging/macos/build.sh -v 1.2.0 \
#       -i "Developer ID Application: ... (TEAMID)" -p <notarytool-keychain-profil>
#
# Nur Apple Silicon: das ONNX-Runtime-Paket bringt keine osx-x64-Binaries mit.
set -eu

version=dev
arch=osx-arm64
identity=-
profile=${UMSATZ_NOTARY_PROFILE-}
installer=1

while getopts v:i:p:n opt; do
    case $opt in
        v) version=$OPTARG ;;
        i) identity=$OPTARG ;;
        p) profile=$OPTARG ;;
        n) installer=0 ;;
        *) echo "build.sh [-v version] [-i identity] [-p notary-profil] [-n]" >&2; exit 2 ;;
    esac
done

here=$(cd "$(dirname "$0")" && pwd)
root=$(cd "$here/../.." && pwd)
dist=$root/dist/$arch
app=$dist/Umsatzschätzung.app
contents=$app/Contents
out=$root/dist/dmg
dotnet=${DOTNET_ROOT:-$HOME/.dotnet}/dotnet
command -v "$dotnet" >/dev/null 2>&1 || dotnet=dotnet

# CFBundleVersion akzeptiert nur x.y.z, Tags heißen v1.2.3 und Vorabversionen
# tragen ein Suffix; beides fällt hier weg.
plistversion=$(printf '%s' "$version" | sed -n 's/^v\{0,1\}\([0-9]\{1,\}\.[0-9]\{1,\}\.[0-9]\{1,\}\).*$/\1/p')
[ -n "$plistversion" ] || plistversion=0.0.0

rm -rf "$dist"
"$dotnet" publish "$root/src/Umsatzschaetzung.App" -c Release -f net10.0 -r "$arch" \
    --self-contained -p:PublishSingleFile=true -p:DebugType=none -p:Version="$plistversion" -o "$contents/MacOS"

# In Contents/MacOS darf nur Code liegen: alles andere dort hält codesign für
# verschachtelte Bundles und verlangt für jedes eine eigene Signatur. Deshalb
# auch der Einzeldatei-Build - die .NET-Assemblies wären sonst genau das.
mkdir -p "$contents/Resources"
mv "$contents/MacOS/models" "$contents/Resources/models"
sed "s/@VERSION@/$plistversion/g" "$here/Info.plist" > "$contents/Info.plist"
cp "$root/packaging/windows/LICENSES.txt" "$contents/Resources/LICENSES.txt"
cp "$root/LICENSE" "$contents/Resources/LICENSE.txt"
cp "$root/LIZENZ" "$contents/Resources/LIZENZ.txt"
sh "$here/icon.sh" "$contents/Resources"

for required in \
    MacOS/umsatzschaetzung MacOS/libAvaloniaNative.dylib MacOS/libSkiaSharp.dylib \
    MacOS/libe_sqlite3.dylib MacOS/libonnxruntime.dylib MacOS/libpdfium.dylib \
    Resources/AppIcon.icns Resources/LICENSES.txt Resources/LICENSE.txt Resources/LIZENZ.txt Info.plist
do
    [ -e "$contents/$required" ] || { echo "$required fehlt in $app" >&2; exit 1; }
done
for required in models/belegtagger/belegtagger.int8.onnx models/belegtagger/vocab.json \
                models/belegtagger/merges.txt models/belegtagger/byte_to_unicode.json \
                models/belegtagger/spec.json models/v6/PP-OCRv6_det_small.onnx
do
    [ -f "$contents/Resources/$required" ] || { echo "$required fehlt; der Build holt es, siehe Models.targets" >&2; exit 1; }
done

# Ein Zeitstempel braucht ein echtes Zertifikat, ad-hoc-Signaturen tragen keinen.
[ "$identity" = "-" ] && stamp=--timestamp=none || stamp=--timestamp
ent=$here/umsatzschätzung.entitlements

# Innen nach außen: die Bundle-Signatur versiegelt, was dann schon signiert ist.
find "$contents/MacOS" -type f \( -name '*.dylib' -o -perm -u+x \) ! -name umsatzschaetzung \
    -exec codesign --force "$stamp" --options runtime --entitlements "$ent" -s "$identity" {} +
codesign --force "$stamp" --options runtime --entitlements "$ent" -s "$identity" "$app"
codesign --verify --strict "$app"

# Anmeldung entweder über ein gespeichertes notarytool-Profil oder, wie in der CI,
# über Apple-ID, Team und App-Passwort aus der Umgebung.
notarise() {
    [ "$identity" != "-" ] || return 0
    if [ -n "$profile" ]; then
        xcrun notarytool submit "$1" --keychain-profile "$profile" --wait
    elif [ -n "${APPLE_ID-}" ]; then
        xcrun notarytool submit "$1" --apple-id "$APPLE_ID" --team-id "$APPLE_TEAM_ID" \
            --password "$APPLE_APP_PASSWORD" --wait
    else
        return 0
    fi
    xcrun stapler staple "$1"
}

notarise "$app"
[ "$installer" -eq 1 ] || { du -sh "$app"; exit 0; }

mkdir -p "$out"
stage=$dist/dmg
rm -rf "$stage"
mkdir -p "$stage"
cp -R "$app" "$stage/"
ln -s /Applications "$stage/Applications"
dmg=$out/umsatzschaetzung-$plistversion-$arch.dmg
rm -f "$dmg"
hdiutil create -volname Umsatzschätzung -srcfolder "$stage" -fs HFS+ -format UDZO -ov -quiet "$dmg"
rm -rf "$stage"

if [ "$identity" != "-" ]; then
    codesign --force --timestamp -s "$identity" "$dmg"
    notarise "$dmg"
fi

cd "$out"
shasum -a 256 -- * | tee SHA256SUMS.txt
